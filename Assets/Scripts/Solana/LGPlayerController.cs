using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
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
        [Header("Identity Anchor")]
        [SerializeField] private Transform characterNameAnchor;
        [SerializeField] private GameObject playerNamePrefab;
        [Header("Skin Switch Animation")]
        [SerializeField] private bool animateSkinSwitch = true;
        [SerializeField] private float skinPopScaleMultiplier = 1.12f;
        [SerializeField] private float skinPopOutDuration = 0.08f;
        [SerializeField] private float skinPopReturnDuration = 0.12f;

        public PlayerSkinId CurrentSkin { get; private set; } = PlayerSkinId.Goblin;
        public Transform CharacterNameAnchorTransform => characterNameAnchor != null ? characterNameAnchor : transform;
        private Vector3 _skinBaseScale = Vector3.one;
        private Sequence _skinSwitchSequence;
        private GameObject _playerNameInstance;
        private TMP_Text _playerNameText;

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

            if (characterNameAnchor == null)
            {
                var fallbackAnchor = transform.Find("NameAnchor");
                if (fallbackAnchor != null)
                {
                    characterNameAnchor = fallbackAnchor;
                }
            }
        }

        private void OnDestroy()
        {
            if (_skinSwitchSequence != null)
            {
                _skinSwitchSequence.Kill();
                _skinSwitchSequence = null;
            }

            if (_playerNameInstance != null)
            {
                Destroy(_playerNameInstance);
                _playerNameInstance = null;
                _playerNameText = null;
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

        public void SetPlayerNamePrefab(GameObject namePrefab)
        {
            if (namePrefab != null)
            {
                playerNamePrefab = namePrefab;
            }

            EnsurePlayerNameTag();
        }

        public void SetDisplayName(string displayName)
        {
            EnsurePlayerNameTag();
            if (_playerNameText == null)
            {
                return;
            }

            _playerNameText.text = string.IsNullOrWhiteSpace(displayName)
                ? string.Empty
                : displayName;
        }

        public void SetDisplayNameVisible(bool isVisible)
        {
            EnsurePlayerNameTag();
            if (_playerNameInstance == null)
            {
                return;
            }

            _playerNameInstance.SetActive(isVisible);
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

        private void EnsurePlayerNameTag()
        {
            if (_playerNameInstance != null)
            {
                var anchor = CharacterNameAnchorTransform;
                if (_playerNameInstance.transform.parent != anchor)
                {
                    _playerNameInstance.transform.SetParent(anchor, false);
                    _playerNameInstance.transform.localPosition = Vector3.zero;
                    _playerNameInstance.transform.localRotation = Quaternion.identity;
                }

                return;
            }

            if (playerNamePrefab == null)
            {
                return;
            }

            var nameAnchor = CharacterNameAnchorTransform;
            _playerNameInstance = Instantiate(playerNamePrefab, nameAnchor, false);
            _playerNameInstance.name = $"{playerNamePrefab.name}_{gameObject.name}";
            _playerNameInstance.transform.localPosition = Vector3.zero;
            _playerNameInstance.transform.localRotation = Quaternion.identity;
            _playerNameText = _playerNameInstance.GetComponentInChildren<TMP_Text>(true);
        }
    }
}
