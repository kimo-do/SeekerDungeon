# Seeker Jackpot Event Plan

Status:
- Previous todo section is complete and cleared.
- This file now tracks implementation plan for the jackpot rat-race event.

## Goal
Add a periodic high-stakes jackpot event that creates a dungeon-wide frenzy while keeping winner selection fair via VRF.

## Event Summary (Target Behavior)
- A global jackpot accumulates over normal gameplay.
- When trigger condition is met, jackpot event starts.
- Program selects one previously-created room this season as the Treasure Room (via VRF).
- Clients show a special interactable in that room (epic chest / portal / glowing object).
- Players interact to enter the raffle.
- After event window (target: 3 minutes), program picks winner via VRF.
- Winner receives jackpot payout.
- Event is closed/reset and normal gameplay continues.

## Trigger Condition (Implementation Choice)
Use one of these (pick easiest/reliable):
1. Pot threshold reached.
2. Time interval reached.
3. Hybrid: whichever happens first.

Current recommendation:
- Start with time-based trigger for predictability and simpler ops.
- Optional pot threshold as secondary gate.

## On-Chain Plan

### 1) Add Jackpot Event State
Create event account to track:
- Event status: inactive / active / settling / settled.
- Event id (incrementing).
- Start slot, end slot.
- Treasure room coordinates (x, y).
- Jackpot amount snapshot (or payout amount at settlement).
- Entrant count.
- VRF request metadata for room-pick and winner-pick.

### 2) Determine Treasure Room
At event start:
- Choose random room from rooms discovered this season.
- Selection driven by VRF.
- Must only choose valid previously-created room.

Note:
- Need a practical way to sample discovered rooms (index/list/scan strategy).

### 3) Enter Raffle Instruction
Player calls `enter_jackpot_raffle` while event active.
Checks:
- Player is in dungeon.
- Player is in Treasure Room.
- One entry per player per event.
- Player meets eligibility criteria.

Store entrant record (PDA) for later fair draw.

### 4) Eligibility Filters (Anti-Bot Friction)
Use fields already available on-chain where possible.
Candidate gates:
- Minimum jobs completed.
- Minimum doors/jobs participated in this run/season.
- Minimum duels played.
- Minimum loot actions.
- Minimum active slots since run start.

Scope recommendation:
- Start with 1-2 simple gates from already-stored counters.
- Avoid adding many new counters unless necessary.

### 5) Winner Selection + Payout
At event end:
- Request/consume VRF randomness.
- Select winner uniformly among entrants.
- Transfer jackpot from prize pool to winner token account.
- Mark event settled.

### 6) Cleanup / Reset
After settlement:
- Event status inactive.
- Entry window closed.
- UI interactable removed by clients.
- Ready for next trigger cycle.

## Client Plan (Unity)
- Listen for active jackpot event account.
- If local room matches Treasure Room and event active:
  - Spawn/show special jackpot interactable.
- On interact:
  - Open modal with event info and eligibility status.
  - Submit `enter_jackpot_raffle` if eligible.
  - Show confirmation toast/state after success.
- Show global event banner/state:
  - "Loot Portal has spawned!"
  - Countdown to raffle close.

## UX Copy (Draft)
- Event start: "Loot Portal has spawned! Find the Treasure Room before time runs out."
- Enter success: "You are in the raffle. Winner selected at event end."
- Event end: "Jackpot claimed by <player>."

## Implementation Checklist
1. Define jackpot event account schema and status enum.
2. Decide trigger condition (time / threshold / hybrid).
3. Implement event start flow + room selection.
4. Implement raffle entry instruction + entrant PDA.
5. Implement eligibility checks (minimal first pass).
6. Implement event settlement + VRF winner selection.
7. Implement payout transfer from prize pool.
8. Implement event reset lifecycle.
9. Add Unity event UI + room interactable + modal.
10. Add smoke test path for full event lifecycle.

## Questions To Ask Me During Implementation
When implementation starts, stop and ask for decisions on:
1. Trigger mode: time-only, threshold-only, or hybrid?
2. Exact timing values (cooldown + event duration).
3. Eligibility gates for v1 (which counters and minimum values).
4. Jackpot payout split (100% winner vs reserve percentage).
5. Treasure room visibility in UI (exact marker behavior).
6. Fallback behavior if zero entrants.
7. Whether loser consolation exists (likely no for v1).

## Notes
- Keep this feature minimal and additive: no broad dungeon redesign.
- Reuse existing SKR prize pool and VRF patterns where possible.
- Optimize for hackathon-stable implementation over perfect anti-bot guarantees.
