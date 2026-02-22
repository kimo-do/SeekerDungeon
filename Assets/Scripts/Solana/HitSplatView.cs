using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeekerDungeon.Solana
{
    public sealed class HitSplatView : MonoBehaviour
    {
        [SerializeField] private Image splatImage;
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private Animator animator;
        [SerializeField] private string removeTriggerName = "remove";
        private Vector3 _baseScale = Vector3.one;

        private void Awake()
        {
            if (splatImage == null)
            {
                splatImage = GetComponentInChildren<Image>(true);
            }

            if (valueText == null)
            {
                valueText = GetComponentInChildren<TMP_Text>(true);
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            _baseScale = transform.localScale;
        }

        public void SetDamage(int damage, Sprite splatSprite)
        {
            if (valueText != null)
            {
                valueText.text = damage.ToString();
            }

            if (splatImage != null && splatSprite != null)
            {
                splatImage.sprite = splatSprite;
            }
        }

        public void TriggerRemove()
        {
            if (animator == null || string.IsNullOrWhiteSpace(removeTriggerName))
            {
                return;
            }

            animator.SetTrigger(removeTriggerName);
        }

        public void SetScaleMultiplier(float multiplier)
        {
            var clamped = Mathf.Max(0.01f, multiplier);
            transform.localScale = _baseScale * clamped;
        }
    }
}
