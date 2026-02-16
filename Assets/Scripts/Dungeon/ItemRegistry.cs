using System;
using System.Collections.Generic;
using SeekerDungeon.Solana;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    public enum ItemCategory
    {
        Weapon,
        Valuable,
        Consumable
    }

    public enum ItemRarity
    {
        Common,
        Rare,
        Legendary,
        Mystic
    }

    [CreateAssetMenu(menuName = "SeekerDungeon/ItemRegistry", fileName = "ItemRegistry")]
    public sealed class ItemRegistry : ScriptableObject
    {
        // ── Rarity colours ──
        // Common:    warm grey
        // Rare:      blue
        // Legendary: gold / orange
        // Mystic:    purple
        private static readonly Color CommonColor    = new(0.68f, 0.68f, 0.68f, 1f);
        private static readonly Color RareColor      = new(0.20f, 0.50f, 1.00f, 1f);
        private static readonly Color LegendaryColor = new(1.00f, 0.72f, 0.10f, 1f);
        private static readonly Color MysticColor    = new(0.70f, 0.15f, 0.95f, 1f);

        public static Color RarityToColor(ItemRarity rarity)
        {
            return rarity switch
            {
                ItemRarity.Common    => CommonColor,
                ItemRarity.Rare      => RareColor,
                ItemRarity.Legendary => LegendaryColor,
                ItemRarity.Mystic    => MysticColor,
                _ => Color.white,
            };
        }

        [Serializable]
        public sealed class ItemEntry
        {
            public ItemId id;
            public string displayName;
            public ItemCategory category;
            public ItemRarity rarity;
            public Sprite icon;
        }

        [SerializeField] private List<ItemEntry> items = new();

        /// <summary>
        /// Look up an item entry by id. Returns null if not found.
        /// </summary>
        public ItemEntry Get(ItemId id)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].id == id)
                {
                    return items[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Get the display name for an item, falling back to the enum name.
        /// </summary>
        public string GetDisplayName(ItemId id)
        {
            var entry = Get(id);
            if (entry != null && !string.IsNullOrWhiteSpace(entry.displayName))
            {
                return entry.displayName;
            }

            return id.ToString();
        }

        /// <summary>
        /// Get the rarity tier for an item, falling back to Common.
        /// </summary>
        public ItemRarity GetRarity(ItemId id)
        {
            var entry = Get(id);
            return entry?.rarity ?? ItemRarity.Common;
        }

        /// <summary>
        /// Get the particle/rarity color for an item, derived from its rarity tier.
        /// </summary>
        public Color GetParticleColor(ItemId id)
        {
            return RarityToColor(GetRarity(id));
        }

        /// <summary>
        /// Get the icon sprite for an item. Returns null if not configured.
        /// </summary>
        public Sprite GetIcon(ItemId id)
        {
            return Get(id)?.icon;
        }

        /// <summary>
        /// Returns true if the item is a wearable weapon (ID range 100-199).
        /// Works even if the item is not registered in the list.
        /// </summary>
        public static bool IsWearable(ItemId id)
        {
            var raw = (ushort)id;
            return raw >= 100 && raw <= 199;
        }

        /// <summary>
        /// Get the category for an item. Falls back to Valuable if not registered.
        /// </summary>
        public ItemCategory GetCategory(ItemId id)
        {
            var entry = Get(id);
            if (entry != null) return entry.category;

            // Infer from ID range
            var raw = (ushort)id;
            if (raw >= 100 && raw <= 199) return ItemCategory.Weapon;
            if (raw >= 300 && raw <= 399) return ItemCategory.Consumable;
            return ItemCategory.Valuable;
        }

        /// <summary>
        /// Returns weapon damage/DPS value for equippable weapons.
        /// Null when the item is not a weapon with a combat value.
        /// </summary>
        public static int? GetWeaponDamage(ItemId id)
        {
            return id switch
            {
                ItemId.LegacyTool => 5,
                ItemId.BronzePickaxe => 4,
                ItemId.IronPickaxe => 6,
                ItemId.BronzeSword => 7,
                ItemId.IronSword => 10,
                ItemId.DiamondSword => 16,
                ItemId.Nokia3310 => 22,
                ItemId.WoodenPipe => 5,
                ItemId.IronScimitar => 12,
                ItemId.WoodenTankard => 3,
                _ => null
            };
        }

        /// <summary>
        /// Returns extraction score value for loot items (200-299).
        /// Null for items that are not scored loot.
        /// </summary>
        public static ulong? GetExtractionValue(ItemId id)
        {
            var raw = (ushort)id;
            if (raw < 200 || raw > 299)
            {
                return null;
            }

            return raw switch
            {
                200 => 1,
                201 => 3,
                202 => 8,
                203 => 12,
                204 => 10,
                205 => 9,
                206 => 9,
                207 => 20,
                208 => 2,
                209 => 15,
                210 => 11,
                211 => 4,
                212 => 7,
                213 => 14,
                214 => 0,
                215 => 13,
                216 => 3,
                217 => 8,
                218 => 18,
                219 => 16,
                _ => 0
            };
        }
    }
}
