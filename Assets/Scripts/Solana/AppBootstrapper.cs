using Cysharp.Threading.Tasks;
using SeekerDungeon;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Put this in a lightweight Preload scene.
    /// It ensures persistent managers exist exactly once, then loads the first runtime scene.
    /// </summary>
    public sealed class AppBootstrapper : MonoBehaviour
    {
        [SerializeField] private string firstSceneName = "Loading";
        [SerializeField] private bool createEventSystemIfMissing = true;
        [SerializeField] private bool useSceneLoadController = false;
        [SerializeField] private bool logDebugMessages = true;

        private bool _isBootstrapping;

        private void Start()
        {
            BootstrapAsync().Forget();
        }

        private async UniTaskVoid BootstrapAsync()
        {
            if (_isBootstrapping)
            {
                return;
            }

            _isBootstrapping = true;
            try
            {
                LGUiInputSystemGuard.EnsureEventSystemForRuntimeUi(createEventSystemIfMissing);
                LGManager.EnsureInstance();
                LGWalletSessionManager.EnsureInstance();

                if (string.IsNullOrWhiteSpace(firstSceneName))
                {
                    Debug.LogError("[AppBootstrapper] First scene name is empty.");
                    return;
                }

                if (useSceneLoadController)
                {
                    await SceneLoadController.GetOrCreate().LoadSceneAsync(firstSceneName, LoadSceneMode.Single);
                    return;
                }

                var loadOperation = SceneManager.LoadSceneAsync(firstSceneName, LoadSceneMode.Single);
                if (loadOperation == null)
                {
                    Debug.LogError($"[AppBootstrapper] Failed to load scene '{firstSceneName}'.");
                    return;
                }

                while (!loadOperation.isDone)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }

                Log($"Bootstrapped. Loaded '{firstSceneName}'.");
            }
            finally
            {
                _isBootstrapping = false;
            }
        }

        private void Log(string message)
        {
            if (!logDebugMessages)
            {
                return;
            }

            Debug.Log($"[AppBootstrapper] {message}");
        }
    }
}
