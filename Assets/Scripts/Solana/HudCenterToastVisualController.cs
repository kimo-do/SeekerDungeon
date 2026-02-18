using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Animated center-screen HUD toast for short, high-visibility gameplay feedback.
    /// </summary>
    public sealed class HudCenterToastVisualController
    {
        private const float IntroDurationSeconds = 0.2f;
        private const float OutroDurationSeconds = 0.2f;
        private const float MinHoldDurationSeconds = 0.2f;

        private VisualElement _toastRoot;
        private Label _toastLabel;

        private float _holdDurationSeconds = 1.5f;
        private float _phaseElapsedSeconds;
        private ToastPhase _phase = ToastPhase.Hidden;

        private enum ToastPhase
        {
            Hidden,
            Intro,
            Hold,
            Outro
        }

        public void Bind(VisualElement root)
        {
            _toastRoot = root?.Q<VisualElement>("hud-center-toast");
            _toastLabel = root?.Q<Label>("hud-center-toast-label");
            SetHidden();
        }

        public void Show(string message, float holdDurationSeconds = 1.5f)
        {
            if (_toastRoot == null || _toastLabel == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            _toastLabel.text = message;
            _holdDurationSeconds = Mathf.Max(MinHoldDurationSeconds, holdDurationSeconds);
            _phase = ToastPhase.Intro;
            _phaseElapsedSeconds = 0f;

            _toastRoot.style.display = DisplayStyle.Flex;
            ApplyVisual(0f);
        }

        public void Tick(float deltaTime)
        {
            if (_toastRoot == null || _phase == ToastPhase.Hidden)
            {
                return;
            }

            _phaseElapsedSeconds += Mathf.Max(0f, deltaTime);

            switch (_phase)
            {
                case ToastPhase.Intro:
                {
                    var t = Mathf.Clamp01(_phaseElapsedSeconds / IntroDurationSeconds);
                    ApplyVisual(EaseOutCubic(t));
                    if (_phaseElapsedSeconds >= IntroDurationSeconds)
                    {
                        _phase = ToastPhase.Hold;
                        _phaseElapsedSeconds = 0f;
                    }
                    break;
                }
                case ToastPhase.Hold:
                {
                    ApplyVisual(1f);
                    if (_phaseElapsedSeconds >= _holdDurationSeconds)
                    {
                        _phase = ToastPhase.Outro;
                        _phaseElapsedSeconds = 0f;
                    }
                    break;
                }
                case ToastPhase.Outro:
                {
                    var t = Mathf.Clamp01(_phaseElapsedSeconds / OutroDurationSeconds);
                    ApplyVisual(1f - EaseInCubic(t));
                    if (_phaseElapsedSeconds >= OutroDurationSeconds)
                    {
                        SetHidden();
                    }
                    break;
                }
            }
        }

        public void Dispose()
        {
            _toastRoot = null;
            _toastLabel = null;
            _phase = ToastPhase.Hidden;
            _phaseElapsedSeconds = 0f;
        }

        private void SetHidden()
        {
            if (_toastRoot == null)
            {
                _phase = ToastPhase.Hidden;
                _phaseElapsedSeconds = 0f;
                return;
            }

            _phase = ToastPhase.Hidden;
            _phaseElapsedSeconds = 0f;
            _toastRoot.style.display = DisplayStyle.None;
            _toastRoot.style.opacity = 0f;
            _toastRoot.style.scale = new Scale(new Vector2(0.94f, 0.94f));
        }

        private void ApplyVisual(float normalized)
        {
            if (_toastRoot == null)
            {
                return;
            }

            var alpha = Mathf.Clamp01(normalized);
            var scale = Mathf.Lerp(0.94f, 1f, alpha);
            _toastRoot.style.opacity = alpha;
            _toastRoot.style.scale = new Scale(new Vector2(scale, scale));
        }

        private static float EaseOutCubic(float t)
        {
            var inv = 1f - Mathf.Clamp01(t);
            return 1f - inv * inv * inv;
        }

        private static float EaseInCubic(float t)
        {
            var clamped = Mathf.Clamp01(t);
            return clamped * clamped * clamped;
        }
    }
}
