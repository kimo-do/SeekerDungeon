using System.Collections.Generic;
using DG.Tweening;
using SeekerDungeon.Solana;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    [System.Serializable]
    public sealed class BossVariantBinding
    {
        [SerializeField] private ushort monsterId = 1;
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private Transform deathPopTarget;
        [SerializeField] private DoorOccupantLayer2D standLayer;
        [SerializeField] private Transform healthBarAnchor;

        public ushort MonsterId => monsterId;
        public GameObject VisualRoot => visualRoot;
        public Transform DeathPopTarget => deathPopTarget;
        public DoorOccupantLayer2D StandLayer => standLayer;
        public Transform HealthBarAnchor => healthBarAnchor;
    }

    public sealed class DungeonBossVisualController : MonoBehaviour
    {
        [SerializeField] private GameObject defaultVisualRoot;
        [SerializeField] private List<BossVariantBinding> variantBindings = new();
        [SerializeField] private BossHealthBarView healthBarView;
        [SerializeField] private BossHealthBarView healthBarPrefab;
        [SerializeField] private Transform healthBarSpawnAnchor;
        [SerializeField] private float deathPopScaleMultiplier = 1.12f;
        [SerializeField] private float deathPopDuration = 0.16f;
        [SerializeField] private string deathAnimatorParameter = "dead";
        [SerializeField] private string deadAnimationStateName = "dead";
        [SerializeField] private string aliveAnimationStateName = "idle";
        [SerializeField] private string[] deadAnimationStateFallbackNames = { "Dead", "death", "Death", "is_dead", "IsDead", "deadchest", "DeadChest" };
        [SerializeField] private string[] aliveAnimationStateFallbackNames = { "Idle", "idle", "idlechest", "IdleChest" };
        [SerializeField] private string[] deathAnimatorParameterFallbackNames = { "Dead", "isDead", "IsDead", "death", "Death" };
        [SerializeField] private bool completeDeadAnimationInstantlyOnFirstApply = true;
        [SerializeField] private bool forceDeadAnimationStateWheneverDead = true;
        [SerializeField] private bool forceAliveAnimationStateWheneverAlive = true;
        [SerializeField] private Material deadMaterialOverride;
        [SerializeField] private bool logDebugMessages;
        private bool _warnedMissingHealthBar;
        private BossHealthBarView _spawnedHealthBarView;
        private bool _hadMonsterState;
        private bool _wasDeadLastApply;
        private ushort _lastMonsterId;
        private ushort _triggeredDeadMonsterId;
        private bool _hasTriggeredDeadMonsterId;
        private Tween _deathPopTween;
        private BossVariantBinding _activeVariantBinding;
        private Transform _occupantVisualSpawnRoot;
        private bool _warnedMissingVariantForMonster;
        private readonly Dictionary<Renderer, Material[]> _baseSharedMaterialsByRenderer = new();
        private readonly List<RubbleJobVfxController> _bossJobVfxControllers = new();

        public void Apply(MonsterView monster, IReadOnlyList<DungeonOccupantVisual> bossOccupants)
        {
            var hadMonsterStateBeforeApply = _hadMonsterState;
            var isDead = monster != null && monster.IsDead;
            var didTransitionToDead = DidTransitionToDead(monster, isDead);
            var isDeadOnFirstApply = monster != null && isDead && !hadMonsterStateBeforeApply;
            var activeRoot = ApplyVariant(monster);
            UpdateBossJobVfx(activeRoot, monster, bossOccupants);
            ApplyDeadMaterialState(activeRoot, isDead);
            ApplyDeathAnimationState(activeRoot, isDead, didTransitionToDead, isDeadOnFirstApply);
            if (didTransitionToDead)
            {
                PlayDeathPop(monster, activeRoot);
            }

            ResolveHealthBarView(activeRoot, monster);
            ApplyHealthBar(monster, GetActiveHealthBarView());

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

        private void OnDisable()
        {
            KillDeathPopTween();
            DestroySpawnedHealthBar();
            _hadMonsterState = false;
            _wasDeadLastApply = false;
            _lastMonsterId = 0;
            _hasTriggeredDeadMonsterId = false;
            _triggeredDeadMonsterId = 0;
            _warnedMissingVariantForMonster = false;
            _baseSharedMaterialsByRenderer.Clear();
            SetAllBossJobVfxActive(false);
        }

        private GameObject ApplyVariant(MonsterView monster)
        {
            var targetRoot = defaultVisualRoot;
            _activeVariantBinding = null;
            BossVariantBinding firstValidBinding = null;

            if (monster != null && variantBindings != null)
            {
                for (var index = 0; index < variantBindings.Count; index += 1)
                {
                    var binding = variantBindings[index];
                    if (binding == null || binding.VisualRoot == null)
                    {
                        continue;
                    }

                    if (firstValidBinding == null)
                    {
                        firstValidBinding = binding;
                    }
                    if (binding.MonsterId == monster.MonsterId)
                    {
                        if (binding.VisualRoot != null)
                        {
                            targetRoot = binding.VisualRoot;
                        }

                        _activeVariantBinding = binding;

                        break;
                    }
                }
            }

            if (targetRoot == null && firstValidBinding != null)
            {
                targetRoot = firstValidBinding.VisualRoot;
                _activeVariantBinding = firstValidBinding;
                if (!_warnedMissingVariantForMonster && monster != null)
                {
                    _warnedMissingVariantForMonster = true;
                    Debug.LogWarning(
                        $"[DungeonBossVisual] No variant binding for MonsterId={monster.MonsterId}. " +
                        $"Falling back to first configured visual root '{targetRoot.name}'.");
                }
            }

            if (targetRoot == null)
            {
                targetRoot = gameObject;
                if (!_warnedMissingVariantForMonster)
                {
                    _warnedMissingVariantForMonster = true;
                    Debug.LogWarning(
                        "[DungeonBossVisual] No default/variant visual root configured. " +
                        "Falling back to controller GameObject as visual root.");
                }
            }

            if (variantBindings != null)
            {
                for (var index = 0; index < variantBindings.Count; index += 1)
                {
                    var binding = variantBindings[index];
                    if (binding?.VisualRoot == null)
                    {
                        continue;
                    }

                    binding.VisualRoot.SetActive(binding.VisualRoot == targetRoot);
                }
            }

            if (defaultVisualRoot != null)
            {
                defaultVisualRoot.SetActive(targetRoot == defaultVisualRoot);
            }

            if (_activeVariantBinding?.StandLayer != null && _occupantVisualSpawnRoot != null)
            {
                _activeVariantBinding.StandLayer.SetVisualSpawnRoot(_occupantVisualSpawnRoot);
            }

            return targetRoot;
        }

        private void ApplyDeathAnimationState(
            GameObject activeRoot,
            bool isDead,
            bool didTransitionToDead,
            bool isDeadOnFirstApply)
        {
            if (activeRoot == null)
            {
                return;
            }

            var animators = activeRoot.GetComponentsInChildren<Animator>(true);
            var appliedDeadVisualToAnyAnimator = false;
            for (var index = 0; index < animators.Length; index += 1)
            {
                var animator = animators[index];
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    continue;
                }

                var boolParameter = ResolveAnimatorParameterName(
                    animator,
                    deathAnimatorParameter,
                    deathAnimatorParameterFallbackNames,
                    AnimatorControllerParameterType.Bool);
                if (!string.IsNullOrWhiteSpace(boolParameter))
                {
                    animator.SetBool(boolParameter, isDead);
                    if (isDead)
                    {
                        appliedDeadVisualToAnyAnimator = true;
                    }
                }

                if (isDeadOnFirstApply &&
                    completeDeadAnimationInstantlyOnFirstApply &&
                    !string.IsNullOrWhiteSpace(deadAnimationStateName))
                {
                    var forced = ForceAnimatorStateComplete(animator, deadAnimationStateName, deadAnimationStateFallbackNames);
                    appliedDeadVisualToAnyAnimator |= forced;
                    if (logDebugMessages)
                    {
                        Debug.Log(
                            $"[DungeonBossVisual] First-apply dead state force on '{animator.name}' " +
                            $"state='{deadAnimationStateName}' applied={forced}");
                    }
                }

                if (isDead &&
                    forceDeadAnimationStateWheneverDead &&
                    !string.IsNullOrWhiteSpace(deadAnimationStateName))
                {
                    var forced = ForceAnimatorStateComplete(animator, deadAnimationStateName, deadAnimationStateFallbackNames);
                    appliedDeadVisualToAnyAnimator |= forced;
                    if (logDebugMessages)
                    {
                        Debug.Log(
                            $"[DungeonBossVisual] Persistent dead state force on '{animator.name}' " +
                            $"state='{deadAnimationStateName}' applied={forced}");
                    }
                }
                else if (!isDead &&
                         forceAliveAnimationStateWheneverAlive &&
                         !string.IsNullOrWhiteSpace(aliveAnimationStateName))
                {
                    var forcedAlive = ForceAnimatorStateStart(animator, aliveAnimationStateName, aliveAnimationStateFallbackNames);
                    if (logDebugMessages)
                    {
                        Debug.Log(
                            $"[DungeonBossVisual] Persistent alive state force on '{animator.name}' " +
                            $"state='{aliveAnimationStateName}' applied={forcedAlive}");
                    }
                }

                var shouldTriggerDead = isDead &&
                                        (!_hasTriggeredDeadMonsterId ||
                                         _triggeredDeadMonsterId != _lastMonsterId ||
                                         didTransitionToDead ||
                                         isDeadOnFirstApply);
                var triggerParameter = ResolveAnimatorParameterName(
                    animator,
                    deathAnimatorParameter,
                    deathAnimatorParameterFallbackNames,
                    AnimatorControllerParameterType.Trigger);
                if (shouldTriggerDead &&
                    !string.IsNullOrWhiteSpace(triggerParameter))
                {
                    animator.SetTrigger(triggerParameter);
                    appliedDeadVisualToAnyAnimator = true;
                    _hasTriggeredDeadMonsterId = true;
                    _triggeredDeadMonsterId = _lastMonsterId;
                    if (logDebugMessages)
                    {
                        Debug.Log(
                            $"[DungeonBossVisual] Triggered death parameter '{triggerParameter}' " +
                            $"on animator '{animator.name}'.");
                    }
                }
                else if (isDead && logDebugMessages)
                {
                    var hasBool =
                        !string.IsNullOrWhiteSpace(deathAnimatorParameter) &&
                        HasAnimatorParameter(animator, deathAnimatorParameter, AnimatorControllerParameterType.Bool);
                    var hasTrigger =
                        !string.IsNullOrWhiteSpace(deathAnimatorParameter) &&
                        HasAnimatorParameter(animator, deathAnimatorParameter, AnimatorControllerParameterType.Trigger);
                    Debug.Log(
                        $"[DungeonBossVisual] Dead apply diagnostics animator='{animator.name}' " +
                        $"hasBool={hasBool} hasTrigger={hasTrigger} state='{deadAnimationStateName}'");
                }
            }

            if (isDead && !appliedDeadVisualToAnyAnimator)
            {
                Debug.LogWarning(
                    $"[DungeonBossVisual] Monster is dead but no dead visual was applied on root '{activeRoot.name}'. " +
                    $"Check Animator param/state names (param='{deathAnimatorParameter}', state='{deadAnimationStateName}').");
            }
        }

        private void ApplyDeadMaterialState(GameObject activeRoot, bool isDead)
        {
            if (activeRoot == null)
            {
                return;
            }

            var renderers = activeRoot.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index += 1)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                if (!_baseSharedMaterialsByRenderer.ContainsKey(renderer))
                {
                    _baseSharedMaterialsByRenderer[renderer] = renderer.sharedMaterials;
                }

                if (isDead && deadMaterialOverride != null)
                {
                    var originalMaterials = _baseSharedMaterialsByRenderer[renderer];
                    var deadMaterials = new Material[originalMaterials.Length];
                    for (var matIndex = 0; matIndex < deadMaterials.Length; matIndex += 1)
                    {
                        deadMaterials[matIndex] = deadMaterialOverride;
                    }

                    renderer.sharedMaterials = deadMaterials;
                    continue;
                }

                if (_baseSharedMaterialsByRenderer.TryGetValue(renderer, out var baseMaterials))
                {
                    renderer.sharedMaterials = baseMaterials;
                }
            }
        }

        private static bool ForceAnimatorStateComplete(Animator animator, string stateName, string[] fallbackNames)
        {
            if (animator == null || string.IsNullOrWhiteSpace(stateName))
            {
                return false;
            }

            if (TryPlayStateComplete(animator, stateName))
            {
                animator.Update(0f);
                return true;
            }

            if (fallbackNames == null || fallbackNames.Length == 0)
            {
                return false;
            }

            for (var index = 0; index < fallbackNames.Length; index += 1)
            {
                var fallback = fallbackNames[index];
                if (string.IsNullOrWhiteSpace(fallback))
                {
                    continue;
                }

                if (TryPlayStateComplete(animator, fallback))
                {
                    animator.Update(0f);
                    return true;
                }
            }

            return false;
        }

        private static bool ForceAnimatorStateStart(Animator animator, string stateName, string[] fallbackNames)
        {
            if (animator == null || string.IsNullOrWhiteSpace(stateName))
            {
                return false;
            }

            if (TryPlayStateStart(animator, stateName))
            {
                animator.Update(0f);
                return true;
            }

            if (fallbackNames == null || fallbackNames.Length == 0)
            {
                return false;
            }

            for (var index = 0; index < fallbackNames.Length; index += 1)
            {
                var fallback = fallbackNames[index];
                if (string.IsNullOrWhiteSpace(fallback))
                {
                    continue;
                }

                if (TryPlayStateStart(animator, fallback))
                {
                    animator.Update(0f);
                    return true;
                }
            }

            return false;
        }

        private static bool TryPlayStateComplete(Animator animator, string stateName)
        {
            var stateHash = Animator.StringToHash(stateName);
            var layerCount = Mathf.Max(1, animator.layerCount);
            var applied = false;
            for (var layer = 0; layer < layerCount; layer += 1)
            {
                if (!animator.HasState(layer, stateHash))
                {
                    continue;
                }

                animator.Play(stateHash, layer, 1f);
                applied = true;
            }

            return applied;
        }

        private static bool TryPlayStateStart(Animator animator, string stateName)
        {
            var stateHash = Animator.StringToHash(stateName);
            var layerCount = Mathf.Max(1, animator.layerCount);
            var applied = false;
            for (var layer = 0; layer < layerCount; layer += 1)
            {
                if (!animator.HasState(layer, stateHash))
                {
                    continue;
                }

                animator.Play(stateHash, layer, 0f);
                applied = true;
            }

            return applied;
        }

        private static bool HasAnimatorParameter(
            Animator animator,
            string parameterName,
            AnimatorControllerParameterType parameterType)
        {
            if (animator == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            var parameters = animator.parameters;
            for (var index = 0; index < parameters.Length; index += 1)
            {
                var parameter = parameters[index];
                if (parameter.type == parameterType &&
                    string.Equals(parameter.name, parameterName, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveAnimatorParameterName(
            Animator animator,
            string preferredName,
            string[] fallbackNames,
            AnimatorControllerParameterType parameterType)
        {
            if (!string.IsNullOrWhiteSpace(preferredName) &&
                HasAnimatorParameter(animator, preferredName, parameterType))
            {
                return preferredName;
            }

            if (fallbackNames == null)
            {
                return null;
            }

            for (var index = 0; index < fallbackNames.Length; index += 1)
            {
                var candidate = fallbackNames[index];
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (HasAnimatorParameter(animator, candidate, parameterType))
                {
                    return candidate;
                }
            }

            return null;
        }

        private bool DidTransitionToDead(MonsterView monster, bool isDead)
        {
            if (monster == null)
            {
                _hadMonsterState = false;
                _wasDeadLastApply = false;
                _lastMonsterId = 0;
                return false;
            }

            var transitioned = _hadMonsterState &&
                               !_wasDeadLastApply &&
                               isDead &&
                               _lastMonsterId == monster.MonsterId;

            _hadMonsterState = true;
            _wasDeadLastApply = isDead;
            _lastMonsterId = monster.MonsterId;
            return transitioned;
        }

        public void SetBossOccupantVisualSpawnRoot(Transform visualSpawnRoot)
        {
            _occupantVisualSpawnRoot = visualSpawnRoot;
            if (variantBindings == null)
            {
                return;
            }

            for (var index = 0; index < variantBindings.Count; index += 1)
            {
                var binding = variantBindings[index];
                if (binding?.StandLayer == null)
                {
                    continue;
                }

                binding.StandLayer.SetVisualSpawnRoot(visualSpawnRoot);
            }
        }

        public bool TryGetBossStandPlacement(out Vector3 worldPosition, out OccupantFacingDirection facingDirection)
        {
            worldPosition = default;
            facingDirection = OccupantFacingDirection.Right;
            if (_activeVariantBinding?.StandLayer == null)
            {
                return false;
            }

            return _activeVariantBinding.StandLayer.TryGetLocalPlayerStandPlacement(out worldPosition, out facingDirection);
        }

        public bool TryGetBossStandPosition(out Vector3 worldPosition)
        {
            worldPosition = default;
            if (_activeVariantBinding?.StandLayer == null)
            {
                return false;
            }

            return _activeVariantBinding.StandLayer.TryGetLocalPlayerStandPosition(out worldPosition);
        }

        public void SetBossOccupants(IReadOnlyList<DungeonOccupantVisual> occupants)
        {
            if (_activeVariantBinding?.StandLayer == null)
            {
                return;
            }

            _activeVariantBinding.StandLayer.SetOccupants(occupants ?? System.Array.Empty<DungeonOccupantVisual>());
        }

        private void PlayDeathPop(MonsterView monster, GameObject activeRoot)
        {
            if (activeRoot == null)
            {
                return;
            }

            var popTarget = ResolveDeathPopTarget(monster, activeRoot);
            if (popTarget == null)
            {
                return;
            }

            KillDeathPopTween();

            var baseScale = popTarget.localScale;
            popTarget.localScale = baseScale * Mathf.Max(1f, deathPopScaleMultiplier);
            _deathPopTween = popTarget
                .DOScale(baseScale, Mathf.Max(0.01f, deathPopDuration))
                .SetEase(Ease.OutBack)
                .SetUpdate(true);
        }

        private Transform ResolveDeathPopTarget(MonsterView monster, GameObject activeRoot)
        {
            if (monster != null && variantBindings != null)
            {
                for (var index = 0; index < variantBindings.Count; index += 1)
                {
                    var binding = variantBindings[index];
                    if (binding == null || binding.MonsterId != monster.MonsterId)
                    {
                        continue;
                    }

                    if (binding.DeathPopTarget != null)
                    {
                        return binding.DeathPopTarget;
                    }

                    break;
                }
            }

            return activeRoot.transform;
        }

        private void ResolveHealthBarView(GameObject activeRoot, MonsterView monster)
        {
            if (monster == null)
            {
                DestroySpawnedHealthBar();
                return;
            }

            if (_spawnedHealthBarView != null)
            {
                return;
            }

            if (healthBarPrefab != null)
            {
                _spawnedHealthBarView = Instantiate(healthBarPrefab);
                _spawnedHealthBarView.gameObject.name = $"{healthBarPrefab.name}_Runtime";
                PlaceSpawnedHealthBar(activeRoot, _spawnedHealthBarView.transform);
                return;
            }

            if (healthBarView != null)
            {
                return;
            }

            if (activeRoot != null)
            {
                healthBarView = activeRoot.GetComponentInChildren<BossHealthBarView>(true);
            }

            if (healthBarView == null)
            {
                healthBarView = GetComponentInChildren<BossHealthBarView>(true);
            }
        }

        private BossHealthBarView GetActiveHealthBarView()
        {
            return _spawnedHealthBarView != null ? _spawnedHealthBarView : healthBarView;
        }

        private void PlaceSpawnedHealthBar(GameObject activeRoot, Transform spawnedTransform)
        {
            if (spawnedTransform == null)
            {
                return;
            }

            var anchor = healthBarSpawnAnchor;
            if (_activeVariantBinding?.HealthBarAnchor != null)
            {
                anchor = _activeVariantBinding.HealthBarAnchor;
            }

            if (anchor == null && activeRoot != null)
            {
                anchor = activeRoot.transform;
            }

            if (anchor != null)
            {
                // Spawn unparented and only assume anchor position.
                spawnedTransform.position = anchor.position;
            }
        }

        private void ApplyHealthBar(MonsterView monster, BossHealthBarView activeHealthBarView)
        {
            if (activeHealthBarView == null)
            {
                if (monster != null && !_warnedMissingHealthBar)
                {
                    _warnedMissingHealthBar = true;
                    Debug.LogWarning("[DungeonBossVisual] No BossHealthBarView found on active boss visual root.");
                }

                return;
            }

            if (monster == null)
            {
                activeHealthBarView.Bind(0UL, 0UL, 0UL, true, false);
                return;
            }

            var hasHp = monster.MaxHp > 0UL;
            activeHealthBarView.Bind(
                monster.CurrentHp,
                monster.MaxHp,
                monster.TotalDps,
                monster.IsDead,
                hasHp && !monster.IsDead);
        }

        private void DestroySpawnedHealthBar()
        {
            if (_spawnedHealthBarView == null)
            {
                return;
            }

            Destroy(_spawnedHealthBarView.gameObject);
            _spawnedHealthBarView = null;
        }

        private void KillDeathPopTween()
        {
            if (_deathPopTween == null)
            {
                return;
            }

            _deathPopTween.Kill();
            _deathPopTween = null;
        }

        private void UpdateBossJobVfx(
            GameObject activeRoot,
            MonsterView monster,
            IReadOnlyList<DungeonOccupantVisual> bossOccupants)
        {
            CacheBossJobVfxControllers();

            var fighterCount = monster != null
                ? Mathf.Max((int)monster.FighterCount, bossOccupants?.Count ?? 0)
                : 0;
            var isBossJobActive = monster != null && !monster.IsDead && fighterCount > 0;

            for (var index = 0; index < _bossJobVfxControllers.Count; index += 1)
            {
                var controller = _bossJobVfxControllers[index];
                if (controller == null)
                {
                    continue;
                }

                var isUnderActiveRoot = activeRoot != null &&
                                        (controller.gameObject == activeRoot ||
                                         controller.transform.IsChildOf(activeRoot.transform));
                controller.SetJobActive(isUnderActiveRoot && isBossJobActive);
            }
        }

        private void CacheBossJobVfxControllers()
        {
            _bossJobVfxControllers.Clear();
            var controllers = GetComponentsInChildren<RubbleJobVfxController>(true);
            if (controllers == null || controllers.Length == 0)
            {
                return;
            }

            for (var index = 0; index < controllers.Length; index += 1)
            {
                var controller = controllers[index];
                if (controller == null)
                {
                    continue;
                }

                _bossJobVfxControllers.Add(controller);
            }
        }

        private void SetAllBossJobVfxActive(bool active)
        {
            CacheBossJobVfxControllers();
            for (var index = 0; index < _bossJobVfxControllers.Count; index += 1)
            {
                var controller = _bossJobVfxControllers[index];
                if (controller == null)
                {
                    continue;
                }

                controller.SetJobActive(active);
            }
        }
    }
}
