# Loot Balance Tuning Sheet

This is the current on-chain loot tuning reference for:
- `solana-program/programs/chaindepth/src/instructions/loot_chest.rs`
- `solana-program/programs/chaindepth/src/instructions/loot_boss.rs`
- score value context from `solana-program/programs/chaindepth/src/state/scoring.rs`

## Bundle Rules

### Chest bundles
- Guaranteed: `1-2` distinct valuable stacks
- Optional: `0-1` weapon stack (`25%`)
- Optional: `0-1` buff stack (`18%`)
- Forced key bonus: if `room.forced_key_drop == true`, chest also grants `SkeletonKey x1` (id `214`)

### Boss bundles
- Guaranteed: `1` weapon stack
- Guaranteed: `2-4` distinct valuable stacks
- Optional: `0-1` buff stack (`60%`)

## Item Reference

### Valuables and score value
| Item | ID | Score |
|---|---:|---:|
| Silver Coin | 200 | 1 |
| Gold Coin | 201 | 3 |
| Gold Bar | 202 | 8 |
| Diamond | 203 | 12 |
| Ruby | 204 | 10 |
| Sapphire | 205 | 9 |
| Emerald | 206 | 9 |
| Ancient Crown | 207 | 20 |
| Goblin Tooth | 208 | 2 |
| Dragon Scale | 209 | 15 |
| Cursed Amulet | 210 | 11 |
| Dusty Tome | 211 | 4 |
| Enchanted Scroll | 212 | 7 |
| Golden Chalice | 213 | 14 |
| SkeletonKey (BoneKey) | 214 | 0 |
| Mystic Orb | 215 | 13 |
| Rusted Compass | 216 | 3 |
| Dwarf Beard Ring | 217 | 8 |
| Phoenix Feather | 218 | 18 |
| Void Shard | 219 | 16 |

### Weapon durability
| Weapon | ID | Durability |
|---|---:|---:|
| Bronze Pickaxe | 100 | 80 |
| Iron Pickaxe | 101 | 120 |
| Bronze Sword | 102 | 80 |
| Iron Sword | 103 | 120 |
| Diamond Sword | 104 | 200 |
| Nokia 3310 | 105 | 9999 |
| Wooden Pipe | 106 | 60 |
| Iron Scimitar | 107 | 120 |
| Wooden Tankard | 108 | 60 |

## Current Chest Tables

### Chest valuables (1-2 stacks guaranteed)
| Item | Weight | Min | Max |
|---|---:|---:|---:|
| Silver Coin | 22 | 4 | 12 |
| Gold Coin | 18 | 3 | 10 |
| Gold Bar | 8 | 1 | 2 |
| Goblin Tooth | 12 | 1 | 4 |
| Dusty Tome | 10 | 1 | 3 |
| Ruby | 6 | 1 | 2 |
| Sapphire | 6 | 1 | 2 |
| Emerald | 6 | 1 | 2 |
| Rusted Compass | 5 | 1 | 1 |
| Dwarf Beard Ring | 4 | 1 | 1 |
| Enchanted Scroll | 3 | 1 | 1 |
| SkeletonKey | 2 | 1 | 1 |

### Chest weapons (optional 25%)
| Item | Weight | Min | Max |
|---|---:|---:|---:|
| Bronze Pickaxe | 17 | 1 | 1 |
| Iron Pickaxe | 14 | 1 | 1 |
| Bronze Sword | 16 | 1 | 1 |
| Iron Sword | 12 | 1 | 1 |
| Wooden Pipe | 13 | 1 | 1 |
| Iron Scimitar | 10 | 1 | 1 |
| Wooden Tankard | 18 | 1 | 1 |

### Chest buffs (optional 18%)
| Item | Weight | Min | Max |
|---|---:|---:|---:|
| Minor Buff | 13 | 1 | 2 |
| Major Buff | 5 | 1 | 1 |

## Current Boss Tables

### Boss weapons (always 1 stack)
| Item | Weight | Min | Max |
|---|---:|---:|---:|
| Iron Pickaxe | 12 | 1 | 1 |
| Iron Sword | 13 | 1 | 1 |
| Diamond Sword | 7 | 1 | 1 |
| Nokia 3310 | 4 | 1 | 1 |
| Iron Scimitar | 10 | 1 | 1 |
| Bronze Sword | 11 | 1 | 1 |
| Bronze Pickaxe | 10 | 1 | 1 |
| Wooden Pipe | 8 | 1 | 1 |
| Wooden Tankard | 9 | 1 | 1 |

### Boss valuables (2-4 stacks guaranteed)
| Item | Weight | Min | Max |
|---|---:|---:|---:|
| Gold Coin | 19 | 6 | 18 |
| Gold Bar | 14 | 1 | 3 |
| Diamond | 7 | 1 | 2 |
| Ruby | 8 | 1 | 2 |
| Sapphire | 8 | 1 | 2 |
| Emerald | 8 | 1 | 2 |
| Ancient Crown | 4 | 1 | 1 |
| Dragon Scale | 5 | 1 | 2 |
| Cursed Amulet | 4 | 1 | 1 |
| Golden Chalice | 5 | 1 | 1 |
| Mystic Orb | 3 | 1 | 1 |
| Phoenix Feather | 3 | 1 | 1 |
| Void Shard | 3 | 1 | 1 |
| Enchanted Scroll | 4 | 1 | 2 |
| SkeletonKey | 3 | 1 | 1 |

### Boss buffs (optional 60%)
| Item | Weight | Min | Max |
|---|---:|---:|---:|
| Minor Buff | 8 | 1 | 3 |
| Major Buff | 14 | 1 | 3 |

## Tuning Guide

Use this loop for balance changes:
1. Set target per-run economy by depth band.
2. Tune guaranteed stack counts first (bundle shape), then per-item weights.
3. Tune min/max amounts last to control variance.
4. Keep high-score valuables lower weight and low max amounts.
5. Keep SkeletonKey rare in random pools, and rely on forced key chest for progression guarantee.
6. Validate with smoke sims and sample logs before changing UI assumptions.

## Practical Design Intent (Current)
- Chests: more common materials, lower value stacks, occasional utility/key.
- Bosses: more mixed and richer bundles, guaranteed weapon, stronger chance of high-value items.
- Bone key progression safety:
  - random SkeletonKey chance in both chest and boss valuable pools
  - deterministic forced-key chest bonus `+1` on specific ring/depth room
