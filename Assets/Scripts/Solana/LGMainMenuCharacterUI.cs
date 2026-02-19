using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using SeekerDungeon.Audio;
using SeekerDungeon.Dungeon;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeekerDungeon.Solana
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class LGMainMenuCharacterUI : MonoBehaviour
    {
        private const int SkinLabelMaxFontSize = 49;
        private const int SkinLabelMinFontSize = 24;
        private const float SpinnerDegreesPerSecond = 360f;

        [SerializeField] private LGMainMenuCharacterManager characterManager;
        [SerializeField] private ItemRegistry itemRegistry;
        [SerializeField] private Animator drinkAnimator;
        [SerializeField] private string drinkParameterName = "drink";
        [SerializeField] private float firstDrinkDelaySeconds = 2f;
        [SerializeField] private float drinkIntervalMinSeconds = 8f;
        [SerializeField] private float drinkIntervalMaxSeconds = 15f;
        [SerializeField] private Sprite settingsCogSprite;
        [SerializeField] private Sprite audioMutedSprite;
        [SerializeField] private Sprite audioUnmutedSprite;

        private UIDocument _document;

        private VisualElement _menuRoot;
        private VisualElement _createIdentityPanel;
        private VisualElement _existingIdentityPanel;
        private VisualElement _createContainer;
        private VisualElement _existingContainer;
        private VisualElement _topCenterLayer;
        private VisualElement _skinNavPanel;
        private Label _skinNameLabel;
        private TextField _displayNameInput;
        private Label _statusLabel;
        private Label _existingNameLabel;
        private Label _menuTotalScoreLabel;
        private VisualElement _menuStoragePanel;
        private VisualElement _menuStorageItemsContainer;
        private Label _menuStorageEmptyLabel;
        private Label _pickCharacterTitleLabel;
        private Label _walletSolBalanceLabel;
        private Label _walletSkrBalanceLabel;
        private Label _walletSessionActionLabel;
        private VisualElement _lowBalanceModalOverlay;
        private VisualElement _sessionSetupOverlay;
        private VisualElement _legacyResetOverlay;
        private VisualElement _settingsOverlay;
        private VisualElement _resetConfirmOverlay;
        private Label _sessionSetupMessageLabel;
        private Label _legacyResetMessageLabel;
        private Label _lowBalanceModalMessageLabel;
        private VisualElement _walletSessionIconInactive;
        private VisualElement _walletSessionIconActive;
        private VisualElement _loadingOverlay;
        private VisualElement _loadingSpinner;
        private Label _loadingLabel;
        private VisualElement _extractionSummaryOverlay;
        private VisualElement _extractionSummaryItemsContainer;
        private Label _extractionSummaryTitleLabel;
        private Label _extractionSummarySubtitleLabel;
        private Label _extractionSummaryLootPointsLabel;
        private Label _extractionSummaryTimePointsLabel;
        private Label _extractionSummaryRunPointsLabel;
        private Label _extractionSummaryTotalPointsLabel;
        private VisualElement _topLeftActions;
        private VisualElement _topRightActions;
        private VisualElement _bottomCenterLayer;
        private Button _previousSkinButton;
        private Button _nextSkinButton;
        private Button _confirmCreateButton;
        private Button _enterDungeonButton;
        private Button _openSettingsButton;
        private Button _sessionPillButton;
        private Button _sessionSetupActivateButton;
        private Button _legacyResetButton;
        private Button _lowBalanceModalDismissButton;
        private Button _lowBalanceTopUpButton;
        private Button _extractionSummaryContinueButton;
        private Button _settingsCloseButton;
        private Button _settingsDisconnectButton;
        private Button _settingsResetAccountButton;
        private Button _settingsMusicMuteButton;
        private Button _settingsSfxMuteButton;
        private Button _resetConfirmCancelButton;
        private Button _resetConfirmConfirmButton;
        private Slider _settingsMusicSlider;
        private Slider _settingsSfxSlider;
        private bool _isApplyingAudioSettings;
        private TouchScreenKeyboard _mobileKeyboard;
        private bool _isApplyingKeyboardText;
        private VisualElement _boundRoot;
        private bool _isHandlersBound;
        private bool _hasRevealedUi;
        private bool _isShowingExtractionSummary;
        private float _spinnerAngle;
        private TxIndicatorVisualController _txIndicatorController;
        private bool _drinkLoopStarted;
        private bool _stopDrinkLoop;

        private void Awake()
        {
            LGUiInputSystemGuard.EnsureEventSystemForRuntimeUi();
            _document = GetComponent<UIDocument>();

            if (characterManager == null)
            {
                characterManager = FindObjectOfType<LGMainMenuCharacterManager>();
            }

            ResolveItemRegistryIfMissing();
        }

        private void OnEnable()
        {
            _hasRevealedUi = false;
            _drinkLoopStarted = false;
            _stopDrinkLoop = false;
            TryRebindUi(force: true);
            if (characterManager != null)
            {
                characterManager.OnStateChanged += HandleStateChanged;
                characterManager.OnError += HandleError;
                HandleStateChanged(characterManager.GetCurrentState());
            }

            TryShowPendingExtractionSummary();
        }

        private void OnDisable()
        {
            _stopDrinkLoop = true;
            ShowSettingsOverlay(false);
            UnbindUiHandlers();
            _isShowingExtractionSummary = false;
            _txIndicatorController?.Dispose();
            _txIndicatorController = null;

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
            ResolveItemRegistryIfMissing();

            _createIdentityPanel = root.Q<VisualElement>("create-identity-panel");
            _menuRoot = root.Q<VisualElement>("menu-root");
            _existingIdentityPanel = root.Q<VisualElement>("existing-identity-panel");
            _createContainer = root.Q<VisualElement>("create-character-container");
            _existingContainer = root.Q<VisualElement>("existing-character-container");
            _topCenterLayer = root.Q<VisualElement>("top-center-layer");
            _skinNavPanel = root.Q<VisualElement>("skin-nav-row");
            _loadingOverlay = root.Q<VisualElement>("loading-overlay");
            _loadingSpinner = root.Q<VisualElement>("loading-spinner");
            _loadingLabel = root.Q<Label>("loading-label");
            _extractionSummaryOverlay = root.Q<VisualElement>("extraction-summary-overlay");
            _extractionSummaryItemsContainer = root.Q<VisualElement>("extraction-summary-items");
            _extractionSummaryTitleLabel = root.Q<Label>("extraction-summary-title");
            _extractionSummarySubtitleLabel = root.Q<Label>("extraction-summary-subtitle");
            _extractionSummaryLootPointsLabel = root.Q<Label>("extraction-summary-loot-points");
            _extractionSummaryTimePointsLabel = root.Q<Label>("extraction-summary-time-points");
            _extractionSummaryRunPointsLabel = root.Q<Label>("extraction-summary-run-points");
            _extractionSummaryTotalPointsLabel = root.Q<Label>("extraction-summary-total-points");
            _topLeftActions = root.Q<VisualElement>("top-left-actions");
            _topRightActions = root.Q<VisualElement>("top-right-actions");
            _bottomCenterLayer = root.Q<VisualElement>("bottom-center-layer");
            _skinNameLabel = root.Q<Label>("selected-skin-label");
            _displayNameInput = root.Q<TextField>("display-name-input");
            _statusLabel = root.Q<Label>("menu-status-label");
            _menuTotalScoreLabel = root.Q<Label>("menu-total-score-label");
            _menuStoragePanel = root.Q<VisualElement>("menu-storage-panel");
            _menuStorageItemsContainer = root.Q<VisualElement>("menu-storage-items");
            _menuStorageEmptyLabel = root.Q<Label>("menu-storage-empty");
            _existingNameLabel = root.Q<Label>("existing-display-name-label");
            _pickCharacterTitleLabel = root.Q<Label>("pick-character-title-label");
            _walletSolBalanceLabel = root.Q<Label>("wallet-sol-balance-label");
            _walletSkrBalanceLabel = root.Q<Label>("wallet-skr-balance-label");
            _walletSessionActionLabel = root.Q<Label>("wallet-session-action-label");
            _lowBalanceModalOverlay = root.Q<VisualElement>("low-balance-modal-overlay");
            _sessionSetupOverlay = root.Q<VisualElement>("session-setup-overlay");
            _legacyResetOverlay = root.Q<VisualElement>("legacy-reset-overlay");
            _settingsOverlay = root.Q<VisualElement>("settings-overlay");
            _resetConfirmOverlay = root.Q<VisualElement>("reset-confirm-overlay");
            _sessionSetupMessageLabel = root.Q<Label>("session-setup-message");
            _legacyResetMessageLabel = root.Q<Label>("legacy-reset-message");
            _lowBalanceModalMessageLabel = root.Q<Label>("low-balance-modal-message");
            _walletSessionIconInactive = root.Q<VisualElement>("wallet-session-icon-inactive");
            _walletSessionIconActive = root.Q<VisualElement>("wallet-session-icon-active");
            _previousSkinButton = root.Q<Button>("btn-prev-skin");
            _nextSkinButton = root.Q<Button>("btn-next-skin");
            _confirmCreateButton = root.Q<Button>("btn-create-character");
            _enterDungeonButton = root.Q<Button>("btn-enter-dungeon");
            _openSettingsButton = root.Q<Button>("btn-open-settings");
            _sessionPillButton = root.Q<Button>("btn-session-pill");
            _sessionSetupActivateButton = root.Q<Button>("btn-session-setup-activate");
            _legacyResetButton = root.Q<Button>("btn-legacy-reset");
            _lowBalanceModalDismissButton = root.Q<Button>("btn-low-balance-dismiss");
            _lowBalanceTopUpButton = root.Q<Button>("btn-low-balance-topup");
            _extractionSummaryContinueButton = root.Q<Button>("btn-extraction-summary-continue");
            _settingsCloseButton = root.Q<Button>("btn-settings-close");
            _settingsDisconnectButton = root.Q<Button>("btn-settings-disconnect");
            _settingsResetAccountButton = root.Q<Button>("btn-settings-reset-account");
            _settingsMusicMuteButton = root.Q<Button>("btn-settings-music-mute");
            _settingsSfxMuteButton = root.Q<Button>("btn-settings-sfx-mute");
            _resetConfirmCancelButton = root.Q<Button>("btn-reset-confirm-cancel");
            _resetConfirmConfirmButton = root.Q<Button>("btn-reset-confirm-confirm");
            _settingsMusicSlider = root.Q<Slider>("settings-music-slider");
            _settingsSfxSlider = root.Q<Slider>("settings-sfx-slider");

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

            if (_openSettingsButton != null)
            {
                _openSettingsButton.clicked += HandleOpenSettingsClicked;
            }

            if (_sessionPillButton != null)
            {
                _sessionPillButton.clicked += HandleEnableSessionClicked;
            }

            if (_sessionSetupActivateButton != null)
            {
                _sessionSetupActivateButton.clicked += HandleEnableSessionClicked;
            }

            if (_legacyResetButton != null)
            {
                _legacyResetButton.clicked += HandleLegacyResetClicked;
            }

            if (_lowBalanceModalDismissButton != null)
            {
                _lowBalanceModalDismissButton.clicked += HandleLowBalanceDismissClicked;
            }

            if (_lowBalanceTopUpButton != null)
            {
                _lowBalanceTopUpButton.clicked += HandleLowBalanceTopUpClicked;
            }

            if (_extractionSummaryContinueButton != null)
            {
                _extractionSummaryContinueButton.clicked += HandleExtractionSummaryContinueClicked;
            }

            if (_settingsCloseButton != null)
            {
                _settingsCloseButton.clicked += HandleSettingsCloseClicked;
            }

            if (_settingsDisconnectButton != null)
            {
                _settingsDisconnectButton.clicked += HandleSettingsDisconnectClicked;
            }

            if (_settingsResetAccountButton != null)
            {
                _settingsResetAccountButton.clicked += HandleSettingsResetAccountClicked;
            }

            if (_settingsMusicMuteButton != null)
            {
                _settingsMusicMuteButton.clicked += HandleSettingsMusicMuteClicked;
            }

            if (_settingsSfxMuteButton != null)
            {
                _settingsSfxMuteButton.clicked += HandleSettingsSfxMuteClicked;
            }

            if (_resetConfirmCancelButton != null)
            {
                _resetConfirmCancelButton.clicked += HandleResetConfirmCancelClicked;
            }

            if (_resetConfirmConfirmButton != null)
            {
                _resetConfirmConfirmButton.clicked += HandleResetConfirmConfirmClicked;
            }

            if (_settingsMusicSlider != null)
            {
                _settingsMusicSlider.RegisterValueChangedCallback(HandleMusicSliderChanged);
            }

            if (_settingsSfxSlider != null)
            {
                _settingsSfxSlider.RegisterValueChangedCallback(HandleSfxSliderChanged);
            }

            if (_displayNameInput != null)
            {
                _displayNameInput.RegisterValueChangedCallback(HandleDisplayNameChanged);
                _displayNameInput.RegisterCallback<PointerDownEvent>(HandleDisplayNamePointerDown);
            }

            _boundRoot = root;
            _isHandlersBound = true;
            _txIndicatorController ??= new TxIndicatorVisualController();
            _txIndicatorController.Bind(root);
            ApplySettingsButtonVisual();
            RefreshAudioSettingsUiFromManager();
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

            if (_openSettingsButton != null)
            {
                _openSettingsButton.clicked -= HandleOpenSettingsClicked;
            }

            if (_sessionPillButton != null)
            {
                _sessionPillButton.clicked -= HandleEnableSessionClicked;
            }

            if (_sessionSetupActivateButton != null)
            {
                _sessionSetupActivateButton.clicked -= HandleEnableSessionClicked;
            }

            if (_legacyResetButton != null)
            {
                _legacyResetButton.clicked -= HandleLegacyResetClicked;
            }

            if (_lowBalanceModalDismissButton != null)
            {
                _lowBalanceModalDismissButton.clicked -= HandleLowBalanceDismissClicked;
            }

            if (_lowBalanceTopUpButton != null)
            {
                _lowBalanceTopUpButton.clicked -= HandleLowBalanceTopUpClicked;
            }

            if (_extractionSummaryContinueButton != null)
            {
                _extractionSummaryContinueButton.clicked -= HandleExtractionSummaryContinueClicked;
            }

            if (_settingsCloseButton != null)
            {
                _settingsCloseButton.clicked -= HandleSettingsCloseClicked;
            }

            if (_settingsDisconnectButton != null)
            {
                _settingsDisconnectButton.clicked -= HandleSettingsDisconnectClicked;
            }

            if (_settingsResetAccountButton != null)
            {
                _settingsResetAccountButton.clicked -= HandleSettingsResetAccountClicked;
            }

            if (_settingsMusicMuteButton != null)
            {
                _settingsMusicMuteButton.clicked -= HandleSettingsMusicMuteClicked;
            }

            if (_settingsSfxMuteButton != null)
            {
                _settingsSfxMuteButton.clicked -= HandleSettingsSfxMuteClicked;
            }

            if (_resetConfirmCancelButton != null)
            {
                _resetConfirmCancelButton.clicked -= HandleResetConfirmCancelClicked;
            }

            if (_resetConfirmConfirmButton != null)
            {
                _resetConfirmConfirmButton.clicked -= HandleResetConfirmConfirmClicked;
            }

            if (_settingsMusicSlider != null)
            {
                _settingsMusicSlider.UnregisterValueChangedCallback(HandleMusicSliderChanged);
            }

            if (_settingsSfxSlider != null)
            {
                _settingsSfxSlider.UnregisterValueChangedCallback(HandleSfxSliderChanged);
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
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Nav);
            GameAudioManager.Instance?.PlayWorld(WorldSfxId.CharacterSwap, transform.position);
            characterManager?.SelectPreviousSkin();
        }

        private void HandleNextSkinClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Nav);
            GameAudioManager.Instance?.PlayWorld(WorldSfxId.CharacterSwap, transform.position);
            characterManager?.SelectNextSkin();
        }

        private void HandleCreateCharacterClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Primary);
            CreateCharacterAsync().Forget();
        }

        private void HandleEnterDungeonClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Primary);
            characterManager?.EnterDungeon();
        }

        private void HandleOpenSettingsClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            ShowSettingsOverlay(true);
            RefreshAudioSettingsUiFromManager();
        }

        private void HandleEnableSessionClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Primary);
            characterManager?.EnsureSessionReadyFromMenu();
        }

        private void HandleLowBalanceDismissClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            characterManager?.DismissLowBalanceModal();
        }

        private void HandleLowBalanceTopUpClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Primary);
            characterManager?.RequestDevnetTopUpFromMenu();
        }

        private void HandleLegacyResetClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Danger);
            characterManager?.RequestLegacyAccountResetFromMenu();
        }

        private void HandleSettingsCloseClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            ShowSettingsOverlay(false);
        }

        private void HandleSettingsDisconnectClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Danger);
            ShowSettingsOverlay(false);
            characterManager?.DisconnectWallet();
        }

        private void HandleSettingsResetAccountClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Danger);
            ShowResetConfirmOverlay(true);
        }

        private void HandleResetConfirmCancelClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            ShowResetConfirmOverlay(false);
        }

        private void HandleResetConfirmConfirmClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Danger);
            ShowResetConfirmOverlay(false);
            ShowSettingsOverlay(false);
            characterManager?.RequestLegacyAccountResetFromMenu();
            characterManager?.RequestLegacyAccountResetFromMenu();
        }

        private void HandleSettingsMusicMuteClicked()
        {
            if (_isApplyingAudioSettings)
            {
                return;
            }

            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            var audioManager = GameAudioManager.Instance;
            if (audioManager == null)
            {
                return;
            }

            audioManager.SetMusicEnabled(!audioManager.IsMusicEnabled);
            RefreshAudioSettingsUiFromManager();
        }

        private void HandleSettingsSfxMuteClicked()
        {
            if (_isApplyingAudioSettings)
            {
                return;
            }

            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            var audioManager = GameAudioManager.Instance;
            if (audioManager == null)
            {
                return;
            }

            audioManager.SetSfxEnabled(!audioManager.IsSfxEnabled);
            RefreshAudioSettingsUiFromManager();
        }

        private void HandleMusicSliderChanged(ChangeEvent<float> changeEvent)
        {
            if (_isApplyingAudioSettings)
            {
                return;
            }

            GameAudioManager.Instance?.SetMusicVolume(changeEvent.newValue);
        }

        private void HandleSfxSliderChanged(ChangeEvent<float> changeEvent)
        {
            if (_isApplyingAudioSettings)
            {
                return;
            }

            GameAudioManager.Instance?.SetSfxVolume(changeEvent.newValue);
        }

        private void RefreshAudioSettingsUiFromManager()
        {
            var audioManager = GameAudioManager.Instance;
            if (audioManager == null)
            {
                return;
            }

            _isApplyingAudioSettings = true;
            try
            {
                _settingsMusicSlider?.SetValueWithoutNotify(audioManager.MusicVolume);
                _settingsSfxSlider?.SetValueWithoutNotify(audioManager.SfxVolume);

                _settingsMusicSlider?.SetEnabled(audioManager.IsMusicEnabled);
                _settingsSfxSlider?.SetEnabled(audioManager.IsSfxEnabled);
                ApplyAudioToggleButtonVisual(_settingsMusicMuteButton, audioManager.IsMusicEnabled, "Music");
                ApplyAudioToggleButtonVisual(_settingsSfxMuteButton, audioManager.IsSfxEnabled, "SFX");
            }
            finally
            {
                _isApplyingAudioSettings = false;
            }
        }

        private void ShowSettingsOverlay(bool show)
        {
            if (_settingsOverlay != null)
            {
                _settingsOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                _settingsOverlay.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
                _settingsOverlay.style.opacity = show ? 1f : 0f;
            }

            if (!show)
            {
                ShowResetConfirmOverlay(false);
            }
        }

        private void ShowResetConfirmOverlay(bool show)
        {
            if (_resetConfirmOverlay != null)
            {
                _resetConfirmOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                _resetConfirmOverlay.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
                _resetConfirmOverlay.style.opacity = show ? 1f : 0f;
            }
        }

        private void ApplySettingsButtonVisual()
        {
            if (_openSettingsButton == null)
            {
                return;
            }

            _openSettingsButton.style.backgroundImage = settingsCogSprite != null
                ? new StyleBackground(settingsCogSprite)
                : new StyleBackground((Sprite)null);
            _openSettingsButton.tooltip = "Settings";
        }

        private void ApplyAudioToggleButtonVisual(Button button, bool isEnabled, string channelName)
        {
            if (button == null)
            {
                return;
            }

            var icon = isEnabled ? audioUnmutedSprite : audioMutedSprite;
            button.style.backgroundImage = icon != null
                ? new StyleBackground(icon)
                : new StyleBackground((Sprite)null);
            button.text = icon == null ? (isEnabled ? "On" : "Off") : string.Empty;
            button.tooltip = isEnabled
                ? $"{channelName} enabled (tap to mute)"
                : $"{channelName} muted (tap to unmute)";
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

            _txIndicatorController?.Tick(Time.unscaledDeltaTime);

            // Animate loading spinner rotation
            if (_loadingSpinner != null &&
                _loadingOverlay != null &&
                _loadingOverlay.resolvedStyle.display == DisplayStyle.Flex)
            {
                _spinnerAngle = (_spinnerAngle + SpinnerDegreesPerSecond * Time.unscaledDeltaTime) % 360f;
                _loadingSpinner.style.rotate = new Rotate(_spinnerAngle);
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

            // ── Loading / Ready state ──
            var isLoading = !state.IsReady;
            if (_loadingOverlay != null)
            {
                _loadingOverlay.style.display = isLoading ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_loadingLabel != null && isLoading)
            {
                _loadingLabel.text = string.IsNullOrWhiteSpace(state.StatusMessage)
                    ? "Loading..."
                    : state.StatusMessage;
            }

            // Hide all interactive chrome while data is loading
            if (_topLeftActions != null)
            {
                _topLeftActions.style.display = isLoading ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_topRightActions != null)
            {
                _topRightActions.style.display = isLoading ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_topCenterLayer != null && isLoading)
            {
                _topCenterLayer.style.display = DisplayStyle.None;
            }

            if (_bottomCenterLayer != null)
            {
                _bottomCenterLayer.style.display = isLoading ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_pickCharacterTitleLabel != null && isLoading)
            {
                _pickCharacterTitleLabel.style.display = DisplayStyle.None;
            }

            if (isLoading)
            {
                return;
            }

            StartDrinkLoopIfNeeded();

            // ── Normal (ready) state handling ──
            if (_skinNameLabel != null)
            {
                _skinNameLabel.style.display = DisplayStyle.None;
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
                _existingNameLabel.text = $"Name: {state.PlayerDisplayName}";
            }

            if (_menuTotalScoreLabel != null)
            {
                var shouldShowTotalScore = state.TotalScore > 0;
                _menuTotalScoreLabel.style.display = shouldShowTotalScore ? DisplayStyle.Flex : DisplayStyle.None;
                _menuTotalScoreLabel.text = $"Total Score: {state.TotalScore}";
            }

            var isLockedProfile = state.HasProfile && !state.HasUnsavedProfileChanges;
            if (_menuStoragePanel != null)
            {
                _menuStoragePanel.style.display = isLockedProfile ? DisplayStyle.Flex : DisplayStyle.None;
            }
            RefreshStoredCollection(state.StoredCollectionItems);

            var previewController = characterManager?.PreviewPlayerController;
            previewController?.SetDisplayName(state.PlayerDisplayName);
            previewController?.SetDisplayNameVisible(state.IsReady);

            if (_pickCharacterTitleLabel != null)
            {
                _pickCharacterTitleLabel.style.display = (!isLockedProfile && state.IsReady)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_walletSolBalanceLabel != null)
            {
                _walletSolBalanceLabel.text = state.SolBalanceText;
            }

            if (_walletSkrBalanceLabel != null)
            {
                _walletSkrBalanceLabel.text = state.SkrBalanceText;
            }

            if (_walletSessionActionLabel != null)
            {
                _walletSessionActionLabel.text = state.IsSessionReady
                    ? "Session Active"
                    : "Session Inactive";
            }

            if (_walletSessionIconInactive != null)
            {
                _walletSessionIconInactive.style.display = state.IsSessionReady ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_walletSessionIconActive != null)
            {
                _walletSessionIconActive.style.display = state.IsSessionReady ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_statusLabel != null)
            {
                _statusLabel.text = string.IsNullOrWhiteSpace(state.StatusMessage)
                    ? "Ready"
                    : state.StatusMessage;
            }

            if (_lowBalanceModalOverlay != null)
            {
                _lowBalanceModalOverlay.style.display = state.IsLowBalanceModalVisible
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            var shouldShowSessionSetupOverlay =
                state.HasProfile &&
                !state.IsSessionReady &&
                !state.IsLowBalanceBlocking &&
                !state.IsLegacyResetRequired;
            if (_sessionSetupOverlay != null)
            {
                _sessionSetupOverlay.style.display = shouldShowSessionSetupOverlay
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_legacyResetOverlay != null)
            {
                _legacyResetOverlay.style.display = state.IsLegacyResetRequired
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_sessionSetupMessageLabel != null)
            {
                _sessionSetupMessageLabel.text = state.IsBusy
                    ? "Please confirm in your wallet to activate your gameplay session."
                    : "Activate your gameplay session to enable smoother actions with fewer wallet prompts.";
            }

            if (_legacyResetMessageLabel != null)
            {
                _legacyResetMessageLabel.text = string.IsNullOrWhiteSpace(state.LegacyResetMessage)
                    ? "Your account data is outdated and must be reset."
                    : state.LegacyResetMessage;
            }

            if (_lowBalanceModalMessageLabel != null)
            {
                _lowBalanceModalMessageLabel.text = state.LowBalanceModalMessage ?? string.Empty;
            }

            if (_lowBalanceTopUpButton != null)
            {
                _lowBalanceTopUpButton.style.display =
                    state.IsDevnetRuntime && state.IsLowBalanceBlocking
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
            }

            if (_createIdentityPanel != null)
            {
                _createIdentityPanel.style.display = DisplayStyle.None;
            }

            if (_existingIdentityPanel != null)
            {
                _existingIdentityPanel.style.display = DisplayStyle.None;
            }

            if (_createContainer != null)
            {
                _createContainer.style.display = isLockedProfile ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_existingContainer != null)
            {
                _existingContainer.style.display = isLockedProfile ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_skinNavPanel != null)
            {
                _skinNavPanel.style.display = isLockedProfile ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_previousSkinButton != null)
            {
                _previousSkinButton.style.display = isLockedProfile ? DisplayStyle.None : DisplayStyle.Flex;
                _previousSkinButton.style.visibility = isLockedProfile ? Visibility.Hidden : Visibility.Visible;
            }

            if (_nextSkinButton != null)
            {
                _nextSkinButton.style.display = isLockedProfile ? DisplayStyle.None : DisplayStyle.Flex;
                _nextSkinButton.style.visibility = isLockedProfile ? Visibility.Hidden : Visibility.Visible;
            }

            if (_topCenterLayer != null)
            {
                _topCenterLayer.style.display = DisplayStyle.Flex;
                _topCenterLayer.style.top = Length.Percent(isLockedProfile ? 32f : 44f);
            }

            var canEditProfile = !state.IsBusy && state.IsReady;
            var canEnter = !state.IsBusy && state.IsReady && isLockedProfile;

            if (_confirmCreateButton != null)
            {
                _confirmCreateButton.text = state.HasProfile
                    ? "Save Character"
                    : "Create Character";
            }

            if (_enterDungeonButton != null)
            {
                _enterDungeonButton.text = state.IsInActiveDungeonRun
                    ? "Resume Dungeon"
                    : "Enter the Dungeon";
            }

            if (_legacyResetButton != null)
            {
                _legacyResetButton.text = state.IsLegacyResetConfirmArmed
                    ? "CONFIRM RESET ACCOUNT"
                    : "RESET ACCOUNT";
            }

            _previousSkinButton?.SetEnabled(canEditProfile);
            _nextSkinButton?.SetEnabled(canEditProfile);
            _displayNameInput?.SetEnabled(false);
            _confirmCreateButton?.SetEnabled(
                canEditProfile &&
                !state.IsLegacyResetRequired &&
                (!state.HasProfile || state.HasUnsavedProfileChanges));
            _enterDungeonButton?.SetEnabled(canEnter && !state.IsLegacyResetRequired);
            _openSettingsButton?.SetEnabled(!state.IsBusy);
            if (_sessionPillButton != null)
            {
                _sessionPillButton.style.display = state.HasProfile ? DisplayStyle.Flex : DisplayStyle.None;
                _sessionPillButton.SetEnabled(state.IsReady && !state.IsBusy && state.HasProfile);
            }
            _sessionSetupActivateButton?.SetEnabled(
                !state.IsSessionReady &&
                state.IsReady &&
                !state.IsBusy &&
                state.HasProfile);
            _legacyResetButton?.SetEnabled(
                state.IsLegacyResetRequired &&
                state.CanSelfResetLegacyAccount &&
                !state.IsResettingLegacyAccount &&
                !state.IsBusy);
            _settingsDisconnectButton?.SetEnabled(!state.IsBusy);
            _settingsResetAccountButton?.SetEnabled(!state.IsBusy);
            _settingsCloseButton?.SetEnabled(true);
            _resetConfirmCancelButton?.SetEnabled(true);
            _resetConfirmConfirmButton?.SetEnabled(!state.IsBusy);
            _lowBalanceModalDismissButton?.SetEnabled(!state.IsRequestingDevnetTopUp);
            _lowBalanceTopUpButton?.SetEnabled(!state.IsRequestingDevnetTopUp && !state.IsBusy);

            TryShowPendingExtractionSummary();
        }

        private void RefreshStoredCollection(IReadOnlyList<CollectionItemView> collectionItems)
        {
            if (_menuStorageItemsContainer == null)
            {
                return;
            }

            _menuStorageItemsContainer.Clear();
            var hasItems = collectionItems != null && collectionItems.Count > 0;
            if (_menuStorageEmptyLabel != null)
            {
                _menuStorageEmptyLabel.style.display = hasItems ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (!hasItems)
            {
                return;
            }

            for (var itemIndex = 0; itemIndex < collectionItems.Count; itemIndex += 1)
            {
                var collectionItem = collectionItems[itemIndex];
                if (collectionItem == null || collectionItem.Amount == 0)
                {
                    continue;
                }

                var row = new VisualElement();
                row.AddToClassList("menu-storage-row");

                var icon = new VisualElement();
                icon.AddToClassList("menu-storage-item-icon");
                var iconSprite = ResolveItemIcon(collectionItem.ItemId);
                if (iconSprite != null)
                {
                    icon.style.backgroundImage = new StyleBackground(iconSprite);
                }
                row.Add(icon);

                var itemNameLabel = new Label(ResolveItemDisplayName(collectionItem.ItemId));
                itemNameLabel.AddToClassList("menu-storage-item-name");
                row.Add(itemNameLabel);

                var countLabel = new Label($"x{collectionItem.Amount}");
                countLabel.AddToClassList("menu-storage-item-count");
                row.Add(countLabel);

                _menuStorageItemsContainer.Add(row);
            }
        }

        private void StartDrinkLoopIfNeeded()
        {
            if (_drinkLoopStarted)
            {
                return;
            }

            _drinkLoopStarted = true;
            DrinkLoopAsync().Forget();
        }

        private async UniTaskVoid DrinkLoopAsync()
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(Mathf.Max(0f, firstDrinkDelaySeconds)),
                cancellationToken: this.GetCancellationTokenOnDestroy());

            while (!_stopDrinkLoop && isActiveAndEnabled)
            {
                TriggerDrinkParameter();

                var min = Mathf.Max(0.1f, drinkIntervalMinSeconds);
                var max = Mathf.Max(min, drinkIntervalMaxSeconds);
                var delaySeconds = UnityEngine.Random.Range(min, max);
                await UniTask.Delay(
                    TimeSpan.FromSeconds(delaySeconds),
                    cancellationToken: this.GetCancellationTokenOnDestroy());
            }
        }

        private void TriggerDrinkParameter()
        {
            var animator = drinkAnimator;
            if (animator == null || string.IsNullOrWhiteSpace(drinkParameterName))
            {
                return;
            }

            var parameters = animator.parameters;
            for (var index = 0; index < parameters.Length; index += 1)
            {
                var parameter = parameters[index];
                if (!string.Equals(parameter.name, drinkParameterName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (parameter.type == AnimatorControllerParameterType.Trigger)
                {
                    animator.SetTrigger(drinkParameterName);
                }
                else if (parameter.type == AnimatorControllerParameterType.Bool)
                {
                    animator.SetBool(drinkParameterName, true);
                    animator.SetBool(drinkParameterName, false);
                }

                return;
            }
        }

        private void TryShowPendingExtractionSummary()
        {
            if (_isShowingExtractionSummary)
            {
                return;
            }

            if (!DungeonExtractionSummaryStore.HasPendingSummary)
            {
                return;
            }

            var summary = DungeonExtractionSummaryStore.ConsumePending();
            if (summary == null)
            {
                return;
            }

            ShowExtractionSummaryAsync(summary).Forget();
        }

        private async UniTaskVoid ShowExtractionSummaryAsync(DungeonExtractionSummary summary)
        {
            if (_extractionSummaryOverlay == null)
            {
                return;
            }

            var isDeathRun = summary != null && summary.RunEndReason == DungeonRunEndReason.Death;
            if (!isDeathRun)
            {
                GameAudioManager.Instance?.PlayStinger(StingerSfxId.ExtractionSuccess);
            }

            _isShowingExtractionSummary = true;
            _extractionSummaryOverlay.style.display = DisplayStyle.Flex;

            if (_extractionSummaryContinueButton != null)
            {
                _extractionSummaryContinueButton.style.display = DisplayStyle.None;
            }

            SetLabelText(
                _extractionSummaryTitleLabel,
                isDeathRun ? "YOU DIED" : "YOU MADE IT OUT ALIVE!");
            SetLabelText(
                _extractionSummarySubtitleLabel,
                isDeathRun ? "Recovering after a failed run..." : "Cashing in recovered loot...");
            SetLabelText(_extractionSummaryLootPointsLabel, "+0");
            SetLabelText(_extractionSummaryTimePointsLabel, "+0");
            SetLabelText(_extractionSummaryRunPointsLabel, "+0");
            SetLabelText(_extractionSummaryTotalPointsLabel, "0");

            if (_extractionSummaryItemsContainer != null)
            {
                _extractionSummaryItemsContainer.Clear();
            }

            var rowPointsLabels = new List<Label>();
            var itemStackScores = new List<ulong>();
            var rowVisuals = new List<VisualElement>();
            for (var itemIndex = 0; itemIndex < summary.Items.Count; itemIndex += 1)
            {
                var item = summary.Items[itemIndex];
                if (item == null)
                {
                    continue;
                }

                var row = new VisualElement();
                row.AddToClassList("extraction-summary-item-row");
                row.style.opacity = 0f;

                var icon = new VisualElement();
                icon.AddToClassList("extraction-summary-item-icon");
                var iconSprite = ResolveItemIcon(item.ItemId);
                if (iconSprite != null)
                {
                    icon.style.backgroundImage = new StyleBackground(iconSprite);
                }
                row.Add(icon);

                var nameLabel = new Label(ResolveItemDisplayName(item.ItemId));
                nameLabel.AddToClassList("extraction-summary-item-name");
                row.Add(nameLabel);

                var amountLabel = new Label($"x{item.Amount}");
                amountLabel.AddToClassList("extraction-summary-item-amount");
                row.Add(amountLabel);

                var pointsLabel = new Label(isDeathRun ? "LOST" : "+0");
                pointsLabel.AddToClassList("extraction-summary-item-points");
                row.Add(pointsLabel);

                _extractionSummaryItemsContainer?.Add(row);
                rowPointsLabels.Add(pointsLabel);
                itemStackScores.Add(item.StackScore);
                rowVisuals.Add(row);
            }

            if (rowVisuals.Count == 0 && _extractionSummaryItemsContainer != null)
            {
                var emptyRow = new VisualElement();
                emptyRow.AddToClassList("extraction-summary-item-row");

                var emptyLabel = new Label(
                    isDeathRun
                        ? "No death-loss loot was carried."
                        : "No scored loot extracted this run.");
                emptyLabel.AddToClassList("extraction-summary-item-name");
                emptyLabel.style.flexGrow = 1f;
                emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                emptyLabel.style.opacity = 0.86f;

                emptyRow.Add(emptyLabel);
                _extractionSummaryItemsContainer.Add(emptyRow);
            }

            ulong runningLootScore = 0;
            for (var rowIndex = 0; rowIndex < rowPointsLabels.Count; rowIndex += 1)
            {
                var rowVisual = rowVisuals[rowIndex];
                if (rowVisual != null)
                {
                    rowVisual.style.opacity = 1f;
                }

                if (!isDeathRun)
                {
                    await AnimateNumberLabelAsync(rowPointsLabels[rowIndex], 0, itemStackScores[rowIndex], "+");
                    runningLootScore += itemStackScores[rowIndex];
                    SetLabelText(_extractionSummaryLootPointsLabel, $"+{runningLootScore}");
                }
                await UniTask.Delay(TimeSpan.FromMilliseconds(90));
            }

            await UniTask.Delay(TimeSpan.FromMilliseconds(120));
            await AnimateNumberLabelAsync(_extractionSummaryTimePointsLabel, 0, summary.TimeScore, "+");
            await UniTask.Delay(TimeSpan.FromMilliseconds(120));
            await AnimateNumberLabelAsync(_extractionSummaryRunPointsLabel, 0, summary.RunScore, "+");
            await UniTask.Delay(TimeSpan.FromMilliseconds(120));
            await AnimateNumberLabelAsync(_extractionSummaryTotalPointsLabel, 0, summary.TotalScoreAfterRun, string.Empty);

            SetLabelText(
                _extractionSummarySubtitleLabel,
                isDeathRun
                    ? "Run failed. Loot lost. No score gained."
                    : summary.RunScore > 0
                        ? "Run complete. Your score has been updated."
                        : "Run complete. No score gained this run.");
            if (_extractionSummaryContinueButton != null)
            {
                _extractionSummaryContinueButton.style.display = DisplayStyle.Flex;
                _extractionSummaryContinueButton.Focus();
            }
        }

        private async UniTask AnimateNumberLabelAsync(Label label, ulong from, ulong to, string prefix)
        {
            if (label == null)
            {
                return;
            }

            const int steps = 16;
            if (to <= from || steps <= 1)
            {
                SetLabelText(label, $"{prefix}{to}");
                return;
            }

            for (var step = 1; step <= steps; step += 1)
            {
                var progress = step / (float)steps;
                var value = from + (ulong)Mathf.RoundToInt((to - from) * progress);
                SetLabelText(label, $"{prefix}{value}");
                await UniTask.Delay(TimeSpan.FromMilliseconds(18));
            }

            SetLabelText(label, $"{prefix}{to}");
        }

        private void HandleExtractionSummaryContinueClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);

            if (_extractionSummaryOverlay != null)
            {
                _extractionSummaryOverlay.style.display = DisplayStyle.None;
            }

            _isShowingExtractionSummary = false;
        }

        private static void SetLabelText(Label label, string text)
        {
            if (label == null)
            {
                return;
            }

            label.text = text ?? string.Empty;
        }

        private void ResolveItemRegistryIfMissing()
        {
            if (itemRegistry != null)
            {
                return;
            }

            var registries = Resources.FindObjectsOfTypeAll<ItemRegistry>();
            if (registries != null && registries.Length > 0)
            {
                itemRegistry = registries[0];
            }
        }

        private Sprite ResolveItemIcon(ItemId itemId)
        {
            ResolveItemRegistryIfMissing();
            return itemRegistry != null ? itemRegistry.GetIcon(itemId) : null;
        }

        private string ResolveItemDisplayName(ItemId itemId)
        {
            ResolveItemRegistryIfMissing();

            if (itemRegistry != null)
            {
                var displayName = itemRegistry.GetDisplayName(itemId);
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    return displayName;
                }
            }

            return HumanizeEnumLikeName(itemId.ToString());
        }

        private static string HumanizeEnumLikeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "Unknown Item";
            }

            var builder = new StringBuilder(raw.Length + 8);
            for (var index = 0; index < raw.Length; index += 1)
            {
                var current = raw[index];
                if (index > 0)
                {
                    var previous = raw[index - 1];
                    var next = index + 1 < raw.Length ? raw[index + 1] : '\0';
                    var upperAfterLower = char.IsUpper(current) && char.IsLower(previous);
                    var upperBeforeLower = char.IsUpper(current) && char.IsUpper(previous) && char.IsLower(next);
                    var digitAfterLetter = char.IsDigit(current) && char.IsLetter(previous);
                    if (upperAfterLower || upperBeforeLower || digitAfterLetter)
                    {
                        builder.Append(' ');
                    }
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        private void ApplySkinLabelSizing(string labelText)
        {
            if (_skinNameLabel == null)
            {
                return;
            }

            var safeText = string.IsNullOrWhiteSpace(labelText) ? "Unknown Skin" : labelText.Trim();
            _skinNameLabel.text = safeText;

            var length = safeText.Length;
            var clampedLength = Mathf.Clamp(length, 8, 24);
            var t = (clampedLength - 8f) / 16f;
            var fontSize = Mathf.RoundToInt(Mathf.Lerp(SkinLabelMaxFontSize, SkinLabelMinFontSize, t));
            _skinNameLabel.style.fontSize = Mathf.Clamp(fontSize, SkinLabelMinFontSize, SkinLabelMaxFontSize);
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
