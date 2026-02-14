# Locked Doors Implementation (Local-Only, Not Deployed)

Date: 2026-02-14

## What Was Implemented

### Onchain (Anchor)
- Added locked wall state:
  - `WALL_LOCKED = 3`
- Added lock kinds:
  - `LOCK_KIND_NONE = 0`
  - `LOCK_KIND_SKELETON = 1`
- Extended `RoomAccount` with:
  - `door_lock_kinds: [u8; 4]`
  - `forced_key_drop: bool`
- Added `unlock_door(direction)` instruction:
  - Validates direction and player-in-room.
  - Requires selected wall is locked.
  - Resolves required key from lock kind.
  - Consumes key from inventory (`remove_item(required_key, 1)`).
  - Opens the door and clears lock kind on both sides.
  - Initializes adjacent room if missing.
  - Emits `DoorUnlocked`.
- Added session allowlist bit:
  - `UNLOCK_DOOR = 1 << 13`
- Added errors:
  - `WallNotLocked`
  - `MissingRequiredKey`
  - `InvalidLockKind`
- Added event:
  - `DoorUnlocked { room_x, room_y, direction, player, key_item_id }`

### Shared Room Generation Refactor
- Added `state/room_generation.rs` and moved deterministic generation there.
- `move_player`, `complete_job`, and `unlock_door` now share generation logic via:
  - `initialize_discovered_room(...)`
  - shared `calculate_depth`, hash/wall generation, and center generation.

### Deterministic Lock + Key Guarantee Rules
- Locks can spawn only on discovered rooms at depth `>= 2`.
- Never lock the return/entrance direction.
- Max one locked door per room.
- Ensures at least one non-locked interactable exit remains.
- Forced key chest rule:
  - For each depth ring `>= 2`, one deterministic room is forced chest and sets `forced_key_drop=true`.
- Chest loot now:
  - keeps normal drop logic
  - additionally grants `SkeletonKey x1` when `forced_key_drop=true`.

### Unity Integration
- Added wall enum support:
  - `RoomWallState.Locked = 3`
- Added lock metadata to `DoorJobView`:
  - `LockKind`
  - `RequiredKeyItemId`
  - `IsLocked`
- Added mapping from generated `RoomAccount`:
  - `DoorLockKinds`
  - `ForcedKeyDrop`
- Updated interaction:
  - `LGManager.InteractWithDoor` now auto-attempts unlock on locked walls.
  - Added `LGManager.UnlockDoor(direction)`.
  - Missing key returns a clear error message.
- Session allowlist in Unity updated:
  - `SessionInstructionAllowlist.UnlockDoor = 1 << 13`

## Validation Run
- Anchor build: success (WSL fallback command used due CRLF in `scripts/wsl/run.sh`).
- `npm test`: success.
- `npm run force-reset-season`: success.
- `npm run smoke-session-join-job`: success.

## Publish Status
- Not deployed onchain.
- This branch is local implementation only.

## TODO Before Publish
- Deploy upgraded program + IDL to devnet.
- Reset or migrate season/accounts because `RoomAccount` layout changed.
- Re-run smoke tests against upgraded deployment:
  - session join job
  - room routing
  - locked door unlock flow
- Tune constants with live playtesting:
  - lock spawn cadence
  - forced key chest cadence
  - key economy pacing.
