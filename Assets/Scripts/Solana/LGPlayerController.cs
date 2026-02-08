using System;
using System.Collections.Generic;
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

        public PlayerSkinId CurrentSkin { get; private set; } = PlayerSkinId.Goblin;

        private void Awake()
        {
            if (skinSpriteRenderer == null)
            {
                skinSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
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
            return mappedSprite != null;
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
