using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace SeekerDungeon.Solana
{
    [Serializable]
    public sealed class PlayerSkinSpriteEntry
    {
        [SerializeField] private PlayerSkinId skin = PlayerSkinId.Goblin;
        [SerializeField] private Sprite sprite;

        public PlayerSkinId Skin => skin;
        public Sprite Sprite => sprite;
    }

    public sealed class LGPlayerController : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer skinSpriteRenderer;
        [SerializeField] private List<PlayerSkinSpriteEntry> skinSprites = new();
        [Header("Skin Switch Animation")]
        [SerializeField] private bool animateSkinSwitch = true;
        [SerializeField] private float skinPopScaleMultiplier = 1.12f;
        [SerializeField] private float skinPopOutDuration = 0.08f;
        [SerializeField] private float skinPopReturnDuration = 0.12f;

        public PlayerSkinId CurrentSkin { get; private set; } = PlayerSkinId.Goblin;
        private Vector3 _skinBaseScale = Vector3.one;
        private Sequence _skinSwitchSequence;

        private void Awake()
        {
            if (skinSpriteRenderer == null)
            {
                skinSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            if (skinSpriteRenderer != null)
            {
                _skinBaseScale = skinSpriteRenderer.transform.localScale;
            }
        }

        private void OnDestroy()
        {
            if (_skinSwitchSequence != null)
            {
                _skinSwitchSequence.Kill();
                _skinSwitchSequence = null;
            }
        }

        public IReadOnlyList<PlayerSkinId> GetConfiguredSkins()
        {
            var availableSkins = new List<PlayerSkinId>(skinSprites.Count);

            foreach (var skinSpriteEntry in skinSprites)
            {
                if (skinSpriteEntry == null)
                {
                    continue;
                }

                if (skinSpriteEntry.Sprite == null)
                {
                    continue;
                }

                if (availableSkins.Contains(skinSpriteEntry.Skin))
                {
                    continue;
                }

                availableSkins.Add(skinSpriteEntry.Skin);
            }

            return availableSkins;
        }

        public bool ApplySkin(PlayerSkinId skin)
        {
            CurrentSkin = skin;

            if (skinSpriteRenderer == null)
            {
                return false;
            }

            var mappedSprite = FindSpriteForSkin(skin);
            skinSpriteRenderer.sprite = mappedSprite;
            if (mappedSprite != null)
            {
                PlaySkinSwitchAnimation();
            }

            return mappedSprite != null;
        }

        private void PlaySkinSwitchAnimation()
        {
            if (!animateSkinSwitch || skinSpriteRenderer == null)
            {
                return;
            }

            var skinTransform = skinSpriteRenderer.transform;
            _skinSwitchSequence?.Kill();
            skinTransform.localScale = _skinBaseScale;

            _skinSwitchSequence = DOTween.Sequence()
                .Append(skinTransform
                    .DOScale(_skinBaseScale * skinPopScaleMultiplier, skinPopOutDuration)
                    .SetEase(Ease.OutQuad))
                .Append(skinTransform
                    .DOScale(_skinBaseScale, skinPopReturnDuration)
                    .SetEase(Ease.OutBack))
                .SetUpdate(true);
        }

        private Sprite FindSpriteForSkin(PlayerSkinId skin)
        {
            foreach (var skinSpriteEntry in skinSprites)
            {
                if (skinSpriteEntry == null)
                {
                    continue;
                }

                if (skinSpriteEntry.Skin != skin)
                {
                    continue;
                }

                return skinSpriteEntry.Sprite;
            }

            return null;
        }
    }
}
