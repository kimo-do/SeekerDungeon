using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    /// <summary>
    /// Toggles rubble VFX based on whether a rubble-clearing job is actively running.
    /// Attach this to a rubble visual and assign any VFX roots / particle systems.
    /// </summary>
    public sealed class RubbleJobVfxController : MonoBehaviour
    {
        [SerializeField] private GameObject[] vfxRoots;
        [SerializeField] private ParticleSystem[] particleSystems;
        [Header("Spawned VFX (optional)")]
        [SerializeField] private GameObject spawnedVfxPrefab;
        [SerializeField] private Transform spawnedVfxAnchor;
        [SerializeField] private Transform spawnedVfxParent;
        [SerializeField] private bool destroySpawnedVfxWhenFullyStopped;
        [SerializeField] private bool deactivateRootsOnStop;
        [SerializeField] private bool stopAndClearOnDisable;

        private bool _isActive;
        private GameObject _spawnedVfxInstance;
        private ParticleSystem[] _spawnedParticleSystems = System.Array.Empty<ParticleSystem>();

        private void Awake()
        {
            SetJobActive(false);
        }

        private void Update()
        {
            if (_isActive || !destroySpawnedVfxWhenFullyStopped || _spawnedVfxInstance == null)
            {
                return;
            }

            if (_spawnedParticleSystems == null || _spawnedParticleSystems.Length == 0)
            {
                return;
            }

            for (var i = 0; i < _spawnedParticleSystems.Length; i += 1)
            {
                var particleSystem = _spawnedParticleSystems[i];
                if (particleSystem != null && particleSystem.IsAlive(true))
                {
                    return;
                }
            }

            Destroy(_spawnedVfxInstance);
            _spawnedVfxInstance = null;
            _spawnedParticleSystems = System.Array.Empty<ParticleSystem>();
        }

        public void SetJobActive(bool active)
        {
            if (_isActive == active)
            {
                return;
            }

            _isActive = active;
            EnsureSpawnedVfxInstance();
            if (active)
            {
                SnapSpawnedVfxToAnchorPosition();
            }

            if (vfxRoots != null)
            {
                for (var i = 0; i < vfxRoots.Length; i += 1)
                {
                    var root = vfxRoots[i];
                    if (root == null)
                    {
                        continue;
                    }

                    if (active)
                    {
                        root.SetActive(true);
                    }
                    else if (deactivateRootsOnStop)
                    {
                        root.SetActive(false);
                    }
                }
            }

            ApplyParticleState(particleSystems, active);
            ApplyParticleState(_spawnedParticleSystems, active);
        }

        private void EnsureSpawnedVfxInstance()
        {
            if (_spawnedVfxInstance != null || spawnedVfxPrefab == null)
            {
                return;
            }

            var anchor = spawnedVfxAnchor != null ? spawnedVfxAnchor : transform;
            var parent = spawnedVfxParent != null ? spawnedVfxParent : null;
            _spawnedVfxInstance = Instantiate(
                spawnedVfxPrefab,
                anchor.position,
                spawnedVfxPrefab.transform.rotation,
                parent);
            _spawnedParticleSystems = _spawnedVfxInstance.GetComponentsInChildren<ParticleSystem>(true);
        }

        private void SnapSpawnedVfxToAnchorPosition()
        {
            if (_spawnedVfxInstance == null)
            {
                return;
            }

            var anchor = spawnedVfxAnchor != null ? spawnedVfxAnchor : transform;
            _spawnedVfxInstance.transform.position = anchor.position;
        }

        private void ApplyParticleState(ParticleSystem[] systems, bool active)
        {
            if (systems == null)
            {
                return;
            }

            for (var i = 0; i < systems.Length; i += 1)
            {
                var particleSystem = systems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                if (active)
                {
                    particleSystem.gameObject.SetActive(true);
                    particleSystem.Play(true);
                }
                else if (stopAndClearOnDisable)
                {
                    particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                else
                {
                    // Natural fade-out: stop emission only, let live particles finish.
                    particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }
    }
}
