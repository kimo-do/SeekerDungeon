using System;
using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using SeekerDungeon.Audio;
using SeekerDungeon.Dungeon;
using UnityEngine;

namespace SeekerDungeon.Solana
{
    [Serializable]
    public sealed class PlayerSkinSpriteEntry
    {
        [SerializeField] private PlayerSkinId skin = PlayerSkinId.CheekyGoblin;
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
        [Header("Skin Mapping")]
        [SerializeField] private List<CharacterRigBindings> skinRigs = new();
        [SerializeField] private SpriteRenderer skinSpriteRenderer;
        [SerializeField] private List<PlayerSkinSpriteEntry> skinSprites = new();

        [Header("Identity Anchor")]
        [SerializeField] private Transform characterNameAnchor;
        [SerializeField] private GameObject playerNamePrefab;
        [SerializeField] private BossHealthBarView playerHealthBarView;
        [SerializeField] private BossHealthBarView playerHealthBarPrefab;
        [SerializeField] private Transform playerHealthBarAnchor;

        [Header("Skin Switch Animation")]
        [SerializeField] private bool animateSkinSwitch = true;
        [SerializeField] private float skinPopScaleMultiplier = 1.12f;
        [SerializeField] private float skinPopOutDuration = 0.08f;
        [SerializeField] private float skinPopReturnDuration = 0.12f;

        [Header("Wieldable Items")]
        [SerializeField] private List<WieldableItemEntry> wieldableItems = new();
        [SerializeField] private bool logWieldDebugMessages;

        [Header("Job Animation")]
        [SerializeField] private string miningAnimatorParameter = "ismining";
        [SerializeField] private string bossJobAnimatorParameter = "ismining";

        public PlayerSkinId CurrentSkin { get; private set; } = PlayerSkinId.CheekyGoblin;

        public Transform CharacterNameAnchorTransform
        {
            get
            {
                if (_activeRigBindings != null && _activeRigBindings.NameAnchor != null)
                {
                    return _activeRigBindings.NameAnchor;
                }

                return characterNameAnchor != null ? characterNameAnchor : transform;
            }
        }

        private Vector3 _skinBaseScale = Vector3.one;
        private Sequence _skinSwitchSequence;
        private Transform _activeVisualRootForAnimation;
        private CharacterRigBindings _activeRigBindings;
        private GameObject _activeWieldedVisual;
        private bool _isMiningAnimationActive;
        private bool _isBossAnimationActive;

        private GameObject _playerNameInstance;
        private TMP_Text _playerNameText;
        private Vector3 _playerNameBaseLocalScale = Vector3.one;
        private bool _hasPlayerNameBaseScale;
        private BossHealthBarView _spawnedPlayerHealthBarView;

        private void Awake()
        {
            ValidateSkinRigConfiguration();

            if (skinSpriteRenderer == null)
            {
                skinSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            if (skinSpriteRenderer != null)
            {
                _skinBaseScale = skinSpriteRenderer.transform.localScale;
                _activeVisualRootForAnimation = skinSpriteRenderer.transform;
            }

            if (characterNameAnchor == null)
            {
                var fallbackAnchor = transform.Find("NameAnchor");
                if (fallbackAnchor != null)
                {
                    characterNameAnchor = fallbackAnchor;
                }
            }

            ActivateRigForSkin(CurrentSkin);
        }

        private void ValidateSkinRigConfiguration()
        {
            if (skinRigs == null || skinRigs.Count <= 1)
            {
                return;
            }

            var seen = new Dictionary<PlayerSkinId, CharacterRigBindings>();
            for (var index = 0; index < skinRigs.Count; index += 1)
            {
                var rig = skinRigs[index];
                if (rig == null)
                {
                    continue;
                }

                if (!seen.TryAdd(rig.SkinId, rig))
                {
                    Debug.LogWarning(
                        $"[LGPlayerController] Duplicate skin mapping for {rig.SkinId} on '{name}'. " +
                        "Keep one rig per SkinId to avoid label/selection confusion.");
                }
            }
        }

        private void OnDestroy()
        {
            GameAudioManager.Instance?.SetLoop(AudioLoopId.Mining, false, transform.position);
            GameAudioManager.Instance?.SetLoop(AudioLoopId.BossAttack, false, transform.position);

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

            DestroySpawnedPlayerHealthBar();
        }

        private void LateUpdate()
        {
            ApplyNameMirrorCompensation();
            UpdatePlayerHealthBarPosition();
        }

        public IReadOnlyList<PlayerSkinId> GetConfiguredSkins()
        {
            var availableSkins = new List<PlayerSkinId>(skinRigs.Count + skinSprites.Count);

            foreach (var skinRig in skinRigs)
            {
                if (skinRig == null)
                {
                    continue;
                }

                if (availableSkins.Contains(skinRig.SkinId))
                {
                    continue;
                }

                availableSkins.Add(skinRig.SkinId);
            }

            foreach (var skinSpriteEntry in skinSprites)
            {
                if (skinSpriteEntry == null || skinSpriteEntry.Sprite == null)
                {
                    continue;
                }

                if (!availableSkins.Contains(skinSpriteEntry.Skin))
                {
                    availableSkins.Add(skinSpriteEntry.Skin);
                }
            }

            return availableSkins;
        }

        public bool TryGetSkinLabelOverride(PlayerSkinId skin, out string label)
        {
            label = string.Empty;
            if (skinRigs == null || skinRigs.Count == 0)
            {
                return false;
            }

            if (_activeRigBindings != null &&
                _activeRigBindings.SkinId == skin &&
                !string.IsNullOrWhiteSpace(_activeRigBindings.SkinLabelOverride))
            {
                label = _activeRigBindings.SkinLabelOverride.Trim();
                return true;
            }

            var bestMatch = string.Empty;
            for (var index = 0; index < skinRigs.Count; index += 1)
            {
                var rig = skinRigs[index];
                if (rig == null || rig.SkinId != skin)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rig.SkinLabelOverride))
                {
                    continue;
                }

                bestMatch = rig.SkinLabelOverride.Trim();
            }

            if (string.IsNullOrWhiteSpace(bestMatch))
            {
                return false;
            }

            label = bestMatch;
            return true;
        }

        public bool ApplySkin(PlayerSkinId skin)
        {
            if (this == null) return false;
            CurrentSkin = skin;

            var rigApplied = ActivateRigForSkin(skin);
            if (rigApplied)
            {
                EnsurePlayerNameTag();
                AttachActiveWieldedItemToHand();
                ApplyAnimatorJobState();
                PlaySkinSwitchAnimation();
                return true;
            }

            if (skinSpriteRenderer == null)
            {
                return false;
            }

            var mappedSprite = FindSpriteForSkin(skin);
            skinSpriteRenderer.sprite = mappedSprite;
            if (mappedSprite != null)
            {
                _activeVisualRootForAnimation = skinSpriteRenderer.transform;
                _skinBaseScale = _activeVisualRootForAnimation.localScale;
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

        public void SetCombatHealth(ushort currentHp, ushort maxHp, bool isBossFightActive)
        {
            var activeHealthBar = EnsurePlayerHealthBarView();
            if (activeHealthBar == null)
            {
                return;
            }

            if (!isBossFightActive || maxHp == 0)
            {
                activeHealthBar.Bind(0UL, 0UL, 0UL, true, false);
                return;
            }

            var clampedCurrentHp = currentHp > maxHp ? maxHp : currentHp;
            var isDead = clampedCurrentHp == 0;
            activeHealthBar.Bind(clampedCurrentHp, maxHp, 0UL, isDead, true);
            UpdatePlayerHealthBarPosition();
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
                _activeWieldedVisual = toShow;
                AttachActiveWieldedItemToHand();
            }
            else
            {
                _activeWieldedVisual = null;
            }

            if (logWieldDebugMessages && bestMatch == null)
            {
                Debug.LogWarning($"[LGPlayerController] No exact wielded visual match for {itemId}; using fallback.");
            }
        }

        /// <summary>
        /// Hide all wielded item visuals.
        /// </summary>
        public void HideAllWieldedItems()
        {
            _activeWieldedVisual = null;
            foreach (var entry in wieldableItems)
            {
                if (entry?.visual != null)
                {
                    entry.visual.SetActive(false);
                }
            }
        }

        public void SetMiningAnimationState(bool isMining)
        {
            if (_isMiningAnimationActive == isMining)
            {
                return;
            }

            _isMiningAnimationActive = isMining;
            GameAudioManager.Instance?.SetLoop(AudioLoopId.Mining, isMining, transform.position);
            ApplyAnimatorJobState();
        }

        public void SetBossJobAnimationState(bool isBossJobActive)
        {
            if (_isBossAnimationActive == isBossJobActive)
            {
                return;
            }

            _isBossAnimationActive = isBossJobActive;
            GameAudioManager.Instance?.SetLoop(AudioLoopId.BossAttack, isBossJobActive, transform.position);
            ApplyAnimatorJobState();
        }

        private void PlaySkinSwitchAnimation()
        {
            var visualRoot = _activeVisualRootForAnimation;
            if (!animateSkinSwitch || visualRoot == null)
            {
                return;
            }

            var skinTransform = visualRoot;
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

        private bool ActivateRigForSkin(PlayerSkinId skin)
        {
            if (skinRigs == null || skinRigs.Count == 0)
            {
                _activeRigBindings = null;
                return false;
            }

            CharacterRigBindings selectedRig = null;
            CharacterRigBindings firstValidRig = null;
            for (var index = 0; index < skinRigs.Count; index += 1)
            {
                var rig = skinRigs[index];
                if (rig == null)
                {
                    continue;
                }

                firstValidRig ??= rig;
                if (rig.SkinId == skin)
                {
                    selectedRig = rig;
                }
            }

            selectedRig ??= firstValidRig;
            if (selectedRig == null)
            {
                _activeRigBindings = null;
                return false;
            }

            for (var index = 0; index < skinRigs.Count; index += 1)
            {
                var rig = skinRigs[index];
                if (rig == null)
                {
                    continue;
                }

                rig.gameObject.SetActive(rig == selectedRig);
            }

            _activeRigBindings = selectedRig;
            _activeVisualRootForAnimation = selectedRig.transform;
            _skinBaseScale = _activeVisualRootForAnimation.localScale;
            UpdatePlayerHealthBarPosition();
            return true;
        }

        private BossHealthBarView EnsurePlayerHealthBarView()
        {
            if (_spawnedPlayerHealthBarView != null)
            {
                return _spawnedPlayerHealthBarView;
            }

            if (playerHealthBarPrefab != null)
            {
                _spawnedPlayerHealthBarView = Instantiate(playerHealthBarPrefab);
                _spawnedPlayerHealthBarView.gameObject.name = $"{playerHealthBarPrefab.name}_{gameObject.name}_Runtime";
                UpdatePlayerHealthBarPosition();
                return _spawnedPlayerHealthBarView;
            }

            if (playerHealthBarView != null)
            {
                return playerHealthBarView;
            }

            playerHealthBarView = GetComponentInChildren<BossHealthBarView>(true);
            return playerHealthBarView;
        }

        private void UpdatePlayerHealthBarPosition()
        {
            var activeHealthBar = _spawnedPlayerHealthBarView != null ? _spawnedPlayerHealthBarView : playerHealthBarView;
            if (activeHealthBar == null)
            {
                return;
            }

            var anchor = playerHealthBarAnchor;
            if (anchor == null)
            {
                anchor = CharacterNameAnchorTransform;
            }

            if (anchor == null)
            {
                anchor = transform;
            }

            activeHealthBar.transform.position = anchor.position;
        }

        private void DestroySpawnedPlayerHealthBar()
        {
            if (_spawnedPlayerHealthBarView == null)
            {
                return;
            }

            Destroy(_spawnedPlayerHealthBarView.gameObject);
            _spawnedPlayerHealthBarView = null;
        }

        private void AttachActiveWieldedItemToHand()
        {
            if (_activeWieldedVisual == null)
            {
                return;
            }

            var hand = ResolveActiveWeaponHand();
            if (hand == null)
            {
                return;
            }

            var visualTransform = _activeWieldedVisual.transform;
            if (visualTransform.parent != hand)
            {
                visualTransform.SetParent(hand, false);
            }

            visualTransform.localPosition = Vector3.zero;
            visualTransform.localRotation = Quaternion.identity;
        }

        private Transform ResolveActiveWeaponHand()
        {
            if (_activeRigBindings != null && _activeRigBindings.PreferredWeaponHand != null)
            {
                return _activeRigBindings.PreferredWeaponHand;
            }

            return null;
        }

        private void ApplyAnimatorJobState()
        {
            var animators = ResolveActiveAnimators();
            if (animators == null || animators.Count == 0)
            {
                return;
            }

            var hasMiningParam = !string.IsNullOrWhiteSpace(miningAnimatorParameter);
            var hasBossParam = !string.IsNullOrWhiteSpace(bossJobAnimatorParameter);
            if (!hasMiningParam && !hasBossParam)
            {
                return;
            }

            var useSharedParameter = hasMiningParam &&
                                     hasBossParam &&
                                     string.Equals(miningAnimatorParameter, bossJobAnimatorParameter, StringComparison.OrdinalIgnoreCase);
            var mergedValue = _isMiningAnimationActive || _isBossAnimationActive;

            for (var index = 0; index < animators.Count; index += 1)
            {
                var animator = animators[index];
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    continue;
                }

                if (useSharedParameter)
                {
                    SetAnimatorBoolIfExists(animator, miningAnimatorParameter, mergedValue);
                    continue;
                }

                if (hasMiningParam)
                {
                    SetAnimatorBoolIfExists(animator, miningAnimatorParameter, _isMiningAnimationActive);
                }

                if (hasBossParam)
                {
                    SetAnimatorBoolIfExists(animator, bossJobAnimatorParameter, _isBossAnimationActive);
                }
            }
        }

        private IReadOnlyList<Animator> ResolveActiveAnimators()
        {
            if (_activeRigBindings != null)
            {
                return _activeRigBindings.ResolveAnimators();
            }

            return GetComponentsInChildren<Animator>(true);
        }

        private static void SetAnimatorBoolIfExists(Animator animator, string parameterName, bool value)
        {
            if (animator == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            var parameters = animator.parameters;
            if (parameters == null || parameters.Length == 0)
            {
                return;
            }

            for (var index = 0; index < parameters.Length; index += 1)
            {
                var parameter = parameters[index];
                if (parameter.type != AnimatorControllerParameterType.Bool)
                {
                    continue;
                }

                if (!string.Equals(parameter.name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                animator.SetBool(parameter.name, value);
                return;
            }
        }
    }
}
