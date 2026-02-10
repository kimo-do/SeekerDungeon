---
name: unity-practises-skill
description: Common pitfalls in unity c# development for unity 6000+
---

## Common Issues

- In Unity 6000+, `Object.FindObjectOfType<T>()` is obsolete.
- Use:
  - `Object.FindFirstObjectByType<T>()` when you want the first valid instance.
  - `Object.FindAnyObjectByType<T>()` when any instance is acceptable and you want better performance.

## Input System

- This project uses Unity's **new Input System** package.
- Do not implement features using the legacy Input Manager unless explicitly requested.

## Inspector References

- Prefer assigning prefab and scene references in the Inspector when that is the cleanest setup.
- Also prefer auto-wiring references at runtime when reliable (for example, resolving by instance in scene), so unnecessary fields are not exposed in the Inspector.
- when you change private fields e.g.
  [SerializeField] private bool allowWalletAdapterSessionOnAndroid = true;
  it won't actually update the value, as that is stored in the scene on the gameobject. You have to remove [SerializeField] to make it force use your value.

## Editor Log
The editor log is located in:
C:\Users\<USERSNAME>\AppData\Local\Unity\Editor\Editor.log
it is a big file, but can we very helpful to debug by looking at the end of that file.