using System.Collections.Generic;
using SeekerDungeon.Solana;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    public sealed class DungeonBossVisualController : MonoBehaviour
    {
        [SerializeField] private bool logDebugMessages;

        public void Apply(MonsterView monster, IReadOnlyList<DungeonOccupantVisual> bossOccupants)
        {
            if (!logDebugMessages)
            {
                return;
            }

            var fighterCount = bossOccupants?.Count ?? 0;
            if (monster == null)
            {
                Debug.Log($"[DungeonBossVisual] Monster missing. Fighters={fighterCount}");
                return;
            }

            Debug.Log($"[DungeonBossVisual] Monster={monster.MonsterId} HP={monster.CurrentHp}/{monster.MaxHp} Fighters={fighterCount} Dead={monster.IsDead}");
        }
    }
}
