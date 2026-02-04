using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Solana.Unity.SDK;
using UnityEngine;
using UnityEngine.UIElements;

// Generated client types
using Chaindepth.Accounts;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Test UI for ChainDepth Solana program interactions using UI Toolkit
    /// Attach this to a GameObject with a UIDocument component
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ChainDepthTestUI : MonoBehaviour
    {
        private UIDocument _document;
        private VisualElement _root;
        private ChainDepthManager _manager;

        // Cached UI elements
        private Label _statusLabel;
        private Label _walletAddress;
        private Label _balanceLabel;
        private Label _globalInfo;
        private Label _playerInfo;
        private Label _roomInfo;
        private Label _currentPos;
        private Label _positionLabel;
        private Label _wallNorth;
        private Label _wallSouth;
        private Label _wallEast;
        private Label _wallWest;
        private Label _chestLabel;
        private Label _logText;
        private ScrollView _logScroll;

        // Buttons
        private Button _btnConnectInGame;
        private Button _btnAirdrop;
        private Button _btnDisconnect;
        private Button _btnFetchGlobal;
        private Button _btnFetchPlayer;
        private Button _btnFetchRoom;
        private Button _btnRefreshAll;
        private Button _btnMoveNorth;
        private Button _btnMoveSouth;
        private Button _btnMoveEast;
        private Button _btnMoveWest;
        private Button _btnJobNorth;
        private Button _btnJobSouth;
        private Button _btnJobEast;
        private Button _btnJobWest;
        private Button _btnLootChest;
        private Button _btnInitPlayer;
        private Label _playerStatus;

        private readonly List<string> _logMessages = new();
        private const int MAX_LOG_LINES = 50;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            _root = _document.rootVisualElement;
            QueryElements();
            SetupButtonCallbacks();
        }

        private void Start()
        {
            _manager = ChainDepthManager.Instance;
            if (_manager == null)
            {
                LogMessage("ERROR: ChainDepthManager not found!");
                Debug.LogError("[ChainDepthTestUI] ChainDepthManager not found! Make sure it's in the scene.");
                return;
            }

            SetupEventListeners();
            UpdateConnectionUI();
            LogMessage("ChainDepth Test UI initialized");
        }

        private void QueryElements()
        {
            // Labels
            _statusLabel = _root.Q<Label>("status-label");
            _walletAddress = _root.Q<Label>("wallet-address");
            _balanceLabel = _root.Q<Label>("balance-label");
            _globalInfo = _root.Q<Label>("global-info");
            _playerInfo = _root.Q<Label>("player-info");
            _roomInfo = _root.Q<Label>("room-info");
            _currentPos = _root.Q<Label>("current-pos");
            _positionLabel = _root.Q<Label>("position-label");
            _wallNorth = _root.Q<Label>("wall-north");
            _wallSouth = _root.Q<Label>("wall-south");
            _wallEast = _root.Q<Label>("wall-east");
            _wallWest = _root.Q<Label>("wall-west");
            _chestLabel = _root.Q<Label>("chest-label");
            _logText = _root.Q<Label>("log-text");
            _logScroll = _root.Q<ScrollView>("log-scroll");

            // Connection buttons
            _btnConnectInGame = _root.Q<Button>("btn-connect-ingame");
            _btnAirdrop = _root.Q<Button>("btn-airdrop");
            _btnDisconnect = _root.Q<Button>("btn-disconnect");

            // State buttons
            _btnFetchGlobal = _root.Q<Button>("btn-fetch-global");
            _btnFetchPlayer = _root.Q<Button>("btn-fetch-player");
            _btnFetchRoom = _root.Q<Button>("btn-fetch-room");
            _btnRefreshAll = _root.Q<Button>("btn-refresh-all");

            // Movement buttons
            _btnMoveNorth = _root.Q<Button>("btn-move-north");
            _btnMoveSouth = _root.Q<Button>("btn-move-south");
            _btnMoveEast = _root.Q<Button>("btn-move-east");
            _btnMoveWest = _root.Q<Button>("btn-move-west");

            // Job buttons
            _btnJobNorth = _root.Q<Button>("btn-job-north");
            _btnJobSouth = _root.Q<Button>("btn-job-south");
            _btnJobEast = _root.Q<Button>("btn-job-east");
            _btnJobWest = _root.Q<Button>("btn-job-west");

            // Chest button
            _btnLootChest = _root.Q<Button>("btn-loot-chest");
            
            // Player actions
            _btnInitPlayer = _root.Q<Button>("btn-init-player");
            _playerStatus = _root.Q<Label>("player-status");
        }

        private void SetupButtonCallbacks()
        {
            // Connection
            _btnConnectInGame?.RegisterCallback<ClickEvent>(_ => OnConnectInGameWallet());
            _btnAirdrop?.RegisterCallback<ClickEvent>(_ => OnRequestAirdrop());
            _btnDisconnect?.RegisterCallback<ClickEvent>(_ => OnDisconnect());

            // State fetching
            _btnFetchGlobal?.RegisterCallback<ClickEvent>(_ => OnFetchGlobalState());
            _btnFetchPlayer?.RegisterCallback<ClickEvent>(_ => OnFetchPlayerState());
            _btnFetchRoom?.RegisterCallback<ClickEvent>(_ => OnFetchCurrentRoom());
            _btnRefreshAll?.RegisterCallback<ClickEvent>(_ => OnRefreshAll());

            // Movement
            _btnMoveNorth?.RegisterCallback<ClickEvent>(_ => OnMove(ChainDepthConfig.DIRECTION_NORTH));
            _btnMoveSouth?.RegisterCallback<ClickEvent>(_ => OnMove(ChainDepthConfig.DIRECTION_SOUTH));
            _btnMoveEast?.RegisterCallback<ClickEvent>(_ => OnMove(ChainDepthConfig.DIRECTION_EAST));
            _btnMoveWest?.RegisterCallback<ClickEvent>(_ => OnMove(ChainDepthConfig.DIRECTION_WEST));

            // Jobs
            _btnJobNorth?.RegisterCallback<ClickEvent>(_ => OnJoinJob(ChainDepthConfig.DIRECTION_NORTH));
            _btnJobSouth?.RegisterCallback<ClickEvent>(_ => OnJoinJob(ChainDepthConfig.DIRECTION_SOUTH));
            _btnJobEast?.RegisterCallback<ClickEvent>(_ => OnJoinJob(ChainDepthConfig.DIRECTION_EAST));
            _btnJobWest?.RegisterCallback<ClickEvent>(_ => OnJoinJob(ChainDepthConfig.DIRECTION_WEST));

            // Chest
            _btnLootChest?.RegisterCallback<ClickEvent>(_ => OnLootChest());
            
            // Player actions
            _btnInitPlayer?.RegisterCallback<ClickEvent>(_ => OnInitPlayer());
        }

        private void SetupEventListeners()
        {
            _manager.OnGlobalStateUpdated += OnGlobalStateUpdated;
            _manager.OnPlayerStateUpdated += OnPlayerStateUpdated;
            _manager.OnRoomStateUpdated += OnRoomStateUpdated;
            _manager.OnTransactionSent += OnTransactionSent;
            _manager.OnError += OnErrorReceived;

            Web3.OnWalletChangeState += OnWalletStateChanged;
        }

        private void OnDestroy()
        {
            if (_manager != null)
            {
                _manager.OnGlobalStateUpdated -= OnGlobalStateUpdated;
                _manager.OnPlayerStateUpdated -= OnPlayerStateUpdated;
                _manager.OnRoomStateUpdated -= OnRoomStateUpdated;
                _manager.OnTransactionSent -= OnTransactionSent;
                _manager.OnError -= OnErrorReceived;
            }

            Web3.OnWalletChangeState -= OnWalletStateChanged;
        }

        #region Button Handlers

        private void OnConnectInGameWallet()
        {
            SetStatus("Creating in-game wallet...");
            LogMessage("Creating local in-game wallet...");
            
            ConnectInGameWalletAsync().Forget();
        }

        private async UniTaskVoid ConnectInGameWalletAsync()
        {
            try
            {
                if (Web3.Instance == null)
                {
                    LogMessage("ERROR: Web3.Instance is null - Add Web3 component to scene!");
                    SetStatus("Missing Web3 component");
                    return;
                }

                LogMessage($"Web3.Instance found, attempting login...");
                
                // Login with in-game wallet (creates a local keypair)
                var account = await Web3.Instance.LoginInGameWallet("testpassword");
                
                if (account != null)
                {
                    LogMessage($"In-game wallet created: {account.PublicKey}");
                    SetStatus("Wallet connected!");
                    UpdateConnectionUI();
                    await UpdateBalanceAsync();
                }
                else
                {
                    LogMessage("ERROR: LoginInGameWallet returned null");
                    LogMessage("Check Web3 component configuration in Inspector");
                    SetStatus("Connection failed - check Web3 config");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    LogMessage($"  Inner: {ex.InnerException.Message}");
                }
                SetStatus("Connection failed");
                Debug.LogException(ex);
            }
        }

        private void OnRequestAirdrop()
        {
            if (!IsWalletConnected())
            {
                SetStatus("Connect wallet first!");
                LogMessage("ERROR: Connect wallet before requesting airdrop");
                return;
            }
            
            SetStatus("Requesting airdrop...");
            LogMessage("Requesting 1 SOL airdrop...");
            
            RequestAirdropAsync().Forget();
        }

        private async UniTaskVoid RequestAirdropAsync()
        {
            try
            {
                var pubkey = Web3.Wallet.Account.PublicKey.Key;
                LogMessage($"Airdrop to: {pubkey}");
                
                // Request 1 SOL airdrop using RPC directly
                var rpc = global::Solana.Unity.Rpc.ClientFactory.GetClient("https://api.devnet.solana.com");
                var result = await rpc.RequestAirdropAsync(pubkey, 1_000_000_000); // 1 SOL in lamports
                
                if (result.WasSuccessful)
                {
                    LogMessage($"Airdrop requested! TX: {result.Result}");
                    SetStatus("Airdrop requested...");
                    
                    // Wait for confirmation then refresh balance
                    LogMessage("Waiting for confirmation...");
                    await UniTask.Delay(5000);
                    await UpdateBalanceAsync();
                    SetStatus("Airdrop confirmed!");
                }
                else
                {
                    LogMessage($"ERROR: Airdrop failed - {result.Reason}");
                    SetStatus("Airdrop failed");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR: Airdrop failed - {ex.Message}");
                SetStatus("Airdrop failed");
                Debug.LogException(ex);
            }
        }

        private async UniTask UpdateBalanceAsync()
        {
            try
            {
                if (!IsWalletConnected()) return;
                
                var balance = await Web3.Instance.WalletBase.GetBalance();
                var solBalance = balance / 1_000_000_000.0; // Convert lamports to SOL
                SetLabel(_balanceLabel, $"Balance: {solBalance:F4} SOL");
                LogMessage($"Balance: {solBalance:F4} SOL");
            }
            catch (Exception ex)
            {
                LogMessage($"Balance fetch error: {ex.Message}");
            }
        }

        private void OnDisconnect()
        {
            LogMessage("Disconnecting wallet...");
            Web3.Instance?.Logout();
            UpdateConnectionUI();
        }

        private void OnFetchGlobalState()
        {
            SetStatus("Fetching global state...");
            LogMessage("Fetching global state...");
            _manager.FetchGlobalState().Forget();
        }

        private void OnFetchPlayerState()
        {
            if (!IsWalletConnected())
            {
                SetStatus("Connect wallet first!");
                LogMessage("ERROR: Wallet not connected");
                return;
            }
            SetStatus("Fetching player state...");
            LogMessage("Fetching player state...");
            _manager.FetchPlayerState().Forget();
        }

        private void OnFetchCurrentRoom()
        {
            if (!IsWalletConnected())
            {
                SetStatus("Connect wallet first!");
                return;
            }
            SetStatus("Fetching current room...");
            LogMessage("Fetching room state...");
            _manager.FetchCurrentRoom().Forget();
        }

        private void OnRefreshAll()
        {
            if (!IsWalletConnected())
            {
                SetStatus("Connect wallet first!");
                return;
            }
            SetStatus("Refreshing all state...");
            LogMessage("Refreshing all state...");
            _manager.RefreshAllState().Forget();
        }

        private void OnMove(byte direction)
        {
            if (!IsWalletConnected())
            {
                SetStatus("Connect wallet first!");
                return;
            }

            var dirName = ChainDepthConfig.GetDirectionName(direction);
            SetStatus($"Moving {dirName}...");
            LogMessage($"Moving {dirName}...");

            var currentX = _manager.CurrentPlayerState?.CurrentRoomX ?? ChainDepthConfig.START_X;
            var currentY = _manager.CurrentPlayerState?.CurrentRoomY ?? ChainDepthConfig.START_Y;
            var (newX, newY) = ChainDepthConfig.GetAdjacentCoords(currentX, currentY, direction);

            LogMessage($"({currentX}, {currentY}) -> ({newX}, {newY})");

            _manager.MovePlayer(newX, newY).ContinueWith(sig =>
            {
                if (sig != null)
                {
                    SetStatus($"Move TX: {sig.Substring(0, 16)}...");
                    LogMessage($"Move success: {sig}");
                }
                else
                {
                    SetStatus("Move failed - check console");
                    LogMessage("Move failed!");
                }
            }).Forget();
        }

        private void OnJoinJob(byte direction)
        {
            if (!IsWalletConnected())
            {
                SetStatus("Connect wallet first!");
                return;
            }

            var dirName = ChainDepthConfig.GetDirectionName(direction);
            SetStatus($"Joining job {dirName}...");
            LogMessage($"Joining job: {dirName}...");

            _manager.JoinJob(direction).ContinueWith(sig =>
            {
                if (sig != null)
                {
                    SetStatus($"Joined {dirName} job!");
                    LogMessage($"Join job success: {sig}");
                }
                else
                {
                    SetStatus("Join job failed - check console");
                    LogMessage("Join job failed!");
                }
            }).Forget();
        }

        private void OnLootChest()
        {
            if (!IsWalletConnected())
            {
                SetStatus("Connect wallet first!");
                return;
            }
            
            SetStatus("Looting chest...");
            LogMessage("Looting chest...");

            _manager.LootChest().ContinueWith(sig =>
            {
                if (sig != null)
                {
                    SetStatus($"Chest looted!");
                    LogMessage($"Loot TX: {sig}");
                }
                else
                {
                    SetStatus("Loot failed - check console");
                    LogMessage("Loot chest failed!");
                }
            }).Forget();
        }

        private void OnInitPlayer()
        {
            if (!IsWalletConnected())
            {
                SetStatus("Connect wallet first!");
                return;
            }
            
            SetStatus("Initializing player...");
            LogMessage("Initializing player at spawn (5, 5)...");
            
            _manager.InitPlayer().ContinueWith(sig =>
            {
                if (sig != null)
                {
                    SetStatus($"Player initialized!");
                    SetLabel(_playerStatus, "Player: Initialized at (5, 5)");
                    LogMessage($"Init TX: {sig}");
                }
                else
                {
                    SetStatus("Init failed - check console");
                    LogMessage("Player init failed!");
                }
            }).Forget();
        }

        #endregion

        #region Event Handlers

        private void OnWalletStateChanged()
        {
            var connected = IsWalletConnected();
            LogMessage($"Wallet state changed. Connected: {connected}");
            UpdateConnectionUI();

            if (connected)
            {
                SetStatus("Wallet connected!");
                UpdateBalanceAsync().Forget();
                _manager.RefreshAllState().Forget();
            }
            else
            {
                SetStatus("Wallet disconnected");
                SetLabel(_balanceLabel, "Balance: --");
            }
        }

        private void OnGlobalStateUpdated(GlobalAccount state)
        {
            LogMessage($"Global: Depth={state.Depth}, Jobs={state.JobsCompleted}");
            UpdateGlobalInfo(state);
        }

        private void OnPlayerStateUpdated(PlayerAccount state)
        {
            if (state != null)
            {
                LogMessage($"Player: ({state.CurrentRoomX}, {state.CurrentRoomY}), Jobs={state.JobsCompleted}");
                UpdatePlayerInfo(state);
                SetLabel(_playerStatus, $"Player: Initialized at ({state.CurrentRoomX}, {state.CurrentRoomY})");
                // Disable init button if player exists
                SetButtonEnabled(_btnInitPlayer, false);
            }
            else
            {
                LogMessage("Player: Not initialized");
                SetLabel(_playerInfo, "Player: Not initialized");
                SetLabel(_playerStatus, "Player: Not Initialized - Click to create");
                // Enable init button if player doesn't exist
                SetButtonEnabled(_btnInitPlayer, IsWalletConnected());
            }
        }

        private void OnRoomStateUpdated(RoomAccount state)
        {
            LogMessage($"Room: ({state.X}, {state.Y})");
            UpdateRoomInfo(state);
        }

        private void OnTransactionSent(string signature)
        {
            LogMessage($"TX Sent: {signature}");
            SetStatus($"TX: {signature.Substring(0, 20)}...");
        }

        private void OnErrorReceived(string error)
        {
            LogMessage($"ERROR: {error}");
            SetStatus($"Error: {error}");
            Debug.LogError($"[ChainDepthTestUI] Error: {error}");
        }

        #endregion

        #region UI Updates

        private void UpdateConnectionUI()
        {
            var connected = IsWalletConnected();

            if (connected)
            {
                var addr = Web3.Wallet.Account.PublicKey.Key;
                SetLabel(_walletAddress, $"Address: {addr.Substring(0, 8)}...{addr.Substring(addr.Length - 8)}");
                SetStatus("Status: Connected");
            }
            else
            {
                SetLabel(_walletAddress, "Address: Not Connected");
                SetStatus("Status: Not Connected");
            }

            // Enable/disable buttons
            SetButtonEnabled(_btnAirdrop, connected);
            SetButtonEnabled(_btnFetchPlayer, connected);
            SetButtonEnabled(_btnFetchRoom, connected);
            SetButtonEnabled(_btnRefreshAll, connected);

            SetButtonEnabled(_btnMoveNorth, connected);
            SetButtonEnabled(_btnMoveSouth, connected);
            SetButtonEnabled(_btnMoveEast, connected);
            SetButtonEnabled(_btnMoveWest, connected);

            SetButtonEnabled(_btnJobNorth, connected);
            SetButtonEnabled(_btnJobSouth, connected);
            SetButtonEnabled(_btnJobEast, connected);
            SetButtonEnabled(_btnJobWest, connected);

            SetButtonEnabled(_btnLootChest, connected);
            SetButtonEnabled(_btnInitPlayer, connected);
        }

        private void UpdateGlobalInfo(GlobalAccount state)
        {
            SetLabel(_globalInfo, $"Global: Depth={state.Depth}, Season={state.SeasonSeed}, Jobs={state.JobsCompleted}");
        }

        private void UpdatePlayerInfo(PlayerAccount state)
        {
            SetLabel(_playerInfo, $"Player: ({state.CurrentRoomX}, {state.CurrentRoomY}), Jobs={state.JobsCompleted}");
            SetLabel(_currentPos, $"({state.CurrentRoomX},{state.CurrentRoomY})");
            SetLabel(_positionLabel, $"Position: ({state.CurrentRoomX}, {state.CurrentRoomY})");
        }

        private void UpdateRoomInfo(RoomAccount state)
        {
            SetLabel(_roomInfo, $"Room: ({state.X}, {state.Y}), Chest={state.HasChest}");
            
            // Get wall state names from the walls array
            UpdateWallLabel(_wallNorth, "North", GetWallStateName(state.Walls, ChainDepthConfig.DIRECTION_NORTH));
            UpdateWallLabel(_wallSouth, "South", GetWallStateName(state.Walls, ChainDepthConfig.DIRECTION_SOUTH));
            UpdateWallLabel(_wallEast, "East", GetWallStateName(state.Walls, ChainDepthConfig.DIRECTION_EAST));
            UpdateWallLabel(_wallWest, "West", GetWallStateName(state.Walls, ChainDepthConfig.DIRECTION_WEST));
            
            SetLabel(_chestLabel, state.HasChest ? "Chest: Available!" : "Chest: None");
            SetButtonEnabled(_btnLootChest, state.HasChest && IsWalletConnected());
        }

        private string GetWallStateName(byte[] walls, byte direction)
        {
            if (walls == null || direction >= walls.Length)
                return "Unknown";
            return ChainDepthConfig.GetWallStateName(walls[direction]);
        }

        private void UpdateWallLabel(Label label, string direction, string wallState)
        {
            if (label == null) return;
            
            label.text = $"{direction}: {wallState}";
            
            // Remove old classes
            label.RemoveFromClassList("wall-solid");
            label.RemoveFromClassList("wall-rubble");
            label.RemoveFromClassList("wall-open");
            
            // Add appropriate class based on state
            switch (wallState)
            {
                case "Solid":
                    label.AddToClassList("wall-solid");
                    break;
                case "Rubble":
                    label.AddToClassList("wall-rubble");
                    break;
                case "Open":
                    label.AddToClassList("wall-open");
                    break;
            }
        }

        private void SetStatus(string message)
        {
            SetLabel(_statusLabel, message);
        }

        private void SetLabel(Label label, string text)
        {
            if (label != null)
                label.text = text;
        }

        private void SetButtonEnabled(Button button, bool enabled)
        {
            if (button != null)
                button.SetEnabled(enabled);
        }

        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}";
            
            _logMessages.Add(logEntry);
            
            // Trim old messages
            while (_logMessages.Count > MAX_LOG_LINES)
                _logMessages.RemoveAt(0);
            
            // Update log text
            if (_logText != null)
                _logText.text = string.Join("\n", _logMessages);
            
            // Scroll to bottom
            _logScroll?.ScrollTo(_logText);
            
            Debug.Log($"[ChainDepthTestUI] {message}");
        }

        private bool IsWalletConnected()
        {
            return Web3.Wallet?.Account != null;
        }

        #endregion
    }
}
