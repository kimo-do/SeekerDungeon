# Spectator Duels (Room-Level Visualization)

Goal:
- Let players in the same room see other players dueling in real time (or near-real-time replay), without camera takeover.

## Product Behavior

- If two visible room occupants are in an active duel, nearby players should see:
  - Both duelers move into duel positions (or use current positions if close enough).
  - Attack/hit-splat replay using onchain transcript.
  - HP bars above duelers during replay.
  - No modal popup for spectators.
  - No camera priority/zoom change for spectators.
- Spectator replay should be informational only (no local inputs enabled for that duel instance).
- If duel already ended before spectator receives update, show replay once (optional fast mode) and then restore idle visuals.

## Data/Detection Strategy

- Reuse current duel account polling feed (`FetchRelevantDuelChallengesAsync` style logic) but extend for room-wide relevance:
  - Query duels where `status in {Open, PendingRandomness, Settled}` and season matches current season.
  - Filter to duels whose room `(x,y)` equals spectator’s current room.
  - Only start replay when both duel wallets are currently rendered in room occupants.
- Deduplicate replay triggers by `(duel_pda, status_transition)` so polling does not replay repeatedly.
- Maintain a short-lived local cache for active spectator duel sessions.

## Visual/Simulation Rules

- Reuse duel replay pipeline from `DuelCoordinator`:
  - Same hit timings / splats / crit scaling.
  - Same deterministic transcript playback.
- Spectator mode flags:
  - Disable camera takeover.
  - Disable local result modal.
  - Optional small toast only: `"Duel: A vs B"`.
- Keep replay speed slightly faster for non-participants (optional tunable).

## Conflict Handling

- Add visual lock for duel participants (already present) and extend semantics for spectator duels:
  - Prevent room presence polling from rebinding facing/animations during replay.
  - Prevent release/pool churn while replay in progress.
- If same player appears in another duel while already locked:
  - Queue next replay or skip with cooldown.
- If one dueler leaves room mid-replay:
  - Abort replay gracefully, clear lock, clean transient effects.

## Performance / Networking

- Poll cadence:
  - Reuse existing duel poll loop; avoid adding high-frequency duplicate loop.
  - Consider room-only filtered pass to reduce account processing.
- Avoid heavy allocations each poll:
  - Reuse collections where possible.
- Enforce replay cooldown per duel PDA to prevent spam on transient RPC flaps.

## UX Details

- Optional spectator nameplate indicator during replay:
  - `"DUELING"` status chip over both participants.
- Optional lightweight room feed line:
  - `"PlayerA is dueling PlayerB for 5 SKR"`.
- Ensure duel replay never blocks door interactions for non-participants.

## Implementation Checklist

1. Extend duel fetch layer for room-scoped spectator relevance.
2. Add spectator duel orchestration path in `DuelCoordinator` (or sibling coordinator).
3. Add replay dedupe map keyed by `duel_pda + settled_slot/status`.
4. Reuse existing replay function with `spectatorMode=true` branch:
   - no camera changes
   - no result modal
5. Integrate with visual lock registry for both duelers.
6. Add abort/cleanup path for missing actor or room change.
7. Add tunables:
   - spectator replay enabled
   - spectator replay speed multiplier
   - spectator toast on/off
8. Add debug logs behind bool:
   - replay queued/started/skipped/completed
   - lock/unlock events

## Test Scenarios

1. Two players duel; third player in room sees full replay.
2. Spectator joins room mid-duel; replay starts once actors available.
3. Poll repeats same duel state; replay does not duplicate.
4. One dueler moves/disconnects mid-replay; cleanup is correct.
5. Multiple duels in same room; queueing/prioritization behaves predictably.
6. Participant still gets participant-specific camera/result flow unchanged.
7. Spectator flow does not reintroduce facing flips or HP bar respawn flicker.
