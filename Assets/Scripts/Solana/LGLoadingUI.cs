using UnityEngine;
using UnityEngine.UIElements;

namespace SeekerDungeon.Solana
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class LGLoadingUI : MonoBehaviour
    {
        [SerializeField] private LGLoadingController loadingController;
        [SerializeField] private LGWalletSessionManager walletSessionManager;

        private UIDocument _document;
        private Button _connectButton;
        private Label _statusLabel;
        private VisualElement _boundRoot;
        private bool _isHandlersBound;

        private void Awake()
        {
            LGUiInputSystemGuard.EnsureEventSystemForRuntimeUi();
            _document = GetComponent<UIDocument>();

            if (loadingController == null)
            {
                loadingController = FindObjectOfType<LGLoadingController>();
            }

            if (walletSessionManager == null)
            {
                walletSessionManager = LGWalletSessionManager.Instance;
            }

            if (walletSessionManager == null)
            {
                walletSessionManager = FindObjectOfType<LGWalletSessionManager>();
            }
        }

        private void OnEnable()
        {
            TryRebindUi(force: true);
            if (walletSessionManager != null)
            {
                walletSessionManager.OnWalletConnectionChanged += HandleWalletConnectionChanged;
                walletSessionManager.OnError += HandleWalletError;
            }

            if (loadingController == null)
            {
                SetStatus("Loading controller not found");
                SetButtonEnabled(false);
            }
        }

        private void OnDisable()
        {
            UnbindUiHandlers();

            if (walletSessionManager != null)
            {
                walletSessionManager.OnWalletConnectionChanged -= HandleWalletConnectionChanged;
                walletSessionManager.OnError -= HandleWalletError;
            }
        }

        private void Update()
        {
            if (_boundRoot != _document?.rootVisualElement || !_isHandlersBound)
            {
                TryRebindUi();
            }
        }

        private void TryRebindUi(bool force = false)
        {
            var root = _document?.rootVisualElement;
            if (root == null)
            {
                return;
            }

            if (!force && ReferenceEquals(root, _boundRoot) && _isHandlersBound)
            {
                return;
            }

            UnbindUiHandlers();

            _connectButton = root.Q<Button>("btn-connect-wallet");
            _statusLabel = root.Q<Label>("connect-status");

            if (_connectButton != null)
            {
                _connectButton.clicked += HandleConnectClicked;
            }

            _boundRoot = root;
            _isHandlersBound = true;
        }

        private void UnbindUiHandlers()
        {
            if (_connectButton != null)
            {
                _connectButton.clicked -= HandleConnectClicked;
            }

            _boundRoot = null;
            _isHandlersBound = false;
        }

        private void HandleConnectClicked()
        {
            if (loadingController == null)
            {
                SetStatus("Loading controller not found");
                return;
            }

            SetStatus("Connecting wallet...");
            SetButtonEnabled(false);
            loadingController.OnConnectSeekerClicked();
        }

        private void HandleWalletConnectionChanged(bool isConnected)
        {
            if (isConnected)
            {
                SetStatus("Connected");
                return;
            }

            SetStatus("Connect your wallet to continue");
            SetButtonEnabled(true);
        }

        private void HandleWalletError(string errorMessage)
        {
            SetStatus(errorMessage);
            SetButtonEnabled(true);
        }

        private void SetStatus(string message)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = message;
            }
        }

        private void SetButtonEnabled(bool enabled)
        {
            if (_connectButton != null)
            {
                _connectButton.SetEnabled(enabled);
            }
        }
    }
}
