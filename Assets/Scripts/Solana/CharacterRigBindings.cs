using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Optional helper for per-skin rig references (arms/hands/animators).
    /// Attach to each character rig root used by LGPlayerController skin mapping.
    /// </summary>
    public sealed class CharacterRigBindings : MonoBehaviour
    {
        [Header("Skin Mapping")]
        [SerializeField] private PlayerSkinId skinId = PlayerSkinId.CheekyGoblin;
        [SerializeField] private string skinLabelOverride = string.Empty;

        [Header("Rig Parts")]
        [SerializeField] private Transform bodyRoot;
        [SerializeField] private Transform leftArm;
        [SerializeField] private Transform rightArm;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private Transform nameAnchor;
        [Header("Animation")]
        [SerializeField] private List<Animator> animators = new();

        public PlayerSkinId SkinId => skinId;
        public string SkinLabelOverride => skinLabelOverride;
        public Transform BodyRoot => bodyRoot != null ? bodyRoot : transform;
        public Transform LeftArm => leftArm;
        public Transform RightArm => rightArm;
        public Transform LeftHand => leftHand;
        public Transform RightHand => rightHand;
        public Transform NameAnchor => nameAnchor;
        public Transform PreferredWeaponHand => leftHand != null ? leftHand : rightHand;

        public IReadOnlyList<Animator> ResolveAnimators()
        {
            if (animators != null && animators.Count > 0)
            {
                return animators;
            }

            var discovered = GetComponentsInChildren<Animator>(true);
            if (discovered == null || discovered.Length == 0)
            {
                return Array.Empty<Animator>();
            }

            animators = new List<Animator>(discovered);
            return animators;
        }
    }
}
