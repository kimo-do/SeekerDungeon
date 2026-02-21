# AI Handoff Notes

Quick reference for AI assistants working on this project.

## TL;DR

- **Build**: `wsl -d Ubuntu -- bash /mnt/e/Github2/SeekerDungeon/solana-program/scripts/wsl/build.sh`
- **Build fallback (if WSL scripts have CRLF issues)**: `wsl -d Ubuntu -- bash -lc "cd /mnt/e/Github2/SeekerDungeon/solana-program && rm -f Cargo.lock && anchor build"`
- **Run commands**: `wsl -d Ubuntu -- bash /mnt/e/Github2/SeekerDungeon/solana-program/scripts/wsl/run.sh "your command"`
- **Program ID**: `3Ctc2FgnNHQtGAcZftMS4ykLhJYjLzBD3hELKy55DnKo`
- **Network**: Devnet
- **Session smoke wallet (fresh)**: `test-wallets/devnet-session-wallet.json` (`2eoK8KdoAJ9hgBjoUbQY6SQypJmNYeEnyxFvkQaWXELP`)

## Critical Implementation Details

### 1. Must Use WSL for Building

Native Windows Solana toolchain has issues. Always build via WSL Ubuntu:
- PATH isn't set by default in non-interactive shells
- Use `scripts/wsl/run.sh` helper which sets PATH automatically
- Delete `Cargo.lock` before building (Windows/WSL Cargo version conflicts)

### 2. ESM Module System

The project uses ESM (`"type": "module"` in package.json) because:
- `solana-kite` is ESM-only
- Scripts use `tsx` instead of `ts-node` for ESM compatibility
- Scripts must define `__dirname` using `import.meta.url`:

```typescript
import { fileURLToPath } from "url";
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
```

### 3. Room/Job Data Model (PDA-based helpers)

Helpers are now modeled as per-player stake PDAs instead of fixed arrays in `RoomAccount`.

- `HelperStake` PDA seeds: `["stake", room, direction, player]`
- One stake account per helper per direction
- `RoomAccount` tracks aggregates (`helper_counts`, `total_staked`, progress, completion state)
- `RoomAccount` also stores discovery metadata: `created_by`, `created_slot`
- Reward flow is split:
  - `complete_job` marks job complete and allocates bonus
  - `claim_job_reward` pays stake+bonus per helper and closes `HelperStake`

### 4. Anchor 0.32 Account Syntax

Use `accountsPartial()` instead of `accounts()` in TypeScript:
```typescript
// Old (breaks):
.accounts({ admin: ..., global: ... })

// New (works):
.accountsPartial({ admin: ..., global: ... })
```

### 5. Program ID Mismatch After Rebuild

If you see `DeclaredProgramIdMismatch` error:
1. Check `declare_id!()` in `lib.rs` matches `Anchor.toml`
2. Rebuild with `anchor build`
3. Redeploy with `solana program deploy`

### 6. Wallet Derivation Paths

Solana CLI and Phantom use different derivation paths from seed phrases:
- CLI default: No path → one address
- Phantom: `m/44'/501'/0'/0'` → different address

Use JSON keypair files (`devnet-wallet.json`) to avoid confusion.

### 7. Inventory + Storage PDA Model (Onchain)

Run inventory is stored onchain in a dedicated PDA per player:

- `InventoryAccount` PDA seeds: `["inventory", player]`
- `InventoryItem` fields:
  - `item_id: u16`
  - `amount: u32`
  - `durability: u16`
- Max inventory stacks per player: `64`

Long-term storage is a separate per-player PDA:

- `StorageAccount` PDA seeds: `["storage", player]`
- Uses the same `InventoryItem` stack shape (`item_id`, `amount`, `durability`)
- Max storage stacks per player: `64`

Behavior:
- `loot_chest` now writes directly to `InventoryAccount` (init-if-needed).
- `add_inventory_item(item_id, amount, durability)` stacks by `(item_id, durability)`.
- `remove_inventory_item(item_id, amount)` removes across all stacks of the same `item_id`.
- `exit_dungeon` scores run valuables, transfers scored loot stacks from run inventory into storage, and keeps non-scored items in run inventory.
- `force_exit_on_death` only removes scored loot from run inventory; storage remains untouched.

Current item ids used by chest loot:
- `1` = Ore
- `2` = Tool (durability defaults to `100`)
- `3` = Buff

### 8. Room Center and Boss System

Rooms now have a center type:
- `0` = empty
- `1` = chest
- `2` = boss

Spawn rules implemented:
- Start room `(10,10)` center is always empty.
- Depth 1 rooms: 50% chest chance, with one guaranteed chest among first-ring rooms.
- Depth 2+ rooms: 50% boss chance.

Boss runtime fields are stored in `RoomAccount`:
- `center_id` for Unity boss prefab selection
- `boss_max_hp`, `boss_current_hp`, `boss_total_dps`, `boss_fighter_count`, `boss_defeated`

Boss instruction flow:
- `equip_item(item_id)` (0 to unequip)
- `set_player_skin(skin_id)`
- `create_player_profile(skin_id, display_name)` (sets profile + grants starter pickaxe once)
- `join_boss_fight()`
- `tick_boss_fight()`
- `loot_boss()` (fighters-only, after defeat)

### 9. Scalable Presence/Profile Indexing

To avoid full player scans for room rendering, two PDAs were added:

- `PlayerProfile` seeds: `["profile", player]`
  - stores `skin_id`
- `RoomPresence` seeds: `["presence", season_seed, room_x, room_y, player]`
  - stores `skin_id`, `equipped_item_id`, activity, and `is_current`

Update points:
- `init_player` creates profile + initial presence
- `move_player` updates old/new room presences
- `equip_item` syncs current room presence `equipped_item_id`
- `set_player_skin` syncs profile and current room presence
- `join_job` marks presence as door-job activity
- `join_boss_fight` marks presence as boss-fight activity
- `abandon_job` / `claim_job_reward` / `loot_boss` return presence to idle

## File Locations

| What | Where |
|------|-------|
| Program source | `programs/chaindepth/src/` |
| Build output | `target/deploy/chaindepth.so` |
| IDL | `target/idl/chaindepth.json` |
| Deployed config | `devnet-config.json` |
| Wallet | `devnet-wallet.json` (gitignored) |
| WSL scripts | `scripts/wsl/` |
| TS scripts | `scripts/*.ts` |
| Constants | `scripts/constants.ts` |
| Unity Generated | `../Assets/Scripts/Solana/Generated/` |
| Unity Codegen (PS) | `../scripts/generate-unity-client.ps1` |
| Unity Codegen (Bash) | `scripts/generate-unity-client.sh` |
| VS Code Tasks | `../.vscode/tasks.json` |

## Libraries Used

### TypeScript (Node.js scripts)

| Library | Purpose |
|---------|---------|
| `solana-kite` | High-level Solana operations (tokens, SOL, PDAs) |
| `@solana/kit` | Low-level Solana Kit v2 (addresses, types) |
| `@coral-xyz/anchor` | Program calls and account fetching (until Codama client generated) |
| `tsx` | ESM-compatible TypeScript runner |

### Unity (C# game client)

| Library | Purpose |
|---------|---------|
| [Solana.Unity-SDK](https://github.com/magicblock-labs/Solana.Unity-SDK) | Full Solana SDK for Unity (RPC, wallets, NFTs) |
| UniTask | Async/await for Unity |

## VS Code Tasks (Recommended)

Use **Ctrl+Shift+P** → "Tasks: Run Task" to access these:

| Task | What it does |
|------|--------------|
| **🚀 Build & Generate (Full Workflow)** | Anchor build + regenerate Unity C# client (default build task) |
| Anchor: Build (WSL) | Just run `anchor build` via WSL |
| Generate: Unity C# Client | Just regenerate C# from IDL |
| Anchor: Deploy to Devnet | Deploy program to devnet |
| Solana: Check State | Query current game state |
| Solana: Watch Logs | Watch program logs (background) |

**Tip:** Press **Ctrl+Shift+B** to run the default build task (Full Workflow).

## Common Commands (Manual)

```bash
# Build
wsl -d Ubuntu -- bash scripts/wsl/build.sh

# Build fallback if scripts/wsl/*.sh were saved with CRLF
wsl -d Ubuntu -- bash -lc "cd /mnt/e/Github2/SeekerDungeon/solana-program && rm -f Cargo.lock && anchor build"

# Deploy/upgrade
wsl -d Ubuntu -- bash scripts/wsl/run.sh "solana program deploy target/deploy/chaindepth.so --program-id 3Ctc2FgnNHQtGAcZftMS4ykLhJYjLzBD3hELKy55DnKo --url devnet -k devnet-wallet.json"

# Check balance
wsl -d Ubuntu -- bash scripts/wsl/run.sh "solana balance --url devnet"

# Run scripts (use the npm commands)
wsl -d Ubuntu -- bash scripts/wsl/run.sh "export ANCHOR_PROVIDER_URL=https://api.devnet.solana.com && export ANCHOR_WALLET=devnet-wallet.json && npm run check-state"
wsl -d Ubuntu -- bash scripts/wsl/run.sh "export ANCHOR_PROVIDER_URL=https://api.devnet.solana.com && export ANCHOR_WALLET=devnet-wallet.json && npm run force-reset-season"
wsl -d Ubuntu -- bash scripts/wsl/run.sh "export ANCHOR_PROVIDER_URL=https://api.devnet.solana.com && export ANCHOR_WALLET=devnet-wallet.json && npm run init-devnet"
wsl -d Ubuntu -- bash scripts/wsl/run.sh "export ANCHOR_PROVIDER_URL=https://api.devnet.solana.com && export ANCHOR_WALLET=devnet-wallet.json && npm run smoke-session-join-job"

# Run session smoke on dedicated test wallet
wsl -d Ubuntu -- bash scripts/wsl/run.sh "export ANCHOR_PROVIDER_URL=https://api.devnet.solana.com && export ANCHOR_WALLET=test-wallets/devnet-session-wallet.json && npm run smoke-session-join-job"
```

## Scripts

| Script | Purpose |
|--------|---------|
| `npm run check-state` | Query current game state on devnet |
| `npm run force-reset-season` | Admin-only immediate season reset override |
| `ensure_start_room` instruction | Admin helper to bootstrap start room `(10,10)` for current season |
| `npm run init-devnet` | Initialize game (create token, global state) |
| `npm run mint-tokens <wallet> [amount]` | Mint test SKR tokens |
| `npm run smoke-door` | End-to-end devnet smoke test (init/move/join/tick/complete/claim flow) |
| `npm run smoke-session-join-job` | Devnet smoke test for `begin_session` + `join_job_with_session` + presence sync |
| `npm run watch-logs` | Watch program logs in real-time |

## Devnet Test Wallets

- `test-wallets/devnet-session-wallet.json`
  - Public key: `2eoK8KdoAJ9hgBjoUbQY6SQypJmNYeEnyxFvkQaWXELP`
  - Intended use: session-key smoke tests
  - Funding baseline: `1 SOL` and `25 SKR`
- `test-wallets/devnet-smoke-wallet.json`
  - Public key: `E2xzqHcjQ78kKzZ2bFE41csGFswn6YgRG8hRKFQj2RvH`
  - Known issue: currently fails `begin_session` with `ConstraintSeeds` on `player_account` (legacy onchain state mismatch)

## Known Issues / Gotchas

1. **PowerShell `&&` syntax**: Use `;` instead, or use `run.sh` helper
2. **Escaping in WSL commands**: Complex escaping breaks. Write to .sh file instead
3. **`init-if-needed` feature**: Must enable in Cargo.toml: `anchor-lang = { version = "...", features = ["init-if-needed"] }`
4. **blake3 dependency**: Pin to `=1.5.5` to avoid Edition 2024 requirement
5. **Temporary value lifetimes**: When building escrow seeds, bind `room.key()` to variable first
6. **Line endings**: WSL scripts must have Unix line endings (LF). If you see `$'\r': command not found`, fix with: `wsl -d Ubuntu -- sed -i 's/\r$//' scripts/wsl/*.sh`
7. **BigInt in Kite**: `getCurrentSlot()` returns BigInt, convert with `Number()` for arithmetic
8. **`init_player` account list changed**: now requires `profile` and `room_presence` PDAs in addition to `player_account`
9. **`mint-test-tokens.ts` can fail for some addresses** with `Expected a Address.` from `@solana/kit`; fallback to CLI:
   - `spl-token create-account <MINT> --owner <WALLET> --fee-payer devnet-wallet.json -u devnet`
   - `spl-token mint <MINT> 25 <RECIPIENT_ATA> --mint-authority devnet-wallet.json --fee-payer devnet-wallet.json -u devnet`
10. **`npm test` can fail on Devnet faucet limits**: Anchor test `before()` currently requests airdrops and may hit `429 Too Many Requests`

## Architecture Notes

- Single global state PDA per season
- Rooms are PDAs keyed by `[season_seed, x, y]`
- Season resets create new room PDAs (old ones orphaned)
- Escrow accounts hold staked SKR during jobs
- Helper stakes are per-player PDAs keyed by `[room, direction, player]`
- Helpers claim stake + bonus with `claim_job_reward`
- Rooms track discovery metadata with `created_by` and `created_slot`
- Prize pool is a token account owned by global PDA
- Player run inventory is a dedicated PDA keyed by `[inventory, player]`
- Player long-term storage is a dedicated PDA keyed by `[storage, player]`

## Unity Integration

### Solana Unity SDK

We use the **Magicblock Solana.Unity-SDK**:
- **GitHub**: https://github.com/magicblock-labs/Solana.Unity-SDK
- **Docs**: https://solana.unity-sdk.gg
- **Install via Git URL**: `https://github.com/magicblock-labs/Solana.Unity-SDK.git`

Key SDK classes:
- `Web3` - Main entry point for wallet/RPC operations
- `Web3.Instance.LoginInGameWallet(password)` - Create local test wallet
- `Web3.Instance.LoginWalletAdapter()` - Connect via Phantom/Solflare/etc.
- `Web3.Wallet.Account.PublicKey` - Current connected wallet address

### Unity Scripts

Unity scripts are in `Assets/Scripts/Solana/`:

| File | Purpose |
|------|---------|
| `LGConfig.cs` | Constants (addresses, seeds, game values) |
| `LGManager.cs` | Main manager for Solana interactions |
| `LGTestUI.cs` | Test UI using UI Toolkit |
| `UI/LGTestUI.uxml` | UI Toolkit layout |
| `UI/LGTestUI.uss` | UI Toolkit styles |
| `Generated/LGClient.cs` | **Auto-generated** IDL client (do not edit) |

### IDL to C# Code Generation

We use `Solana.Unity.Anchor.Tool` to generate type-safe C# client code from the Anchor IDL.

**Install the tool (once):**
```bash
dotnet tool install -g Solana.Unity.Anchor.Tool
```

**Regenerate after Anchor program changes:**

Windows (PowerShell):
```powershell
.\scripts\generate-unity-client.ps1
```

WSL (Bash):
```bash
bash scripts/generate-unity-client.sh
```

Manual command:
```bash
dotnet anchorgen -i solana-program/target/idl/chaindepth.json -o Assets/Scripts/Solana/Generated/LGClient.cs
```

**Workflow after changing the Rust program:**
1. `anchor build` - Generates new IDL in `target/idl/chaindepth.json`
2. Run regeneration script - Updates `LGClient.cs`
3. Unity automatically picks up changes

**Generated code provides:**
- `ChaindepthProgram.InitPlayer()`, `ChaindepthProgram.MovePlayer()`, etc. - Type-safe instruction builders
- `InitPlayerAccounts`, `MovePlayerAccounts`, etc. - Strongly-typed account structs
- Account discriminators and serialization handled automatically

**Example usage in LGManager.cs:**
```csharp
using Chaindepth.Program;

var instruction = ChaindepthProgram.InitPlayer(
    new InitPlayerAccounts
    {
        Player = Web3.Wallet.Account.PublicKey,
        Global = _globalPda,
        PlayerAccount = playerPda,
        SystemProgram = SystemProgram.ProgramIdKey
    },
    _programId
);
```

### Setup in Unity

1. Import the Solana Unity SDK via Package Manager (Git URL above)
2. Add `Web3` component to scene (from SDK samples or create manually)
3. Add `LGManager` to a GameObject (it's a singleton)
4. For Test UI:
   - Create Panel Settings asset (Create > UI Toolkit > Panel Settings Asset)
   - Create GameObject with `UIDocument` component
   - Assign `LGTestUI.uxml` as Source Asset
   - Add `LGTestUI` script component
5. Configure Web3 component with devnet RPC URL

### Key Unity Classes

- `GlobalState` - Parsed global account data
- `PlayerState` - Parsed player account data  
- `RoomState` - Parsed room account data

### Note on Instruction Building

The Unity scripts use generated instruction builders from `LGClient.cs`. For instructions requiring token accounts (join_job, complete_job, etc.), additional setup is needed:
1. Token account setup for escrow
2. Associated token account creation

See the TypeScript scripts for reference on account structure.

