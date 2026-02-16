# Extraction / Score Telemetry Dashboard

This repo now includes a devnet telemetry report that inspects:

- recent `DungeonExited` events
- recent `DungeonExitItemScored` events
- recent `DoorUnlocked` events
- current-season `RoomAccount` state (locked doors + forced key rooms)

and generates:

- `solana-program/logs/extraction-telemetry-dashboard.json`
- `solana-program/logs/extraction-telemetry-dashboard.md`

## Run

From `solana-program`:

```bash
npm run telemetry-extraction -- --limit-signatures 600
```

Defaults:

- RPC: `https://api.devnet.solana.com`
- signature sample size: `500`

## What the report gives you

- extraction run count, unique extractors, average run/loot/time score
- time-score share of total extracted score
- average run duration in minutes
- door unlock event count
- current-season rooms discovered
- locked-door room rate + total locked doors
- forced-key room counts by depth ring
- per-ring room coverage (so forced-key alerts only trigger on fully discovered rings)
- top scored extracted item IDs with units/score totals
- threshold-based alerts
- tuning recommendations

## How this maps to TODO tuning

Use this dashboard before changing:

- locked door spawn cadence constants in `solana-program/programs/chaindepth/src/state/room_generation.rs`
- forced key chest cadence constants in `solana-program/programs/chaindepth/src/state/room_generation.rs`
- extraction item-value table in `solana-program/programs/chaindepth/src/state/scoring.rs`
- extraction time-bonus constants/cap in `solana-program/programs/chaindepth/src/state/scoring.rs`

Suggested loop:

1. Run telemetry report on current season data.
2. Apply a single balance change.
3. Collect at least 50 additional extracted runs.
4. Re-run telemetry and compare deltas.
5. Repeat.
