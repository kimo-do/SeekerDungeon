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
        [SerializeField] private TMP_Text valueLabel;
        [SerializeField] private float smoothSpeed = 6f;
        [SerializeField] private bool hideWhenNoBoss = true;

        private float _displayNormalized = 1f;
        private float _targetNormalized = 1f;
        private bool _wasVisible;

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
        }

        public void Bind(ulong currentHp, ulong maxHp, bool isVisible)
        {
            if (root != null)
            {
                root.SetActive(isVisible || !hideWhenNoBoss);
            }

            if (!isVisible || maxHp == 0UL)
            {
                _targetNormalized = 0f;
                _displayNormalized = 0f;
                ApplyNormalized(0f);
                _wasVisible = false;
                if (valueLabel != null)
                {
                    valueLabel.text = string.Empty;
                }

                return;
            }

            var clampedCurrent = currentHp > maxHp ? maxHp : currentHp;
            if (!_wasVisible)
            {
                // Reset to full whenever the bar is shown again (spawn/reuse),
                // then animate down to the current HP target.
                _displayNormalized = 1f;
                ApplyNormalized(1f);
            }

            _targetNormalized = (float)clampedCurrent / maxHp;
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
    }
}
