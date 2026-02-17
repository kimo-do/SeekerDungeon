Overall Implementation Plan for ChainDepth
High-Level Overview: ChainDepth is a shared, procedural onchain dungeon mining game for Seeker phones, built with Unity for the frontend (2D pixel art visuals, tweens, mini-games) and Solana for the backend (Anchor program handling state, SKR transactions, and shared progress). The game uses a 10x10 grid of rooms starting from (5,5), procedurally generated client-side from an onchain seed for synchronization across players. Players stake SKR to join jobs (e.g., clear rubble), loot treasures, and move rooms, with multi-player collaboration speeding progress. Visuals rely on static SpriteCook-generated sprites animated via Unity tweens (no sprite sheets). The onchain component ensures fully shared state (no servers), with SKR as native utility (stakes, tips, rewards). Total scope: 4 weeks, solo-friendly but split between Unity (gameplay/visuals) and blockchain (program/logic).
We'll use:

Unity 2022.3+: For 2D Tilemap/Grid, DOTween for animations, Solana.Unity-SDK for RPC/wallet interactions.
Solana Tools: Anchor framework for Rust program, Solana CLI for devnet deploy/test, Helius for webhooks/relayers (free dev tier).
Wallet Integration: Mobile Wallet Adapter (MWA) via Solana.Unity-SDK for Seeker-compatible one-tap txs/signing.
Testing Strategy: Start on devnet (free, fast). Console: Anchor tests/CLI for program logic. Unity: Mock RPC initially, then connect to devnet endpoint for e2e. Use Phantom/Backpack for desktop testing, Seeker emulator for mobile.

Assumptions: You're the lead (Unity focus), with agentic AI handling blockchain code gen/deploy. SpriteCook for asset gen. No multiplayer P2P—pure polling (every 5-10s) for live updates.
Timeline Breakdown (4 Weeks Total):

Week 1: Unity prototypes map/mini-game; Blockchain deploys basic program to devnet.
Week 2: Unity adds tx integrations; Blockchain adds full instructions/tests.
Week 3: Unity polishes visuals/inventory; Blockchain sets up relayer/resets.
Week 4: E2E testing, mobile build, hackathon submit (dApp Store by Mar 9, 2026—wait, date is Feb 4, 2026? Adjust if typo; assume ongoing).

The Unity Dev
Role: Focus on visuals, gameplay loop, and SDK integration. Leverage Solana.Unity-SDK (NuGet install) for RPC calls (GetMultipleAccounts, SendTransaction), MWA for mobile wallet (auto-detects Seeker Seed Vault). Use devnet RPC. Keep it simple: One-room view, tweens for "animations," local state synced via polling.
Techniques & Tools:

Scene Setup: Single scene with Canvas (UI), Grid GameObject for room prefab.
Room Rendering: Procedural gen in code (hash functions for walls/treasures). Static sprites (import from SpriteCook: floor.png, rubble.png, door.png, avatar.png, pickaxe.png, chest.png, 5-10 item icons). No animators—use DOTween (free asset) for moves (e.g., avatar.DOJump(pos, 1f, 1, 0.5f)), rotations (tool.DORotate(360, 2f).SetLoops(-1)), scales (bar.DOScaleX(progress/100, 0.2f)).
Player Avatars: Scatter via code (random offset in room bounds, avoid overlap). Color tint from pubkey hash (e.g., Color.HSVToRGB(hash % 1f, 1f, 1f)).
Mini-Game: Simple Input.tapCount rhythm (offchain, calculate score → tx boost).
Inventory: UI Panels with Image slots (static item sprites tween in on loot).
Polling: Coroutine every 5s: SDK.GetMultipleAccounts([globalPda, playerPda, currentRoomPda]) → parse Borsh/JSON → update visuals (e.g., if progress[dir] > old, tween bar).
Tx Flow: SDK.RequestAirdrop for devnet SOL/SKR (mock SKR as SPL token on devnet). MWA.Connect() for auth, then SendTx for claim/move/loot (signAndSend).
Mobile Optimizations: Touch controls (Unity.InputSystem), build for Android (Seeker target), test on emulator (Solana Mobile SDK tools).

Detailed Tasks by Week:

Week 1: Build room prefab (edges, center chest logic). Implement procedural hash gen (mock data). Add avatar tween moves, mini-game prototype. Test offline loop.
Week 2: Integrate Solana.Unity-SDK (setup WalletAdapter, RPC client). Add polling coroutine for room/player state. Implement tx buttons (e.g., Button.onClick → async ClaimJob(dir)).
Week 3: Add inventory UI (local sync post-tx). Polish particles (Unity ParticleSystem for rubble burst, simple sprites). Handle multi-avatars (fetch helpers[] → spawn tinted sprites).
Week 4: E2E with devnet (connect to deployed program). Bugfix edge cases (e.g., race conditions on complete). Build APK, test on Seeker emulator (MWA flow).

Testing Approach:

Local: Use Unity Editor with mock JSON data (simulate PDAs).
Devnet Connection: Set RPC to devnet in SDK. Airdrop SOL via CLI (solana airdrop 2), mint mock SKR SPL (create token via spl-token CLI). Run playmode: Connect wallet, send txs, verify visuals update on poll.
Console Tie-In: After blockchain deploy, use Anchor CLI to seed data (e.g., init season), then Unity polls to verify.

The Blockchain Dev
Role: Implement the Anchor Rust program for all shared state/logic. Focus on PDAs (cheap, deterministic storage), SKR as SPL token (use spl-token crate for transfers/escrows). Program is ~300-400 LoC, AI-generatable in chunks (e.g., prompt: "Anchor program with these accounts/instructions"). Deploy to devnet for free testing. Use relayer (Helius webhook) for ticks (no true cron, but client/relayer submits "tick" txs). SKR integration: Use as escrow currency (transferFrom user to PDA on stake, back on complete).
Techniques & Tools:

Anchor Framework: Rust + IDL for Unity SDK compatibility (auto-gen C# bindings via solana-unity-sdk).
PDAs & Accounts: Global (seed: "global"), Player (seed: [pubkey]), Room (seed: [global_seed, x, y]).
SKR Handling: SPL token program (TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA). Instructions transfer SKR to/from escrow PDAs.
Progress Ticking: Hybrid: Client/relayer submits "tick_job(dir)" tx (calculates delta from start_slot, adds to progress). Use slots (~0.4s each) for time (e.g., base_slots = 120s / 0.4 = 300).
Exponential Difficulty: Base_slots = 300 * (depth / 10 + 1); multi-help: effective_add = 1 / helpers.len() per tick.
Loot: Random via hash (no true RNG; use slot + pubkey hash % 3 for item type/amount).
Resets: Weekly relayer tx (Helius cron: call "reset_season" if current_slot > end_slot).
Security: Checks (e.g., valid move: adjacent & open), no reentrancy (Anchor cpi safe).

Program Structure (Detailed Spec for AI Gen):

Crate Setup: anchor-lang = "0.29", spl-token = "0.3". One lib.rs with #[program] mod.
Accounts:
GlobalAccount: #[account] pub struct Global { season_seed: u64, depth: u32, prize_pool: Pubkey (SPL ATA for SKR), end_slot: u64 }
PlayerAccount: #[account(seeds=[b"player", signer.key().as_ref()])] pub struct Player { current_room: (i8,i8), active_jobs: Vec<( (i8,i8), u8 )> } // dir 0-3: N/S/E/W
RoomAccount: #[account(seeds=[b"room", global.season_seed.to_le_bytes(), x.to_le_bytes(), y.to_le_bytes()])] pub struct Room { walls: [u8;4], // 0=wall,1=rubble,2=open
helpers: [Vec<Pubkey>;4], progress: [u64;4], start_slot: [u64;4], base_slots: [u64;4], has_chest: bool, looted_by: Vec<Pubkey> }
Escrow ATA: Derived for each job (seed: [room, dir])—holds staked SKR.

Instructions (Each with ctx: Context<Struct>):
init_global (one-time, dev only): Ctx: payer, global, prize_pool_ata (init_if_needed). Set seed=rand(), end_slot=current+1week_slots, fund pool with initial SKR transfer.
reset_season: Ctx: global, relayer (auth). If slot > end, new_seed=rand_hash(old), depth=0, archive old (emit event). Relayer calls weekly.
move_player (x,y to new_x,new_y): Ctx: player (init_if_needed), current_room, new_room (init_if_needed). Check adjacent & open (get wall[dir] from current). Update player.current_room.
join_job (dir): Ctx: player, room, escrow_ata (init_if_needed), user_skr_ata, token_program. Transfer 0.01 SKR to escrow. Add signer to helpers[dir]. If first, set start_slot=current, base_slots=calc_from_depth(global.depth), progress=0.
boost_job (dir, add: u64): Ctx: room, user_skr_ata, prize_pool_ata, token_program. Transfer 0.001 SKR tip to pool. Add to progress[dir] (cap 100).
tick_job (dir): Ctx: room. Calc delta_slots = current - start_slot. Effective_add = delta_slots / helpers.len(). progress += effective_add. If >= base_slots, ready for complete. (Client/relayer submits periodically).
complete_job (dir): Ctx: room, global, escrow_ata, helpers' atas, token_program. If progress >= base_slots && helpers includes signer, set walls[dir]=2 (and adjacent room's opposite wall=2, init if needed). global.depth +=1. Split escrow SKR: refund to helpers + bonus from prize_pool / helpers.len(). Clear helpers/progress. Emit event for airdrop.
loot_chest: Ctx: room, user_skr_ata?, token_program?. If has_chest && !looted_by.contains(signer), add signer. "Roll" item: hash(current_slot + signer) %3 = type (0=ore u64=1-5,1=tool,2=buff). Emit event with item data (Unity parses for inventory).
abandon_job (dir): Ctx: room, escrow_ata. Remove from helpers, slash 20% to pool, refund 80%.

Events: Emit for completes (depth update), loots (item details), resets.
Errors: InvalidMove, AlreadyLooted, NotReady, etc.

Detailed Tasks by Week (Heavy Focus Here):

Week 1: AI gen base program (global/room/player accounts, init/move/join). Write unit tests (#[test] fn test_join()). Deploy to devnet: anchor deploy --provider.cluster devnet. Fund with spl-token create-token (mock SKR), airdrop SOL.
Week 2: Add tick/boost/complete/loot instructions. Handle SKR CPIs (transfer checked). Tests: Simulate multi-helpers (create keypairs, join, tick till complete, assert refunds). Setup Helius webhook (API key free, trigger tick on interval/event).
Week 3: Exponential calc, chest logic, abandon. Relayer script (Node.js: solana-web3.js to submit tick txs every 30s). Tests: Full e2e (reset, join, complete, depth++, loot).
Week 4: Polish (events for Unity), mainnet-ready (but stay devnet for hack). Document IDL for Unity bindings.

Testing Approach (Console & Unity):

Console (Anchor CLI/Solana CLI):
Unit/Integration: anchor test (local validator auto-spins). Mock SKR: spl-token create-account <token_mint>, mint to test wallets.
Devnet Deploy: anchor build; anchor deploy. Get program ID, derive PDAs (Pubkey::find_program_address).
Manual Tx: Use anchor run <instruction> or solana-program-library CLI. E.g., init: solana program invoke <prog_id> --data <borsh>. Monitor: solana account <pda> --output json. Simulate multi-player: Create wallets (solana-keygen new), airdrop, join same job, tick till complete, check splits.
Relayer Test: Run local Node script, watch logs for tx sigs (solana confirm <sig>).

Unity Connection:
Setup: In Unity, Solana.Unity-SDK RpcClient = new("https://api.devnet.solana.com"); Wallet = MWA (on mobile) or RpcActiveWallet (desktop test).
Polling Test: Coroutine fetches GetAccountInfo(global_pda), deserialize Borsh to C# structs (gen from IDL). Assert visuals match (e.g., depth UI updates).
Tx Test: Button sends join_job (serialize args, Program.Invoke), sign via MWA, confirm sig. Debug: Unity console logs tx errors. E2E: Deploy program, init via CLI, play in Unity—join job, poll sees progress, complete refunds SKR (check balance via SDK).
Mock SKR: On devnet, create SPL token mimicking SKR (same decimals), fund users. For Seeker emu: Use Solana Mobile tools to simulate MWA.