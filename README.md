# Loot Goblins

<div align="center">
  <strong>A shared onchain dungeon crawler built for Solana Seeker.</strong>
  <br/>
  <sub>Working title for the current build track.</sub>
</div>

<p align="center">
  <img src="Docs/media/loot-goblins-splash.png" alt="Loot Goblins splash artwork" width="100%" />
</p>

<br/>

## Table of Contents
- [What Is Loot Goblins?](#what-is-loot-goblins)
- [Gameplay at a Glance](#gameplay-at-a-glance)
- [Core Game Loop](#core-game-loop)
- [Built for Seeker + SKR](#built-for-seeker--skr)
- [Tech Stack](#tech-stack)
- [Architecture](#architecture)
- [Repository Structure](#repository-structure)
- [Current Status](#current-status)

## What Is Loot Goblins?
**Loot Goblins** is a mobile-first, shared dungeon game where every player contributes to the same evolving world.

You enter a room, team up with other players to clear rubble, open deeper paths, fight bosses, and collect loot. The dungeon state is onchain, so progress is persistent, visible, and shared across all players.

This project is designed for fast, readable play sessions (5-15 minutes) while still giving meaningful onchain ownership and progression.

## Gameplay at a Glance
| Pillar | Experience |
|---|---|
| Shared world | Everyone plays in the same season and contributes to global depth |
| Co-op progression | Multiple helpers speed up door-clearing jobs |
| Onchain persistence | Rooms, jobs, loot, and player state are program-owned accounts |
| Session-friendly | Quick runs, clear goals, mobile-first interactions |
| Seasonal rhythm | New seasons reset progression and restart the dungeon race |

## Core Game Loop
1. Spawn into the current room.
2. Move through open doors or join a rubble-clearing job.
3. Stake SKR to participate in jobs and earn completion rewards.
4. Clear routes, unlock deeper rooms, and raise global depth.
5. Loot chests or join boss fights in center encounters.
6. Repeat, optimize loadout, and push further each season.

## Built for Seeker + SKR
### Seeker-first design
- Mobile wallet flow is designed around Solana Mobile / Seed Vault patterns.
- Gameplay actions are structured for tap-driven, short-session play.
- The game loop favors frequent lightweight interactions over long desktop sessions.

### SKR as real in-game utility
- `join_job`: players stake SKR to help clear rubble doors.
- `boost_job`: players can tip SKR to accelerate progress.
- `claim_job_reward`: helpers recover stake plus reward share on completion.
- `abandon_job`: early exits refund partially with a slash to the pool.

The intent is utility, not passive token gating: SKR is used to coordinate cooperation, incentives, and pace.

## Tech Stack
### Client (Game)
| Layer | Choice |
|---|---|
| Engine | Unity `2022.3+` |
| Language | C# |
| UI | Unity UI Toolkit |
| Solana integration | Solana Unity SDK |
| Generated client | Codama-generated C# client bindings |
| Platform target | Solana Seeker / Android |

### Backend (Onchain)
| Layer | Choice |
|---|---|
| Chain | Solana |
| Program framework | Anchor `0.32.1` |
| Program language | Rust |
| Client/testing scripts | TypeScript (`tsx`, Anchor TS) |
| Token model | SPL token flows for SKR staking/rewards |
| Environment | Devnet-first iteration |

## Architecture
```text
Unity Client (C# + UI Toolkit)
    |
    | Solana Unity SDK + generated client
    v
Anchor Program (Rust)
    |
    | Program-owned accounts (PDAs)
    v
Global / Player / Room / Inventory / Presence / BossFight / HelperStake
```

### Main onchain entities
- `Global`: season config, depth, prize pool, mint info.
- `Player`: room position, jobs, equipment progress stats.
- `PlayerProfile`: skin + display identity.
- `Room`: walls, jobs, chest/boss center logic, looter tracking.
- `Inventory`: onchain item stacks.
- `RoomPresence`: scalable room occupancy + live activity state.
- `HelperStake` / `BossFight`: per-player participation records.

## Repository Structure
```text
.
|-- Assets/                  # Unity game client
|-- Docs/                    # Design docs and implementation notes
|-- solana-program/          # Anchor program, tests, scripts, IDL/codama config
|-- scripts/                 # Project-level helper scripts
`-- README.md                # You are here
```

## Current Status
- Core room traversal, job flow, chest/boss logic, and inventory paths are implemented onchain.
- Unity side includes typed wrappers and integration points for movement, jobs, center interactions, and presence rendering.
- Current network target is **Solana devnet** for active iteration.

For deeper implementation details:
- `Docs/current-game-state-and-logic.md`
- `Docs/initialimplementationplan.md`
- `solana-program/README.md`
