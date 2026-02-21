# ChainDepth - Solana Program

On-chain dungeon mining game for the Seeker ecosystem. Players collaboratively clear rubble walls to explore deeper into a procedurally generated dungeon, staking SKR tokens and earning rewards.

## Current Deployment (Devnet)

| Setting | Value |
|---------|-------|
| **Program ID** | `3Ctc2FgnNHQtGAcZftMS4ykLhJYjLzBD3hELKy55DnKo` |
| **SKR Token** | `Dkpjmf6mUxxLyw9HmbdkBKhVf7zjGZZ6jNjruhjYpkiN` |
| **Global PDA** | `9JudM6MujJyg5tBb7YaMw7DSQYVgCYNyzATzfyRSdy7G` |
| **Network** | Devnet |
| **RPC** | `https://api.devnet.solana.com` |

Full config in `devnet-config.json`.

## Project Structure

```
solana-program/
├── programs/chaindepth/src/    # Rust program source
│   ├── lib.rs                  # Entry point, instruction handlers
│   ├── instructions/           # Individual instruction logic
│   ├── state/                  # Account structures
│   ├── errors.rs               # Custom error codes
│   └── events.rs               # Event definitions
├── scripts/
│   ├── wsl/                    # WSL helper scripts for building
│   │   ├── run.sh              # Run any command with PATH set
│   │   ├── setup.sh            # One-time dev environment setup
│   │   └── build.sh            # Build the program
│   ├── init-devnet.ts          # Initialize game on devnet
│   ├── mint-test-tokens.ts     # Mint test SKR tokens
│   └── check-state.ts          # Query current game state
├── target/
│   ├── deploy/chaindepth.so    # Compiled program
│   ├── idl/chaindepth.json     # Anchor IDL for clients
│   └── types/chaindepth.ts     # TypeScript types
├── devnet-config.json          # Deployed addresses
├── devnet-wallet.json          # Deployment wallet (gitignored)
└── Anchor.toml                 # Anchor configuration
```

## Development Setup (Windows + WSL)

### Prerequisites
- Windows 10/11 with WSL2
- Ubuntu installed in WSL (`wsl --install -d Ubuntu`)

### First-Time Setup

```powershell
# Run the setup script (installs Rust, Solana CLI, Anchor, Node.js)
wsl -d Ubuntu -- bash /mnt/e/Github2/SeekerDungeon/solana-program/scripts/wsl/setup.sh
```

### Building

```powershell
# Build the program
wsl -d Ubuntu -- bash /mnt/e/Github2/SeekerDungeon/solana-program/scripts/wsl/build.sh
```

### Running Commands

Use the `run.sh` helper for any Solana/Anchor command:

```powershell
# Check versions
wsl -d Ubuntu -- bash /mnt/e/Github2/SeekerDungeon/solana-program/scripts/wsl/run.sh "solana --version"

# Check wallet balance
wsl -d Ubuntu -- bash /mnt/e/Github2/SeekerDungeon/solana-program/scripts/wsl/run.sh "solana balance --url devnet"

# Deploy/upgrade program
wsl -d Ubuntu -- bash /mnt/e/Github2/SeekerDungeon/solana-program/scripts/wsl/run.sh "solana program deploy target/deploy/chaindepth.so --program-id 3Ctc2FgnNHQtGAcZftMS4ykLhJYjLzBD3hELKy55DnKo --url devnet -k devnet-wallet.json"
```

### Install Dependencies

```powershell
cd solana-program
npm install
```

### Scripts

All scripts use [Solana Kite](https://solanakite.org) for standard operations and constants from `scripts/constants.ts`.

```powershell
# Initialize game state on devnet
npm run init-devnet

# Check current game state
npm run check-state

# Mint test tokens to a wallet
npm run mint-tokens <wallet_address> [amount]

# Watch program logs in real-time
npm run watch-logs
```

Scripts require `ANCHOR_PROVIDER_URL` and `ANCHOR_WALLET` environment variables to be set (or use default Solana CLI config).

### Generate Codama Client (Optional)

After building the program, generate a type-safe client:

```powershell
npm run codama
```

This creates a client in `generated/client/` that can replace the Anchor TypeScript client.

## Game Mechanics

### Core Loop
1. Players spawn at room (10,10)
2. Rooms have 4 walls: solid, rubble (clearable), or open
3. Join a job to clear rubble by staking 0.01 SKR
4. Jobs complete after enough slots pass (faster with more helpers)
5. Completing a job opens a new room and puts rewards into escrow
6. Each helper claims stake + bonus with `claim_job_reward`
7. Some rooms have chests with loot

### Instructions
- `init_global` - Admin: Initialize game state and starting room
- `move_player` - Move to adjacent open room
- `join_job` - Stake SKR to help clear a rubble wall
- `tick_job` - Update job progress based on elapsed time
- `boost_job` - Tip SKR to speed up a job
- `complete_job` - Finish job and open wall
- `claim_job_reward` - Claim staked SKR + completion bonus
- `abandon_job` - Leave job early (80% refund, 20% slashed)
- `loot_chest` - Collect items from a room's chest
- `reset_season` - Admin: Start a new season
- `force_reset_season` - Admin: Immediate season reset override (ignores season end gate)
- `ensure_start_room` - Admin: Ensure `(10,10)` start room exists for current season

### Accounts
- **GlobalAccount** - Game state (depth, season, prize pool)
- **PlayerAccount** - Player position and active jobs
- **RoomAccount** - Room state (walls, job aggregates, chests, creator metadata)
- **HelperStake** - Per-helper stake record for one room direction

## Unity Integration

Use `devnet-config.json` and `target/idl/chaindepth.json` with Solana Unity SDK.

```csharp
public const string PROGRAM_ID = "3Ctc2FgnNHQtGAcZftMS4ykLhJYjLzBD3hELKy55DnKo";
public const string RPC_URL = "https://api.devnet.solana.com";
```

## Tool Versions

| Tool | Version |
|------|---------|
| Rust | 1.93.0 |
| Solana CLI | 3.0.13 (Agave) |
| Anchor | 0.32.1 |
| Node.js | 20.x |
| Solana Kite | ^0.6.0 |
| Solana Kit | ^2.1.0 |
