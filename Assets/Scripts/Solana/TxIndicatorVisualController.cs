using UnityEngine;
using UnityEngine.UIElements;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// UI Toolkit visual controller for the shared on-chain transaction indicator.
    /// </summary>
    public sealed class TxIndicatorVisualController
    {
        private const float RotationDegreesPerSecond = 300f;
        private const float PulseSpeed = 4.5f;
        private const float PulseAmplitude = 0.05f;
        private const float SuccessBurstDuration = 0.45f;

        private VisualElement _root;
        private VisualElement _indicator;
        private VisualElement _ring;
        private VisualElement _successBurst;
        private bool _isActive;
        private float _rotation;
        private float _pulse;
        private float _successBurstRemaining;

        public TxIndicatorVisualController()
        {
            _isActive = LGTransactionActivity.IsActive;
            LGTransactionActivity.OnActiveStateChanged += HandleActiveStateChanged;
            LGTransactionActivity.OnTransactionCompleted += HandleTransactionCompleted;
        }

        public void Bind(VisualElement root)
        {
            _root = root;
            _indicator = _root?.Q<VisualElement>("tx-indicator");
            _ring = _root?.Q<VisualElement>("tx-indicator-ring");
            _successBurst = _root?.Q<VisualElement>("tx-indicator-success");
            UpdateVisibilityAndScale();
            UpdateSuccessBurstVisual();
        }

        public void Tick(float deltaTime)
        {
            if (_indicator == null)
            {
                return;
            }

            if (_isActive)
            {
                _rotation = (_rotation + RotationDegreesPerSecond * deltaTime) % 360f;
                _ring.style.rotate = new Rotate(_rotation);

                _pulse += deltaTime * PulseSpeed;
                var pulseScale = 1f + Mathf.Sin(_pulse) * PulseAmplitude;
                _indicator.style.scale = new Scale(new Vector2(pulseScale, pulseScale));
            }
            else
            {
                _indicator.style.scale = new Scale(Vector2.one);
            }

            if (_successBurstRemaining > 0f)
            {
                _successBurstRemaining = Mathf.Max(0f, _successBurstRemaining - deltaTime);
                UpdateSuccessBurstVisual();
            }

            UpdateVisibilityAndScale();
        }

        public void Dispose()
        {
            LGTransactionActivity.OnActiveStateChanged -= HandleActiveStateChanged;
            LGTransactionActivity.OnTransactionCompleted -= HandleTransactionCompleted;
            _root = null;
            _indicator = null;
            _ring = null;
            _successBurst = null;
        }

        private void HandleActiveStateChanged(bool isActive)
        {
            _isActive = isActive;
            if (!isActive && _indicator != null)
            {
                _indicator.style.scale = new Scale(Vector2.one);
            }

            UpdateVisibilityAndScale();
        }

        private void HandleTransactionCompleted(bool success)
        {
            if (!success)
            {
                return;
            }

            _successBurstRemaining = SuccessBurstDuration;
            UpdateSuccessBurstVisual();
            UpdateVisibilityAndScale();
        }

        private void UpdateVisibilityAndScale()
        {
            if (_indicator == null)
            {
                return;
            }

            var shouldShow = _isActive || _successBurstRemaining > 0f;
            _indicator.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateSuccessBurstVisual()
        {
            if (_successBurst == null)
            {
                return;
            }

            if (_successBurstRemaining <= 0f)
            {
                _successBurst.style.opacity = 0f;
                _successBurst.style.scale = new Scale(Vector2.one);
                return;
            }

            var t = 1f - (_successBurstRemaining / SuccessBurstDuration);
            var alpha = Mathf.Pow(1f - t, 2f) * 0.9f;
            var scale = Mathf.Lerp(0.7f, 1.9f, t);
            _successBurst.style.opacity = alpha;
            _successBurst.style.scale = new Scale(new Vector2(scale, scale));
        }
    }
}
