# Shared Devnet Rollout Checklist

This is the implementation-time checklist for the todo:

- publish upgraded program + reset/migrate season state before enabling locked doors on shared devnet/mainnet.

## 1) Verify readiness

From `solana-program`:

```bash
npm run rollout-readiness
```

This verifies:

- global account exists
- start room south wall is `EntranceStairs`
- room below start exists and north wall is sealed

## 2) Deploy program + refresh client artifacts

1. Build program in WSL:
   - `solana-program/scripts/wsl/build.sh`
2. Deploy to target network.
3. Regenerate Unity client from updated IDL:
   - refresh `Assets/Scripts/Solana/Generated/LGClient.cs`
4. Confirm domain mapping changes are reflected in:
   - `Assets/Scripts/Solana/LGDomainModels.cs`

## 3) Reset / migrate state for expanded account layouts

If older player accounts exist (pre-expanded layout), use reset scripts for test wallets before smoke:

- `npm run reset-player-only <wallet>`
- `npm run reset-player-and-fund <wallet> [sol] [skr]`

For shared environments, perform season reset as needed:

- `npm run force-reset-season`

## 4) Run smoke + telemetry

Required:

- `npm test`
- `npm run smoke-session-join-job`
- `npm run smoke-room-routing`

Then collect telemetry:

```bash
npm run telemetry-extraction -- --limit-signatures 600
```

Review:

- `solana-program/logs/extraction-telemetry-dashboard.md`

## 5) Balance pass before opening wider testing

Adjust based on telemetry:

- lock cadence and forced key cadence (`room_generation.rs`)
- item score table and time bonus caps (`scoring.rs`)

