using System;
using UnityEngine;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Place on a rig object (for example left arm) and call EmitSwingEvent from an Animation Event.
    /// </summary>
    public sealed class DuelSwingEventRelay : MonoBehaviour
    {
        public static event Action<LGPlayerController> OnSwing;

        [SerializeField] private LGPlayerController owner;

        private void Awake()
        {
            if (owner == null)
            {
                owner = GetComponentInParent<LGPlayerController>();
            }
        }

        public void EmitSwingEvent()
        {
            if (owner == null)
            {
                owner = GetComponentInParent<LGPlayerController>();
            }

            if (owner != null)
            {
                OnSwing?.Invoke(owner);
            }
        }
    }
}
