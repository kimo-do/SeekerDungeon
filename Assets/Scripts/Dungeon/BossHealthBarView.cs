using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeekerDungeon.Dungeon
{
    public sealed class BossHealthBarView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Slider slider;
        [SerializeField] private Image fillImage;
        [SerializeField] private Image delayedDamageFillImage;
        [SerializeField] private TMP_Text valueLabel;
        [SerializeField] private float smoothSpeed = 6f;
        [SerializeField] private float delayedDamageCatchupSpeed = 2.2f;
        [SerializeField] private float delayedDamageHoldSeconds = 0.16f;
        [SerializeField] private bool hideWhenNoBoss = true;
        [SerializeField] private float slotSecondsEstimate = 0.4f;
        [SerializeField] private bool useChunkedClientProjection = true;
        [SerializeField] private float visualHitIntervalSeconds = 0.6f;
        [SerializeField] private ulong minDamagePerVisualHit = 1UL;

        private float _displayNormalized = 1f;
        private float _targetNormalized = 1f;
        private float _delayedDisplayNormalized = 1f;
        private float _delayedHoldUntilUnscaled;
        private bool _wasVisible;
        private ulong _boundCurrentHp;
        private ulong _boundMaxHp;
        private ulong _boundTotalDps;
        private bool _boundDead;
        private float _boundAtUnscaledTime;

        private void Awake()
        {
            if (root == null)
            {
                root = gameObject;
            }
        }

        private void Update()
        {
            if (!Mathf.Approximately(_displayNormalized, _targetNormalized))
            {
                _displayNormalized = Mathf.MoveTowards(
                    _displayNormalized,
                    _targetNormalized,
                    Time.unscaledDeltaTime * smoothSpeed);
                ApplyNormalized(_displayNormalized);
            }

            TickDelayedDamageFill();

            if (_boundMaxHp == 0UL || _boundDead || _boundTotalDps == 0UL)
            {
                return;
            }

            var projectedHp = ComputeProjectedCurrentHp();
            var projectedNormalized = (float)projectedHp / _boundMaxHp;
            if (projectedNormalized < _targetNormalized)
            {
                _targetNormalized = projectedNormalized;
            }

            if (valueLabel != null)
            {
                valueLabel.text = $"{projectedHp}/{_boundMaxHp}";
            }
        }

        public void Bind(ulong currentHp, ulong maxHp, ulong totalDps, bool isDead, bool isVisible)
        {
            if (root != null)
            {
                root.SetActive(isVisible || !hideWhenNoBoss);
            }

            if (!isVisible || maxHp == 0UL)
            {
                _targetNormalized = 0f;
                _displayNormalized = 0f;
                _delayedDisplayNormalized = 0f;
                ApplyNormalized(0f);
                ApplyDelayedNormalized(0f);
                _wasVisible = false;
                _boundCurrentHp = 0UL;
                _boundMaxHp = 0UL;
                _boundTotalDps = 0UL;
                _boundDead = false;
                if (valueLabel != null)
                {
                    valueLabel.text = string.Empty;
                }

                return;
            }

            var clampedCurrent = currentHp > maxHp ? maxHp : currentHp;
            _boundCurrentHp = clampedCurrent;
            _boundMaxHp = maxHp;
            _boundTotalDps = totalDps;
            _boundDead = isDead;
            _boundAtUnscaledTime = Time.unscaledTime;
            if (!_wasVisible)
            {
                // Reset to full whenever the bar is shown again (spawn/reuse),
                // then animate down to the current HP target.
                _displayNormalized = 1f;
                _delayedDisplayNormalized = 1f;
                ApplyNormalized(1f);
                ApplyDelayedNormalized(1f);
            }

            var newTargetNormalized = (float)clampedCurrent / maxHp;
            if (delayedDamageFillImage != null &&
                newTargetNormalized < _displayNormalized - 0.0001f)
            {
                _delayedDisplayNormalized = Mathf.Max(_delayedDisplayNormalized, _displayNormalized);
                _delayedHoldUntilUnscaled = Time.unscaledTime + Mathf.Max(0f, delayedDamageHoldSeconds);
                ApplyDelayedNormalized(_delayedDisplayNormalized);
            }

            _targetNormalized = newTargetNormalized;
            if (_displayNormalized < _targetNormalized)
            {
                _displayNormalized = _targetNormalized;
                ApplyNormalized(_displayNormalized);
            }

            _wasVisible = true;

            if (valueLabel != null)
            {
                valueLabel.text = $"{clampedCurrent}/{maxHp}";
            }
        }

        private void TickDelayedDamageFill()
        {
            if (delayedDamageFillImage == null)
            {
                return;
            }

            if (_delayedDisplayNormalized < _displayNormalized)
            {
                _delayedDisplayNormalized = _displayNormalized;
            }

            if (Time.unscaledTime < _delayedHoldUntilUnscaled)
            {
                ApplyDelayedNormalized(_delayedDisplayNormalized);
                return;
            }

            if (_delayedDisplayNormalized > _displayNormalized)
            {
                _delayedDisplayNormalized = Mathf.MoveTowards(
                    _delayedDisplayNormalized,
                    _displayNormalized,
                    Time.unscaledDeltaTime * Mathf.Max(0.01f, delayedDamageCatchupSpeed));
                ApplyDelayedNormalized(_delayedDisplayNormalized);
            }
        }

        private ulong ComputeProjectedCurrentHp()
        {
            if (_boundDead || _boundTotalDps == 0UL || _boundCurrentHp == 0UL)
            {
                return _boundCurrentHp;
            }

            var elapsedSeconds = Mathf.Max(0f, Time.unscaledTime - _boundAtUnscaledTime);
            if (elapsedSeconds <= 0f)
            {
                return _boundCurrentHp;
            }

            ulong projectedDamage;
            if (useChunkedClientProjection)
            {
                // Client-side projection in discrete hit windows for chunked visuals.
                var hitInterval = Mathf.Max(0.05f, visualHitIntervalSeconds);
                var hitsElapsedDouble = Math.Floor(elapsedSeconds / hitInterval);
                if (hitsElapsedDouble <= 0d)
                {
                    return _boundCurrentHp;
                }

                var hitsElapsed = hitsElapsedDouble >= ulong.MaxValue
                    ? ulong.MaxValue
                    : (ulong)hitsElapsedDouble;

                var damagePerSecond = _boundTotalDps / Math.Max(0.01, slotSecondsEstimate);
                var damagePerHitDouble = Math.Floor(damagePerSecond * hitInterval);
                var computedDamagePerHit = damagePerHitDouble >= ulong.MaxValue
                    ? ulong.MaxValue
                    : (ulong)damagePerHitDouble;
                var damagePerHit = Math.Max(minDamagePerVisualHit, computedDamagePerHit);

                projectedDamage = hitsElapsed >= ulong.MaxValue / Math.Max(1UL, damagePerHit)
                    ? ulong.MaxValue
                    : hitsElapsed * damagePerHit;
            }
            else
            {
                var slotsElapsed = elapsedSeconds / Mathf.Max(0.01f, slotSecondsEstimate);
                var projectedDamageDouble = Math.Floor(_boundTotalDps * slotsElapsed);
                projectedDamage = projectedDamageDouble >= ulong.MaxValue
                    ? ulong.MaxValue
                    : (ulong)projectedDamageDouble;
            }

            return _boundCurrentHp > projectedDamage
                ? _boundCurrentHp - projectedDamage
                : 0UL;
        }

        private void ApplyNormalized(float normalized)
        {
            var value = Mathf.Clamp01(normalized);
            if (slider != null)
            {
                slider.normalizedValue = value;
            }

            if (fillImage != null)
            {
                fillImage.fillAmount = value;
            }
        }

        private void ApplyDelayedNormalized(float normalized)
        {
            if (delayedDamageFillImage == null)
            {
                return;
            }

            delayedDamageFillImage.fillAmount = Mathf.Clamp01(normalized);
        }
    }
}
