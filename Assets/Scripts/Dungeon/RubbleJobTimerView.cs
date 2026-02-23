using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeekerDungeon.Dungeon
{
    public sealed class RubbleJobTimerView : MonoBehaviour
    {
        [Header("Optional Bindings")]
        [SerializeField] private Image fillImage;
        [SerializeField] private TMP_Text timeText;
        [SerializeField] private TMP_Text helperCountText;
        [SerializeField] private string helperCountPrefix = "x";

        private void Awake()
        {
            if (fillImage == null)
            {
                fillImage = GetComponentInChildren<Image>(true);
            }

            if (timeText == null)
            {
                timeText = GetComponentInChildren<TMP_Text>(true);
            }
        }

        public void Apply(float secondsRemaining, float totalSeconds, string formattedTime, uint helperCount)
        {
            var clampedRemaining = Mathf.Max(0f, secondsRemaining);
            var clampedTotal = Mathf.Max(0.01f, totalSeconds);
            var normalized = Mathf.Clamp01(clampedRemaining / clampedTotal);

            if (fillImage != null)
            {
                fillImage.fillAmount = normalized;
            }

            if (timeText != null)
            {
                timeText.text = formattedTime ?? string.Empty;
            }

            if (helperCountText != null)
            {
                helperCountText.text = helperCount > 0
                    ? $"{helperCountPrefix}{helperCount}"
                    : string.Empty;
            }
        }
    }
}

