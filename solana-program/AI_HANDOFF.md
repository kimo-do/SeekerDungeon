# AI Handoff Notes

Quick reference for AI assistants working on this project.

## TL;DR

- **Build**: `wsl -d Ubuntu -- bash /mnt/e/Github2/SeekerDungeon/solana-program/scripts/wsl/build.sh`
- **Run commands**: `wsl -d Ubuntu -- bash /mnt/e/Github2/SeekerDungeon/solana-program/scripts/wsl/run.sh "your command"`
- **Program ID**: `3Ctc2FgnNHQtGAcZftMS4ykLhJYjLzBD3hELKy55DnKo`
- **Network**: Devnet

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

### 3. Stack Size Limits

Solana programs have a 4KB stack limit. Original `RoomAccount` struct caused stack overflow.

**Solution**: Reduced array sizes:
- `MAX_HELPERS_PER_DIRECTION`: 8 → 4
- `MAX_LOOTERS`: 32 → 8
- `helpers` array: `[[Pubkey; 8]; 4]` → `[[Pubkey; 4]; 4]`

If adding new fields or account structs, watch for stack overflow errors like:
```
Access violation in stack frame at address 0x...
```

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

## Libraries Used

| Library | Purpose |
|---------|---------|
| `solana-kite` | High-level Solana operations (tokens, SOL, PDAs) |
| `@solana/kit` | Low-level Solana Kit v2 (addresses, types) |
| `@coral-xyz/anchor` | Program calls and account fetching (until Codama client generated) |
| `tsx` | ESM-compatible TypeScript runner |

## Common Commands

```bash
# Build
wsl -d Ubuntu -- bash scripts/wsl/build.sh

# Deploy/upgrade
wsl -d Ubuntu -- bash scripts/wsl/run.sh "solana program deploy target/deploy/chaindepth.so --program-id 3Ctc2FgnNHQtGAcZftMS4ykLhJYjLzBD3hELKy55DnKo --url devnet -k devnet-wallet.json"

# Check balance
wsl -d Ubuntu -- bash scripts/wsl/run.sh "solana balance --url devnet"

# Run scripts (use the npm commands)
wsl -d Ubuntu -- bash scripts/wsl/run.sh "export ANCHOR_PROVIDER_URL=https://api.devnet.solana.com && export ANCHOR_WALLET=devnet-wallet.json && npm run check-state"
wsl -d Ubuntu -- bash scripts/wsl/run.sh "export ANCHOR_PROVIDER_URL=https://api.devnet.solana.com && export ANCHOR_WALLET=devnet-wallet.json && npm run init-devnet"
```

## Scripts

| Script | Purpose |
|--------|---------|
| `npm run check-state` | Query current game state on devnet |
| `npm run init-devnet` | Initialize game (create token, global state) |
| `npm run mint-tokens <wallet> [amount]` | Mint test SKR tokens |
| `npm run watch-logs` | Watch program logs in real-time |

## Known Issues / Gotchas

1. **PowerShell `&&` syntax**: Use `;` instead, or use `run.sh` helper
2. **Escaping in WSL commands**: Complex escaping breaks. Write to .sh file instead
3. **`init-if-needed` feature**: Must enable in Cargo.toml: `anchor-lang = { version = "...", features = ["init-if-needed"] }`
4. **blake3 dependency**: Pin to `=1.5.5` to avoid Edition 2024 requirement
5. **Temporary value lifetimes**: When building escrow seeds, bind `room.key()` to variable first
6. **Line endings**: WSL scripts must have Unix line endings (LF). Fix with: `wsl -d Ubuntu -- sed -i 's/\r$//' scripts/wsl/*.sh`
7. **BigInt in Kite**: `getCurrentSlot()` returns BigInt, convert with `Number()` for arithmetic

## Architecture Notes

- Single global state PDA per season
- Rooms are PDAs keyed by `[season_seed, x, y]`
- Season resets create new room PDAs (old ones orphaned)
- Escrow accounts hold staked SKR during jobs
- Prize pool is a token account owned by global PDA
