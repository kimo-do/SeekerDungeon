# Seeker Dungeon -- Gameplay Overview

A dungeon crawler on Solana where players explore procedurally generated rooms, clear rubble to open new paths, fight bosses, loot chests, and collect items. All game state lives on-chain.

---

## The Dungeon

The dungeon is a 10x10 grid of rooms. Every player starts in the center room at (5,5). Rooms are generated on-chain the first time someone enters them, so the dungeon is discovered collaboratively.

**Depth** increases as you move further from the center. The room at (5,5) is depth 0. One step away is depth 1, two steps is depth 2, and so on. Deeper rooms have longer jobs and tougher bosses.

### Walls and Doors

Each room has four walls (North, South, East, West). A wall can be:

- **Solid** -- impassable, no door
- **Rubble** -- blocked by rubble that players can clear (a "job")
- **Open** -- passable, walk right through

When you clear rubble on a wall, it becomes Open permanently. The room on the other side is created at that moment if it didn't already exist, and the return door is always set to Open so you can always go back.

### Room Centers

Each room can have something in the middle:

- **Empty** -- nothing special
- **Chest** -- lootable once per player per season
- **Boss** -- a boss fight that multiple players can participate in

At depth 1, rooms have roughly a 50/50 chance of containing a chest. At depth 2 and beyond, rooms have a 50% chance of containing a boss instead.

---

## Jobs (Rubble Clearing)

Rubble walls are cleared by working "jobs." Here's the flow:

1. **Join** -- Click the rubble. Your character walks over, pulls out a pickaxe, and starts mining. This stakes a small amount of SKR tokens.
2. **Work** -- A timer counts down based on the job's required progress. More helpers = faster completion. The base time at depth 0 is about 2 minutes and scales up with depth.
3. **Complete** -- When the timer hits zero, the rubble is automatically cleared. The wall becomes Open and the adjacent room is generated.
4. **Claim** -- Your staked SKR is returned plus a bonus from the prize pool.

You can also:
- **Boost** a job by tipping extra SKR to speed it up
- **Abandon** a job to get 80% of your stake back

Multiple players can work the same rubble together. Each helper speeds up the timer. When the job completes, everyone gets their stake back plus a share of the bonus.

---

## Chests

Rooms with a chest in the center can be looted once per player. Click the chest to open it and receive a random item:

- **60% chance**: Valuables (coins, gems, curiosities) -- 1 to 5 items
- **25% chance**: A weapon (pickaxe, sword, scimitar, pipe, tankard) -- 1 item
- **15% chance**: Buff items -- 1 to 3 items

Each player gets their own loot roll. Looting is tracked per-player so everyone gets a fair shot.

---

## Boss Fights

Rooms at depth 2+ can contain a boss. Boss HP scales with depth and boss variant (there are 4 boss types).

1. **Join** -- Click the boss to join the fight. Your equipped weapon determines your DPS.
2. **Tick** -- Damage is applied over time based on elapsed Solana slots and total party DPS.
3. **Defeat** -- When HP hits 0, the boss is defeated.
4. **Loot** -- Only players who participated in the fight can loot. Boss loot is better than chest loot:
   - 35% Valuables (3-10 items)
   - 35% Weapons (including rarer ones)
   - 30% Buffs (2-6 items)

---

## Items and Inventory

Items are stored in an on-chain inventory account. There are three categories:

### Weapons (equippable)
| Item | Durability |
|------|-----------|
| Bronze Pickaxe | 80 |
| Iron Pickaxe | 120 |
| Bronze Sword | 80 |
| Iron Sword | 120 |
| Diamond Sword | 200 |
| Nokia 3310 | 9999 |
| Wooden Pipe | 60 |
| Iron Scimitar | 120 |
| Wooden Tankard | 60 |

Every new player receives a starter Bronze Pickaxe when they create their profile.

### Valuables (collectibles)
Silver Coin, Gold Coin, Gold Bar, Diamond, Ruby, Sapphire, Emerald, Ancient Crown, Goblin Tooth, Dragon Scale, Cursed Amulet, Dusty Tome, Enchanted Scroll, Golden Chalice, Skeleton Key, Mystic Orb, Rusted Compass, Dwarf Beard Ring, Phoenix Feather, Void Shard.

### Buffs
Minor Buff, Major Buff.

---

## Character Customization

Players pick a character skin and display name when creating their profile. There are 17 skins:

Cheeky Goblin, Scrappy Dwarf, Drunk Dwarf, Fat Dwarf, Friendly Goblin, Ginger Beard, Happy Drunk Dwarf, Idle Goblin, Idle Human, Jolly Dwarf, Jolly Dwarf II, Old Dwarf, Scrappy Ginger Beard, Scrappy Dwarf II, Scrappy Assassin, Scrappy Skeleton, Sinister Hooded Figure.

Your skin and equipped weapon are visible to other players when you're in the same room.

---

## Multiplayer

The dungeon is shared. When you enter a room, you can see other players who are also in that room. You see their character skin, display name, and what they're doing (idle, working a job, fighting a boss). Players appear at the door or job they're working on.

Room occupancy updates in real-time through Solana account subscriptions.

---

## Sessions

To avoid signing every single action with your wallet, the game supports delegated sessions. When you connect, a temporary session key is created that can sign gameplay transactions on your behalf for up to 60 minutes. This makes the experience feel like a regular game instead of a blockchain app.

---

## Seasons

The dungeon layout is tied to a season seed. When a season resets, a new seed generates an entirely new dungeon layout. All rooms, loot receipts, and boss states start fresh. Player accounts and inventories persist across seasons.

---

## Current Game Flow

1. **Connect wallet** on the loading screen
2. **Create profile** -- pick a skin and display name
3. **Enter the dungeon** -- spawn in the center room
4. **Explore** -- walk through open doors to discover rooms
5. **Clear rubble** -- work jobs to open new paths (earns SKR bonus)
6. **Loot chests** -- find and open chests for items
7. **Fight bosses** -- team up to defeat bosses deeper in the dungeon
8. **Equip gear** -- use weapons from your inventory
9. **Go deeper** -- push further from the center for tougher challenges and better rewards
