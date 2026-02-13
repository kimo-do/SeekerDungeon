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

### 8. Clamp dungeon generation at map boundaries -- TODO

Edge rooms (where x == 0, x == 9, y == 0, or y == 9 on the current 10x10 grid) must not generate Open or Rubble walls facing outward. A door on the East wall at x=9 leads nowhere and the MovePlayer instruction correctly rejects it as `OutOfBounds`, but the player sees an interactable door and has no idea why it doesn't work. The room seed/generation logic should enforce: if a wall faces outside `MIN_COORD..MAX_COORD`, force it to `Solid`. This applies to both initial room creation and any future re-roll or season-reset logic.

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

---

## Priority Order (updated)

**Completed:**
1. ~~Make CompleteJob auto-tick~~ -- DONE (3 steps reduced to 2)
2. ~~Decouple TX callbacks from state refreshing~~ -- DONE
3. ~~Use TxResult instead of null~~ -- DONE
4. ~~Free job slot on CompleteJob~~ -- DONE (Option B)

**Remaining (in suggested order):**
1. **Clamp dungeon generation at map boundaries** -- edge rooms must not have doors facing outside the grid
2. **Merge CompleteJob + ClaimJobReward fully on-chain** -- eliminates the 2-TX chain, the cross-room cleanup system becomes trivial
3. **Formalize optimistic state** -- replaces ad-hoc flags with a clean `OptimisticStateManager`
4. **TX pipeline** -- reusable sequential TX runner with abort-on-failure
