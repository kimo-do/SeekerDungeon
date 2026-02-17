# Architecture Recommendations

> Based on a full session of debugging the job system, room transitions, and stale-RPC visual glitches. The question: **What can we change on the Solana program or Unity side (or both) to make development easier and spend more time on features and game feel?**

---

## Solana Program Changes

### 1. Merge TickJob + CompleteJob (high impact) -- DONE

> Implemented in `complete_job.rs`. CompleteJob now auto-ticks progress inline before the completion check. TickJob remains as a standalone permissionless instruction for UI timer updates.

### 2. Auto-claim on CompleteJob, or merge CompleteJob + ClaimJobReward (highest impact) -- PARTIAL

> **Option B implemented**: `complete_job` now calls `remove_job()` to free the player's active job slot immediately on completion. This prevents `TooManyActiveJobs` even if the player never sends `ClaimJobReward`.
>
> **Remaining**: Full merge (Option A) where token payout + HelperStake closure happen atomically inside CompleteJob. This would eliminate the need for the separate `ClaimJobReward` TX entirely and simplify the auto-completer from 2 steps to 1.

### 3. Keep TickJob as a permissionless "UI update" instruction -- DONE (no change needed)

TickJob is useful for keeping the client's timer display accurate. Since it's permissionless, a relayer or the client can call it periodically just to update the on-chain progress field so the UI reflects reality. But it should not be a prerequisite for completion.

### 8. Clamp dungeon generation at map boundaries -- DONE

> Implemented `RoomAccount::clamp_boundary_walls()` in `room.rs`. This helper forces any wall facing outside `MIN_COORD..MAX_COORD` to `WALL_SOLID`. Called after every wall generation site: `move_player.rs`, `complete_job.rs`, `ensure_start_room.rs`, and `init_global.rs`. Also integrated into `generate_start_walls` which now accepts `(x, y)` coordinates. Edge rooms will no longer show interactable doors that lead nowhere.

---

## Unity Client Changes

### 4. Decouple TX callbacks from event-driven snapshots (highest impact) -- DONE

> `ExecuteGameplayActionAsync` no longer accepts an `onSuccess` callback. All 17 LGManager methods return `TxResult` and do not fetch state or fire events. Callers control their own refreshes. `SuppressEventSnapshots` / `ResumeEventSnapshots` public methods removed.

### 5. Use a Result type instead of returning null (medium impact) -- DONE

> `TxResult` struct added (`Assets/Scripts/Solana/TxResult.cs`) with `Success`, `Signature`, `Error` properties and `Ok()`/`Fail()` factory methods. All LGManager gameplay methods return `UniTask<TxResult>`. All callers updated (`DungeonInputController`, `DungeonJobAutoCompleter`, `DungeonManager`, `LGMainMenuCharacterManager`, `LGTestUI`).

### 6. Formalize the optimistic state pattern (medium impact) -- TODO

We added three separate optimistic flags: `_optimisticJobDirection`, `_optimisticTargetRoom`, `_roomEntryDirection`. Each handles a specific case of "TX confirmed but RPC is stale." A proper pattern would be:

```csharp
// Before sending TX:
var override = optimisticLayer.Push(expectedStateChange);

// On TX failure:
optimisticLayer.Revert(override);

// On real data arriving that matches:
optimisticLayer.Confirm(override);

// On timeout:
optimisticLayer.AutoExpire();
```

This could be a small `OptimisticStateManager` class that DungeonManager consults when building snapshots, replacing the ad-hoc fields.

### 7. Add a TX pipeline for multi-step operations (lower impact, but nice) -- TODO

The auto-completer and cleanup logic both chain multiple TXs (complete -> claim). A reusable pipeline would handle:

- Sequential execution with return-value checking
- Abort-on-failure
- Single state refresh at the end
- Logging of the full chain result

### 9. Remove magic numbers for grid size / make dungeon dimensions configurable -- DONE

> All hardcoded grid values replaced with `GlobalAccount::START_X/Y` and `MIN_COORD/MAX_COORD`:
> - `complete_job.rs` `calculate_depth` now uses `GlobalAccount::START_X/Y` instead of literal `5`.
> - `complete_job.rs` `is_forced_depth_one_chest` now computes offsets from `START_X/Y` instead of hardcoded coordinates.
> - Unity side: `LGConfig.cs` already had named constants; two remaining hardcoded `"(5, 5)"` strings in `LGTestUI.cs` were updated to use `LGConfig.START_X/Y`.
> - Resizing the dungeon is now a matter of changing four constants in `GlobalAccount` and mirroring them in `LGConfig.cs`.

---

## Priority Order (updated)

**Completed:**
1. ~~Make CompleteJob auto-tick~~ -- DONE (3 steps reduced to 2)
2. ~~Decouple TX callbacks from state refreshing~~ -- DONE
3. ~~Use TxResult instead of null~~ -- DONE
4. ~~Free job slot on CompleteJob~~ -- DONE (Option B)

5. ~~Remove magic numbers for grid size~~ -- DONE (constants everywhere, grid is now configurable)
6. ~~Clamp dungeon generation at map boundaries~~ -- DONE (`clamp_boundary_walls` applied at all generation sites)

**Remaining (in suggested order):**
1. **Merge CompleteJob + ClaimJobReward fully on-chain** -- eliminates the 2-TX chain, the cross-room cleanup system becomes trivial
2. **Formalize optimistic state** -- replaces ad-hoc flags with a clean `OptimisticStateManager`
3. **TX pipeline** -- reusable sequential TX runner with abort-on-failure
