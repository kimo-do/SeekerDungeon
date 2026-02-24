using UnityEngine;
using UnityEngine.UIElements;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Animated top-of-screen banner for subtle server feed updates.
    /// </summary>
    public sealed class HudTopFeedBannerVisualController
    {
        private const float IntroDurationSeconds = 0.22f;
        private const float OutroDurationSeconds = 0.24f;
        private const float MinHoldDurationSeconds = 0.25f;

        private VisualElement _bannerRoot;
        private Label _bannerLabel;

        private float _holdDurationSeconds = 2.5f;
        private float _phaseElapsedSeconds;
        private BannerPhase _phase = BannerPhase.Hidden;

        private enum BannerPhase
        {
            Hidden,
            Intro,
            Hold,
            Outro
        }

        public void Bind(VisualElement root)
        {
            _bannerRoot = root?.Q<VisualElement>("hud-feed-banner");
            _bannerLabel = root?.Q<Label>("hud-feed-banner-label");
            SetHidden();
        }

        public void Show(string message, float holdDurationSeconds = 2.5f)
        {
            if (_bannerRoot == null || _bannerLabel == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            _bannerLabel.text = message.Trim();
            _holdDurationSeconds = Mathf.Max(MinHoldDurationSeconds, holdDurationSeconds);
            _phase = BannerPhase.Intro;
            _phaseElapsedSeconds = 0f;

            _bannerRoot.style.display = DisplayStyle.Flex;
            ApplyVisual(0f);
        }

        public void Tick(float deltaTime)
        {
            if (_bannerRoot == null || _phase == BannerPhase.Hidden)
            {
                return;
            }

            _phaseElapsedSeconds += Mathf.Max(0f, deltaTime);
            switch (_phase)
            {
                case BannerPhase.Intro:
                {
                    var t = Mathf.Clamp01(_phaseElapsedSeconds / IntroDurationSeconds);
                    ApplyVisual(EaseOutCubic(t));
                    if (_phaseElapsedSeconds >= IntroDurationSeconds)
                    {
                        _phase = BannerPhase.Hold;
                        _phaseElapsedSeconds = 0f;
                    }

                    break;
                }
                case BannerPhase.Hold:
                {
                    ApplyVisual(1f);
                    if (_phaseElapsedSeconds >= _holdDurationSeconds)
                    {
                        _phase = BannerPhase.Outro;
                        _phaseElapsedSeconds = 0f;
                    }

                    break;
                }
                case BannerPhase.Outro:
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
            _bannerRoot = null;
            _bannerLabel = null;
            _phase = BannerPhase.Hidden;
            _phaseElapsedSeconds = 0f;
        }

        private void SetHidden()
        {
            if (_bannerRoot == null)
            {
                _phase = BannerPhase.Hidden;
                _phaseElapsedSeconds = 0f;
                return;
            }

            _phase = BannerPhase.Hidden;
            _phaseElapsedSeconds = 0f;
            _bannerRoot.style.display = DisplayStyle.None;
            _bannerRoot.style.opacity = 0f;
            _bannerRoot.style.translate = new Translate(0f, -22f, 0f);
            _bannerRoot.style.scale = new Scale(new Vector2(0.98f, 0.98f));
        }

        private void ApplyVisual(float normalized)
        {
            if (_bannerRoot == null)
            {
                return;
            }

            var alpha = Mathf.Clamp01(normalized);
            var y = Mathf.Lerp(-22f, 0f, alpha);
            var scale = Mathf.Lerp(0.98f, 1f, alpha);
            _bannerRoot.style.opacity = alpha;
            _bannerRoot.style.translate = new Translate(0f, y, 0f);
            _bannerRoot.style.scale = new Scale(new Vector2(scale, scale));
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
