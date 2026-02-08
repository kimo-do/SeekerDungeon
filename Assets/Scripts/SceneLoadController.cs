using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SeekerDungeon
{
    public sealed class SceneLoadController : MonoBehaviour
    {
        private const string ControllerObjectName = "SceneLoadController";

        public static SceneLoadController Instance { get; private set; }

        [SerializeField] private float fadeDurationSeconds = 0.22f;
        [SerializeField] private Color fadeColor = Color.black;
        [SerializeField] private int canvasSortOrder = 10000;
        [SerializeField] private float maxBlackHoldSeconds = 20f;

        private CanvasGroup _fadeCanvasGroup;
        private bool _isTransitioning;
        private readonly Dictionary<string, int> _blackScreenHoldsByReason = new();
        private int _blackScreenHoldCount;

        public static SceneLoadController GetOrCreate()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var existingController = Object.FindFirstObjectByType<SceneLoadController>();
            if (existingController != null)
            {
                return existingController;
            }

            var controllerObject = new GameObject(ControllerObjectName);
            return controllerObject.AddComponent<SceneLoadController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureFadeOverlay();
            SetFadeAlpha(0f);
        }

        public async UniTask<bool> LoadSceneAsync(string sceneName, LoadSceneMode loadSceneMode = LoadSceneMode.Single)
        {
            if (_isTransitioning)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogError("[SceneLoadController] Scene name is empty.");
                return false;
            }

            EnsureFadeOverlay();
            _isTransitioning = true;

            try
            {
                await FadeAsync(1f);

                var sceneLoadOperation = SceneManager.LoadSceneAsync(sceneName, loadSceneMode);
                if (sceneLoadOperation == null)
                {
                    Debug.LogError($"[SceneLoadController] Failed to load scene '{sceneName}'.");
                    await FadeAsync(0f);
                    return false;
                }

                while (!sceneLoadOperation.isDone)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }

                await UniTask.NextFrame();
                await WaitForBlackScreenHoldsAsync();
                await FadeAsync(0f);
                return true;
            }
            finally
            {
                _isTransitioning = false;
            }
        }

        public async UniTask FadeToBlackAsync()
        {
            EnsureFadeOverlay();
            await FadeAsync(1f);
        }

        public async UniTask FadeFromBlackAsync()
        {
            EnsureFadeOverlay();
            await FadeAsync(0f);
        }

        public void HoldBlackScreen(string reason = "unspecified")
        {
            var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
            if (_blackScreenHoldsByReason.TryGetValue(normalizedReason, out var currentCount))
            {
                _blackScreenHoldsByReason[normalizedReason] = currentCount + 1;
            }
            else
            {
                _blackScreenHoldsByReason[normalizedReason] = 1;
            }

            _blackScreenHoldCount += 1;
        }

        public void ReleaseBlackScreen(string reason = "unspecified")
        {
            var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
            if (!_blackScreenHoldsByReason.TryGetValue(normalizedReason, out var currentCount) || currentCount <= 0)
            {
                return;
            }

            if (currentCount == 1)
            {
                _blackScreenHoldsByReason.Remove(normalizedReason);
            }
            else
            {
                _blackScreenHoldsByReason[normalizedReason] = currentCount - 1;
            }

            _blackScreenHoldCount = Mathf.Max(0, _blackScreenHoldCount - 1);
        }

        private async UniTask WaitForBlackScreenHoldsAsync()
        {
            if (_blackScreenHoldCount <= 0)
            {
                return;
            }

            var elapsed = 0f;
            while (_blackScreenHoldCount > 0)
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
                elapsed += Time.unscaledDeltaTime;

                if (elapsed < maxBlackHoldSeconds)
                {
                    continue;
                }

                Debug.LogWarning(
                    $"[SceneLoadController] Hold timeout reached ({maxBlackHoldSeconds:F1}s). Releasing black screen automatically.");
                _blackScreenHoldsByReason.Clear();
                _blackScreenHoldCount = 0;
                break;
            }
        }

        private void EnsureFadeOverlay()
        {
            if (_fadeCanvasGroup != null)
            {
                return;
            }

            var canvasObject = new GameObject("SceneFadeCanvas");
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = canvasSortOrder;

            canvasObject.AddComponent<GraphicRaycaster>();

            _fadeCanvasGroup = canvasObject.AddComponent<CanvasGroup>();
            _fadeCanvasGroup.interactable = false;
            _fadeCanvasGroup.blocksRaycasts = false;

            var imageObject = new GameObject("FadeImage");
            imageObject.transform.SetParent(canvasObject.transform, false);

            var imageTransform = imageObject.AddComponent<RectTransform>();
            imageTransform.anchorMin = Vector2.zero;
            imageTransform.anchorMax = Vector2.one;
            imageTransform.offsetMin = Vector2.zero;
            imageTransform.offsetMax = Vector2.zero;

            var image = imageObject.AddComponent<Image>();
            image.color = fadeColor;
        }

        private async UniTask FadeAsync(float targetAlpha)
        {
            if (_fadeCanvasGroup == null)
            {
                return;
            }

            var startAlpha = _fadeCanvasGroup.alpha;
            if (Mathf.Approximately(startAlpha, targetAlpha))
            {
                SetFadeAlpha(targetAlpha);
                return;
            }

            var duration = Mathf.Max(0.01f, fadeDurationSeconds);
            var elapsed = 0f;

            _fadeCanvasGroup.blocksRaycasts = true;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var normalized = Mathf.Clamp01(elapsed / duration);
                _fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, normalized);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            SetFadeAlpha(targetAlpha);
        }

        private void SetFadeAlpha(float alpha)
        {
            if (_fadeCanvasGroup == null)
            {
                return;
            }

            _fadeCanvasGroup.alpha = alpha;
            _fadeCanvasGroup.blocksRaycasts = alpha > 0.001f;
        }
    }
}
