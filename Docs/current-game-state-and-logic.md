# ChainDepth Current Onchain Logic (Unity Dev View)

This is the current gameplay logic that exists onchain, translated into Unity-friendly terms.

## Core Accounts

### Global (`["global"]`)
- Season-wide config and stats.
- Important fields:
  - `season_seed`
  - `depth`
  - `skr_mint`, `prize_pool`
  - `jobs_completed`, `end_slot`

### Player (`["player", wallet]`)
- Per-player position and gameplay state.
- Important fields:
  - `current_room_x`, `current_room_y`
  - `active_jobs` (max 4 door jobs)
  - `equipped_item_id` (0 = no weapon equipped)
  - `jobs_completed`, `chests_looted`

### Player Profile (`["profile", wallet]`)
- Visual profile data, currently:
  - `skin_id`
  - `display_name`

### Room (`["room", season_seed, x, y]`)
- Authoritative state for one map room.
- Door fields:
  - `walls[4]`: `0=solid`, `1=rubble`, `2=open`
  - `helper_counts`, `progress`, `base_slots`, `job_completed`, `bonus_per_helper`
- Center fields:
  - `center_type`: `0=empty`, `1=chest`, `2=boss`
  - `center_id`: boss identifier for Unity spawn selection
  - Boss runtime: `boss_max_hp`, `boss_current_hp`, `boss_total_dps`, `boss_fighter_count`, `boss_defeated`
- Loot memory:
  - `looted_by` (who already looted this center)

### Helper stake (`["stake", room, direction, player]`)
- One PDA per player per door direction job.
- Used for stake/refund/claim flow.

### Boss fight (`["boss_fight", room, player]`)
- One PDA per player per room boss.
- Stores fighter DPS snapshot used in boss HP calculations.

### Inventory (`["inventory", player]`)
- Onchain inventory stacks.
- Item stack = `(item_id, amount, durability)`.
- Max 64 stacks.

### Room Presence Index (`["presence", season_seed, x, y, player]`)
- Indexed occupancy record for scalable room rendering.
- Stores:
  - `skin_id`
  - `equipped_item_id`
  - activity (`idle` / `door job` / `boss fight`)
  - `is_current`

## Generation Rules (Now)

### Spawn room `(5,5)`
- Center is always empty.
- Four directions always exist, each door randomly starts as rubble or open.

### Depth 1 rooms
- Chest chance = 50%.
- Guaranteed at least one chest in first ring:
  - one of the 4 adjacent rooms is forced chest by `season_seed`.

### Depth 2+ rooms
- Boss chance = 50%.
- If boss spawns:
  - `center_type = boss`
  - `center_id` set deterministically for Unity boss variant
  - HP scales by depth and boss id.

## Door Job Flow (Rubble Doors)

`ChainDepthManager.InteractWithDoor(direction)` performs:
- `Join -> Tick -> Complete -> Claim`

```mermaid
flowchart TD
    A[Click Door] --> B{Wall state}
    B -->|Solid| C[Cannot interact]
    B -->|Open| D[Already open]
    B -->|Rubble| E{Player has active job?}
    E -->|No| F[JoinJob]
    E -->|Yes| G{Job completed?}
    G -->|Yes| H[ClaimJobReward]
    G -->|No| I{progress >= base_slots?}
    I -->|No| J[TickJob]
    I -->|Yes| K[CompleteJob]
```

Notes:
- Progress updates only when `tick_job` is sent.
- More helpers speed progress (`elapsed_slots * helper_count`).

## Boss Flow (Center Boss)

```mermaid
flowchart TD
    A[Enter boss room] --> B[Equip item outside battle optional]
    B --> C[JoinBossFight]
    C --> D[TickBossFight loop]
    D --> E{boss_current_hp == 0?}
    E -->|No| D
    E -->|Yes| F[Boss defeated]
    F --> G[LootBoss per fighter once]
```

Rules:
- Only rooms with `center_type = boss` can be joined.
- Damage is time-based by total DPS of joined fighters.
- A fighter's DPS is snapped from `equipped_item_id` at join time.
- Weapon changes are expected outside combat flow.
- Looting a defeated boss is fighters-only and once per player.

## Chest/Boss Loot Rules

- `looted_by` prevents double-loot.
- Max looters per room center: 128.
- Chest and boss loot both write to onchain inventory.

Current item ids:
- `1 = Ore`
- `2 = Tool`
- `3 = Buff`

## Unity Methods Already Wired

In `ChainDepthManager.cs`:
- Existing:
  - `MovePlayer`, `JoinJob`, `TickJob`, `CompleteJob`, `ClaimJobReward`, `LootChest`, `BoostJob`, `AbandonJob`
- New:
  - `InteractWithCenter()` (auto selects chest/boss action)
  - `EquipItem(itemId)`
  - `JoinBossFight()`
  - `TickBossFight()`
  - `LootBoss()`
  - `GetCurrentRoomView()` / `GetCurrentPlayerView()` typed wrappers
  - `FetchRoomOccupants(roomX, roomY)` for occupant rendering
  - `StartRoomOccupantSubscriptions(roomX, roomY)` live updates from room-presence accounts
  - `SetPlayerSkin(skinId)` profile + presence sync
  - `CreatePlayerProfile(skinId, displayName)` onboarding profile setup + one-time starter bronze pickaxe

Typed Unity wrappers are in:
- `Assets/Scripts/Solana/ChainDepthDomainModels.cs`
  - `RoomView.HasChest()`, `RoomView.IsEmpty()`, `RoomView.TryGetMonster(out monster)`
  - enums for center type, wall state, direction, item ids

## Practical Constraints

- Active door jobs/player: 4.
- Inventory stacks/player: 64.
- Max looters per center: 128.
- Boss/chest/room state is season-scoped by `season_seed`.
