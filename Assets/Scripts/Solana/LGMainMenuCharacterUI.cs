using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeekerDungeon.Solana
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class LGMainMenuCharacterUI : MonoBehaviour
    {
        [SerializeField] private LGMainMenuCharacterManager characterManager;

        private UIDocument _document;

        private VisualElement _createIdentityPanel;
        private VisualElement _existingIdentityPanel;
        private VisualElement _createContainer;
        private VisualElement _existingContainer;
        private VisualElement _skinNavPanel;
        private Label _skinNameLabel;
        private TextField _displayNameInput;
        private Label _statusLabel;
        private Label _existingNameLabel;
        private Button _previousSkinButton;
        private Button _nextSkinButton;
        private Button _confirmCreateButton;
        private Button _enterDungeonButton;
        private Button _disconnectButton;
        private TouchScreenKeyboard _mobileKeyboard;
        private bool _isApplyingKeyboardText;
        private VisualElement _boundRoot;
        private bool _isHandlersBound;

        private void Awake()
        {
            LGUiInputSystemGuard.EnsureEventSystemForRuntimeUi();
            _document = GetComponent<UIDocument>();

            if (characterManager == null)
            {
                characterManager = FindObjectOfType<LGMainMenuCharacterManager>();
            }
        }

        private void OnEnable()
        {
            TryRebindUi(force: true);
            if (characterManager != null)
            {
                characterManager.OnStateChanged += HandleStateChanged;
                characterManager.OnError += HandleError;
                HandleStateChanged(characterManager.GetCurrentState());
            }
        }

        private void OnDisable()
        {
            UnbindUiHandlers();

            if (characterManager != null)
            {
                characterManager.OnStateChanged -= HandleStateChanged;
                characterManager.OnError -= HandleError;
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

            _createIdentityPanel = root.Q<VisualElement>("create-identity-panel");
            _existingIdentityPanel = root.Q<VisualElement>("existing-identity-panel");
            _createContainer = root.Q<VisualElement>("create-character-container");
            _existingContainer = root.Q<VisualElement>("existing-character-container");
            _skinNavPanel = root.Q<VisualElement>("skin-nav-panel");
            _skinNameLabel = root.Q<Label>("selected-skin-label");
            _displayNameInput = root.Q<TextField>("display-name-input");
            _statusLabel = root.Q<Label>("menu-status-label");
            _existingNameLabel = root.Q<Label>("existing-display-name-label");
            _previousSkinButton = root.Q<Button>("btn-prev-skin");
            _nextSkinButton = root.Q<Button>("btn-next-skin");
            _confirmCreateButton = root.Q<Button>("btn-create-character");
            _enterDungeonButton = root.Q<Button>("btn-enter-dungeon");
            _disconnectButton = root.Q<Button>("btn-disconnect-wallet");

            if (_previousSkinButton != null)
            {
                _previousSkinButton.clicked += HandlePreviousSkinClicked;
            }

            if (_nextSkinButton != null)
            {
                _nextSkinButton.clicked += HandleNextSkinClicked;
            }

            if (_confirmCreateButton != null)
            {
                _confirmCreateButton.clicked += HandleCreateCharacterClicked;
            }

            if (_enterDungeonButton != null)
            {
                _enterDungeonButton.clicked += HandleEnterDungeonClicked;
            }

            if (_disconnectButton != null)
            {
                _disconnectButton.clicked += HandleDisconnectWalletClicked;
            }

            if (_displayNameInput != null)
            {
                _displayNameInput.RegisterValueChangedCallback(HandleDisplayNameChanged);
                _displayNameInput.RegisterCallback<PointerDownEvent>(HandleDisplayNamePointerDown);
            }

            _boundRoot = root;
            _isHandlersBound = true;
        }

        private void UnbindUiHandlers()
        {
            if (_previousSkinButton != null)
            {
                _previousSkinButton.clicked -= HandlePreviousSkinClicked;
            }

            if (_nextSkinButton != null)
            {
                _nextSkinButton.clicked -= HandleNextSkinClicked;
            }

            if (_confirmCreateButton != null)
            {
                _confirmCreateButton.clicked -= HandleCreateCharacterClicked;
            }

            if (_enterDungeonButton != null)
            {
                _enterDungeonButton.clicked -= HandleEnterDungeonClicked;
            }

            if (_disconnectButton != null)
            {
                _disconnectButton.clicked -= HandleDisconnectWalletClicked;
            }

            if (_displayNameInput != null)
            {
                _displayNameInput.UnregisterValueChangedCallback(HandleDisplayNameChanged);
                _displayNameInput.UnregisterCallback<PointerDownEvent>(HandleDisplayNamePointerDown);
            }

            _boundRoot = null;
            _isHandlersBound = false;
        }

        private void HandlePreviousSkinClicked()
        {
            characterManager?.SelectPreviousSkin();
        }

        private void HandleNextSkinClicked()
        {
            characterManager?.SelectNextSkin();
        }

        private void HandleCreateCharacterClicked()
        {
            CreateCharacterAsync().Forget();
        }

        private void HandleEnterDungeonClicked()
        {
            characterManager?.EnterDungeon();
        }

        private void HandleDisconnectWalletClicked()
        {
            characterManager?.DisconnectWallet();
        }

        private void HandleDisplayNameChanged(ChangeEvent<string> changeEvent)
        {
            if (_isApplyingKeyboardText)
            {
                return;
            }

            characterManager?.SetPendingDisplayName(changeEvent.newValue);
        }

        private void HandleDisplayNamePointerDown(PointerDownEvent pointerDownEvent)
        {
            if (!Application.isMobilePlatform || _displayNameInput == null || !_displayNameInput.enabledSelf)
            {
                return;
            }

            _displayNameInput.Focus();
            _mobileKeyboard = TouchScreenKeyboard.Open(
                _displayNameInput.value ?? string.Empty,
                TouchScreenKeyboardType.Default,
                autocorrection: false,
                multiline: false,
                secure: false,
                alert: false,
                textPlaceholder: "Enter your name");
        }

        private async UniTaskVoid CreateCharacterAsync()
        {
            if (characterManager == null)
            {
                return;
            }

            await characterManager.CreateCharacterAsync();
        }

        private void Update()
        {
            if (_boundRoot != _document?.rootVisualElement || !_isHandlersBound)
            {
                TryRebindUi();
            }

            if (!Application.isMobilePlatform || _mobileKeyboard == null)
            {
                return;
            }

            var keyboardText = _mobileKeyboard.text ?? string.Empty;
            if (_displayNameInput != null && _displayNameInput.value != keyboardText)
            {
                _isApplyingKeyboardText = true;
                _displayNameInput.SetValueWithoutNotify(keyboardText);
                _isApplyingKeyboardText = false;
                characterManager?.SetPendingDisplayName(keyboardText);
            }

            if (_mobileKeyboard.status == TouchScreenKeyboard.Status.Done ||
                _mobileKeyboard.status == TouchScreenKeyboard.Status.Canceled ||
                _mobileKeyboard.status == TouchScreenKeyboard.Status.LostFocus)
            {
                _mobileKeyboard = null;
            }
        }

        private void HandleStateChanged(MainMenuCharacterState state)
        {
            if (state == null)
            {
                return;
            }

            if (_skinNameLabel != null)
            {
                _skinNameLabel.text = state.SelectedSkinLabel;
            }

            if (_displayNameInput != null && _displayNameInput.value != state.DisplayName)
            {
                _displayNameInput.SetValueWithoutNotify(state.DisplayName);
            }

            if (_displayNameInput != null)
            {
                _displayNameInput.label = "Name";
                _displayNameInput.tooltip = "Onchain display name";
            }

            if (_existingNameLabel != null)
            {
                _existingNameLabel.text = $"Name: {state.DisplayName}";
            }

            if (_statusLabel != null)
            {
                _statusLabel.text = string.IsNullOrWhiteSpace(state.StatusMessage)
                    ? "Ready"
                    : state.StatusMessage;
            }

            if (_createIdentityPanel != null)
            {
                _createIdentityPanel.style.display = state.HasProfile ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_existingIdentityPanel != null)
            {
                _existingIdentityPanel.style.display = state.HasProfile ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_createContainer != null)
            {
                _createContainer.style.display = state.HasProfile ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_existingContainer != null)
            {
                _existingContainer.style.display = state.HasProfile ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_skinNavPanel != null)
            {
                _skinNavPanel.style.display = state.HasProfile ? DisplayStyle.None : DisplayStyle.Flex;
            }

            var canCreate = !state.IsBusy && state.IsReady && !state.HasProfile;
            var canEnter = !state.IsBusy && state.IsReady && state.HasProfile;

            _previousSkinButton?.SetEnabled(canCreate);
            _nextSkinButton?.SetEnabled(canCreate);
            _displayNameInput?.SetEnabled(canCreate);
            _confirmCreateButton?.SetEnabled(canCreate);
            _enterDungeonButton?.SetEnabled(canEnter);
            _disconnectButton?.SetEnabled(!state.IsBusy);
        }

        private void HandleError(string message)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = message;
            }
        }
    }
}
