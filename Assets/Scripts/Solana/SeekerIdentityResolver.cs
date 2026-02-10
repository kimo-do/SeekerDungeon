using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Solana.Unity.Rpc;
using Solana.Unity.Rpc.Types;
using Solana.Unity.Wallet;
using UnityEngine;
using UnityEngine.Networking;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Best-effort resolver for Seeker `.skr` identity by wallet.
    /// Never throws to callers; returns null when unknown/unavailable.
    /// </summary>
    public static class SeekerIdentityResolver
    {
        private const string DefaultMainnetRpcUrl = "https://api.mainnet-beta.solana.com";
        private const string TldhProgramId = "TLDHkysf5pCnKsVA4gXpNvmy7psXLPEu4LAdDJthT9S";
        private const int DefaultSignatureScanLimit = 20;
        private const int MaxTransactionFetchesPerLookup = 24;
        private const int MaxTransactionFetchAttempts = 3;
        private const int RateLimitBreakThreshold = 8;
        private const int InterTransactionDelayMs = 50;
        private const int BaseRetryBackoffMs = 180;
        private const int EnhancedHistoryTimeoutSeconds = 12;
        private const int EnhancedHistoryBodyLogLimit = 320;
        private const bool EnableDebugLogs = true;
        private const int MemoryCacheTtlMinutes = 20;
        private const int PersistentCacheTtlHours = 24;
        private const string PersistentCachePrefix = "LG_SEEKER_ID_CACHE_V1_";

        private static readonly Regex AnySkrDomainRegex = new(
            @"([a-z0-9][a-z0-9\-]*\.skr)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BuyingDomainRegex = new(
            @"Buying domain\s+([a-z0-9][a-z0-9\-]*\.skr)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        [Serializable]
        private sealed class BackendResolveResponse
        {
            public bool found;
            public string seekerId;
            public string source;
            public long updatedAtUnix;
        }

        private sealed class CacheEntry
        {
            public string SeekerId { get; init; }
            public DateTimeOffset ExpiresAtUtc { get; init; }
        }

        private static readonly Dictionary<string, CacheEntry> MemoryCache = new();

        public static async UniTask<string> TryResolveSkrForWalletAsync(
            PublicKey walletPublicKey,
            string mainnetRpcUrl = DefaultMainnetRpcUrl,
            int signatureScanLimit = DefaultSignatureScanLimit,
            IReadOnlyList<string> fallbackRpcUrls = null,
            bool preferEnhancedHistory = true,
            string enhancedHistoryUrlTemplate = null,
            IReadOnlyList<string> fallbackEnhancedHistoryUrlTemplates = null,
            string backendResolveUrlTemplate = null,
            IReadOnlyList<string> fallbackBackendResolveUrlTemplates = null)
        {
            if (walletPublicKey == null)
            {
                return null;
            }

            if (signatureScanLimit <= 0)
            {
                signatureScanLimit = DefaultSignatureScanLimit;
            }

            var walletKey = walletPublicKey.Key;
            if (string.IsNullOrWhiteSpace(walletKey))
            {
                return null;
            }

            var rpcCandidates = BuildRpcCandidateList(mainnetRpcUrl, fallbackRpcUrls);
            if (rpcCandidates.Count == 0)
            {
                rpcCandidates.Add(DefaultMainnetRpcUrl);
            }
            var enhancedCandidates = BuildEnhancedTemplateCandidateList(
                enhancedHistoryUrlTemplate,
                fallbackEnhancedHistoryUrlTemplates);
            var backendCandidates = BuildBackendTemplateCandidateList(
                backendResolveUrlTemplate,
                fallbackBackendResolveUrlTemplates);

            var cached = TryGetCached(walletKey);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                Log($"Cache hit wallet={walletKey} seekerId={cached}");
                return cached;
            }

            if (backendCandidates.Count > 0)
            {
                var backendSeekerId = await TryResolveWithBackendAsync(walletKey, backendCandidates);
                if (!string.IsNullOrWhiteSpace(backendSeekerId))
                {
                    StoreCached(walletKey, backendSeekerId);
                    Log($"Resolved seekerId={backendSeekerId} for wallet={walletKey} via backend");
                    return backendSeekerId;
                }
            }

            if (preferEnhancedHistory && enhancedCandidates.Count > 0)
            {
                var enhancedSeekerId = await TryResolveWithEnhancedHistoryAsync(walletKey, enhancedCandidates);
                if (!string.IsNullOrWhiteSpace(enhancedSeekerId))
                {
                    StoreCached(walletKey, enhancedSeekerId);
                    Log($"Resolved seekerId={enhancedSeekerId} for wallet={walletKey} via enhanced history");
                    return enhancedSeekerId;
                }
            }

            foreach (var rpcUrl in rpcCandidates)
            {
                var seekerId = await TryResolveOnRpcAsync(walletKey, rpcUrl, signatureScanLimit);
                if (!string.IsNullOrWhiteSpace(seekerId))
                {
                    StoreCached(walletKey, seekerId);
                    Log($"Resolved seekerId={seekerId} for wallet={walletKey} rpc={rpcUrl}");
                    return seekerId;
                }
            }

            Log($"No matching .skr lookup transaction found for wallet={walletKey} across {rpcCandidates.Count} rpc(s)");
            return null;
        }

        private static async UniTask<string> TryResolveWithBackendAsync(
            string walletKey,
            IReadOnlyList<string> backendUrlTemplates)
        {
            if (string.IsNullOrWhiteSpace(walletKey) || backendUrlTemplates == null)
            {
                return null;
            }

            var encodedWallet = Uri.EscapeDataString(walletKey);
            for (var index = 0; index < backendUrlTemplates.Count; index += 1)
            {
                var template = backendUrlTemplates[index];
                if (string.IsNullOrWhiteSpace(template) || template.IndexOf("{address}", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Log($"Skipping invalid backend template at index={index} (missing {{address}} placeholder)");
                    continue;
                }

                var requestUrl = ReplaceAddressPlaceholder(template.Trim(), encodedWallet);
                if (string.IsNullOrWhiteSpace(requestUrl))
                {
                    Log($"Skipping invalid backend template at index={index} (could not build URL)");
                    continue;
                }

                Log($"Resolving wallet={walletKey} backendUrl={requestUrl}");
                var body = await TryGetTextAsync(requestUrl, EnhancedHistoryTimeoutSeconds);
                if (string.IsNullOrWhiteSpace(body))
                {
                    continue;
                }

                var seekerId = TryExtractSkrDomainFromBackendResponse(body);
                if (string.IsNullOrWhiteSpace(seekerId))
                {
                    Log($"No .skr match in backend response wallet={walletKey} templateIndex={index}");
                    continue;
                }

                return seekerId;
            }

            return null;
        }

        private static async UniTask<string> TryResolveWithEnhancedHistoryAsync(
            string walletKey,
            IReadOnlyList<string> enhancedHistoryUrlTemplates)
        {
            if (string.IsNullOrWhiteSpace(walletKey) || enhancedHistoryUrlTemplates == null)
            {
                return null;
            }

            var encodedWallet = Uri.EscapeDataString(walletKey);
            for (var index = 0; index < enhancedHistoryUrlTemplates.Count; index += 1)
            {
                var template = enhancedHistoryUrlTemplates[index];
                if (string.IsNullOrWhiteSpace(template) || template.IndexOf("{address}", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Log($"Skipping invalid enhanced template at index={index} (missing {{address}} placeholder)");
                    continue;
                }

                var requestUrl = ReplaceAddressPlaceholder(template.Trim(), encodedWallet);
                if (string.IsNullOrWhiteSpace(requestUrl))
                {
                    Log($"Skipping invalid enhanced template at index={index} (could not build URL)");
                    continue;
                }

                Log($"Resolving wallet={walletKey} enhancedUrl={requestUrl}");
                var body = await TryGetTextAsync(requestUrl, EnhancedHistoryTimeoutSeconds);
                if (string.IsNullOrWhiteSpace(body))
                {
                    continue;
                }

                var seekerId = TryExtractSkrDomainFromText(body);
                if (string.IsNullOrWhiteSpace(seekerId))
                {
                    Log($"No .skr match in enhanced response wallet={walletKey} templateIndex={index}");
                    continue;
                }

                return seekerId;
            }

            return null;
        }

        private static async UniTask<string> TryResolveOnRpcAsync(
            string walletKey,
            string rpcUrl,
            int signatureScanLimit)
        {
            try
            {
                Log($"Resolving wallet={walletKey} rpc={rpcUrl} scanLimit={signatureScanLimit}");
                var rpcClient = ClientFactory.GetClient(rpcUrl);
                var signaturesResult = await rpcClient.GetSignaturesForAddressAsync(
                    walletKey,
                    (ulong)signatureScanLimit,
                    null,
                    null,
                    Commitment.Confirmed);

                if (!signaturesResult.WasSuccessful || signaturesResult.Result == null)
                {
                    Log(
                        $"Signatures query failed wallet={walletKey} rpc={rpcUrl} " +
                        $"successful={signaturesResult.WasSuccessful} reason={signaturesResult.Reason}");
                    return null;
                }

                if (signaturesResult.Result.Count == 0)
                {
                    Log($"No signatures found for wallet={walletKey} rpc={rpcUrl}");
                    return null;
                }

                var transactionFetchCount = 0;
                var consecutiveRateLimitFailures = 0;
                foreach (var signatureInfo in signaturesResult.Result)
                {
                    if (signatureInfo == null || string.IsNullOrWhiteSpace(signatureInfo.Signature))
                    {
                        continue;
                    }

                    if (transactionFetchCount >= MaxTransactionFetchesPerLookup)
                    {
                        Log(
                            $"Stopping scan early at {transactionFetchCount} transaction fetches " +
                            $"for wallet={walletKey} rpc={rpcUrl}");
                        break;
                    }

                    transactionFetchCount += 1;

                    var fetchedTransaction = false;
                    for (var attempt = 1; attempt <= MaxTransactionFetchAttempts; attempt += 1)
                    {
                        var transactionResult = await rpcClient.GetTransactionAsync(
                            signatureInfo.Signature,
                            Commitment.Confirmed,
                            0);

                        if (transactionResult.WasSuccessful && transactionResult.Result != null)
                        {
                            fetchedTransaction = true;
                            consecutiveRateLimitFailures = 0;

                            var transactionInfo = transactionResult.Result.Transaction;
                            var accountKeys = transactionInfo?.Message?.AccountKeys;
                            if (accountKeys == null || accountKeys.Length == 0)
                            {
                                break;
                            }

                            if (!ContainsAccount(accountKeys, walletKey) || !ContainsAccount(accountKeys, TldhProgramId))
                            {
                                break;
                            }

                            var logMessages = transactionResult.Result.Meta?.LogMessages;
                            if (logMessages == null || logMessages.Length == 0)
                            {
                                break;
                            }

                            var seekerId = TryExtractSkrDomain(logMessages);
                            if (string.IsNullOrWhiteSpace(seekerId))
                            {
                                break;
                            }

                            return seekerId;
                        }

                        var reason = transactionResult.Reason;
                        var isRateLimited = IsRateLimitReason(reason);
                        if (!isRateLimited || attempt >= MaxTransactionFetchAttempts)
                        {
                            Log(
                                $"Transaction fetch failed signature={signatureInfo.Signature} rpc={rpcUrl} " +
                                $"successful={transactionResult.WasSuccessful} reason={reason}");
                            if (isRateLimited)
                            {
                                consecutiveRateLimitFailures += 1;
                            }

                            break;
                        }

                        var backoffMs = ComputeBackoffMs(attempt);
                        Log(
                            $"Rate limited fetching signature={signatureInfo.Signature} rpc={rpcUrl} " +
                            $"attempt={attempt}/{MaxTransactionFetchAttempts}. retryInMs={backoffMs}");
                        await UniTask.Delay(backoffMs);
                    }

                    if (!fetchedTransaction && consecutiveRateLimitFailures >= RateLimitBreakThreshold)
                    {
                        Log(
                            $"Stopping lookup due to repeated RPC rate limits wallet={walletKey} " +
                            $"rpc={rpcUrl} count={consecutiveRateLimitFailures}");
                        break;
                    }

                    await UniTask.Delay(InterTransactionDelayMs);
                }

                return null;
            }
            catch (Exception exception)
            {
                // Non-fatal by design; callers should always be able to fall back.
                Log($"Resolver exception wallet={walletKey} rpc={rpcUrl} message={exception.Message}");
                return null;
            }
        }

        private static List<string> BuildRpcCandidateList(
            string primaryRpcUrl,
            IReadOnlyList<string> fallbackRpcUrls)
        {
            var urls = new List<string>();
            AddRpcUrlIfValid(urls, primaryRpcUrl);

            if (fallbackRpcUrls != null)
            {
                for (var index = 0; index < fallbackRpcUrls.Count; index += 1)
                {
                    AddRpcUrlIfValid(urls, fallbackRpcUrls[index]);
                }
            }

            if (urls.Count == 0)
            {
                AddRpcUrlIfValid(urls, DefaultMainnetRpcUrl);
            }

            return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> BuildEnhancedTemplateCandidateList(
            string primaryTemplate,
            IReadOnlyList<string> fallbackTemplates)
        {
            var templates = new List<string>();
            AddTemplateIfValid(templates, primaryTemplate);

            if (fallbackTemplates != null)
            {
                for (var index = 0; index < fallbackTemplates.Count; index += 1)
                {
                    AddTemplateIfValid(templates, fallbackTemplates[index]);
                }
            }

            return templates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> BuildBackendTemplateCandidateList(
            string primaryTemplate,
            IReadOnlyList<string> fallbackTemplates)
        {
            var templates = new List<string>();
            AddTemplateIfValid(templates, primaryTemplate);

            if (fallbackTemplates != null)
            {
                for (var index = 0; index < fallbackTemplates.Count; index += 1)
                {
                    AddTemplateIfValid(templates, fallbackTemplates[index]);
                }
            }

            return templates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddRpcUrlIfValid(ICollection<string> urls, string candidate)
        {
            if (urls == null || string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            var trimmed = candidate.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            urls.Add(trimmed);
        }

        private static void AddTemplateIfValid(ICollection<string> templates, string candidate)
        {
            if (templates == null || string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            var trimmed = candidate.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            templates.Add(trimmed);
        }

        private static string ReplaceAddressPlaceholder(string template, string encodedWallet)
        {
            if (string.IsNullOrWhiteSpace(template) || string.IsNullOrWhiteSpace(encodedWallet))
            {
                return null;
            }

            return Regex.Replace(
                template,
                "\\{address\\}",
                encodedWallet,
                RegexOptions.IgnoreCase);
        }

        private static string TryExtractSkrDomain(IReadOnlyList<string> logMessages)
        {
            for (var index = 0; index < logMessages.Count; index += 1)
            {
                var logLine = logMessages[index];
                if (string.IsNullOrWhiteSpace(logLine))
                {
                    continue;
                }

                var match = BuyingDomainRegex.Match(logLine);
                if (!match.Success || match.Groups.Count < 2)
                {
                    continue;
                }

                var domain = match.Groups[1].Value?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(domain))
                {
                    continue;
                }

                return domain;
            }

            return null;
        }

        private static string TryExtractSkrDomainFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var buyingMatch = BuyingDomainRegex.Match(text);
            if (buyingMatch.Success && buyingMatch.Groups.Count >= 2)
            {
                var buyingDomain = buyingMatch.Groups[1].Value?.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(buyingDomain))
                {
                    return buyingDomain;
                }
            }

            var genericMatch = AnySkrDomainRegex.Match(text);
            if (genericMatch.Success && genericMatch.Groups.Count >= 2)
            {
                var genericDomain = genericMatch.Groups[1].Value?.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(genericDomain))
                {
                    return genericDomain;
                }
            }

            return null;
        }

        private static string TryExtractSkrDomainFromBackendResponse(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            try
            {
                var parsed = JsonUtility.FromJson<BackendResolveResponse>(responseBody);
                if (parsed == null)
                {
                    return TryExtractSkrDomainFromText(responseBody);
                }

                if (!parsed.found || string.IsNullOrWhiteSpace(parsed.seekerId))
                {
                    return null;
                }

                return TryExtractSkrDomainFromText(parsed.seekerId);
            }
            catch (Exception exception)
            {
                Log($"Backend response parse failed message={exception.Message}");
                return TryExtractSkrDomainFromText(responseBody);
            }
        }

        private static bool ContainsAccount(IReadOnlyList<string> accountKeys, string expected)
        {
            for (var index = 0; index < accountKeys.Count; index += 1)
            {
                if (string.Equals(accountKeys[index], expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string TryGetCached(string walletKey)
        {
            var now = DateTimeOffset.UtcNow;
            if (MemoryCache.TryGetValue(walletKey, out var memoryEntry))
            {
                if (memoryEntry.ExpiresAtUtc > now && !string.IsNullOrWhiteSpace(memoryEntry.SeekerId))
                {
                    return memoryEntry.SeekerId;
                }

                MemoryCache.Remove(walletKey);
            }

            var persistentKey = GetPersistentCacheKey(walletKey);
            var rawValue = PlayerPrefs.GetString(persistentKey, string.Empty);
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            var separatorIndex = rawValue.IndexOf('|');
            if (separatorIndex <= 0 || separatorIndex >= rawValue.Length - 1)
            {
                PlayerPrefs.DeleteKey(persistentKey);
                return null;
            }

            var seekerId = rawValue.Substring(0, separatorIndex);
            var expiryText = rawValue.Substring(separatorIndex + 1);
            if (!long.TryParse(expiryText, out var expiryUnix))
            {
                PlayerPrefs.DeleteKey(persistentKey);
                return null;
            }

            var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expiryUnix);
            if (expiresAtUtc <= now || string.IsNullOrWhiteSpace(seekerId))
            {
                PlayerPrefs.DeleteKey(persistentKey);
                return null;
            }

            MemoryCache[walletKey] = new CacheEntry
            {
                SeekerId = seekerId,
                ExpiresAtUtc = now.AddMinutes(MemoryCacheTtlMinutes)
            };

            return seekerId;
        }

        private static void StoreCached(string walletKey, string seekerId)
        {
            if (string.IsNullOrWhiteSpace(walletKey) || string.IsNullOrWhiteSpace(seekerId))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            MemoryCache[walletKey] = new CacheEntry
            {
                SeekerId = seekerId,
                ExpiresAtUtc = now.AddMinutes(MemoryCacheTtlMinutes)
            };

            var persistentExpiry = now.AddHours(PersistentCacheTtlHours).ToUnixTimeSeconds();
            var persistentValue = $"{seekerId}|{persistentExpiry}";
            PlayerPrefs.SetString(GetPersistentCacheKey(walletKey), persistentValue);
            PlayerPrefs.Save();
        }

        private static string GetPersistentCacheKey(string walletKey)
        {
            return $"{PersistentCachePrefix}{walletKey}";
        }

        private static bool IsRateLimitReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return false;
            }

            return
                reason.IndexOf("Too many requests", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int ComputeBackoffMs(int attempt)
        {
            var exponent = Math.Max(0, attempt - 1);
            var jitterMs = UnityEngine.Random.Range(40, 110);
            return (BaseRetryBackoffMs * (1 << exponent)) + jitterMs;
        }

        private static async UniTask<string> TryGetTextAsync(string url, int timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            using var request = UnityWebRequest.Get(url);
            request.timeout = Mathf.Max(1, timeoutSeconds);
            try
            {
                await request.SendWebRequest().ToUniTask();
            }
            catch (Exception exception)
            {
                Log($"Enhanced request exception url={url} message={exception.Message}");
                return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Log(
                    $"Enhanced request failed url={url} code={request.responseCode} " +
                    $"error={request.error} body={TruncateForLog(request.downloadHandler?.text, EnhancedHistoryBodyLogLimit)}");
                return null;
            }

            return request.downloadHandler?.text;
        }

        private static string TruncateForLog(string text, int limit)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "<empty>";
            }

            if (text.Length <= limit)
            {
                return text;
            }

            return text.Substring(0, Math.Max(1, limit)) + "...";
        }

        private static void Log(string message)
        {
            if (!EnableDebugLogs || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Debug.Log($"[SeekerIdentityResolver] {message}");
        }
    }
}
