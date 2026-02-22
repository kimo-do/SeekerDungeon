using System;
using System.Collections.Generic;
using SeekerDungeon.Dungeon;
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
        [Header("Presence Visuals")]
        [SerializeField] private List<SpriteRenderer> presenceSpriteRenderers = new();
        [SerializeField] private Material activePresenceMaterial;
        [SerializeField] private Material idlePresenceMaterial;
        [SerializeField] private Material afkPresenceMaterial;

        private List<Material> _defaultPresenceMaterials;

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

        public void ApplyPresenceState(OccupantPresenceState presenceState)
        {
            var renderers = ResolvePresenceSpriteRenderers();
            if (renderers == null || renderers.Count == 0)
            {
                return;
            }

            EnsureDefaultPresenceMaterials(renderers);
            var selectedMaterial = ResolvePresenceMaterial(presenceState);
            for (var index = 0; index < renderers.Count; index += 1)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                if (selectedMaterial != null)
                {
                    renderer.sharedMaterial = selectedMaterial;
                    continue;
                }

                if (_defaultPresenceMaterials != null && index < _defaultPresenceMaterials.Count)
                {
                    renderer.sharedMaterial = _defaultPresenceMaterials[index];
                }
            }
        }

        private List<SpriteRenderer> ResolvePresenceSpriteRenderers()
        {
            if (presenceSpriteRenderers != null && presenceSpriteRenderers.Count > 0)
            {
                return presenceSpriteRenderers;
            }

            var root = bodyRoot != null ? bodyRoot : transform;
            var discovered = root.GetComponentsInChildren<SpriteRenderer>(true);
            if (discovered == null || discovered.Length == 0)
            {
                return null;
            }

            presenceSpriteRenderers = new List<SpriteRenderer>(discovered);
            return presenceSpriteRenderers;
        }

        private void EnsureDefaultPresenceMaterials(IReadOnlyList<SpriteRenderer> renderers)
        {
            if (_defaultPresenceMaterials != null && _defaultPresenceMaterials.Count == renderers.Count)
            {
                return;
            }

            _defaultPresenceMaterials = new List<Material>(renderers.Count);
            for (var index = 0; index < renderers.Count; index += 1)
            {
                var renderer = renderers[index];
                _defaultPresenceMaterials.Add(renderer != null ? renderer.sharedMaterial : null);
            }
        }

        private Material ResolvePresenceMaterial(OccupantPresenceState presenceState)
        {
            return presenceState switch
            {
                OccupantPresenceState.Active => activePresenceMaterial,
                OccupantPresenceState.Idle => idlePresenceMaterial != null ? idlePresenceMaterial : activePresenceMaterial,
                OccupantPresenceState.Afk => afkPresenceMaterial != null
                    ? afkPresenceMaterial
                    : (idlePresenceMaterial != null ? idlePresenceMaterial : activePresenceMaterial),
                _ => activePresenceMaterial
            };
        }
    }
}
