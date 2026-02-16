using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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
        private Label _pickCharacterTitleLabel;
        private Label _walletSolBalanceLabel;
        private Label _walletSkrBalanceLabel;
        private Label _walletSessionActionLabel;
        private VisualElement _lowBalanceModalOverlay;
        private VisualElement _sessionSetupOverlay;
        private Label _sessionSetupMessageLabel;
        private Label _lowBalanceModalMessageLabel;
        private VisualElement _walletSessionIconInactive;
        private VisualElement _walletSessionIconActive;
        private VisualElement _loadingOverlay;
        private VisualElement _loadingSpinner;
        private Label _loadingLabel;
        private VisualElement _extractionSummaryOverlay;
        private VisualElement _extractionSummaryItemsContainer;
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
        private Button _disconnectButton;
        private Button _sessionPillButton;
        private Button _sessionSetupActivateButton;
        private Button _lowBalanceModalDismissButton;
        private Button _lowBalanceTopUpButton;
        private Button _extractionSummaryContinueButton;
        private TouchScreenKeyboard _mobileKeyboard;
        private bool _isApplyingKeyboardText;
        private VisualElement _boundRoot;
        private bool _isHandlersBound;
        private bool _hasRevealedUi;
        private bool _isShowingExtractionSummary;
        private float _spinnerAngle;

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
            _hasRevealedUi = false;
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
            UnbindUiHandlers();
            _isShowingExtractionSummary = false;

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
            _existingNameLabel = root.Q<Label>("existing-display-name-label");
            _pickCharacterTitleLabel = root.Q<Label>("pick-character-title-label");
            _walletSolBalanceLabel = root.Q<Label>("wallet-sol-balance-label");
            _walletSkrBalanceLabel = root.Q<Label>("wallet-skr-balance-label");
            _walletSessionActionLabel = root.Q<Label>("wallet-session-action-label");
            _lowBalanceModalOverlay = root.Q<VisualElement>("low-balance-modal-overlay");
            _sessionSetupOverlay = root.Q<VisualElement>("session-setup-overlay");
            _sessionSetupMessageLabel = root.Q<Label>("session-setup-message");
            _lowBalanceModalMessageLabel = root.Q<Label>("low-balance-modal-message");
            _walletSessionIconInactive = root.Q<VisualElement>("wallet-session-icon-inactive");
            _walletSessionIconActive = root.Q<VisualElement>("wallet-session-icon-active");
            _previousSkinButton = root.Q<Button>("btn-prev-skin");
            _nextSkinButton = root.Q<Button>("btn-next-skin");
            _confirmCreateButton = root.Q<Button>("btn-create-character");
            _enterDungeonButton = root.Q<Button>("btn-enter-dungeon");
            _disconnectButton = root.Q<Button>("btn-disconnect-wallet");
            _sessionPillButton = root.Q<Button>("btn-session-pill");
            _sessionSetupActivateButton = root.Q<Button>("btn-session-setup-activate");
            _lowBalanceModalDismissButton = root.Q<Button>("btn-low-balance-dismiss");
            _lowBalanceTopUpButton = root.Q<Button>("btn-low-balance-topup");
            _extractionSummaryContinueButton = root.Q<Button>("btn-extraction-summary-continue");

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

            if (_sessionPillButton != null)
            {
                _sessionPillButton.clicked += HandleEnableSessionClicked;
            }

            if (_sessionSetupActivateButton != null)
            {
                _sessionSetupActivateButton.clicked += HandleEnableSessionClicked;
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

            if (_sessionPillButton != null)
            {
                _sessionPillButton.clicked -= HandleEnableSessionClicked;
            }

            if (_sessionSetupActivateButton != null)
            {
                _sessionSetupActivateButton.clicked -= HandleEnableSessionClicked;
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

        private void HandleEnableSessionClicked()
        {
            characterManager?.EnsureSessionReadyFromMenu();
        }

        private void HandleLowBalanceDismissClicked()
        {
            characterManager?.DismissLowBalanceModal();
        }

        private void HandleLowBalanceTopUpClicked()
        {
            characterManager?.RequestDevnetTopUpFromMenu();
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

            var isLockedProfile = state.HasProfile && !state.HasUnsavedProfileChanges;
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
                !state.IsLowBalanceBlocking;
            if (_sessionSetupOverlay != null)
            {
                _sessionSetupOverlay.style.display = shouldShowSessionSetupOverlay
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_sessionSetupMessageLabel != null)
            {
                _sessionSetupMessageLabel.text = state.IsBusy
                    ? "Please confirm in your wallet to activate your gameplay session."
                    : "Activate your gameplay session to enable smoother actions with fewer wallet prompts.";
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

            _previousSkinButton?.SetEnabled(canEditProfile);
            _nextSkinButton?.SetEnabled(canEditProfile);
            _displayNameInput?.SetEnabled(false);
            _confirmCreateButton?.SetEnabled(
                canEditProfile &&
                (!state.HasProfile || state.HasUnsavedProfileChanges));
            _enterDungeonButton?.SetEnabled(canEnter);
            _disconnectButton?.SetEnabled(!state.IsBusy);
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
            _lowBalanceModalDismissButton?.SetEnabled(!state.IsRequestingDevnetTopUp);
            _lowBalanceTopUpButton?.SetEnabled(!state.IsRequestingDevnetTopUp && !state.IsBusy);

            TryShowPendingExtractionSummary();
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

            _isShowingExtractionSummary = true;
            _extractionSummaryOverlay.style.display = DisplayStyle.Flex;

            if (_extractionSummaryContinueButton != null)
            {
                _extractionSummaryContinueButton.style.display = DisplayStyle.None;
            }

            SetLabelText(_extractionSummarySubtitleLabel, "Cashing in recovered loot...");
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
                if (itemRegistry != null)
                {
                    var iconSprite = itemRegistry.GetIcon(item.ItemId);
                    if (iconSprite != null)
                    {
                        icon.style.backgroundImage = new StyleBackground(iconSprite);
                    }
                }
                row.Add(icon);

                var nameLabel = new Label(itemRegistry != null ? itemRegistry.GetDisplayName(item.ItemId) : item.ItemId.ToString());
                nameLabel.AddToClassList("extraction-summary-item-name");
                row.Add(nameLabel);

                var amountLabel = new Label($"x{item.Amount}");
                amountLabel.AddToClassList("extraction-summary-item-amount");
                row.Add(amountLabel);

                var pointsLabel = new Label("+0");
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

                var emptyLabel = new Label("No scored loot extracted this run.");
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

                await AnimateNumberLabelAsync(rowPointsLabels[rowIndex], 0, itemStackScores[rowIndex], "+");
                runningLootScore += itemStackScores[rowIndex];
                SetLabelText(_extractionSummaryLootPointsLabel, $"+{runningLootScore}");
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
                summary.RunScore > 0
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
