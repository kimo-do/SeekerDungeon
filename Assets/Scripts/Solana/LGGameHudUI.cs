using System;
using System.Collections.Generic;
using System.Linq;
using Chaindepth.Accounts;
using Cysharp.Threading.Tasks;
using SeekerDungeon.Audio;
using SeekerDungeon.Dungeon;
using Solana.Unity.Programs;
using Solana.Unity.Rpc.Types;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SeekerDungeon.Solana
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class LGGameHudUI : MonoBehaviour
    {
        private const float HudSlotWidthPx = 118f;
        private const float HudSlotHorizontalMarginPx = 14f;
        private const int FallbackVisibleSlotLimit = 6;

        [Header("References")]
        [SerializeField] private LGManager manager;
        [SerializeField] private LGWalletSessionManager walletSessionManager;
        [SerializeField] private ItemRegistry itemRegistry;
        [SerializeField] private InventoryPanelUI inventoryPanelUI;

        [Header("Scene Flow")]
        [SerializeField] private string backSceneName = "MenuScene";
        [SerializeField] private string loadingSceneName = "Loading";

        [Header("Refresh")]
        [SerializeField] private float hudRefreshSeconds = 3f;
        [SerializeField] private bool logDebugMessages = false;
        [Header("Inventory UI")]
        [SerializeField] private Sprite hotbarGlowSprite;

        /// <summary>
        /// Fired when the inventory button is clicked.
        /// Listeners should open the full inventory panel.
        /// </summary>
        public event Action OnBagClicked;

        private UIDocument _document;
        private Label _solBalanceLabel;
        private Label _skrBalanceLabel;
        private Label _jobInfoLabel;
        private Label _statusLabel;
        private Button _backButton;
        private Button _bagButton;
        private Label _networkLabel;
        private VisualElement _inventorySlotsContainer;
        private VisualElement _itemTooltip;
        private Label _itemTooltipNameLabel;
        private Label _itemTooltipDamageLabel;
        private Label _itemTooltipValueLabel;
        private Button _itemTooltipEquipButton;
        private Color _tooltipDefaultNameColor = Color.white;
        private VisualElement _exitConfirmOverlay;
        private Button _exitConfirmCloseButton;
        private Button _exitConfirmLeaveButton;
        private VisualElement _sessionFeeOverlay;
        private Label _sessionFeeMessageLabel;
        private Button _sessionFeeTopUpButton;
        private Button _sessionFeeUseWalletButton;
        private UniTaskCompletionSource<bool> _exitConfirmTcs;
        private VisualElement _root;
        private TxIndicatorVisualController _txIndicatorController;
        private HudCenterToastVisualController _centerToastController;

        private readonly Dictionary<ItemId, VisualElement> _slotsByItemId = new();
        private ItemId _tooltipItemId = ItemId.None;
        private bool _isEquippingFromTooltip;
        private float _lastInventorySlotsWidth = -1f;
        private bool _isFundingSessionFee;

        private bool _isLoadingScene;

        private void Awake()
        {
            LGUiInputSystemGuard.EnsureEventSystemForRuntimeUi(createIfMissing: true);
            _document = GetComponent<UIDocument>();

            if (manager == null)
            {
                manager = LGManager.Instance;
            }

            if (manager == null)
            {
                manager = UnityEngine.Object.FindFirstObjectByType<LGManager>();
            }

            if (walletSessionManager == null)
            {
                walletSessionManager = LGWalletSessionManager.Instance;
            }

            if (walletSessionManager == null)
            {
                walletSessionManager = UnityEngine.Object.FindFirstObjectByType<LGWalletSessionManager>();
            }

            if (inventoryPanelUI == null)
            {
                inventoryPanelUI = UnityEngine.Object.FindFirstObjectByType<InventoryPanelUI>();
            }
        }

        private void OnEnable()
        {
            _root = _document?.rootVisualElement;
            if (_root == null)
            {
                return;
            }

            _solBalanceLabel = _root.Q<Label>("wallet-sol-balance-label");
            _skrBalanceLabel = _root.Q<Label>("wallet-skr-balance-label");
            _jobInfoLabel = _root.Q<Label>("hud-job-info");
            _statusLabel = _root.Q<Label>("hud-status");
            _backButton = _root.Q<Button>("hud-btn-back");
            _bagButton = _root.Q<Button>("hud-btn-bag");
            _networkLabel = _root.Q<Label>("wallet-network-label");
            _inventorySlotsContainer = _root.Q<VisualElement>("hud-inventory-slots");
            _itemTooltip = _root.Q<VisualElement>("hud-item-tooltip");
            _itemTooltipNameLabel = _root.Q<Label>("hud-item-tooltip-name");
            _itemTooltipDamageLabel = _root.Q<Label>("hud-item-tooltip-damage");
            _itemTooltipValueLabel = _root.Q<Label>("hud-item-tooltip-value");
            _itemTooltipEquipButton = _root.Q<Button>("hud-item-tooltip-equip");
            _exitConfirmOverlay = _root.Q<VisualElement>("exit-confirm-overlay");
            _exitConfirmCloseButton = _root.Q<Button>("btn-exit-confirm-close");
            _exitConfirmLeaveButton = _root.Q<Button>("btn-exit-confirm-leave");
            _sessionFeeOverlay = _root.Q<VisualElement>("session-fee-overlay");
            _sessionFeeMessageLabel = _root.Q<Label>("session-fee-message");
            _sessionFeeTopUpButton = _root.Q<Button>("btn-session-fee-topup");
            _sessionFeeUseWalletButton = _root.Q<Button>("btn-session-fee-use-wallet");
            HideExitConfirmModal();
            HideSessionFeeModal();
            HideItemTooltip();
            CacheTooltipDefaultStyles();
            ResolveHotbarGlowSpriteIfMissing();

            SetLabel(_networkLabel, "DEVNET");

            if (_backButton != null)
            {
                _backButton.clicked += HandleBackClicked;
            }

            if (_bagButton != null)
            {
                _bagButton.clicked += HandleBagClicked;
            }
            if (_exitConfirmCloseButton != null)
            {
                _exitConfirmCloseButton.clicked += HandleExitConfirmCancelClicked;
            }
            if (_exitConfirmLeaveButton != null)
            {
                _exitConfirmLeaveButton.clicked += HandleExitConfirmLeaveClicked;
            }
            if (_itemTooltipEquipButton != null)
            {
                _itemTooltipEquipButton.clicked += HandleItemTooltipEquipClicked;
            }
            if (_sessionFeeTopUpButton != null)
            {
                _sessionFeeTopUpButton.clicked += HandleSessionFeeTopUpClicked;
            }
            if (_sessionFeeUseWalletButton != null)
            {
                _sessionFeeUseWalletButton.clicked += HandleSessionFeeUseWalletClicked;
            }
            if (_root != null)
            {
                _root.RegisterCallback<PointerDownEvent>(HandleRootPointerDown);
            }
            if (_inventorySlotsContainer != null)
            {
                _inventorySlotsContainer.RegisterCallback<GeometryChangedEvent>(HandleInventorySlotsGeometryChanged);
            }

            if (manager != null)
            {
                manager.OnPlayerStateUpdated += HandlePlayerStateUpdated;
                manager.OnRoomStateUpdated += HandleRoomStateUpdated;
                manager.OnInventoryUpdated += HandleInventoryUpdated;
                manager.OnSessionFeeFundingRequired += HandleSessionFeeFundingRequired;
            }

            if (walletSessionManager != null)
            {
                walletSessionManager.OnStatus += HandleWalletSessionStatus;
                walletSessionManager.OnError += HandleWalletSessionError;
            }

            _txIndicatorController ??= new TxIndicatorVisualController();
            _txIndicatorController.Bind(_root);
            _centerToastController ??= new HudCenterToastVisualController();
            _centerToastController.Bind(_root);

            RefreshHudAsync().Forget();
            RefreshLoopAsync(this.GetCancellationTokenOnDestroy()).Forget();
            RefreshInventorySlots(manager?.CurrentInventoryState);
        }

        private void OnDisable()
        {
            if (_backButton != null)
            {
                _backButton.clicked -= HandleBackClicked;
            }

            if (_bagButton != null)
            {
                _bagButton.clicked -= HandleBagClicked;
            }
            if (_exitConfirmCloseButton != null)
            {
                _exitConfirmCloseButton.clicked -= HandleExitConfirmCancelClicked;
            }
            if (_exitConfirmLeaveButton != null)
            {
                _exitConfirmLeaveButton.clicked -= HandleExitConfirmLeaveClicked;
            }
            if (_itemTooltipEquipButton != null)
            {
                _itemTooltipEquipButton.clicked -= HandleItemTooltipEquipClicked;
            }
            if (_sessionFeeTopUpButton != null)
            {
                _sessionFeeTopUpButton.clicked -= HandleSessionFeeTopUpClicked;
            }
            if (_sessionFeeUseWalletButton != null)
            {
                _sessionFeeUseWalletButton.clicked -= HandleSessionFeeUseWalletClicked;
            }
            if (_root != null)
            {
                _root.UnregisterCallback<PointerDownEvent>(HandleRootPointerDown);
            }
            if (_inventorySlotsContainer != null)
            {
                _inventorySlotsContainer.UnregisterCallback<GeometryChangedEvent>(HandleInventorySlotsGeometryChanged);
            }

            if (manager != null)
            {
                manager.OnPlayerStateUpdated -= HandlePlayerStateUpdated;
                manager.OnRoomStateUpdated -= HandleRoomStateUpdated;
                manager.OnInventoryUpdated -= HandleInventoryUpdated;
                manager.OnSessionFeeFundingRequired -= HandleSessionFeeFundingRequired;
            }

            if (walletSessionManager != null)
            {
                walletSessionManager.OnStatus -= HandleWalletSessionStatus;
                walletSessionManager.OnError -= HandleWalletSessionError;
            }

            _txIndicatorController?.Dispose();
            _txIndicatorController = null;
            _centerToastController?.Dispose();
            _centerToastController = null;

            HideExitConfirmModal();
            HideSessionFeeModal();
            HideItemTooltip();
            _exitConfirmTcs?.TrySetResult(false);
            _exitConfirmTcs = null;
        }

        private void Update()
        {
            _txIndicatorController?.Tick(Time.unscaledDeltaTime);
            _centerToastController?.Tick(Time.unscaledDeltaTime);
        }

        public void ShowCenterToast(string message, float holdSeconds = 1.5f)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (_centerToastController == null)
            {
                SetStatus(message);
                return;
            }

            _centerToastController.Show(message, holdSeconds);
        }

        private async UniTaskVoid RefreshLoopAsync(System.Threading.CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0.5f, hudRefreshSeconds)), cancellationToken: cancellationToken);
                    await RefreshHudAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    SetStatus($"HUD refresh failed: {exception.Message}");
                }
            }
        }

        private void HandlePlayerStateUpdated(Chaindepth.Accounts.PlayerAccount _)
        {
            RefreshJobInfo();
            UpdateEquippedSlotHighlight();
        }

        private void HandleRoomStateUpdated(Chaindepth.Accounts.RoomAccount _)
        {
            RefreshJobInfo();
        }

        private void HandleBackClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);

            if (_isLoadingScene)
            {
                return;
            }

            LoadSceneWithFadeAsync(backSceneName).Forget();
        }

        private async UniTaskVoid LoadSceneWithFadeAsync(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return;
            }

            _isLoadingScene = true;
            try
            {
                await SceneLoadController.GetOrCreate().LoadSceneAsync(sceneName, LoadSceneMode.Single);
            }
            finally
            {
                _isLoadingScene = false;
            }
        }

        private async UniTask RefreshHudAsync()
        {
            await RefreshBalancesAsync();
            RefreshJobInfo();
        }

        private async UniTask RefreshBalancesAsync()
        {
            var wallet = Web3.Wallet;
            var account = wallet?.Account;
            if (wallet?.ActiveRpcClient == null || account == null)
            {
                SetLabel(_solBalanceLabel, "SOL: --");
                SetLabel(_skrBalanceLabel, "--");
                return;
            }

            if (logDebugMessages)
                Debug.Log($"[GameHud] Refreshing balances for wallet={account.PublicKey.Key.Substring(0, 8)}..");

            var solResult = await wallet.ActiveRpcClient.GetBalanceAsync(account.PublicKey, Commitment.Confirmed);
            if (solResult.WasSuccessful && solResult.Result != null)
            {
                var sol = solResult.Result.Value / 1_000_000_000d;
                SetLabel(_solBalanceLabel, $"{sol:F3}");
            }
            else
            {
                SetLabel(_solBalanceLabel, "--");
            }

            // Derive the player's ATA directly (same approach as the main menu)
            // instead of wallet.GetTokenAccounts which can return stale/empty results.
            var skrMint = new PublicKey(LGConfig.ActiveSkrMint);
            var playerAta = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(account.PublicKey, skrMint);
            var tokenResult = await wallet.ActiveRpcClient.GetTokenAccountBalanceAsync(playerAta, Commitment.Confirmed);
            if (tokenResult.WasSuccessful && tokenResult.Result?.Value != null)
            {
                var rawAmount = tokenResult.Result.Value.Amount ?? "0";
                var amountLamports = ulong.TryParse(rawAmount, out var parsed) ? parsed : 0UL;
                var skrUi = amountLamports / (double)LGConfig.SKR_MULTIPLIER;
                SetLabel(_skrBalanceLabel, $"{skrUi:F3}");
                if (logDebugMessages)
                    Debug.Log($"[GameHud] SKR balance: wallet={account.PublicKey.Key.Substring(0, 8)}.. ata={playerAta.Key.Substring(0, 8)}.. raw={rawAmount} ui={skrUi:F3}");
            }
            else
            {
                SetLabel(_skrBalanceLabel, "0");
                if (logDebugMessages)
                    Debug.Log($"[GameHud] SKR balance fetch failed: wallet={account.PublicKey.Key.Substring(0, 8)}.. ata={playerAta.Key.Substring(0, 8)}.. reason={tokenResult?.Reason}");
            }
        }

        private void RefreshJobInfo()
        {
            if (manager?.CurrentPlayerState == null)
            {
                SetLabel(_jobInfoLabel, "job:none");
                return;
            }

            var player = manager.CurrentPlayerState;
            var activeJobs = player.ActiveJobs;
            if (activeJobs == null || activeJobs.Length == 0)
            {
                SetLabel(_jobInfoLabel, "job:none");
                return;
            }

            for (var i = activeJobs.Length - 1; i >= 0; i -= 1)
            {
                var job = activeJobs[i];
                if (job == null || job.RoomX != player.CurrentRoomX || job.RoomY != player.CurrentRoomY)
                {
                    continue;
                }

                var direction = job.Direction;
                var directionName = LGConfig.GetDirectionName(direction);

                if (manager.CurrentRoomState == null || direction >= manager.CurrentRoomState.Walls.Length)
                {
                    SetLabel(_jobInfoLabel, $"job:{directionName.ToLowerInvariant()}");
                    return;
                }

                var room = manager.CurrentRoomState;
                var progress = room.Progress[direction];
                var required = room.BaseSlots[direction];
                var helpers = room.HelperCounts[direction];
                SetLabel(_jobInfoLabel, $"job:{directionName.ToLowerInvariant()} p={progress}/{required} h={helpers}");
                return;
            }

            SetLabel(_jobInfoLabel, "job:none");
        }

        private void SetStatus(string message)
        {
            if (logDebugMessages)
            {
                Debug.Log($"[GameHUD] {message}");
            }

            SetLabel(_statusLabel, message);
        }

        private void HandleWalletSessionStatus(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                SetStatus(message);
            }
        }

        private void HandleWalletSessionError(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                SetStatus(message);
            }
        }

        private void HandleBagClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Nav);

            if (OnBagClicked != null)
            {
                OnBagClicked.Invoke();
                return;
            }

            if (inventoryPanelUI != null)
            {
                inventoryPanelUI.Toggle();
                return;
            }

            if (inventoryPanelUI == null)
            {
                inventoryPanelUI = UnityEngine.Object.FindFirstObjectByType<InventoryPanelUI>();
            }

            if (inventoryPanelUI != null)
            {
                inventoryPanelUI.Toggle();
                return;
            }

            if (OnBagClicked == null)
            {
                SetStatus("Inventory panel is not configured.");
                return;
            }
        }

        public UniTask<bool> ShowExitDungeonConfirmationAsync()
        {
            if (_exitConfirmOverlay == null)
            {
                return UniTask.FromResult(true);
            }

            _exitConfirmTcs?.TrySetResult(false);
            _exitConfirmTcs = new UniTaskCompletionSource<bool>();
            _exitConfirmOverlay.style.display = DisplayStyle.Flex;
            return _exitConfirmTcs.Task;
        }

        private void HandleExitConfirmCancelClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            var tcs = _exitConfirmTcs;
            HideExitConfirmModal();
            tcs?.TrySetResult(false);
        }

        private void HandleExitConfirmLeaveClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Confirm);
            var tcs = _exitConfirmTcs;
            HideExitConfirmModal();
            tcs?.TrySetResult(true);
        }

        private void HideExitConfirmModal()
        {
            if (_exitConfirmOverlay != null)
            {
                _exitConfirmOverlay.style.display = DisplayStyle.None;
            }
        }

        private void HandleSessionFeeFundingRequired(string message)
        {
            ShowSessionFeeModal(message);
        }

        private void ShowSessionFeeModal(string message)
        {
            if (_sessionFeeOverlay == null)
            {
                return;
            }

            if (_sessionFeeMessageLabel != null)
            {
                _sessionFeeMessageLabel.text = string.IsNullOrWhiteSpace(message)
                    ? "Your session wallet is low on SOL for transaction fees. Top up session funding to continue smooth gameplay."
                    : message;
            }

            _sessionFeeOverlay.style.display = DisplayStyle.Flex;
            UpdateSessionFeeButtonsInteractable();
        }

        private void HideSessionFeeModal()
        {
            if (_sessionFeeOverlay != null)
            {
                _sessionFeeOverlay.style.display = DisplayStyle.None;
            }
        }

        private void HandleSessionFeeTopUpClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Primary);
            TopUpSessionFeeAsync().Forget();
        }

        private void HandleSessionFeeUseWalletClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            HideSessionFeeModal();
            ShowCenterToast("Using wallet signing for now.", 1.25f);
        }

        private async UniTaskVoid TopUpSessionFeeAsync()
        {
            if (_isFundingSessionFee)
            {
                return;
            }

            if (walletSessionManager == null)
            {
                SetStatus("Session manager unavailable.");
                return;
            }

            _isFundingSessionFee = true;
            UpdateSessionFeeButtonsInteractable();

            try
            {
                var funded = await walletSessionManager.EnsureSessionSignerFundedAsync(emitPromptStatus: true);
                if (funded)
                {
                    HideSessionFeeModal();
                    ShowCenterToast("Session wallet funded.", 1.25f);
                    await RefreshBalancesAsync();
                    return;
                }

                SetStatus("Session top-up failed. Please retry.");
            }
            catch (Exception exception)
            {
                SetStatus($"Session top-up failed: {exception.Message}");
            }
            finally
            {
                _isFundingSessionFee = false;
                UpdateSessionFeeButtonsInteractable();
            }
        }

        private void UpdateSessionFeeButtonsInteractable()
        {
            _sessionFeeTopUpButton?.SetEnabled(!_isFundingSessionFee);
            _sessionFeeUseWalletButton?.SetEnabled(!_isFundingSessionFee);
        }

        private void HandleInventoryUpdated(InventoryAccount inventory)
        {
            RefreshInventorySlots(inventory);
            if (_tooltipItemId != ItemId.None)
            {
                RefreshItemTooltipContent(_tooltipItemId);
            }
        }

        private void HandleInventorySlotsGeometryChanged(GeometryChangedEvent _)
        {
            var width = _inventorySlotsContainer?.resolvedStyle.width ?? -1f;
            if (Mathf.Approximately(width, _lastInventorySlotsWidth))
            {
                return;
            }

            _lastInventorySlotsWidth = width;
            RefreshInventorySlots(manager?.CurrentInventoryState);
        }

        private void RefreshInventorySlots(InventoryAccount inventory)
        {
            if (_inventorySlotsContainer == null)
            {
                return;
            }

            _inventorySlotsContainer.Clear();
            _slotsByItemId.Clear();

            if (inventory?.Items == null || inventory.Items.Length == 0)
            {
                return;
            }

            var visibleSlotLimit = CalculateVisibleInventorySlotLimit();
            var shownCount = 0;
            foreach (var item in inventory.Items)
            {
                if (item == null || item.Amount == 0)
                {
                    continue;
                }

                if (shownCount >= visibleSlotLimit)
                {
                    break;
                }

                var itemId = LGDomainMapper.ToItemId(item.ItemId);
                var slot = CreateSlotElement(itemId, item.Amount);
                _inventorySlotsContainer.Add(slot);
                _slotsByItemId[itemId] = slot;
                shownCount += 1;
            }

            UpdateEquippedSlotHighlight();
        }

        private int CalculateVisibleInventorySlotLimit()
        {
            if (_inventorySlotsContainer == null)
            {
                return FallbackVisibleSlotLimit;
            }

            var containerWidth = _inventorySlotsContainer.resolvedStyle.width;
            var slotFootprint = HudSlotWidthPx + HudSlotHorizontalMarginPx;
            if (slotFootprint <= 0f || containerWidth <= 0f || float.IsNaN(containerWidth))
            {
                return FallbackVisibleSlotLimit;
            }

            var visible = Mathf.FloorToInt(containerWidth / slotFootprint);
            return Mathf.Max(1, visible);
        }

        private VisualElement CreateSlotElement(ItemId itemId, uint amount)
        {
            var slot = new VisualElement();
            slot.AddToClassList("hud-inventory-slot");

            var glow = new VisualElement();
            glow.AddToClassList("hud-inventory-slot-glow");
            var rarityColor = ResolveRarityColor(itemId);
            glow.style.unityBackgroundImageTintColor = new StyleColor(new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.38f));
            if (hotbarGlowSprite != null)
            {
                glow.style.backgroundImage = new StyleBackground(hotbarGlowSprite);
            }
            slot.Add(glow);

            var icon = new VisualElement();
            icon.AddToClassList("hud-inventory-slot-icon");
            if (itemRegistry != null)
            {
                var sprite = itemRegistry.GetIcon(itemId);
                if (sprite != null)
                {
                    icon.style.backgroundImage = new StyleBackground(sprite);
                }
            }

            slot.Add(icon);

            var countLabel = new Label(amount > 1 ? amount.ToString() : string.Empty);
            countLabel.AddToClassList("hud-inventory-slot-count");
            slot.Add(countLabel);

            // Tooltip-style: add item name via tooltip
            if (itemRegistry != null)
            {
                slot.tooltip = itemRegistry.GetDisplayName(itemId);
            }
            slot.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                ShowItemTooltip(itemId, slot);
            });

            return slot;
        }

        private void HandleRootPointerDown(PointerDownEvent evt)
        {
            if (_itemTooltip == null || _itemTooltip.resolvedStyle.display == DisplayStyle.None)
            {
                return;
            }

            var target = evt.target as VisualElement;
            if (target != null && IsDescendantOf(target, _itemTooltip))
            {
                return;
            }

            HideItemTooltip();
        }

        private static bool IsDescendantOf(VisualElement element, VisualElement potentialAncestor)
        {
            var current = element;
            while (current != null)
            {
                if (current == potentialAncestor)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private void ShowItemTooltip(ItemId itemId, VisualElement anchorSlot)
        {
            if (_itemTooltip == null || _root == null || anchorSlot == null)
            {
                return;
            }

            // Clicking the same slot again should close the tooltip.
            if (_tooltipItemId == itemId && _itemTooltip.resolvedStyle.display != DisplayStyle.None)
            {
                HideItemTooltip();
                return;
            }

            _tooltipItemId = itemId;
            RefreshItemTooltipContent(itemId);

            _itemTooltip.style.display = DisplayStyle.Flex;
            _itemTooltip.BringToFront();

            var slotRect = anchorSlot.worldBound;
            var rootRect = _root.worldBound;
            var tooltipWidth = 320f;
            var tooltipHeight = 190f;
            var left = slotRect.center.x - rootRect.x - (tooltipWidth * 0.5f);
            var top = slotRect.yMin - rootRect.y - tooltipHeight - 36f;

            if (left < 12f) left = 12f;
            if (left > rootRect.width - tooltipWidth - 12f) left = Mathf.Max(12f, rootRect.width - tooltipWidth - 12f);
            if (top < 12f) top = slotRect.yMax - rootRect.y + 8f;
            if (top > rootRect.height - tooltipHeight - 12f) top = Mathf.Max(12f, rootRect.height - tooltipHeight - 12f);

            _itemTooltip.style.left = left;
            _itemTooltip.style.top = top;
        }

        private void HideItemTooltip()
        {
            _tooltipItemId = ItemId.None;
            if (_itemTooltip != null)
            {
                _itemTooltip.style.display = DisplayStyle.None;
            }
        }

        private void RefreshItemTooltipContent(ItemId itemId)
        {
            SetLabel(_itemTooltipNameLabel, itemRegistry != null ? itemRegistry.GetDisplayName(itemId) : itemId.ToString());
            var rarityColor = ResolveRarityColor(itemId);
            if (_itemTooltipNameLabel != null)
            {
                _itemTooltipNameLabel.style.color = new StyleColor(rarityColor);
            }
            if (_itemTooltip != null)
            {
                var borderColor = new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.85f);
                var borderShadow = new Color(rarityColor.r * 0.45f, rarityColor.g * 0.45f, rarityColor.b * 0.45f, 0.8f);
                _itemTooltip.style.borderTopColor = new StyleColor(borderColor);
                _itemTooltip.style.borderRightColor = new StyleColor(borderColor);
                _itemTooltip.style.borderBottomColor = new StyleColor(borderShadow);
                _itemTooltip.style.borderLeftColor = new StyleColor(borderColor);
            }

            var damage = ItemRegistry.GetWeaponDamage(itemId);
            SetLabel(_itemTooltipDamageLabel, damage.HasValue ? $"Damage: {damage.Value}" : "Damage: -");

            var value = ItemRegistry.GetExtractionValue(itemId);
            SetLabel(_itemTooltipValueLabel, value.HasValue ? $"Value: {value.Value}" : "Value: -");

            if (_itemTooltipEquipButton == null)
            {
                return;
            }

            var isWearable = ItemRegistry.IsWearable(itemId);
            var equippedItemId = manager?.CurrentPlayerState != null
                ? LGDomainMapper.ToItemId(manager.CurrentPlayerState.EquippedItemId)
                : ItemId.None;
            var isAlreadyEquipped = equippedItemId == itemId;

            _itemTooltipEquipButton.style.display = isWearable ? DisplayStyle.Flex : DisplayStyle.None;
            _itemTooltipEquipButton.text = isAlreadyEquipped ? "Equipped" : "Equip";
            _itemTooltipEquipButton.SetEnabled(isWearable && !isAlreadyEquipped && !_isEquippingFromTooltip);
        }

        private void HandleItemTooltipEquipClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Primary);

            if (_tooltipItemId == ItemId.None || manager == null || _isEquippingFromTooltip)
            {
                return;
            }

            EquipTooltipItemAsync(_tooltipItemId).Forget();
        }

        private async UniTaskVoid EquipTooltipItemAsync(ItemId itemId)
        {
            _isEquippingFromTooltip = true;
            RefreshItemTooltipContent(itemId);

            try
            {
                var result = await manager.EquipItem((ushort)itemId);
                if (!result.Success)
                {
                    SetStatus($"Equip failed: {result.Error}");
                    return;
                }

                GameAudioManager.Instance?.PlayWorld(WorldSfxId.Equip, transform.position);
                await manager.RefreshAllState();
                HideItemTooltip();
            }
            finally
            {
                _isEquippingFromTooltip = false;
                if (_tooltipItemId != ItemId.None)
                {
                    RefreshItemTooltipContent(_tooltipItemId);
                }
            }
        }

        /// <summary>
        /// Returns the screen-space center position of the inventory slot for a given item id.
        /// Used by the loot fly animation to know where to send items.
        /// Returns null if the slot does not exist or is not laid out yet.
        /// </summary>
        public Vector3? GetSlotScreenPosition(ItemId itemId)
        {
            if (!_slotsByItemId.TryGetValue(itemId, out var slot))
            {
                // Fall back to the bag button position if the slot doesn't exist yet
                if (_bagButton != null)
                {
                    var bagRect = _bagButton.worldBound;
                    if (bagRect.width > 0 && bagRect.height > 0)
                    {
                        // UIToolkit y is top-down, Screen y is bottom-up
                        var screenY = Screen.height - bagRect.center.y;
                        return new Vector3(bagRect.center.x, screenY, 0f);
                    }
                }

                return null;
            }

            var rect = slot.worldBound;
            if (rect.width <= 0 || rect.height <= 0)
            {
                return null;
            }

            // UIToolkit y is top-down, Screen y is bottom-up
            var y = Screen.height - rect.center.y;
            return new Vector3(rect.center.x, y, 0f);
        }

        private static void SetLabel(Label label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }

        private void UpdateEquippedSlotHighlight()
        {
            if (_slotsByItemId.Count == 0)
            {
                return;
            }

            var equippedItemId = manager?.CurrentPlayerState != null
                ? LGDomainMapper.ToItemId(manager.CurrentPlayerState.EquippedItemId)
                : ItemId.None;

            foreach (var entry in _slotsByItemId)
            {
                var slot = entry.Value;
                if (slot == null)
                {
                    continue;
                }

                if (entry.Key == equippedItemId)
                {
                    slot.AddToClassList("hud-inventory-slot-equipped");
                }
                else
                {
                    slot.RemoveFromClassList("hud-inventory-slot-equipped");
                }
            }
        }

        private void CacheTooltipDefaultStyles()
        {
            if (_itemTooltipNameLabel != null)
            {
                _tooltipDefaultNameColor = _itemTooltipNameLabel.resolvedStyle.color;
            }
        }

        private Color ResolveRarityColor(ItemId itemId)
        {
            if (itemRegistry == null)
            {
                return _tooltipDefaultNameColor;
            }

            var rarity = itemRegistry.GetRarity(itemId);
            return ItemRegistry.RarityToColor(rarity);
        }

        private void ResolveHotbarGlowSpriteIfMissing()
        {
            if (hotbarGlowSprite != null)
            {
                return;
            }

            var candidate = Resources
                .FindObjectsOfTypeAll<Sprite>()
                .FirstOrDefault(sprite => string.Equals(sprite.name, "Glow", StringComparison.OrdinalIgnoreCase));
            if (candidate != null)
            {
                hotbarGlowSprite = candidate;
            }
        }
    }
}
