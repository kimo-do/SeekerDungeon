# Loot Goblins Technical Writeup (2026-02-23)

## Project Summary
Loot Goblins is a multiplayer dungeon crawler built in Unity with an Anchor/Solana backend where core run state, room discovery, jobs, boss fights, inventory, and extraction outcomes are authoritative on-chain.

## Core Concept: The Rat Race in a Messy Dungeon
The technical architecture is designed around a shared "rat race" loop:
- Players compete and cooperate in the same persistent dungeon space.
- Every opened path, cleared rubble wall, and defeated boss alters shared world state.
- The dungeon is intentionally dense and messy (rubble, locked routes, risky depth scaling), creating constant pressure to move fast, make tradeoffs, and race other players to opportunities.

From a systems perspective, this means the game prioritizes deterministic room generation, concurrent participation, and low-friction interaction loops that still preserve trust and fairness.

## High-Level Architecture
- Client: Unity (C#) in `Assets/`
- Program: Solana Anchor program in `solana-program/`
- Program-client boundary: IDL-generated client code + Unity domain wrappers
- Data model: PDA-based account graph for global state, player state, room state, inventory, stake records, and boss participation

The Unity client is presentation + input + local UX orchestration. The Solana program owns game truth for multiplayer-critical outcomes.

## On-Chain State Model
Primary account types:
- Global config/state (`global` PDA)
- Player runtime state (`player` PDA)
- Player profile visuals (`profile` PDA)
- Room state (`room` PDA)
- Door job stake records (`stake` PDA)
- Boss participation records (`boss_fight` PDA)
- Player inventory (`inventory` PDA)
- Presence index for room occupancy (`presence` PDA)

Why this structure:
- Supports a persistent shared dungeon where players can enter/leave independently.
- Keeps race-critical interactions (jobs, boss damage, loot eligibility, extraction) verifiable.
- Enables room-level subscriptions in Unity for live multiplayer rendering.

## World Generation and Progression Design
Dungeon progression uses deterministic generation from season seed + coordinates:
- Center spawn room is fixed and safe.
- Depth 1 emphasizes chest discovery and early progression.
- Depth 2+ introduces boss pressure and lock/key constraints.
- Return paths are protected by generation rules to reduce hard dead-ends.

This supports the intended rat-race behavior: the deeper players push, the more contested and volatile the run economy becomes.

## Gameplay Systems as Transactions
### Door Job Loop (Rubble Clearing)
Join -> Tick -> Complete -> Claim
- Stake participation is explicit and recoverable by rule.
- Multi-helper acceleration creates social pressure and race dynamics.
- Door completion permanently opens routes for everyone.

### Boss Loop
Join -> Tick damage -> Defeat -> Eligible looters claim
- Damage and participation are captured in on-chain records.
- Bosses act as high-risk race checkpoints in deeper rooms.

### Extraction Loop
- Players return to entrance stairs and exit the dungeon to finalize run outcomes.
- This creates a strategic push-your-luck layer between continuing deeper vs securing progress.

## Multiplayer and Presence
Room occupancy is tracked by indexed presence accounts:
- Efficiently query who is in a room.
- Show skin/equipment/activity state in Unity.
- Preserve shared-world feeling critical to the rat-race identity.

## Economy and Fairness Constraints
- Staking + reward flows for door jobs align contribution with payout.
- Loot rights are gated by participation and per-player tracking rules.
- Deterministic generation reduces manipulation surface area.
- Program-side checks enforce invariant safety despite untrusted clients.

## Solana-Specific Constraints and Mitigations
### Constraint: Transactions are not free/instant in the same way as local game state
Mitigation:
- Keep interaction loops short and composable.
- Use tick-based progression where needed.
- Provide clear client feedback for pending/confirmed actions.

### Constraint: Account size and compute budgets are finite
Mitigation:
- Split state into focused PDAs.
- Index presence separately from core room data.
- Keep instruction responsibilities narrow.

### Constraint: Multiplayer contention (many players touching same room state)
Mitigation:
- Model explicit join/claim records.
- Use deterministic tie-safe state transitions.
- Avoid hidden client-side authority.

## Unity Integration Notes
When account layouts/instructions/events change:
1. Rebuild program to refresh IDL.
2. Regenerate Unity generated client (`Assets/Scripts/Solana/Generated/LGClient.cs`).
3. Update domain wrappers in `Assets/Scripts/Solana/LGDomainModels.cs` where needed.

This keeps gameplay code insulated from raw serialization concerns.

## Testing and Operational Workflow
For Solana program changes:
- Use repo scripts in `solana-program/scripts/wsl/` (`run.sh`, `build.sh`).
- Use `npm` workflows.
- Validate with `npm test` before finishing.
- If session auth logic changes, also run `npm run smoke-session-join-job`.

## Current Technical Risks and Focus Areas
- Throughput stress under high room contention.
- UX latency handling for transaction lifecycle states.
- Long-session inventory/account growth management.
- Tooling consistency between Unity iteration speed and program/IDL updates.

## Why the Architecture Fits the Game Concept
Loot Goblins' "messy dungeon rat race" only works if players trust that competition is fair, shared, and persistent. Keeping race-defining systems on-chain provides this trust layer, while Unity handles responsive moment-to-moment feel and readability.
