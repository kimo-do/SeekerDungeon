using System;
using System.Collections.Generic;
using SeekerDungeon.Audio;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Shared runtime pop animation for UI Toolkit modal cards.
    /// </summary>
    public static class ModalPopAnimator
    {
        private const float StartScale = 0.9f;
        private const int DurationMilliseconds = 170;
        private static readonly Dictionary<VisualElement, IVisualElementScheduledItem> ActiveAnimations = new();

        public static void PlayOpen(VisualElement card)
        {
            if (card == null)
            {
                return;
            }

            if (ShouldPlayModalOpenSfx())
            {
                GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.ModalOpen);
            }

            if (ActiveAnimations.TryGetValue(card, out var existingAnimation) && existingAnimation != null)
            {
                existingAnimation.Pause();
            }

            card.style.opacity = 0f;
            card.style.scale = new Scale(new Vector3(StartScale, StartScale, 1f));
            var startTime = Time.realtimeSinceStartup;

            IVisualElementScheduledItem animation = null;
            animation = card.schedule.Execute(() =>
            {
                var elapsed = Time.realtimeSinceStartup - startTime;
                var t = Mathf.Clamp01(elapsed / (DurationMilliseconds / 1000f));
                // Smooth ease-out for a subtle modal "pop".
                var eased = 1f - Mathf.Pow(1f - t, 3f);
                var scale = Mathf.Lerp(StartScale, 1f, eased);
                card.style.opacity = eased;
                card.style.scale = new Scale(new Vector3(scale, scale, 1f));

                if (t < 0.999f)
                {
                    return;
                }

                card.style.opacity = 1f;
                card.style.scale = new Scale(Vector3.one);
                animation?.Pause();
                ActiveAnimations.Remove(card);
            }).Every(16);

            ActiveAnimations[card] = animation;
        }

        private static bool ShouldPlayModalOpenSfx()
        {
            var sceneName = SceneManager.GetActiveScene().name;
            if (string.Equals(sceneName, "Loading", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(sceneName, "Preload", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }
    }
}
