# Next Session TODO

Context:
- Core dungeon flow is mostly working (join/tick/complete/move/transition).
- We now want to harden session-based gameplay and finish chest/inventory gameplay loop.

## 1) Use Sessions For All Gameplay Actions (High Priority)

Goal:
- On Seeker, avoid wallet approval prompts for each action after initial session start.

Implement:
- [ ] Audit all gameplay calls in Unity and ensure they route through session-enabled instruction paths.
- [ ] Confirm session policy/allowlist includes every action used in dungeon flow.
- [ ] Add fallback behavior: if session is missing/expired, prompt to re-start session once, then continue action.
- [ ] Verify end-to-end on device: move, join job, tick, complete, claim, loot chest/boss, equip.

## 2) Validate Room Routing Logic (High Priority)

Goal:
- Ensure door topology is consistent and directional travel is correct.

Implement:
- [ ] Test edge case: room with a single open door must return player to the room they came from.
- [ ] Add debug validation/logging for door state + target coordinates before move.
- [ ] Add a lightweight regression script/test path for repeated move chains (forward/backward loops).

## 3) Chest Loot + Inventory UX (High Priority)

Goal:
- Looting chest should award items and visibly update player inventory in Unity.

Implement:
- [ ] Confirm onchain chest loot writes to `InventoryAccount` correctly for all chest outcomes.
- [ ] Add/finish Unity inventory view binding to onchain inventory items.
- [ ] Refresh UI immediately after successful loot transaction.
- [ ] Show clear player feedback (loot result + inventory update).

## 4) Unity Dev Task: Mining Animation

Owner:
- Unity (you)

Implement:
- [ ] Play mining animation while player is actively on a rubble job.
- [ ] Stop animation when leaving/completing/abandoning the job.
- [ ] Keep idle/move/combat animation transitions clean.
