using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
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
        private VisualElement _editorWalletControls;
        private DropdownField _editorWalletSlotDropdown;
        private Button _useSelectedEditorWalletButton;
        private Button _useNewEditorWalletButton;
        private VisualElement _boundRoot;
        private bool _isHandlersBound;
        private bool _isSwitchingEditorWallet;
        private CancellationTokenSource _connectButtonCooldownCts;
        private const int EditorWalletSlotDisplayCount = 10;

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
                walletSessionManager = LGWalletSessionManager.EnsureInstance();
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
                return;
            }

            SetStatus("Connect your wallet to continue");
        }

        private void OnDisable()
        {
            UnbindUiHandlers();
            CancelConnectButtonCooldown();

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
            _editorWalletControls = root.Q<VisualElement>("editor-wallet-controls");
            _editorWalletSlotDropdown = root.Q<DropdownField>("editor-wallet-slot-dropdown");
            _useSelectedEditorWalletButton = root.Q<Button>("btn-use-selected-editor-wallet");
            _useNewEditorWalletButton = root.Q<Button>("btn-use-new-editor-wallet");

            if (_connectButton != null)
            {
                _connectButton.clicked += HandleConnectClicked;
            }

            if (_useSelectedEditorWalletButton != null)
            {
                _useSelectedEditorWalletButton.clicked += HandleUseSelectedEditorWalletClicked;
            }

            if (_useNewEditorWalletButton != null)
            {
                _useNewEditorWalletButton.clicked += HandleUseNewEditorWalletClicked;
            }

            ConfigureEditorWalletControls();

            _boundRoot = root;
            _isHandlersBound = true;
        }

        private void UnbindUiHandlers()
        {
            if (_connectButton != null)
            {
                _connectButton.clicked -= HandleConnectClicked;
            }

            if (_useSelectedEditorWalletButton != null)
            {
                _useSelectedEditorWalletButton.clicked -= HandleUseSelectedEditorWalletClicked;
            }

            if (_useNewEditorWalletButton != null)
            {
                _useNewEditorWalletButton.clicked -= HandleUseNewEditorWalletClicked;
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
            StartConnectButtonCooldown();
            loadingController.OnConnectSeekerClicked();
        }

        private void ConfigureEditorWalletControls()
        {
#if UNITY_EDITOR
            if (_editorWalletControls == null)
            {
                return;
            }

            _editorWalletControls.style.display = walletSessionManager == null
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            if (walletSessionManager == null || _editorWalletSlotDropdown == null)
            {
                return;
            }

            var maxSlot = Mathf.Max(EditorWalletSlotDisplayCount - 1, walletSessionManager.EditorWalletSlot + 1);
            var slotChoices = new List<string>(maxSlot + 1);
            for (var slot = 0; slot <= maxSlot; slot += 1)
            {
                slotChoices.Add(slot.ToString());
            }

            _editorWalletSlotDropdown.choices = slotChoices;
            _editorWalletSlotDropdown.SetValueWithoutNotify(walletSessionManager.EditorWalletSlot.ToString());
#else
            if (_editorWalletControls != null)
            {
                _editorWalletControls.style.display = DisplayStyle.None;
            }
#endif
        }

        private void HandleUseSelectedEditorWalletClicked()
        {
            if (_isSwitchingEditorWallet || walletSessionManager == null || _editorWalletSlotDropdown == null)
            {
                return;
            }

            if (!int.TryParse(_editorWalletSlotDropdown.value, out var selectedSlot))
            {
                SetStatus("Invalid editor wallet slot selection.");
                return;
            }

            SelectEditorWalletSlotAsync(selectedSlot, "Selected").Forget();
        }

        private void HandleUseNewEditorWalletClicked()
        {
            if (_isSwitchingEditorWallet || walletSessionManager == null)
            {
                return;
            }

            var newSlot = walletSessionManager.EditorWalletSlot + 1;
            SelectEditorWalletSlotAsync(newSlot, "New").Forget();
        }

        private async UniTaskVoid SelectEditorWalletSlotAsync(int slot, string reason)
        {
            if (walletSessionManager == null)
            {
                return;
            }

            _isSwitchingEditorWallet = true;
            try
            {
                var switched = await walletSessionManager.SwitchEditorWalletSlotAsync(slot, reconnect: false);
                if (!switched)
                {
                    return;
                }

                ConfigureEditorWalletControls();
                SetStatus($"{reason} editor account selected ({walletSessionManager.GetEditorWalletSelectionLabel()}). Press Connect.");
            }
            finally
            {
                _isSwitchingEditorWallet = false;
            }
        }

        private void HandleWalletConnectionChanged(bool isConnected)
        {
            if (isConnected)
            {
                CancelConnectButtonCooldown();
                SetStatus("Connected");
                return;
            }

            CancelConnectButtonCooldown();
            SetStatus("Connect your wallet to continue");
            SetButtonEnabled(true);
        }

        private void HandleWalletError(string errorMessage)
        {
            CancelConnectButtonCooldown();
            SetStatus(errorMessage);
            SetButtonEnabled(true);
        }

        private void StartConnectButtonCooldown()
        {
            CancelConnectButtonCooldown();
            _connectButtonCooldownCts = new CancellationTokenSource();
            RestoreConnectButtonAfterDelayAsync(_connectButtonCooldownCts.Token).Forget();
        }

        private async UniTaskVoid RestoreConnectButtonAfterDelayAsync(CancellationToken cancellationToken)
        {
            try
            {
                await UniTask.Delay(8000, cancellationToken: cancellationToken);
                if (walletSessionManager == null || walletSessionManager.IsWalletConnected)
                {
                    return;
                }

                SetStatus("Tap connect to try again");
                SetButtonEnabled(true);
            }
            catch (System.OperationCanceledException)
            {
                // expected when connection succeeds/fails before timeout
            }
        }

        private void CancelConnectButtonCooldown()
        {
            if (_connectButtonCooldownCts == null)
            {
                return;
            }

            _connectButtonCooldownCts.Cancel();
            _connectButtonCooldownCts.Dispose();
            _connectButtonCooldownCts = null;
        }

        private void SetStatus(string message)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = FormatStatus(message);
            }
        }

        private void SetButtonEnabled(bool enabled)
        {
            if (_connectButton != null)
            {
                _connectButton.SetEnabled(enabled);
            }
        }

        private string FormatStatus(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "Ready";
            }

#if UNITY_EDITOR
            if (walletSessionManager != null)
            {
                var slotLabel = walletSessionManager.GetEditorWalletSelectionLabel();
                if (!string.IsNullOrWhiteSpace(slotLabel))
                {
                    return $"{message}\n{slotLabel}";
                }
            }
#endif

            return message;
        }
    }
}
