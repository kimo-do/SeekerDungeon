using System;
using System.Collections.Generic;
using System.Globalization;
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
        private static readonly double[] SolTopUpPresetValues = { 0.05d, 0.1d, 0.5d, 1d };
        private static readonly int[] SkrTopUpPresetValues = { 1, 5, 20, 50 };
        private static readonly int[] DuelStakePresetValues = { 1, 5, 20, 50 };
        private const double PlannedDuelFeeBps = 200d;

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
        public event Action<PublicKey, ulong> OnDuelChallengeRequested;
        public event Action<PublicKey> OnDuelAcceptRequested;
        public event Action<PublicKey> OnDuelDeclineRequested;
        public event Action<PublicKey> OnDuelClaimExpiredRequested;

        private UIDocument _document;
        private Label _solBalanceLabel;
        private Label _skrBalanceLabel;
        private VisualElement _playerHealthRoot;
        private VisualElement _playerHealthFill;
        private Label _playerHealthLabel;
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
        private VisualElement _exitConfirmCard;
        private VisualElement _sessionFeeCard;
        private Label _sessionFeeMessageLabel;
        private Label _sessionFeeNeededLabel;
        private Label _sessionFeeSolBalanceLabel;
        private Label _sessionFeeSkrBalanceLabel;
        private Button _sessionFeeCloseButton;
        private Button _sessionFeeTopUpButton;
        private Button _sessionFeeSolCustomButton;
        private Button _sessionFeeSkrCustomButton;
        private TextField _sessionFeeSolCustomInput;
        private TextField _sessionFeeSkrCustomInput;
        private readonly List<Button> _sessionFeeSolPresetButtons = new();
        private readonly List<Button> _sessionFeeSkrPresetButtons = new();
        private VisualElement _duelChallengeOverlay;
        private VisualElement _duelChallengeCard;
        private Label _duelChallengeSubtitle;
        private Label _duelBreakdownStakeValue;
        private Label _duelBreakdownTaxValue;
        private Label _duelBreakdownWinnerValue;
        private Label _duelChallengeFeeLine;
        private Button _duelChallengeConfirmButton;
        private Button _duelChallengeCancelButton;
        private VisualElement _duelSubmitConfirmOverlay;
        private VisualElement _duelSubmitConfirmCard;
        private Label _duelSubmitConfirmMessageLabel;
        private Button _duelSubmitConfirmCloseButton;
        private Button _duelSubmitConfirmCancelButton;
        private Button _duelSubmitConfirmConfirmButton;
        private readonly List<Button> _duelStakePresetButtons = new();
        private Button _duelInboxButton;
        private Button _duelIncomingCtaButton;
        private Label _duelInboxCountLabel;
        private VisualElement _duelInboxOverlay;
        private VisualElement _duelInboxCard;
        private ScrollView _duelInboxList;
        private Label _duelInboxDetailTitle;
        private Label _duelInboxDetailStatusPill;
        private Label _duelInboxDetailResultPill;
        private Label _duelInboxDetailPlayerValue;
        private Label _duelInboxDetailStakeValue;
        private Label _duelInboxDetailWinnerValue;
        private VisualElement _duelInboxDetailExpiryRadial;
        private Label _duelInboxDetailFee;
        private Button _duelInboxAcceptButton;
        private Button _duelInboxDeclineButton;
        private Button _duelInboxClaimExpiredButton;
        private Button _duelInboxCloseButton;
        private VisualElement _duelResultOverlay;
        private VisualElement _duelResultCard;
        private Label _duelResultTitle;
        private Label _duelResultMessage;
        private Button _duelResultDismissButton;
        private readonly List<DuelChallengeView> _duelInboxItems = new();
        private readonly Dictionary<Button, DuelChallengeView> _duelInboxRows = new();
        private ulong? _duelInboxCurrentSlot;
        private UniTaskCompletionSource<bool> _exitConfirmTcs;
        private VisualElement _root;
        private TxIndicatorVisualController _txIndicatorController;
        private HudCenterToastVisualController _centerToastController;

        private readonly Dictionary<ItemId, VisualElement> _slotsByItemId = new();
        private ItemId _tooltipItemId = ItemId.None;
        private bool _isEquippingFromTooltip;
        private float _lastInventorySlotsWidth = -1f;
        private bool _isFundingSessionFee;
        private bool _isSolCustomSelected;
        private bool _isSkrCustomSelected;
        private double _selectedSolTopUp = SolTopUpPresetValues[0];
        private int _selectedSkrTopUp = SkrTopUpPresetValues[0];
        private int _selectedDuelStakeSkr = DuelStakePresetValues[0];
        private PublicKey _selectedDuelTarget;
        private string _selectedDuelTargetDisplayName = string.Empty;
        private DuelChallengeView _selectedDuelInboxItem;
        private PublicKey _localWalletForDuelInbox;
        private string _duelIncomingCtaChallengePdaKey = string.Empty;
        private float _duelSlotSampleRealtime;
        private float _lastDuelExpiryVisualRefreshRealtime;
        private const string DuelInboxRowClass = "duel-inbox-row";
        private const string DuelInboxRowActiveClass = "duel-inbox-row-active";
        private const string DuelInboxRowExpiredClass = "duel-inbox-row-expired";
        private const string DuelInboxRowTerminalClass = "duel-inbox-row-terminal";
        private const string DuelInboxRowWinClass = "duel-inbox-row-win";
        private const string DuelInboxRowLossClass = "duel-inbox-row-loss";
        private const string DuelInboxRowDrawClass = "duel-inbox-row-draw";
        private const string DuelInboxRowSelectedClass = "duel-inbox-row-selected";
        private const string DuelRowChipClass = "duel-row-chip";
        private const string DuelRowChipOpenClass = "duel-row-chip-open";
        private const string DuelRowChipExpiredClass = "duel-row-chip-expired";
        private const string DuelRowChipWinClass = "duel-row-chip-win";
        private const string DuelRowChipLossClass = "duel-row-chip-loss";
        private const string DuelRowChipDrawClass = "duel-row-chip-draw";
        private const string DuelRowChipDeclinedClass = "duel-row-chip-declined";
        private const string DuelPillNeutralClass = "hud-duel-pill-neutral";
        private const string DuelPillOpenClass = "hud-duel-pill-open";
        private const string DuelPillExpiredClass = "hud-duel-pill-expired";
        private const string DuelPillWinClass = "hud-duel-pill-win";
        private const string DuelPillLossClass = "hud-duel-pill-loss";
        private const string DuelPillDrawClass = "hud-duel-pill-draw";
        private const string DuelPillHiddenClass = "hud-duel-pill-hidden";
        private sealed class DuelExpiryRadialState
        {
            public float Progress;
            public Color FillColor;
            public Color TrackColor;
            public Color CenterColor;
        }

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
            if (FindFirstObjectByType<DuelCoordinator>() == null)
            {
                gameObject.AddComponent<DuelCoordinator>();
            }

            _root = _document?.rootVisualElement;
            if (_root == null)
            {
                return;
            }

            _solBalanceLabel = _root.Q<Label>("wallet-sol-balance-label");
            _skrBalanceLabel = _root.Q<Label>("wallet-skr-balance-label");
            _playerHealthRoot = _root.Q<VisualElement>("hud-player-health");
            _playerHealthFill = _root.Q<VisualElement>("hud-player-health-fill");
            _playerHealthLabel = _root.Q<Label>("hud-player-health-label");
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
            _exitConfirmCard = _root.Q<VisualElement>("exit-confirm-card");
            _exitConfirmCloseButton = _root.Q<Button>("btn-exit-confirm-close");
            _exitConfirmLeaveButton = _root.Q<Button>("btn-exit-confirm-leave");
            _sessionFeeOverlay = _root.Q<VisualElement>("session-fee-overlay");
            _sessionFeeCard = _root.Q<VisualElement>("session-fee-card");
            _sessionFeeMessageLabel = _root.Q<Label>("session-fee-message");
            _sessionFeeNeededLabel = _root.Q<Label>("session-fee-needed");
            _sessionFeeSolBalanceLabel = _root.Q<Label>("session-fee-balance-sol");
            _sessionFeeSkrBalanceLabel = _root.Q<Label>("session-fee-balance-skr");
            _sessionFeeCloseButton = _root.Q<Button>("btn-session-fee-close");
            _sessionFeeTopUpButton = _root.Q<Button>("btn-session-fee-topup");
            _sessionFeeSolCustomButton = _root.Q<Button>("btn-session-sol-custom");
            _sessionFeeSkrCustomButton = _root.Q<Button>("btn-session-skr-custom");
            _sessionFeeSolCustomInput = _root.Q<TextField>("session-sol-custom-input");
            _sessionFeeSkrCustomInput = _root.Q<TextField>("session-skr-custom-input");
            _duelInboxButton = _root.Q<Button>("btn-duel-inbox");
            _duelIncomingCtaButton = _root.Q<Button>("btn-duel-incoming-cta");
            _duelInboxCountLabel = _root.Q<Label>("duel-inbox-count");
            _duelChallengeOverlay = _root.Q<VisualElement>("duel-challenge-overlay");
            _duelChallengeCard = _root.Q<VisualElement>("duel-challenge-card");
            _duelChallengeSubtitle = _root.Q<Label>("duel-challenge-subtitle");
            _duelBreakdownStakeValue = _root.Q<Label>("duel-breakdown-stake");
            _duelBreakdownTaxValue = _root.Q<Label>("duel-breakdown-tax");
            _duelBreakdownWinnerValue = _root.Q<Label>("duel-breakdown-winner");
            _duelChallengeFeeLine = _root.Q<Label>("duel-challenge-fee-line");
            _duelChallengeConfirmButton = _root.Q<Button>("btn-duel-challenge-confirm");
            _duelChallengeCancelButton = _root.Q<Button>("btn-duel-challenge-cancel");
            _duelSubmitConfirmOverlay = _root.Q<VisualElement>("duel-submit-confirm-overlay");
            _duelSubmitConfirmCard = _root.Q<VisualElement>("duel-submit-confirm-card");
            _duelSubmitConfirmMessageLabel = _root.Q<Label>("duel-submit-confirm-message");
            _duelSubmitConfirmCloseButton = _root.Q<Button>("btn-duel-submit-confirm-close");
            _duelSubmitConfirmCancelButton = _root.Q<Button>("btn-duel-submit-cancel");
            _duelSubmitConfirmConfirmButton = _root.Q<Button>("btn-duel-submit-confirm");
            _duelInboxOverlay = _root.Q<VisualElement>("duel-inbox-overlay");
            _duelInboxCard = _root.Q<VisualElement>("duel-inbox-card");
            _duelInboxList = _root.Q<ScrollView>("duel-inbox-list");
            _duelInboxDetailTitle = _root.Q<Label>("duel-inbox-detail-title");
            _duelInboxDetailStatusPill = _root.Q<Label>("duel-inbox-detail-status-pill");
            _duelInboxDetailResultPill = _root.Q<Label>("duel-inbox-detail-result-pill");
            _duelInboxDetailPlayerValue = _root.Q<Label>("duel-inbox-detail-player-value");
            _duelInboxDetailStakeValue = _root.Q<Label>("duel-inbox-detail-stake-value");
            _duelInboxDetailWinnerValue = _root.Q<Label>("duel-inbox-detail-winner-value");
            _duelInboxDetailExpiryRadial = _root.Q<VisualElement>("duel-inbox-detail-expiry-radial");
            _duelInboxDetailFee = _root.Q<Label>("duel-inbox-detail-fee");
            _duelInboxAcceptButton = _root.Q<Button>("btn-duel-inbox-accept");
            _duelInboxDeclineButton = _root.Q<Button>("btn-duel-inbox-decline");
            _duelInboxClaimExpiredButton = _root.Q<Button>("btn-duel-inbox-claim-expired");
            _duelInboxCloseButton = _root.Q<Button>("btn-duel-inbox-close");
            _duelResultOverlay = _root.Q<VisualElement>("duel-result-overlay");
            _duelResultCard = _root.Q<VisualElement>("duel-result-card");
            _duelResultTitle = _root.Q<Label>("duel-result-title");
            _duelResultMessage = _root.Q<Label>("duel-result-message");
            _duelResultDismissButton = _root.Q<Button>("btn-duel-result-dismiss");
            HideExitConfirmModal();
            HideSessionFeeModal();
            HideDuelChallengeModal();
            HideDuelSubmitConfirmModal();
            HideDuelInboxModal();
            HideDuelResultModal();
            HideItemTooltip();
            CacheTooltipDefaultStyles();
            ResolveHotbarGlowSpriteIfMissing();
            InitializeSessionFeeModalUi();
            InitializeDuelUi();

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
            if (_sessionFeeCloseButton != null)
            {
                _sessionFeeCloseButton.clicked += HandleSessionFeeCloseClicked;
            }
            if (_duelInboxButton != null)
            {
                _duelInboxButton.clicked += HandleDuelInboxOpenClicked;
            }
            if (_duelIncomingCtaButton != null)
            {
                _duelIncomingCtaButton.clicked += HandleDuelIncomingCtaClicked;
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
            if (_sessionFeeCloseButton != null)
            {
                _sessionFeeCloseButton.clicked -= HandleSessionFeeCloseClicked;
            }
            if (_duelInboxButton != null)
            {
                _duelInboxButton.clicked -= HandleDuelInboxOpenClicked;
            }
            if (_duelIncomingCtaButton != null)
            {
                _duelIncomingCtaButton.clicked -= HandleDuelIncomingCtaClicked;
            }
            UnbindDuelUiHandlers();
            UnbindSessionFeePresetHandlers();
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
            HideDuelSubmitConfirmModal();
            HideDuelResultModal();
            _exitConfirmTcs?.TrySetResult(false);
            _exitConfirmTcs = null;
        }

        private void Update()
        {
            _txIndicatorController?.Tick(Time.unscaledDeltaTime);
            _centerToastController?.Tick(Time.unscaledDeltaTime);
            if (Time.realtimeSinceStartup - _lastDuelExpiryVisualRefreshRealtime >= 0.12f)
            {
                _lastDuelExpiryVisualRefreshRealtime = Time.realtimeSinceStartup;
                RefreshDuelExpiryVisuals();
            }
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

        private void HandlePlayerStateUpdated(Chaindepth.Accounts.PlayerAccount playerState)
        {
            RefreshPlayerHealth(playerState);
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
            RefreshPlayerHealth(manager?.CurrentPlayerState);
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

        private void RefreshPlayerHealth(Chaindepth.Accounts.PlayerAccount playerState)
        {
            if (_playerHealthRoot == null || _playerHealthFill == null || _playerHealthLabel == null)
            {
                return;
            }

            if (playerState == null)
            {
                _playerHealthRoot.style.display = DisplayStyle.None;
                _playerHealthLabel.text = "--/--";
                _playerHealthFill.style.width = Length.Percent(100f);
                return;
            }

            var maxHp = Mathf.Max(1, (int)playerState.MaxHp);
            var currentHp = Mathf.Clamp((int)playerState.CurrentHp, 0, maxHp);
            var hpPercent = Mathf.Clamp01((float)currentHp / maxHp) * 100f;

            _playerHealthRoot.style.display = DisplayStyle.Flex;
            _playerHealthFill.style.width = Length.Percent(hpPercent);
            _playerHealthLabel.text = $"{currentHp}/{maxHp}";
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
            SetOverlayVisible(_exitConfirmOverlay, _exitConfirmCard, true);
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
            SetOverlayVisible(_exitConfirmOverlay, _exitConfirmCard, false);
        }

        private void HandleSessionFeeFundingRequired(string message)
        {
            ShowSessionFeeModal(message);
        }

        private void InitializeDuelUi()
        {
            _duelStakePresetButtons.Clear();
            BindDuelStakePresetButton("btn-duel-stake-1", DuelStakePresetValues[0]);
            BindDuelStakePresetButton("btn-duel-stake-5", DuelStakePresetValues[1]);
            BindDuelStakePresetButton("btn-duel-stake-20", DuelStakePresetValues[2]);
            BindDuelStakePresetButton("btn-duel-stake-50", DuelStakePresetValues[3]);

            if (_duelChallengeConfirmButton != null)
            {
                _duelChallengeConfirmButton.clicked += HandleDuelChallengeConfirmClicked;
            }

            if (_duelChallengeCancelButton != null)
            {
                _duelChallengeCancelButton.clicked += HandleDuelChallengeCancelClicked;
            }

            if (_duelSubmitConfirmCloseButton != null)
            {
                _duelSubmitConfirmCloseButton.clicked += HandleDuelSubmitConfirmCancelClicked;
            }

            if (_duelSubmitConfirmCancelButton != null)
            {
                _duelSubmitConfirmCancelButton.clicked += HandleDuelSubmitConfirmCancelClicked;
            }

            if (_duelSubmitConfirmConfirmButton != null)
            {
                _duelSubmitConfirmConfirmButton.clicked += HandleDuelSubmitConfirmConfirmedClicked;
            }

            if (_duelInboxCloseButton != null)
            {
                _duelInboxCloseButton.clicked += HandleDuelInboxCloseClicked;
            }

            if (_duelInboxAcceptButton != null)
            {
                _duelInboxAcceptButton.clicked += HandleDuelInboxAcceptClicked;
            }

            if (_duelInboxDeclineButton != null)
            {
                _duelInboxDeclineButton.clicked += HandleDuelInboxDeclineClicked;
            }

            if (_duelInboxClaimExpiredButton != null)
            {
                _duelInboxClaimExpiredButton.clicked += HandleDuelInboxClaimExpiredClicked;
            }
            if (_duelResultDismissButton != null)
            {
                _duelResultDismissButton.clicked += HandleDuelResultDismissClicked;
            }

            RefreshDuelSelectionStyles();
            RefreshDuelInboxCountLabel();
            RefreshDuelInboxDetail();
        }

        private void UnbindDuelUiHandlers()
        {
            for (var i = 0; i < _duelStakePresetButtons.Count; i += 1)
            {
                var button = _duelStakePresetButtons[i];
                button?.UnregisterCallback<ClickEvent>(HandleDuelStakePresetClicked);
            }

            if (_duelChallengeConfirmButton != null)
            {
                _duelChallengeConfirmButton.clicked -= HandleDuelChallengeConfirmClicked;
            }

            if (_duelChallengeCancelButton != null)
            {
                _duelChallengeCancelButton.clicked -= HandleDuelChallengeCancelClicked;
            }

            if (_duelSubmitConfirmCloseButton != null)
            {
                _duelSubmitConfirmCloseButton.clicked -= HandleDuelSubmitConfirmCancelClicked;
            }

            if (_duelSubmitConfirmCancelButton != null)
            {
                _duelSubmitConfirmCancelButton.clicked -= HandleDuelSubmitConfirmCancelClicked;
            }

            if (_duelSubmitConfirmConfirmButton != null)
            {
                _duelSubmitConfirmConfirmButton.clicked -= HandleDuelSubmitConfirmConfirmedClicked;
            }

            if (_duelInboxCloseButton != null)
            {
                _duelInboxCloseButton.clicked -= HandleDuelInboxCloseClicked;
            }

            if (_duelInboxAcceptButton != null)
            {
                _duelInboxAcceptButton.clicked -= HandleDuelInboxAcceptClicked;
            }

            if (_duelInboxDeclineButton != null)
            {
                _duelInboxDeclineButton.clicked -= HandleDuelInboxDeclineClicked;
            }

            if (_duelInboxClaimExpiredButton != null)
            {
                _duelInboxClaimExpiredButton.clicked -= HandleDuelInboxClaimExpiredClicked;
            }
            if (_duelResultDismissButton != null)
            {
                _duelResultDismissButton.clicked -= HandleDuelResultDismissClicked;
            }
        }

        private void BindDuelStakePresetButton(string buttonName, int stakeSkr)
        {
            var button = _root?.Q<Button>(buttonName);
            if (button == null)
            {
                return;
            }

            button.userData = stakeSkr;
            _duelStakePresetButtons.Add(button);
            button.RegisterCallback<ClickEvent>(HandleDuelStakePresetClicked);
        }

        public void OpenDuelChallengeForTarget(string walletKey, string displayName, PublicKey targetWallet)
        {
            if (targetWallet == null)
            {
                return;
            }

            _selectedDuelTarget = targetWallet;
            _selectedDuelTargetDisplayName = string.IsNullOrWhiteSpace(displayName)
                ? ShortWallet(walletKey)
                : displayName.Trim();
            if (string.IsNullOrWhiteSpace(_selectedDuelTargetDisplayName))
            {
                _selectedDuelTargetDisplayName = ShortWallet(targetWallet.Key);
            }

            if (_duelChallengeSubtitle != null)
            {
                _duelChallengeSubtitle.text = $"Challenge {_selectedDuelTargetDisplayName}";
            }

            RefreshDuelSelectionStyles();
            SetOverlayVisible(_duelChallengeOverlay, _duelChallengeCard, true);
        }

        public void UpdateDuelInbox(IReadOnlyList<DuelChallengeView> items, PublicKey localWallet, ulong? currentSlot = null)
        {
            _localWalletForDuelInbox = localWallet;
            _duelInboxCurrentSlot = currentSlot;
            if (_duelInboxCurrentSlot.HasValue)
            {
                _duelSlotSampleRealtime = Time.realtimeSinceStartup;
            }

            SetDuelInboxItems(items);
        }

        public void SetDuelActionBusy(bool isBusy)
        {
            _duelChallengeConfirmButton?.SetEnabled(!isBusy);
            _duelChallengeCancelButton?.SetEnabled(!isBusy);
            _duelSubmitConfirmCloseButton?.SetEnabled(!isBusy);
            _duelSubmitConfirmCancelButton?.SetEnabled(!isBusy);
            _duelSubmitConfirmConfirmButton?.SetEnabled(!isBusy);
            _duelInboxAcceptButton?.SetEnabled(!isBusy && _selectedDuelInboxItem != null);
            _duelInboxDeclineButton?.SetEnabled(!isBusy && _selectedDuelInboxItem != null);
            _duelInboxClaimExpiredButton?.SetEnabled(!isBusy && _selectedDuelInboxItem != null);
            _duelInboxCloseButton?.SetEnabled(!isBusy);
            _duelIncomingCtaButton?.SetEnabled(!isBusy && _duelIncomingCtaButton.resolvedStyle.display == DisplayStyle.Flex);
        }

        private void SetDuelInboxItems(IReadOnlyList<DuelChallengeView> items)
        {
            _duelInboxItems.Clear();
            if (items != null)
            {
                for (var i = 0; i < items.Count; i += 1)
                {
                    var challenge = items[i];
                    if (challenge == null)
                    {
                        continue;
                    }

                    _duelInboxItems.Add(challenge);
                }
            }

            if (_duelInboxList != null)
            {
                _duelInboxList.Clear();
            }

            _duelInboxRows.Clear();
            for (var i = 0; i < _duelInboxItems.Count; i += 1)
            {
                var challenge = _duelInboxItems[i];
                var rowButton = new Button { name = $"duel-inbox-row-{i}" };
                rowButton.AddToClassList(DuelInboxRowClass);
                BuildDuelInboxRowVisual(rowButton, challenge);
                rowButton.clicked += () => SelectDuelInboxItem(challenge);
                _duelInboxRows[rowButton] = challenge;
                _duelInboxList?.Add(rowButton);
            }

            if (_selectedDuelInboxItem == null || !_duelInboxItems.Any(item => item.Pda?.Key == _selectedDuelInboxItem.Pda?.Key))
            {
                _selectedDuelInboxItem = _duelInboxItems.Count > 0 ? _duelInboxItems[0] : null;
            }
            else
            {
                _selectedDuelInboxItem = _duelInboxItems.FirstOrDefault(item => item.Pda?.Key == _selectedDuelInboxItem.Pda?.Key);
            }

            RefreshDuelInboxRowVisualState();
            RefreshDuelInboxCountLabel();
            RefreshIncomingDuelRequestButton();
            RefreshDuelInboxDetail();
        }

        private void BuildDuelInboxRowVisual(Button rowButton, DuelChallengeView challenge)
        {
            if (rowButton == null || challenge == null)
            {
                return;
            }

            rowButton.Clear();
            var incoming = _localWalletForDuelInbox != null && challenge.IsIncomingFor(_localWalletForDuelInbox);
            var otherName = GetDuelDisplayNameForChallenge(challenge, incoming);
            var stake = challenge.StakeRaw / (double)LGConfig.SKR_MULTIPLIER;
            var winnerPayout = stake * 2d * (1d - (PlannedDuelFeeBps / 10_000d));

            var layout = new VisualElement();
            layout.AddToClassList("duel-row-layout");

            var top = new VisualElement();
            top.AddToClassList("duel-row-top");
            var nameLabel = new Label(incoming ? $"From {otherName}" : $"Vs {otherName}");
            nameLabel.AddToClassList("duel-row-name");
            top.Add(nameLabel);

            var statusChip = new Label(GetStatusChipText(challenge));
            statusChip.AddToClassList(DuelRowChipClass);
            AddStatusChipClass(statusChip, challenge);
            var statusWrap = new VisualElement();
            statusWrap.AddToClassList("duel-row-status-wrap");
            statusWrap.Add(statusChip);
            if (incoming &&
                challenge.IsOpenLike &&
                !IsChallengeExpiredOpen(challenge) &&
                TryGetExpiryProgress(challenge, out var rowProgress))
            {
                var expiryRadial = new VisualElement();
                expiryRadial.AddToClassList("duel-row-expiry-radial");
                ApplyExpiryRadialState(expiryRadial, rowProgress);
                statusWrap.Add(expiryRadial);
            }

            top.Add(statusWrap);

            var bottom = new VisualElement();
            bottom.AddToClassList("duel-row-bottom");
            var metricLabel = new Label($"Stake {stake:F3} SKR");
            metricLabel.AddToClassList("duel-row-metrics");
            bottom.Add(metricLabel);
            var payoutLabel = new Label(BuildDuelSidebarPayoutText(challenge, winnerPayout));
            payoutLabel.AddToClassList("duel-row-metrics");
            bottom.Add(payoutLabel);

            layout.Add(top);
            layout.Add(bottom);
            rowButton.Add(layout);
        }

        private void HandleDuelStakePresetClicked(ClickEvent clickEvent)
        {
            if (clickEvent.currentTarget is not Button button || button.userData is not int stake)
            {
                return;
            }

            _selectedDuelStakeSkr = Mathf.Max(1, stake);
            RefreshDuelSelectionStyles();
        }

        private void RefreshDuelSelectionStyles()
        {
            for (var i = 0; i < _duelStakePresetButtons.Count; i += 1)
            {
                var button = _duelStakePresetButtons[i];
                if (button == null || button.userData is not int stake)
                {
                    continue;
                }

                SetSelectedClass(button, stake == _selectedDuelStakeSkr);
            }

            var winnerPayoutSkr = (_selectedDuelStakeSkr * 2d) * (1d - (PlannedDuelFeeBps / 10_000d));
            var stakeSkr = _selectedDuelStakeSkr;
            var taxSkr = (stakeSkr * 2d) * (PlannedDuelFeeBps / 10_000d);
            SetLabel(_duelBreakdownStakeValue, $"{FormatSkrAmount(stakeSkr)} SKR");
            SetLabel(_duelBreakdownTaxValue, $"{FormatSkrAmount(taxSkr)} SKR");
            SetLabel(_duelBreakdownWinnerValue, $"{FormatSkrAmount(winnerPayoutSkr)} SKR");
            if (_duelChallengeFeeLine != null)
            {
                _duelChallengeFeeLine.text = "Draw refunds both players.";
            }

            if (_duelChallengeConfirmButton != null)
            {
                var canCreate = !HasOpenOutgoingDuel();
                _duelChallengeConfirmButton.SetEnabled(canCreate);
                if (!canCreate && _duelChallengeFeeLine != null)
                {
                    _duelChallengeFeeLine.text =
                        "You already have an outgoing duel request. Resolve or expire it before sending a new one.";
                }
            }
        }

        private void HandleDuelChallengeConfirmClicked()
        {
            if (_selectedDuelTarget == null)
            {
                return;
            }

            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Primary);
            ShowDuelSubmitConfirmModal();
        }

        private void HandleDuelChallengeCancelClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            HideDuelChallengeModal();
        }

        private void ShowDuelSubmitConfirmModal()
        {
            var stakeSkr = _selectedDuelStakeSkr;
            SetLabel(
                _duelSubmitConfirmMessageLabel,
                $"You are about to stake {FormatSkrAmount(stakeSkr)} SKR in a random duel.\nThis can potentially be lost and not retrieved.");
            SetOverlayVisible(_duelSubmitConfirmOverlay, _duelSubmitConfirmCard, true);
        }

        private void HideDuelSubmitConfirmModal()
        {
            SetOverlayVisible(_duelSubmitConfirmOverlay, _duelSubmitConfirmCard, false);
        }

        private void HandleDuelSubmitConfirmCancelClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            HideDuelSubmitConfirmModal();
        }

        private void HandleDuelSubmitConfirmConfirmedClicked()
        {
            if (_selectedDuelTarget == null)
            {
                return;
            }

            // Prevent accidental double-submit from rapid taps on mobile.
            _duelSubmitConfirmConfirmButton?.SetEnabled(false);
            _duelSubmitConfirmCancelButton?.SetEnabled(false);
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Confirm);
            var stakeRaw = (ulong)_selectedDuelStakeSkr * LGConfig.SKR_MULTIPLIER;
            OnDuelChallengeRequested?.Invoke(_selectedDuelTarget, stakeRaw);
            HideDuelSubmitConfirmModal();
            HideDuelChallengeModal();
        }

        private void HandleDuelInboxOpenClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Nav);
            SetOverlayVisible(_duelInboxOverlay, _duelInboxCard, true);
            RefreshDuelInboxDetail();
        }

        private void HandleDuelIncomingCtaClicked()
        {
            var incomingChallenge = GetPrimaryIncomingOpenChallenge();
            if (incomingChallenge == null)
            {
                HandleDuelInboxOpenClicked();
                return;
            }

            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Nav);
            _selectedDuelInboxItem = incomingChallenge;
            SetOverlayVisible(_duelInboxOverlay, _duelInboxCard, true);
            RefreshDuelInboxRowVisualState();
            RefreshDuelInboxDetail();
        }

        private void HandleDuelInboxCloseClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            HideDuelInboxModal();
        }

        private void SelectDuelInboxItem(DuelChallengeView challenge)
        {
            _selectedDuelInboxItem = challenge;
            RefreshDuelInboxRowVisualState();
            RefreshDuelInboxDetail();
        }

        private void RefreshDuelInboxDetail()
        {
            if (_selectedDuelInboxItem == null)
            {
                SetDetailStatusPillVisible(true);
                SetLabel(_duelInboxDetailTitle, "DUELS");
                SetLabel(_duelInboxDetailPlayerValue, "-");
                SetLabel(_duelInboxDetailStakeValue, "- SKR");
                SetLabel(_duelInboxDetailWinnerValue, "- SKR");
                SetDetailStatusPill("NO REQUEST", DuelPillNeutralClass);
                SetDetailResultPill(string.Empty, string.Empty);
                if (_duelInboxDetailExpiryRadial != null)
                {
                    _duelInboxDetailExpiryRadial.style.display = DisplayStyle.None;
                }
                SetLabel(_duelInboxDetailFee, string.Empty);
                _duelInboxAcceptButton?.SetEnabled(false);
                _duelInboxDeclineButton?.SetEnabled(false);
                _duelInboxClaimExpiredButton?.SetEnabled(false);
                return;
            }

            var challenge = _selectedDuelInboxItem;
            var incoming = _localWalletForDuelInbox != null && challenge.IsIncomingFor(_localWalletForDuelInbox);
            var stake = challenge.StakeRaw / (double)LGConfig.SKR_MULTIPLIER;
            var winnerPayout = stake * 2d * (1d - (PlannedDuelFeeBps / 10_000d));
            var isExpiredOpen = IsChallengeExpiredOpen(challenge);
            var statusText = GetStatusChipText(challenge);
            var localWon = _localWalletForDuelInbox != null &&
                           challenge.Status == DuelChallengeStatus.Settled &&
                           string.Equals(challenge.Winner?.Key, _localWalletForDuelInbox.Key, StringComparison.Ordinal);
            var localLost = _localWalletForDuelInbox != null &&
                            challenge.Status == DuelChallengeStatus.Settled &&
                            !challenge.IsDraw &&
                            challenge.Winner != null &&
                            !string.Equals(challenge.Winner.Key, _localWalletForDuelInbox.Key, StringComparison.Ordinal);

            SetLabel(_duelInboxDetailTitle, incoming ? "INCOMING DUEL" : "OUTGOING DUEL");
            SetLabel(_duelInboxDetailPlayerValue, GetDuelDisplayNameForChallenge(challenge, incoming));
            SetLabel(_duelInboxDetailStakeValue, $"{stake:F3} SKR");
            SetLabel(_duelInboxDetailWinnerValue, $"{winnerPayout:F3} SKR");
            if (challenge.Status == DuelChallengeStatus.Settled)
            {
                SetDetailStatusPillVisible(false);
                if (challenge.IsDraw)
                {
                    SetDetailResultPill("DRAW", DuelPillDrawClass);
                }
                else if (localWon)
                {
                    SetDetailResultPill("YOU WON", DuelPillWinClass);
                }
                else if (localLost)
                {
                    SetDetailResultPill("YOU LOST", DuelPillLossClass);
                }
                else
                {
                    SetDetailResultPill("SETTLED", DuelPillNeutralClass);
                }
            }
            else
            {
                SetDetailStatusPillVisible(true);
                SetDetailStatusPill(statusText, GetDetailStatusPillClass(challenge));
                SetDetailResultPill(string.Empty, string.Empty);
            }

            if (incoming &&
                challenge.IsOpenLike &&
                !isExpiredOpen &&
                TryGetExpiryProgress(challenge, out var detailProgress))
            {
                if (_duelInboxDetailExpiryRadial != null)
                {
                    _duelInboxDetailExpiryRadial.style.display = DisplayStyle.Flex;
                    ApplyExpiryRadialState(_duelInboxDetailExpiryRadial, detailProgress);
                }
            }
            else if (_duelInboxDetailExpiryRadial != null)
            {
                _duelInboxDetailExpiryRadial.style.display = DisplayStyle.None;
            }

            SetLabel(_duelInboxDetailFee, "Draw refunds both players.");

            var canAcceptOrDecline = incoming && challenge.IsOpenLike && !isExpiredOpen;
            var canClaimExpired = isExpiredOpen;
            _duelInboxAcceptButton?.SetEnabled(canAcceptOrDecline);
            _duelInboxDeclineButton?.SetEnabled(canAcceptOrDecline);
            _duelInboxClaimExpiredButton?.SetEnabled(canClaimExpired);
        }

        private void HandleDuelInboxAcceptClicked()
        {
            if (_selectedDuelInboxItem?.Pda == null)
            {
                return;
            }

            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Confirm);
            HideDuelInboxModal();
            OnDuelAcceptRequested?.Invoke(_selectedDuelInboxItem.Pda);
        }

        private void HandleDuelInboxDeclineClicked()
        {
            if (_selectedDuelInboxItem?.Pda == null)
            {
                return;
            }

            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            OnDuelDeclineRequested?.Invoke(_selectedDuelInboxItem.Pda);
        }

        private void HandleDuelInboxClaimExpiredClicked()
        {
            if (_selectedDuelInboxItem?.Pda == null)
            {
                return;
            }

            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Primary);
            OnDuelClaimExpiredRequested?.Invoke(_selectedDuelInboxItem.Pda);
        }

        public void ShowDuelResultModal(string title, string message)
        {
            SetLabel(_duelResultTitle, string.IsNullOrWhiteSpace(title) ? "DUEL RESULT" : title.Trim());
            SetLabel(_duelResultMessage, string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim());
            SetOverlayVisible(_duelResultOverlay, _duelResultCard, true);
        }

        public void PrepareForDuelReplay()
        {
            HideDuelSubmitConfirmModal();
            HideDuelChallengeModal();
            HideDuelInboxModal();
            HideDuelResultModal();
        }

        private void HandleDuelResultDismissClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            HideDuelResultModal();
        }

        private void RefreshDuelInboxCountLabel()
        {
            if (_duelInboxCountLabel == null)
            {
                return;
            }

            var incomingOpen = _duelInboxItems.Count(item =>
                _localWalletForDuelInbox != null &&
                item.IsIncomingFor(_localWalletForDuelInbox) &&
                item.IsOpenLike &&
                !IsChallengeExpiredOpen(item));
            _duelInboxCountLabel.text = incomingOpen > 0 ? incomingOpen.ToString() : string.Empty;
            _duelInboxCountLabel.style.display = incomingOpen > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshIncomingDuelRequestButton()
        {
            if (_duelIncomingCtaButton == null)
            {
                return;
            }

            var incomingChallenge = GetPrimaryIncomingOpenChallenge();
            if (incomingChallenge == null)
            {
                _duelIncomingCtaChallengePdaKey = string.Empty;
                _duelIncomingCtaButton.style.display = DisplayStyle.None;
                _duelIncomingCtaButton.SetEnabled(false);
                return;
            }

            var stakeSkr = incomingChallenge.StakeRaw / (double)LGConfig.SKR_MULTIPLIER;
            var pdaKey = incomingChallenge.Pda?.Key ?? string.Empty;
            _duelIncomingCtaButton.text = $"DUEL REQUEST [{FormatSkrAmount(stakeSkr)} SKR]";
            _duelIncomingCtaButton.style.display = DisplayStyle.Flex;
            _duelIncomingCtaButton.SetEnabled(true);

            if (!string.Equals(_duelIncomingCtaChallengePdaKey, pdaKey, StringComparison.Ordinal))
            {
                _duelIncomingCtaChallengePdaKey = pdaKey;
                ModalPopAnimator.PlayOpen(_duelIncomingCtaButton);
            }
        }

        private DuelChallengeView GetPrimaryIncomingOpenChallenge()
        {
            if (_localWalletForDuelInbox == null)
            {
                return null;
            }

            for (var i = 0; i < _duelInboxItems.Count; i += 1)
            {
                var challenge = _duelInboxItems[i];
                if (challenge == null)
                {
                    continue;
                }

                if (!challenge.IsIncomingFor(_localWalletForDuelInbox))
                {
                    continue;
                }

                if (!challenge.IsOpenLike || IsChallengeExpiredOpen(challenge))
                {
                    continue;
                }

                return challenge;
            }

            return null;
        }

        private void RefreshDuelExpiryVisuals()
        {
            if (_duelInboxRows != null && _duelInboxRows.Count > 0)
            {
                foreach (var kvp in _duelInboxRows)
                {
                    var rowButton = kvp.Key;
                    var challenge = kvp.Value;
                    if (rowButton == null || challenge == null)
                    {
                        continue;
                    }

                    var radial = rowButton.Q<VisualElement>(className: "duel-row-expiry-radial");
                    if (radial == null)
                    {
                        continue;
                    }

                    if (TryGetExpiryProgress(challenge, out var rowProgress))
                    {
                        ApplyExpiryRadialState(radial, rowProgress);
                    }
                }
            }

            if (_duelInboxDetailExpiryRadial != null &&
                _selectedDuelInboxItem != null &&
                TryGetExpiryProgress(_selectedDuelInboxItem, out var detailProgress))
            {
                ApplyExpiryRadialState(_duelInboxDetailExpiryRadial, detailProgress);
            }
        }

        private ulong? GetEstimatedCurrentSlot()
        {
            if (!_duelInboxCurrentSlot.HasValue)
            {
                return null;
            }

            var elapsedSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - _duelSlotSampleRealtime);
            var slotDelta = (ulong)Mathf.FloorToInt(elapsedSeconds / 0.4f);
            return _duelInboxCurrentSlot.Value + slotDelta;
        }

        private bool IsChallengeExpiredOpen(DuelChallengeView challenge)
        {
            var estimatedSlot = GetEstimatedCurrentSlot();
            return challenge != null &&
                   challenge.Status == DuelChallengeStatus.Open &&
                   estimatedSlot.HasValue &&
                   estimatedSlot.Value > challenge.ExpiresAtSlot;
        }

        private bool TryGetExpiryProgress(DuelChallengeView challenge, out float progress)
        {
            progress = 0f;
            var estimatedSlot = GetEstimatedCurrentSlot();
            if (challenge == null || !estimatedSlot.HasValue)
            {
                return false;
            }

            var currentSlot = estimatedSlot.Value;
            var durationSlots = challenge.ExpiresAtSlot > challenge.RequestedSlot
                ? challenge.ExpiresAtSlot - challenge.RequestedSlot
                : 1UL;
            var remainingSlots = challenge.ExpiresAtSlot > currentSlot
                ? challenge.ExpiresAtSlot - currentSlot
                : 0UL;
            progress = Mathf.Clamp01((float)remainingSlots / Mathf.Max(1f, durationSlots));
            return true;
        }

        private static Color GetExpiryFillColor(float progress)
        {
            var low = new Color(0.82f, 0.22f, 0.20f, 0.98f);
            var high = new Color(0.28f, 0.78f, 0.42f, 0.98f);
            return Color.Lerp(low, high, Mathf.Clamp01(progress));
        }

        private void ApplyExpiryRadialState(VisualElement element, float progress)
        {
            if (element == null)
            {
                return;
            }

            var state = element.userData as DuelExpiryRadialState;
            if (state == null)
            {
                state = new DuelExpiryRadialState();
                element.userData = state;
                element.generateVisualContent += context => DrawExpiryRadial(context, element);
            }

            state.Progress = Mathf.Clamp01(progress);
            state.TrackColor = new Color(0.15f, 0.20f, 0.23f, 0.95f);
            state.FillColor = GetExpiryFillColor(state.Progress);
            state.CenterColor = new Color(0.05f, 0.09f, 0.10f, 0.92f);
            element.MarkDirtyRepaint();
        }

        private static void DrawExpiryRadial(MeshGenerationContext context, VisualElement element)
        {
            if (element?.userData is not DuelExpiryRadialState state)
            {
                return;
            }

            var rect = element.contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            var center = rect.center;
            var radius = Mathf.Min(rect.width, rect.height) * 0.5f - 1f;
            if (radius <= 0f)
            {
                return;
            }

            var painter = context.painter2D;
            DrawFilledCircle(painter, center, radius, state.TrackColor);
            if (state.Progress > 0f)
            {
                DrawFilledSector(painter, center, radius, state.Progress, state.FillColor);
            }

            DrawFilledCircle(painter, center, radius * 0.58f, state.CenterColor);
        }

        private static void DrawFilledCircle(Painter2D painter, Vector2 center, float radius, Color color)
        {
            const int segments = 48;
            painter.fillColor = color;
            painter.BeginPath();
            for (var i = 0; i <= segments; i += 1)
            {
                var angleDeg = -90f + (360f * i / segments);
                var angleRad = angleDeg * Mathf.Deg2Rad;
                var point = new Vector2(
                    center.x + Mathf.Cos(angleRad) * radius,
                    center.y + Mathf.Sin(angleRad) * radius);
                if (i == 0)
                {
                    painter.MoveTo(point);
                }
                else
                {
                    painter.LineTo(point);
                }
            }

            painter.ClosePath();
            painter.Fill();
        }

        private static void DrawFilledSector(Painter2D painter, Vector2 center, float radius, float progress, Color color)
        {
            progress = Mathf.Clamp01(progress);
            var sweepDeg = 360f * progress;
            var segments = Mathf.Max(4, Mathf.CeilToInt(48f * progress));
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(center);
            for (var i = 0; i <= segments; i += 1)
            {
                var angleDeg = -90f - (sweepDeg * i / Mathf.Max(1, segments));
                var angleRad = angleDeg * Mathf.Deg2Rad;
                var point = new Vector2(
                    center.x + Mathf.Cos(angleRad) * radius,
                    center.y + Mathf.Sin(angleRad) * radius);
                painter.LineTo(point);
            }

            painter.ClosePath();
            painter.Fill();
        }

        private string GetStatusChipText(DuelChallengeView challenge)
        {
            if (challenge == null)
            {
                return "UNKNOWN";
            }

            if (IsChallengeExpiredOpen(challenge))
            {
                return "EXPIRED";
            }

            return challenge.Status switch
            {
                DuelChallengeStatus.Open => "OPEN",
                DuelChallengeStatus.PendingRandomness => "ROLLING",
                DuelChallengeStatus.Settled => GetSettledOutcomeLabel(challenge),
                DuelChallengeStatus.Declined => "DECLINED",
                DuelChallengeStatus.Expired => "EXPIRED",
                _ => "UNKNOWN"
            };
        }

        private string GetSettledOutcomeLabel(DuelChallengeView challenge)
        {
            if (challenge == null)
            {
                return "SETTLED";
            }

            if (challenge.IsDraw)
            {
                return "DRAW";
            }

            var localWallet = _localWalletForDuelInbox;
            if (localWallet != null && challenge.Winner != null)
            {
                return string.Equals(challenge.Winner.Key, localWallet.Key, StringComparison.Ordinal)
                    ? "WIN"
                    : "LOSE";
            }

            return "SETTLED";
        }

        private string BuildDuelSidebarPayoutText(DuelChallengeView challenge, double winnerPayout)
        {
            if (challenge == null)
            {
                return string.Empty;
            }

            if (challenge.Status == DuelChallengeStatus.Settled)
            {
                if (challenge.IsDraw)
                {
                    return "Draw";
                }

                if (_localWalletForDuelInbox != null && challenge.Winner != null)
                {
                    var localWon = string.Equals(
                        challenge.Winner.Key,
                        _localWalletForDuelInbox.Key,
                        StringComparison.Ordinal);
                    return localWon ? $"Won {winnerPayout:F3} SKR" : "Lost";
                }

                return "Settled";
            }

            if (IsChallengeExpiredOpen(challenge))
            {
                return "No payout";
            }

            if (challenge.Status == DuelChallengeStatus.Declined)
            {
                return "Declined";
            }

            return "Pending";
        }

        private string GetDuelDisplayNameForChallenge(DuelChallengeView challenge, bool incoming)
        {
            if (challenge == null)
            {
                return "Unknown";
            }

            var snapshot = incoming
                ? challenge.ChallengerDisplayNameSnapshot
                : challenge.OpponentDisplayNameSnapshot;
            if (!string.IsNullOrWhiteSpace(snapshot))
            {
                return snapshot.Trim();
            }

            var wallet = incoming ? challenge.Challenger?.Key : challenge.Opponent?.Key;
            return ShortWallet(wallet);
        }

        private void AddStatusChipClass(VisualElement chip, DuelChallengeView challenge)
        {
            if (chip == null || challenge == null)
            {
                return;
            }

            if (IsChallengeExpiredOpen(challenge) || challenge.Status == DuelChallengeStatus.Expired)
            {
                chip.AddToClassList(DuelRowChipExpiredClass);
                return;
            }

            if (challenge.Status == DuelChallengeStatus.Open || challenge.Status == DuelChallengeStatus.PendingRandomness)
            {
                chip.AddToClassList(DuelRowChipOpenClass);
                return;
            }

            if (challenge.Status == DuelChallengeStatus.Declined)
            {
                chip.AddToClassList(DuelRowChipDeclinedClass);
                return;
            }

            if (challenge.Status == DuelChallengeStatus.Settled)
            {
                if (challenge.IsDraw)
                {
                    chip.AddToClassList(DuelRowChipDrawClass);
                    return;
                }

                var localWallet = _localWalletForDuelInbox;
                if (localWallet != null && challenge.Winner != null)
                {
                    var localWon = string.Equals(challenge.Winner.Key, localWallet.Key, StringComparison.Ordinal);
                    chip.AddToClassList(localWon ? DuelRowChipWinClass : DuelRowChipLossClass);
                    return;
                }
            }
        }

        private string GetDetailStatusPillClass(DuelChallengeView challenge)
        {
            if (challenge == null)
            {
                return DuelPillNeutralClass;
            }

            if (IsChallengeExpiredOpen(challenge) || challenge.Status == DuelChallengeStatus.Expired)
            {
                return DuelPillExpiredClass;
            }

            if (challenge.Status == DuelChallengeStatus.Open || challenge.Status == DuelChallengeStatus.PendingRandomness)
            {
                return DuelPillOpenClass;
            }

            if (challenge.Status == DuelChallengeStatus.Settled)
            {
                return challenge.IsDraw ? DuelPillDrawClass : DuelPillNeutralClass;
            }

            return DuelPillNeutralClass;
        }

        private void SetDetailStatusPill(string text, string stateClass)
        {
            if (_duelInboxDetailStatusPill == null)
            {
                return;
            }

            _duelInboxDetailStatusPill.RemoveFromClassList(DuelPillNeutralClass);
            _duelInboxDetailStatusPill.RemoveFromClassList(DuelPillOpenClass);
            _duelInboxDetailStatusPill.RemoveFromClassList(DuelPillExpiredClass);
            _duelInboxDetailStatusPill.RemoveFromClassList(DuelPillWinClass);
            _duelInboxDetailStatusPill.RemoveFromClassList(DuelPillLossClass);
            _duelInboxDetailStatusPill.RemoveFromClassList(DuelPillDrawClass);
            _duelInboxDetailStatusPill.AddToClassList(string.IsNullOrWhiteSpace(stateClass) ? DuelPillNeutralClass : stateClass);
            SetLabel(_duelInboxDetailStatusPill, text);
        }

        private void SetDetailResultPill(string text, string stateClass)
        {
            if (_duelInboxDetailResultPill == null)
            {
                return;
            }

            _duelInboxDetailResultPill.RemoveFromClassList(DuelPillHiddenClass);
            _duelInboxDetailResultPill.RemoveFromClassList(DuelPillNeutralClass);
            _duelInboxDetailResultPill.RemoveFromClassList(DuelPillOpenClass);
            _duelInboxDetailResultPill.RemoveFromClassList(DuelPillExpiredClass);
            _duelInboxDetailResultPill.RemoveFromClassList(DuelPillWinClass);
            _duelInboxDetailResultPill.RemoveFromClassList(DuelPillLossClass);
            _duelInboxDetailResultPill.RemoveFromClassList(DuelPillDrawClass);

            if (string.IsNullOrWhiteSpace(text))
            {
                SetLabel(_duelInboxDetailResultPill, string.Empty);
                _duelInboxDetailResultPill.AddToClassList(DuelPillHiddenClass);
                return;
            }

            _duelInboxDetailResultPill.AddToClassList(string.IsNullOrWhiteSpace(stateClass) ? DuelPillNeutralClass : stateClass);
            SetLabel(_duelInboxDetailResultPill, text);
        }

        private void SetDetailStatusPillVisible(bool visible)
        {
            if (_duelInboxDetailStatusPill == null)
            {
                return;
            }

            _duelInboxDetailStatusPill.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshDuelInboxRowVisualState()
        {
            if (_duelInboxRows == null || _duelInboxRows.Count == 0)
            {
                return;
            }

            foreach (var kvp in _duelInboxRows)
            {
                var row = kvp.Key;
                var challenge = kvp.Value;
                if (row == null || challenge == null)
                {
                    continue;
                }

                row.RemoveFromClassList(DuelInboxRowActiveClass);
                row.RemoveFromClassList(DuelInboxRowExpiredClass);
                row.RemoveFromClassList(DuelInboxRowTerminalClass);
                row.RemoveFromClassList(DuelInboxRowWinClass);
                row.RemoveFromClassList(DuelInboxRowLossClass);
                row.RemoveFromClassList(DuelInboxRowDrawClass);
                row.RemoveFromClassList(DuelInboxRowSelectedClass);

                if (IsChallengeExpiredOpen(challenge))
                {
                    row.AddToClassList(DuelInboxRowExpiredClass);
                }
                else if (challenge.IsOpenLike)
                {
                    row.AddToClassList(DuelInboxRowActiveClass);
                }
                else
                {
                    row.AddToClassList(DuelInboxRowTerminalClass);

                    if (challenge.Status == DuelChallengeStatus.Settled)
                    {
                        if (challenge.IsDraw)
                        {
                            row.AddToClassList(DuelInboxRowDrawClass);
                        }
                        else if (_localWalletForDuelInbox != null && challenge.Winner != null)
                        {
                            var localWon = string.Equals(
                                challenge.Winner.Key,
                                _localWalletForDuelInbox.Key,
                                StringComparison.Ordinal);
                            row.AddToClassList(localWon ? DuelInboxRowWinClass : DuelInboxRowLossClass);
                        }
                    }
                }

                if (_selectedDuelInboxItem != null &&
                    string.Equals(challenge.Pda?.Key, _selectedDuelInboxItem.Pda?.Key, StringComparison.Ordinal))
                {
                    row.AddToClassList(DuelInboxRowSelectedClass);
                }
            }
        }

        private void HideDuelChallengeModal()
        {
            SetOverlayVisible(_duelChallengeOverlay, _duelChallengeCard, false);
            HideDuelSubmitConfirmModal();
        }

        private void HideDuelInboxModal()
        {
            SetOverlayVisible(_duelInboxOverlay, _duelInboxCard, false);
        }

        private void HideDuelResultModal()
        {
            SetOverlayVisible(_duelResultOverlay, _duelResultCard, false);
        }

        private static string ShortWallet(string walletKey)
        {
            if (string.IsNullOrWhiteSpace(walletKey))
            {
                return "Unknown";
            }

            var trimmed = walletKey.Trim();
            if (trimmed.Length <= 10)
            {
                return trimmed;
            }

            return $"{trimmed.Substring(0, 4)}...{trimmed.Substring(trimmed.Length - 4)}";
        }

        private static string FormatSkrAmount(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private bool HasOpenOutgoingDuel()
        {
            if (_localWalletForDuelInbox == null)
            {
                return false;
            }

            for (var i = 0; i < _duelInboxItems.Count; i += 1)
            {
                var item = _duelInboxItems[i];
                if (item != null && item.IsOutgoingFrom(_localWalletForDuelInbox) && item.IsOpenLike)
                {
                    return true;
                }
            }

            return false;
        }

        private void InitializeSessionFeeModalUi()
        {
            _sessionFeeSolPresetButtons.Clear();
            _sessionFeeSkrPresetButtons.Clear();

            BindSessionFeePresetButton("btn-session-sol-005", isSol: true, SolTopUpPresetValues[0]);
            BindSessionFeePresetButton("btn-session-sol-01", isSol: true, SolTopUpPresetValues[1]);
            BindSessionFeePresetButton("btn-session-sol-05", isSol: true, SolTopUpPresetValues[2]);
            BindSessionFeePresetButton("btn-session-sol-1", isSol: true, SolTopUpPresetValues[3]);
            BindSessionFeePresetButton("btn-session-sol-0", isSol: true, 0d);
            BindSessionFeePresetButton("btn-session-skr-0", isSol: false, 0d);
            BindSessionFeePresetButton("btn-session-skr-1", isSol: false, SkrTopUpPresetValues[0]);
            BindSessionFeePresetButton("btn-session-skr-5", isSol: false, SkrTopUpPresetValues[1]);
            BindSessionFeePresetButton("btn-session-skr-20", isSol: false, SkrTopUpPresetValues[2]);
            BindSessionFeePresetButton("btn-session-skr-50", isSol: false, SkrTopUpPresetValues[3]);

            if (_sessionFeeSolCustomButton != null)
            {
                _sessionFeeSolCustomButton.clicked += HandleSessionFeeSolCustomClicked;
            }

            if (_sessionFeeSkrCustomButton != null)
            {
                _sessionFeeSkrCustomButton.clicked += HandleSessionFeeSkrCustomClicked;
            }

            if (_sessionFeeSolCustomInput != null)
            {
                _sessionFeeSolCustomInput.isDelayed = true;
                _sessionFeeSolCustomInput.RegisterValueChangedCallback(HandleSessionFeeCustomInputChanged);
            }

            if (_sessionFeeSkrCustomInput != null)
            {
                _sessionFeeSkrCustomInput.isDelayed = true;
                _sessionFeeSkrCustomInput.RegisterValueChangedCallback(HandleSessionFeeCustomInputChanged);
            }

            RefreshSessionFeeSelectionStyles();
        }

        private void UnbindSessionFeePresetHandlers()
        {
            for (var i = 0; i < _sessionFeeSolPresetButtons.Count; i += 1)
            {
                var button = _sessionFeeSolPresetButtons[i];
                if (button != null)
                {
                    button.UnregisterCallback<ClickEvent>(HandleSessionFeeSolPresetClicked);
                }
            }

            for (var i = 0; i < _sessionFeeSkrPresetButtons.Count; i += 1)
            {
                var button = _sessionFeeSkrPresetButtons[i];
                if (button != null)
                {
                    button.UnregisterCallback<ClickEvent>(HandleSessionFeeSkrPresetClicked);
                }
            }

            if (_sessionFeeSolCustomButton != null)
            {
                _sessionFeeSolCustomButton.clicked -= HandleSessionFeeSolCustomClicked;
            }

            if (_sessionFeeSkrCustomButton != null)
            {
                _sessionFeeSkrCustomButton.clicked -= HandleSessionFeeSkrCustomClicked;
            }

            if (_sessionFeeSolCustomInput != null)
            {
                _sessionFeeSolCustomInput.UnregisterValueChangedCallback(HandleSessionFeeCustomInputChanged);
            }

            if (_sessionFeeSkrCustomInput != null)
            {
                _sessionFeeSkrCustomInput.UnregisterValueChangedCallback(HandleSessionFeeCustomInputChanged);
            }
        }

        private void BindSessionFeePresetButton(string buttonName, bool isSol, double value)
        {
            var button = _root?.Q<Button>(buttonName);
            if (button == null)
            {
                return;
            }

            button.userData = value;
            if (isSol)
            {
                _sessionFeeSolPresetButtons.Add(button);
                button.RegisterCallback<ClickEvent>(HandleSessionFeeSolPresetClicked);
                return;
            }

            _sessionFeeSkrPresetButtons.Add(button);
            button.RegisterCallback<ClickEvent>(HandleSessionFeeSkrPresetClicked);
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

            _ = RefreshSessionFeeNeededLabelAsync();
            RefreshSessionFeeSelectionStyles();
            SetOverlayVisible(_sessionFeeOverlay, _sessionFeeCard, true);
            UpdateSessionFeeButtonsInteractable();
        }

        private void HideSessionFeeModal()
        {
            SetOverlayVisible(_sessionFeeOverlay, _sessionFeeCard, false);
        }

        private static void SetOverlayVisible(VisualElement overlay, VisualElement card, bool show)
        {
            if (overlay == null)
            {
                return;
            }

            var wasVisible = overlay.style.display == DisplayStyle.Flex;
            overlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            overlay.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
            overlay.style.opacity = show ? 1f : 0f;

            if (show && !wasVisible)
            {
                ModalPopAnimator.PlayOpen(card);
            }
        }

        private void HandleSessionFeeTopUpClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Primary);
            TopUpSessionFeeAsync().Forget();
        }

        private void HandleSessionFeeCloseClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            HideSessionFeeModal();
        }

        private void HandleSessionFeeSolPresetClicked(ClickEvent clickEvent)
        {
            if (_isFundingSessionFee)
            {
                return;
            }

            if (clickEvent.currentTarget is Button button &&
                button.userData is double value)
            {
                _selectedSolTopUp = Math.Max(0d, value);
            }

            _isSolCustomSelected = false;
            RefreshSessionFeeSelectionStyles();
        }

        private void HandleSessionFeeSkrPresetClicked(ClickEvent clickEvent)
        {
            if (_isFundingSessionFee)
            {
                return;
            }

            if (clickEvent.currentTarget is Button button &&
                button.userData is double value)
            {
                _selectedSkrTopUp = Mathf.Max(0, Mathf.RoundToInt((float)value));
            }

            _isSkrCustomSelected = false;
            RefreshSessionFeeSelectionStyles();
        }

        private void HandleSessionFeeSolCustomClicked()
        {
            if (_isFundingSessionFee)
            {
                return;
            }

            _isSolCustomSelected = true;
            _sessionFeeSolCustomInput?.Focus();
            RefreshSessionFeeSelectionStyles();
        }

        private void HandleSessionFeeSkrCustomClicked()
        {
            if (_isFundingSessionFee)
            {
                return;
            }

            _isSkrCustomSelected = true;
            _sessionFeeSkrCustomInput?.Focus();
            RefreshSessionFeeSelectionStyles();
        }

        private void HandleSessionFeeCustomInputChanged(ChangeEvent<string> _)
        {
            _isSolCustomSelected = IsCustomInputPopulated(_sessionFeeSolCustomInput);
            _isSkrCustomSelected = IsCustomInputPopulated(_sessionFeeSkrCustomInput);
            RefreshSessionFeeSelectionStyles();
        }

        private static bool IsCustomInputPopulated(TextField input)
        {
            return input != null && !string.IsNullOrWhiteSpace(input.value);
        }

        private void RefreshSessionFeeSelectionStyles()
        {
            if (IsCustomInputPopulated(_sessionFeeSolCustomInput))
            {
                _isSolCustomSelected = true;
            }

            if (IsCustomInputPopulated(_sessionFeeSkrCustomInput))
            {
                _isSkrCustomSelected = true;
            }

            for (var i = 0; i < _sessionFeeSolPresetButtons.Count; i += 1)
            {
                var button = _sessionFeeSolPresetButtons[i];
                if (button == null || !(button.userData is double value))
                {
                    continue;
                }

                SetSelectedClass(button, !_isSolCustomSelected && Math.Abs(value - _selectedSolTopUp) < 0.00001d);
            }

            for (var i = 0; i < _sessionFeeSkrPresetButtons.Count; i += 1)
            {
                var button = _sessionFeeSkrPresetButtons[i];
                if (button == null || !(button.userData is double value))
                {
                    continue;
                }

                SetSelectedClass(button, !_isSkrCustomSelected && Math.Abs(value - _selectedSkrTopUp) < 0.00001d);
            }

            SetSelectedClass(_sessionFeeSolCustomButton, _isSolCustomSelected);
            SetSelectedClass(_sessionFeeSkrCustomButton, _isSkrCustomSelected);
            SetSelectedClass(_sessionFeeSolCustomInput, _isSolCustomSelected);
            SetSelectedClass(_sessionFeeSkrCustomInput, _isSkrCustomSelected);
        }

        private static void SetSelectedClass(VisualElement element, bool isSelected)
        {
            if (element == null)
            {
                return;
            }

            if (isSelected)
            {
                element.AddToClassList("selected");
                return;
            }

            element.RemoveFromClassList("selected");
        }

        private async UniTaskVoid RefreshSessionFeeNeededLabelAsync()
        {
            if (_sessionFeeNeededLabel == null || walletSessionManager == null)
            {
                return;
            }

            var recommendedSolLamports = walletSessionManager.GetSessionRecommendedSolLamports();
            var recommendedSkrRaw = walletSessionManager.GetSessionRecommendedSkrRawAmount();
            var currentSolLamports = await walletSessionManager.GetSessionSignerSolBalanceLamportsAsync();
            var currentSkrRaw = await walletSessionManager.GetSessionSignerSkrBalanceRawAsync();
            if (!currentSolLamports.HasValue)
            {
                _sessionFeeNeededLabel.text = "Session balances";
                SetSessionBalanceLine(_sessionFeeSolBalanceLabel, "Session SOL: --", false);
                SetSessionBalanceLine(_sessionFeeSkrBalanceLabel, "Session SKR: --", false);
                return;
            }

            _sessionFeeNeededLabel.text = "Session balances";

            var currentSol = currentSolLamports.Value / 1_000_000_000d;
            var solIsLow = currentSolLamports.Value < recommendedSolLamports;
            SetSessionBalanceLine(
                _sessionFeeSolBalanceLabel,
                $"Session SOL: {currentSol:F3}",
                solIsLow);

            if (!currentSkrRaw.HasValue)
            {
                SetSessionBalanceLine(_sessionFeeSkrBalanceLabel, "Session SKR: --", false);
                return;
            }

            var currentSkr = currentSkrRaw.Value / (double)LGConfig.SKR_MULTIPLIER;
            var skrIsLow = currentSkrRaw.Value < recommendedSkrRaw;
            SetSessionBalanceLine(
                _sessionFeeSkrBalanceLabel,
                $"Session SKR: {currentSkr:F3}",
                skrIsLow);
        }

        private static void SetSessionBalanceLine(Label label, string text, bool isWarning)
        {
            if (label == null)
            {
                return;
            }

            label.text = text;
            if (isWarning)
            {
                label.AddToClassList("warning");
                return;
            }

            label.RemoveFromClassList("warning");
        }

        private bool TryResolveSessionTopUpAmounts(out ulong solLamports, out ulong skrRawAmount, out string validationError)
        {
            validationError = null;
            solLamports = 0UL;
            skrRawAmount = 0UL;

            double solAmount;
            if (_isSolCustomSelected)
            {
                if (!TryParsePositiveDouble(_sessionFeeSolCustomInput?.value, out solAmount))
                {
                    validationError = "Enter a valid custom SOL amount.";
                    return false;
                }
            }
            else
            {
                solAmount = _selectedSolTopUp;
            }

            double skrAmount;
            if (_isSkrCustomSelected)
            {
                if (!TryParsePositiveDouble(_sessionFeeSkrCustomInput?.value, out skrAmount))
                {
                    validationError = "Enter a valid custom SKR amount.";
                    return false;
                }
            }
            else
            {
                skrAmount = _selectedSkrTopUp;
            }

            if (solAmount < 0d || skrAmount < 0d)
            {
                validationError = "Top-up values must be non-negative.";
                return false;
            }

            solLamports = (ulong)Math.Round(solAmount * 1_000_000_000d, MidpointRounding.AwayFromZero);
            skrRawAmount = (ulong)Math.Round(
                skrAmount * LGConfig.SKR_MULTIPLIER,
                MidpointRounding.AwayFromZero);

            if (solLamports == 0UL && skrRawAmount == 0UL)
            {
                validationError = "Select a SOL or SKR amount greater than 0.";
                return false;
            }

            return true;
        }

        private static bool TryParsePositiveDouble(string value, out double parsed)
        {
            parsed = 0d;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
                   parsed > 0d;
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
                if (!TryResolveSessionTopUpAmounts(out var solLamports, out var skrRawAmount, out var validationError))
                {
                    SetStatus(validationError);
                    return;
                }

                var funded = await walletSessionManager.FundSessionWalletAsync(
                    solLamports,
                    skrRawAmount,
                    emitPromptStatus: true);
                if (funded)
                {
                    HideSessionFeeModal();
                    GameAudioManager.Instance?.PlayWorld(WorldSfxId.SessionWalletTopUp, transform.position);
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
            _sessionFeeCloseButton?.SetEnabled(!_isFundingSessionFee);

            for (var i = 0; i < _sessionFeeSolPresetButtons.Count; i += 1)
            {
                _sessionFeeSolPresetButtons[i]?.SetEnabled(!_isFundingSessionFee);
            }

            for (var i = 0; i < _sessionFeeSkrPresetButtons.Count; i += 1)
            {
                _sessionFeeSkrPresetButtons[i]?.SetEnabled(!_isFundingSessionFee);
            }

            _sessionFeeSolCustomButton?.SetEnabled(!_isFundingSessionFee);
            _sessionFeeSkrCustomButton?.SetEnabled(!_isFundingSessionFee);
            _sessionFeeSolCustomInput?.SetEnabled(!_isFundingSessionFee);
            _sessionFeeSkrCustomInput?.SetEnabled(!_isFundingSessionFee);
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
