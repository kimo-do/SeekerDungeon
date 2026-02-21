using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeekerDungeon.Solana
{
    public sealed class HitSplatView : MonoBehaviour
    {
        [SerializeField] private Image splatImage;
        [SerializeField] private TMP_Text valueText;

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
    }
}
