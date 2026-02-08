using UnityEngine;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Keeps the attached root object alive across scene loads.
    /// Attach to your bootstrap root and place EventSystem/EventManager under it.
    /// </summary>
    public sealed class PersistentGameObject : MonoBehaviour
    {
        private static readonly System.Collections.Generic.HashSet<string> LiveObjects = new();

        [SerializeField] private bool enforceSingleInstanceByName = true;

        private void Awake()
        {
            if (enforceSingleInstanceByName)
            {
                var objectName = gameObject.name;
                if (LiveObjects.Contains(objectName))
                {
                    Destroy(gameObject);
                    return;
                }

                LiveObjects.Add(objectName);
            }

            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (enforceSingleInstanceByName)
            {
                LiveObjects.Remove(gameObject.name);
            }
        }
    }
}
