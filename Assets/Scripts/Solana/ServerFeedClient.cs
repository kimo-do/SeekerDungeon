using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Solana.Unity.SDK;
using UnityEngine;
using UnityEngine.Networking;

namespace SeekerDungeon.Solana
{
    public sealed class ServerFeedClient : MonoBehaviour
    {
        [Serializable]
        private sealed class FeedPublishRequest
        {
            public string type;
            public string actorDisplayName;
            public string targetDisplayName;
            public string itemName;
            public string itemRarity;
            public string roomLabel;
            public long unixTime;
            public string clientEventId;
        }

        [Serializable]
        private sealed class FeedEventDto
        {
            public int id;
            public string type;
            public string message;
            public long createdAtUnix;
        }

        [Serializable]
        private sealed class FeedPollResponse
        {
            public FeedEventDto[] events;
        }

        private static readonly HttpClient SharedHttpClient = new();
        private const int MaxRecentEventIds = 1024;
        private const string LocalSeekerIdentityConfigResourcePath = "LocalSecrets/LocalSeekerIdentityConfig";

        [Header("Endpoints")]
        [SerializeField] private string publishUrl = string.Empty;
        [SerializeField] private string streamUrl = string.Empty;
        [SerializeField] private string pollUrl = string.Empty;

        [Header("Receive")]
        [SerializeField] private int pollLimit = 20;
        [SerializeField] private float pollIntervalSeconds = 2.5f;
        [SerializeField] private float reconnectDelaySeconds = 6f;
        [SerializeField] private bool logDebugMessages;

        [Header("References")]
        [SerializeField] private LGManager manager;
        [SerializeField] private ServerFeedHudQueue hudQueue;

        private readonly ConcurrentQueue<string> _incomingJsonQueue = new();
        private readonly HashSet<int> _seenEventIds = new();
        private readonly Queue<int> _seenEventOrder = new();
        private CancellationTokenSource _loopCts;
        private int _lastSeenEventId;
        private LocalSeekerIdentityConfig _localSeekerIdentityConfig;

        public static ServerFeedClient Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (manager == null)
            {
                manager = LGManager.Instance ?? FindFirstObjectByType<LGManager>();
            }

            if (hudQueue == null)
            {
                hudQueue = FindFirstObjectByType<ServerFeedHudQueue>();
            }

            _localSeekerIdentityConfig = Resources.Load<LocalSeekerIdentityConfig>(LocalSeekerIdentityConfigResourcePath);
            if (string.IsNullOrWhiteSpace(publishUrl))
            {
                publishUrl = _localSeekerIdentityConfig?.ServerFeedPublishUrl?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                streamUrl = _localSeekerIdentityConfig?.ServerFeedStreamUrl?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(pollUrl))
            {
                pollUrl = _localSeekerIdentityConfig?.ServerFeedPollUrl?.Trim() ?? string.Empty;
            }
        }

        private void OnEnable()
        {
            if (string.IsNullOrWhiteSpace(streamUrl) && string.IsNullOrWhiteSpace(pollUrl))
            {
                return;
            }

            _loopCts = new CancellationTokenSource();
            RunReceiveLoopAsync(_loopCts.Token).Forget();
        }

        private void OnDisable()
        {
            if (_loopCts != null)
            {
                _loopCts.Cancel();
                _loopCts.Dispose();
                _loopCts = null;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            while (_incomingJsonQueue.TryDequeue(out var json))
            {
                ConsumeIncomingEventJson(json);
            }
        }

        public void PublishChestOpened(string roomLabel = null)
        {
            PublishEvent("chest_opened", null, null, null, roomLabel);
        }

        public void PublishChestLoot(string itemName, string itemRarity, string roomLabel = null)
        {
            PublishEvent("chest_loot", null, itemName, itemRarity, roomLabel);
        }

        public void PublishBossDefeated(string roomLabel = null)
        {
            PublishEvent("boss_defeated", null, null, null, roomLabel);
        }

        public void PublishExtracted(string roomLabel = null)
        {
            PublishEvent("extracted", null, null, null, roomLabel);
        }

        public void PublishDuelWon(string targetDisplayName)
        {
            PublishEvent("duel_won", targetDisplayName, null, null, null);
        }

        private void PublishEvent(
            string type,
            string targetDisplayName,
            string itemName,
            string itemRarity,
            string roomLabel)
        {
            if (string.IsNullOrWhiteSpace(publishUrl))
            {
                return;
            }

            var request = new FeedPublishRequest
            {
                type = type,
                actorDisplayName = GetLocalDisplayName(),
                targetDisplayName = targetDisplayName,
                itemName = itemName,
                itemRarity = itemRarity,
                roomLabel = roomLabel,
                unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                clientEventId = Guid.NewGuid().ToString("N")
            };
            PublishAsync(request).Forget();
        }

        private async UniTaskVoid PublishAsync(FeedPublishRequest request)
        {
            try
            {
                var payload = JsonUtility.ToJson(request);
                using var webRequest = new UnityWebRequest(publishUrl.Trim(), UnityWebRequest.kHttpVerbPOST);
                var bodyBytes = Encoding.UTF8.GetBytes(payload);
                webRequest.uploadHandler = new UploadHandlerRaw(bodyBytes);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.timeout = 8;
                await webRequest.SendWebRequest().ToUniTask();

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    Log($"Publish failed: {webRequest.error}");
                }
            }
            catch (Exception exception)
            {
                Log($"Publish exception: {exception.Message}");
            }
        }

        private async UniTaskVoid RunReceiveLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var streamConnected = await TryReadSseStreamAsync(cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var fallbackStart = Time.realtimeSinceStartup;
                while (!cancellationToken.IsCancellationRequested &&
                       Time.realtimeSinceStartup - fallbackStart < reconnectDelaySeconds)
                {
                    await PollOnceAsync(cancellationToken);
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(Mathf.Max(0.5f, pollIntervalSeconds)),
                        cancellationToken: cancellationToken);
                }

                if (!streamConnected)
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(Mathf.Max(0.5f, reconnectDelaySeconds)),
                        cancellationToken: cancellationToken);
                }
            }
        }

        private async UniTask<bool> TryReadSseStreamAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                return false;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, streamUrl.Trim());
                request.Headers.Accept.ParseAdd("text/event-stream");
                if (_lastSeenEventId > 0)
                {
                    request.Headers.Add("Last-Event-ID", _lastSeenEventId.ToString());
                }

                using var response = await SharedHttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    Log($"SSE connection failed: {(int)response.StatusCode}");
                    return false;
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                var currentId = string.Empty;
                var dataBuilder = new StringBuilder();
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null)
                    {
                        return true;
                    }

                    if (line.StartsWith(":", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (line.StartsWith("id:", StringComparison.Ordinal))
                    {
                        currentId = line.Substring(3).Trim();
                        continue;
                    }

                    if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        if (dataBuilder.Length > 0)
                        {
                            dataBuilder.Append('\n');
                        }

                        dataBuilder.Append(line.Substring(5).TrimStart());
                        continue;
                    }

                    if (line.Length == 0 && dataBuilder.Length > 0)
                    {
                        _incomingJsonQueue.Enqueue(dataBuilder.ToString());
                        if (int.TryParse(currentId, out var parsedId))
                        {
                            _lastSeenEventId = Mathf.Max(_lastSeenEventId, parsedId);
                        }

                        dataBuilder.Clear();
                        currentId = string.Empty;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                Log($"SSE read failed: {exception.Message}");
            }

            return false;
        }

        private async UniTask PollOnceAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pollUrl))
            {
                return;
            }

            var requestUrl = BuildPollRequestUrl();
            using var webRequest = UnityWebRequest.Get(requestUrl);
            webRequest.timeout = 8;

            try
            {
                await webRequest.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                Log($"Poll exception: {exception.Message}");
                return;
            }

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Log($"Poll failed: {webRequest.error}");
                return;
            }

            var body = webRequest.downloadHandler?.text;
            if (string.IsNullOrWhiteSpace(body))
            {
                return;
            }

            FeedPollResponse parsed;
            try
            {
                parsed = JsonUtility.FromJson<FeedPollResponse>(body);
            }
            catch (Exception exception)
            {
                Log($"Poll parse failed: {exception.Message}");
                return;
            }

            if (parsed?.events == null)
            {
                return;
            }

            for (var index = 0; index < parsed.events.Length; index += 1)
            {
                var evt = parsed.events[index];
                if (evt == null || evt.id <= 0 || string.IsNullOrWhiteSpace(evt.message))
                {
                    continue;
                }

                if (!MarkSeen(evt.id))
                {
                    continue;
                }

                _lastSeenEventId = Mathf.Max(_lastSeenEventId, evt.id);
                if (hudQueue != null)
                {
                    hudQueue.EnqueueMessage(evt.message);
                }
            }
        }

        private string BuildPollRequestUrl()
        {
            var separator = pollUrl.IndexOf("?", StringComparison.Ordinal) >= 0 ? "&" : "?";
            var safeLimit = Mathf.Clamp(pollLimit, 1, 100);
            return $"{pollUrl.Trim()}{separator}afterId={_lastSeenEventId}&limit={safeLimit}";
        }

        private void ConsumeIncomingEventJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            FeedEventDto parsed;
            try
            {
                parsed = JsonUtility.FromJson<FeedEventDto>(json);
            }
            catch (Exception exception)
            {
                Log($"SSE parse failed: {exception.Message}");
                return;
            }

            if (parsed == null || parsed.id <= 0 || string.IsNullOrWhiteSpace(parsed.message))
            {
                return;
            }

            if (!MarkSeen(parsed.id))
            {
                return;
            }

            _lastSeenEventId = Mathf.Max(_lastSeenEventId, parsed.id);
            if (hudQueue != null)
            {
                hudQueue.EnqueueMessage(parsed.message);
            }
        }

        private bool MarkSeen(int eventId)
        {
            if (_seenEventIds.Contains(eventId))
            {
                return false;
            }

            _seenEventIds.Add(eventId);
            _seenEventOrder.Enqueue(eventId);

            while (_seenEventOrder.Count > MaxRecentEventIds)
            {
                var stale = _seenEventOrder.Dequeue();
                _seenEventIds.Remove(stale);
            }

            return true;
        }

        private string GetLocalDisplayName()
        {
            if (manager == null)
            {
                manager = LGManager.Instance ?? FindFirstObjectByType<LGManager>();
            }

            var profileName = manager?.CurrentProfileState?.DisplayName?.Trim();
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                return profileName;
            }

            var wallet = Web3.Wallet?.Account?.PublicKey?.Key;
            if (string.IsNullOrWhiteSpace(wallet) || wallet.Length < 10)
            {
                return "Unknown";
            }

            return $"{wallet.Substring(0, 4)}...{wallet.Substring(wallet.Length - 4)}";
        }

        private void Log(string message)
        {
            if (!logDebugMessages)
            {
                return;
            }

            Debug.Log($"[ServerFeedClient] {message}");
        }
    }
}
