using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    /// <summary>
    /// Toggleable outline highlight for interactable objects.
    /// Searches children recursively for all SpriteRenderers and animates
    /// _OuterOutlineFade on their materials in a pingpong loop when interactable.
    /// Applies to ALL child renderers so it works on objects with multiple visual
    /// states (e.g. doors that swap between rubble/open/solid children).
    /// </summary>
    public sealed class VisualInteractable : MonoBehaviour
    {
        [Header("Animation")]
        [SerializeField] private float minFade = 0f;
        private float maxFade = 0.2f;
        private float speed = 1f;

        [Header("State")]
        [SerializeField] private bool interactable;

        private SpriteRenderer[] _renderers;
        private MaterialPropertyBlock _propertyBlock;
        private bool _resolved;
        private float _phase;

        private static readonly int OuterOutlineFadeId = Shader.PropertyToID("_OuterOutlineFade");

        public bool Interactable
        {
            get => interactable;
            set
            {
                if (interactable == value)
                {
                    return;
                }

                interactable = value;
                if (interactable)
                {
                    // Re-resolve renderers in case the hierarchy changed (e.g. new room)
                    ResolveRenderers();
                }
                else
                {
                    SetOutlineFade(0f);
                }
            }
        }

        /// <summary>
        /// Force re-resolve renderers. Call after the visual hierarchy changes
        /// (e.g. room transition enabling/disabling child objects).
        /// </summary>
        public void Refresh()
        {
            _resolved = false;
            ResolveRenderers();
        }

        private void OnEnable()
        {
            if (!_resolved)
            {
                ResolveRenderers();
            }
        }

        private void Update()
        {
            if (!_resolved)
            {
                ResolveRenderers();
            }

            if (_renderers == null || _renderers.Length == 0 || !interactable)
            {
                return;
            }

            _phase += Time.deltaTime * speed;
            var t = Mathf.PingPong(_phase, 1f);
            var fade = Mathf.Lerp(minFade, maxFade, t);
            SetOutlineFade(fade);
        }

        private void OnDisable()
        {
            SetOutlineFade(0f);
            _phase = 0f;
        }

        private void ResolveRenderers()
        {
            _resolved = true;
            _renderers = GetComponentsInChildren<SpriteRenderer>(true);
            if (_renderers.Length > 0)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }
        }

        private void SetOutlineFade(float value)
        {
            if (_renderers == null || _propertyBlock == null)
            {
                return;
            }

            for (var i = 0; i < _renderers.Length; i++)
            {
                var sr = _renderers[i];
                if (sr == null)
                {
                    continue;
                }

                sr.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetFloat(OuterOutlineFadeId, value);
                sr.SetPropertyBlock(_propertyBlock);
            }
        }
    }
}
