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

    [Serializable]
    public sealed class WieldableItemEntry
    {
        public ItemId itemId;
        public GameObject visual;
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
        [Header("Wieldable Items")]
        [SerializeField] private List<WieldableItemEntry> wieldableItems = new();

        public PlayerSkinId CurrentSkin { get; private set; } = PlayerSkinId.Goblin;
        public Transform CharacterNameAnchorTransform => characterNameAnchor != null ? characterNameAnchor : transform;
        private Vector3 _skinBaseScale = Vector3.one;
        private Sequence _skinSwitchSequence;
        private GameObject _playerNameInstance;
        private TMP_Text _playerNameText;
        private Vector3 _playerNameBaseLocalScale = Vector3.one;
        private bool _hasPlayerNameBaseScale;

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

        private void LateUpdate()
        {
            ApplyNameMirrorCompensation();
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
            if (this == null) return false;
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
            if (this == null) return;
            if (namePrefab != null)
            {
                playerNamePrefab = namePrefab;
            }

            EnsurePlayerNameTag();
        }

        public void SetDisplayName(string displayName)
        {
            if (this == null) return;
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
            if (this == null) return;
            EnsurePlayerNameTag();
            if (_playerNameInstance == null)
            {
                return;
            }

            _playerNameInstance.SetActive(isVisible);
        }

        /// <summary>
        /// Show the wielded item visual that best matches the player's equipped item.
        /// Disables all other wieldable item visuals. If no match is found for the
        /// given itemId, falls back to the first pickaxe available, then first weapon.
        /// </summary>
        public void ShowWieldedItem(ItemId itemId)
        {
            GameObject bestMatch = null;
            GameObject fallbackPickaxe = null;
            GameObject fallbackAny = null;

            foreach (var entry in wieldableItems)
            {
                if (entry?.visual == null) continue;

                entry.visual.SetActive(false);

                if (entry.itemId == itemId)
                {
                    bestMatch = entry.visual;
                }
                else if (fallbackPickaxe == null &&
                         (entry.itemId == ItemId.BronzePickaxe || entry.itemId == ItemId.IronPickaxe))
                {
                    fallbackPickaxe = entry.visual;
                }
                else if (fallbackAny == null)
                {
                    fallbackAny = entry.visual;
                }
            }

            var toShow = bestMatch ?? fallbackPickaxe ?? fallbackAny;
            if (toShow != null)
            {
                toShow.SetActive(true);
            }
        }

        /// <summary>
        /// Hide all wielded item visuals.
        /// </summary>
        public void HideAllWieldedItems()
        {
            foreach (var entry in wieldableItems)
            {
                if (entry?.visual != null)
                {
                    entry.visual.SetActive(false);
                }
            }
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
            _playerNameBaseLocalScale = _playerNameInstance.transform.localScale;
            _hasPlayerNameBaseScale = true;
            ApplyNameMirrorCompensation();
        }

        private void ApplyNameMirrorCompensation()
        {
            if (_playerNameInstance == null)
            {
                return;
            }

            var nameTransform = _playerNameInstance.transform;
            if (!_hasPlayerNameBaseScale)
            {
                _playerNameBaseLocalScale = nameTransform.localScale;
                _hasPlayerNameBaseScale = true;
            }

            var parent = nameTransform.parent;
            var parentSignX = 1f;
            if (parent != null)
            {
                parentSignX = Mathf.Sign(parent.lossyScale.x);
                if (Mathf.Approximately(parentSignX, 0f))
                {
                    parentSignX = 1f;
                }
            }

            var targetScale = _playerNameBaseLocalScale;
            targetScale.x = Mathf.Abs(_playerNameBaseLocalScale.x) * (parentSignX < 0f ? -1f : 1f);
            nameTransform.localScale = targetScale;
        }
    }
}
