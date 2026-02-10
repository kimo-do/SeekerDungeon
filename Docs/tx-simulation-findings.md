# TX Simulation Findings (2026-02-10)

## Scope
- Reproduced from fresh log buffers.
- Goal: explain why create-character still fails with wallet simulation issues and avoid repeating the same debugging loop.

## What was observed

1. Wallet flow is triggered correctly.
- Log shows Solana Mobile Wallet Adapter flow opening, including `ACTION_SIGN_TRANSACTION`.
- This confirms the button press reaches signing flow.

2. Immediately after returning to app, logs are flooded by one repeating fatal line.
- Repeated line:
  - `FATAL ThreadPoolWorkQueue.Dispatch Connection refused`
- In captured log, this starts at `.tmp_logcat_full.txt:434` and repeats continuously.

3. Useful gameplay/tx debug logs are drowned out.
- No actionable `[MainMenuCharacter]`, `[LGManager]`, or `[WalletSession]` failure detail was visible in this capture.
- This prevents identifying the exact instruction/account causing wallet simulation failure from current logs alone.

4. Character create is currently **not** bundled in code.
- Current flow in `LGMainMenuCharacterManager.CreateCharacterAsync()` is:
  1) `EnsurePlayerInitializedAsync()`
  2) `CreatePlayerProfile(...)`
- If player is already initialized, only `create_player_profile` is sent, which still results in a single wallet prompt.

## Current evidence-based conclusion

- There are two problems happening together:
  1. A background network task is repeatedly failing with `Connection refused` and flooding logs.
  2. The create-character transaction reaches wallet signing flow but simulation still fails (wallet message: cannot predict balance changes).

- Because of (1), we currently cannot reliably extract the exact low-level simulation failure reason from app logs.

## Newly confirmed root-cause chain (latest run)

From latest captured logs:
- Wallet adapter path fails first with:
  - `Transaction simulation failed: Error processing Instruction 2: custom program error: 0x0`
- After that, client code previously fell back to direct RPC send path.
- That fallback emitted secondary errors:
  - `Unable to parse json`
  - raw probe response showing `SignatureFailure` / `Transaction did not pass signature verification`

Interpretation:
- `SignatureFailure` is a **secondary** failure from fallback submit path, not the original gameplay/program rejection.
- Primary failure remains the wallet simulation rejection (`custom program error: 0x0`).

## On-chain state confirmation for failing wallet

Checked on devnet for wallet `CbBMc9dnTg1vBAWijVqi6chZ1JYvtMwdkn1hpZyCBW6s`:
- `player` PDA: missing
- `profile` PDA: missing
- start-room `room_presence` PDA: exists

This is a partial-init state. `init_player` currently uses `init` for `room_presence`, so simulation rejects when that account already exists.

## Config changes already applied to stabilize RPC

1. Wallet/controller custom RPC moved to:
- `https://api.devnet.solana.com`

2. App bootstrap fallback RPCs moved to:
- `https://api.devnet.solana.com`

3. Solana deployment config devnet RPC and fallback moved to:
- `https://api.devnet.solana.com`

4. Code default fallback constant moved to:
- `LGConfig.RPC_FALLBACK_URL = https://api.devnet.solana.com`

These changes are intended to reduce endpoint mismatch/refusal and get cleaner logs.

## Next steps (in order)

1. Rebuild and run once with current RPC stabilization.
- Then reproduce create-character once.

2. Capture fresh logs with:
- `Docs/howtoadb.md` steps (clear -> reproduce -> filtered capture).

3. If wallet still reports simulation failure:
- Temporarily disable bundled create flow (send `init_player` and `create_player_profile` as separate tx again) to recover working behavior first.

4. Add targeted runtime diagnostics (if needed):
- Explicit exception stack logging for background async tasks to identify the exact source of `ThreadPoolWorkQueue.Dispatch Connection refused`.
- One-line tx summary log right before submit: instruction count + instruction names + selected RPC endpoint.

## Additional stabilization patch applied (after this capture)

1. Disabled LG streaming RPC by default (polling-only mode).
- File: `Assets/Scripts/Solana/LGManager.cs`
- New flag: `enableStreamingRpc` (default `false`)

2. Disabled Web3 websocket RPC override by default.
- File: `Assets/Scripts/Solana/LGWalletSessionManager.cs`
- New flag: `useWebSocketRpcOverride` (default `false`)

Reason:
- `Connection refused` flood is consistent with repeated websocket/socket reconnect failures.
- Disabling websocket paths should reduce log spam and let transaction failure details surface.

3. Stopped invalid fallback after wallet-adapter simulation failure.
- File: `Assets/Scripts/Solana/LGManager.cs`
- Change: when wallet-adapter send fails, return immediately instead of trying direct RPC resend with rebuilt tx.
- Why: prevents misleading secondary `SignatureFailure` noise and keeps logs focused on the real wallet simulation error.

4. Added client recovery for partial-init state (stale start presence).
- File: `Assets/Scripts/Solana/LGManager.cs`
- Behavior:
  - If `init_player` fails and state is `player/profile missing + start presence exists`,
  - client attempts one `move_player` bootstrap to an open adjacent room.
  - `move_player` instruction uses on-chain `init_if_needed` accounts and can recover this state.

## Why this document exists

- The failure is reproducible, but current logs are dominated by a separate connection-refused loop.
- This doc records the exact state and decisions so we can move forward without redoing the same investigation steps.
