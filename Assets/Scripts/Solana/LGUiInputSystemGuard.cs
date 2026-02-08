using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace SeekerDungeon.Solana
{
    public static class LGUiInputSystemGuard
    {
        public static bool EnsureEventSystemForRuntimeUi(bool createIfMissing = false)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                eventSystem = Object.FindObjectOfType<EventSystem>(true);
            }

            if (eventSystem == null)
            {
                if (!createIfMissing)
                {
                    Debug.LogWarning("[LGInput] No EventSystem found. Add a persistent EventSystem/InputSystemUIInputModule to bootstrap.");
                    return false;
                }

                var eventSystemObject = new GameObject("EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

#if ENABLE_INPUT_SYSTEM
            var oldInputModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (oldInputModule != null)
            {
                Object.Destroy(oldInputModule);
            }

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }
#else
            Debug.LogError("[LGInput] ENABLE_INPUT_SYSTEM is not defined. UI expects the new Input System package.");
            return false;
#endif

            return true;
        }
    }
}
