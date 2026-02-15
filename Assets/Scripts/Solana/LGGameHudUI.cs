using System;
using System.Collections.Generic;
using Chaindepth.Accounts;
using Cysharp.Threading.Tasks;
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
        [Header("References")]
        [SerializeField] private LGManager manager;
        [SerializeField] private LGWalletSessionManager walletSessionManager;
        [SerializeField] private ItemRegistry itemRegistry;

        [Header("Scene Flow")]
        [SerializeField] private string backSceneName = "MenuScene";
        [SerializeField] private string loadingSceneName = "Loading";

        [Header("Refresh")]
        [SerializeField] private float hudRefreshSeconds = 3f;
        [SerializeField] private bool logDebugMessages = false;

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

        private readonly Dictionary<ItemId, VisualElement> _slotsByItemId = new();

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
        }

        private void OnEnable()
        {
            var root = _document?.rootVisualElement;
            if (root == null)
            {
                return;
            }

            _solBalanceLabel = root.Q<Label>("wallet-sol-balance-label");
            _skrBalanceLabel = root.Q<Label>("wallet-skr-balance-label");
            _jobInfoLabel = root.Q<Label>("hud-job-info");
            _statusLabel = root.Q<Label>("hud-status");
            _backButton = root.Q<Button>("hud-btn-back");
            _bagButton = root.Q<Button>("hud-btn-bag");
            _networkLabel = root.Q<Label>("wallet-network-label");
            _inventorySlotsContainer = root.Q<VisualElement>("hud-inventory-slots");

            SetLabel(_networkLabel, "DEVNET");

            if (_backButton != null)
            {
                _backButton.clicked += HandleBackClicked;
            }

            if (_bagButton != null)
            {
                _bagButton.clicked += HandleBagClicked;
            }

            if (manager != null)
            {
                manager.OnPlayerStateUpdated += HandlePlayerStateUpdated;
                manager.OnRoomStateUpdated += HandleRoomStateUpdated;
                manager.OnInventoryUpdated += HandleInventoryUpdated;
            }

            if (walletSessionManager != null)
            {
                walletSessionManager.OnStatus += HandleWalletSessionStatus;
                walletSessionManager.OnError += HandleWalletSessionError;
            }

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

            if (manager != null)
            {
                manager.OnPlayerStateUpdated -= HandlePlayerStateUpdated;
                manager.OnRoomStateUpdated -= HandleRoomStateUpdated;
                manager.OnInventoryUpdated -= HandleInventoryUpdated;
            }

            if (walletSessionManager != null)
            {
                walletSessionManager.OnStatus -= HandleWalletSessionStatus;
                walletSessionManager.OnError -= HandleWalletSessionError;
            }
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
        }

        private void HandleRoomStateUpdated(Chaindepth.Accounts.RoomAccount _)
        {
            RefreshJobInfo();
        }

        private void HandleBackClicked()
        {
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
            OnBagClicked?.Invoke();
        }

        private void HandleInventoryUpdated(InventoryAccount inventory)
        {
            RefreshInventorySlots(inventory);
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

            foreach (var item in inventory.Items)
            {
                if (item == null || item.Amount == 0)
                {
                    continue;
                }

                var itemId = LGDomainMapper.ToItemId(item.ItemId);
                var slot = CreateSlotElement(itemId, item.Amount);
                _inventorySlotsContainer.Add(slot);
                _slotsByItemId[itemId] = slot;
            }
        }

        private VisualElement CreateSlotElement(ItemId itemId, uint amount)
        {
            var slot = new VisualElement();
            slot.AddToClassList("hud-inventory-slot");

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

            return slot;
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
    }
}
