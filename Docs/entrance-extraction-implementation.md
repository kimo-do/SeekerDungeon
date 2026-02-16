# Entrance Stairs + Extraction Implementation

## Scope
- Implemented locally only.
- Not yet treated as final onchain rollout for shared environments.

## Onchain Changes
- New wall state: `WALL_ENTRANCE_STAIRS = 4`.
- Spawn room `(5,5)` is now deterministic and fixed:
  - `North/East/West = WALL_OPEN`
  - `South = WALL_ENTRANCE_STAIRS`
- Added `exit_dungeon` instruction.
- Added player scoring/run fields:
  - `total_score: u64`
  - `current_run_start_slot: u64`
  - `runs_extracted: u64`
  - `last_extraction_slot: u64`
- Added session allowlist bit: `EXIT_DUNGEON`.
- Added extraction errors:
  - `NotAtEntranceRoom`
  - `EntranceStairsRequired`
  - `CannotExitWithActiveJobs`
- Added event:
  - `DungeonExited { player, run_score, time_score, loot_score, extracted_item_stacks, extracted_item_units, total_score, run_duration_slots }`
  - `DungeonExitItemScored { player, item_id, amount, unit_score, stack_score }` (one per scored extracted stack)

## Extraction Scoring
- `loot_score`: sum of configured per-item values for extracted loot-range items (`item_id` `200..=299`).
- `base_time_bonus` (piecewise, slot-based at 400ms/slot):
  - First hour (`<= 9000` slots): `+1` point per `300` slots (~2 min)
  - After first hour: `+1` point per `3000` slots (~20 min)
- Anti-idle cap:
  - `time_bonus_cap = max(5, loot_score / 4)`
  - `time_score = min(base_time_bonus, time_bonus_cap)`
- Final:
  - `run_score = loot_score + time_score`

## Inventory Conversion Policy
- On extraction:
  - convert and clear loot-range items (`200..=299`)
  - keep non-loot items (weapons/consumables/etc.)

## Topology Safety
- Added special topology guard to prevent opening the spawn south edge from room `(5,4)` north wall.
- Guard is applied in discovered room init and in adjacency-opening flows (`move_player`, `complete_job`, `unlock_door`) after topology updates.

## Unity Integration
- Added client wall constant/state for entrance stairs.
- Door mapping includes `EntranceStairs`.
- Stairs are interactable in room interaction state updates.
- Door click flow:
  - stairs triggers `ExitDungeon` transaction
  - no rubble-style optimistic movement
  - on success, client returns to `MenuScene` by default
- Session default allowlist now includes `ExitDungeon`.

## Validation Notes
- Program builds with Anchor after changes.
- `npm test` passes.
- `npm run smoke-session-join-job` currently fails on existing devnet player accounts with old layout decode mismatch after account expansion; requires reset/migration strategy for legacy PDAs before smoke can pass on old wallets.

## Publish Checklist (Deferred)
- Decide/reset strategy for legacy `PlayerAccount` PDAs (layout changed).
- Re-run session smoke using fresh/reset player PDAs.
- Tune item-value table and time bonus constants from telemetry.
- Deploy only after remaining pending gameplay features are merged.
