using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    /// <summary>
    /// Drives boss attack trigger playback while boss combat is active.
    /// Attach to boss root or controller and wire from DungeonBossVisualController.
    /// </summary>
    public sealed class BossCombatAnimator : MonoBehaviour
    {
        [SerializeField] private string attackTrigger = "doattack";
        [SerializeField] private float firstAttackDelaySeconds = 0.15f;
        [SerializeField] private float attackIntervalSeconds = 1.25f;
        [SerializeField] private float attackIntervalJitterSeconds = 0.2f;
        [SerializeField] private Animator[] explicitAnimators;
        [SerializeField] private GameObject aliveVisualRoot;
        [SerializeField] private GameObject deadVisualRoot;

        private readonly List<Animator> _resolvedAnimators = new();
        private Transform _animatorRoot;
        private bool _isCombatActive;
        private Coroutine _combatLoopCoroutine;

        public bool UsesAliveDeadVisualRoots => aliveVisualRoot != null || deadVisualRoot != null;

        public void SetAnimatorRoot(Transform animatorRoot)
        {
            if (_animatorRoot == animatorRoot)
            {
                return;
            }

            _animatorRoot = animatorRoot;
            _resolvedAnimators.Clear();
        }

        public void SetIsDead(bool isDead)
        {
            if (!UsesAliveDeadVisualRoots)
            {
                return;
            }

            if (aliveVisualRoot != null)
            {
                aliveVisualRoot.SetActive(!isDead);
            }

            if (deadVisualRoot != null)
            {
                deadVisualRoot.SetActive(isDead);
            }
        }

        public void SetCombatActive(bool isActive)
        {
            if (_isCombatActive == isActive)
            {
                return;
            }

            _isCombatActive = isActive;
            if (!_isCombatActive)
            {
                StopCombatLoop();
                return;
            }

            StartCombatLoop();
        }

        public void TriggerAttackOnce()
        {
            if (string.IsNullOrWhiteSpace(attackTrigger))
            {
                return;
            }

            var animators = ResolveAnimators();
            for (var index = 0; index < animators.Count; index += 1)
            {
                var animator = animators[index];
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    continue;
                }

                SetAnimatorTriggerIfExists(animator, attackTrigger);
            }
        }

        private void OnDisable()
        {
            _isCombatActive = false;
            StopCombatLoop();
        }

        private void StartCombatLoop()
        {
            StopCombatLoop();
            _combatLoopCoroutine = StartCoroutine(CombatLoop());
        }

        private void StopCombatLoop()
        {
            if (_combatLoopCoroutine == null)
            {
                return;
            }

            StopCoroutine(_combatLoopCoroutine);
            _combatLoopCoroutine = null;
        }

        private IEnumerator CombatLoop()
        {
            if (firstAttackDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(firstAttackDelaySeconds);
            }

            while (_isCombatActive && isActiveAndEnabled)
            {
                TriggerAttackOnce();

                var interval = Mathf.Max(0.1f, attackIntervalSeconds);
                if (attackIntervalJitterSeconds > 0f)
                {
                    interval += UnityEngine.Random.Range(-attackIntervalJitterSeconds, attackIntervalJitterSeconds);
                    interval = Mathf.Max(0.1f, interval);
                }

                yield return new WaitForSeconds(interval);
            }

            _combatLoopCoroutine = null;
        }

        private IReadOnlyList<Animator> ResolveAnimators()
        {
            _resolvedAnimators.Clear();
            if (explicitAnimators != null && explicitAnimators.Length > 0)
            {
                for (var index = 0; index < explicitAnimators.Length; index += 1)
                {
                    var animator = explicitAnimators[index];
                    if (animator != null)
                    {
                        _resolvedAnimators.Add(animator);
                    }
                }

                return _resolvedAnimators;
            }

            var root = _animatorRoot != null ? _animatorRoot : transform;
            var animators = root.GetComponentsInChildren<Animator>(true);
            if (animators == null || animators.Length == 0)
            {
                return _resolvedAnimators;
            }

            for (var index = 0; index < animators.Length; index += 1)
            {
                var animator = animators[index];
                if (animator != null)
                {
                    _resolvedAnimators.Add(animator);
                }
            }

            return _resolvedAnimators;
        }

        private static void SetAnimatorTriggerIfExists(Animator animator, string parameterName)
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
                if (parameter.type != AnimatorControllerParameterType.Trigger)
                {
                    continue;
                }

                if (!string.Equals(parameter.name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                animator.SetTrigger(parameter.name);
                return;
            }
        }
    }
}
