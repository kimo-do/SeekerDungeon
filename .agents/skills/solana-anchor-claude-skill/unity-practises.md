---
name: unity-practises-skill
description: Common pitfalls in unity c# development for unity 6000+
---

## common issues
warning CS0618: 'Object.FindObjectOfType<T>()' is obsolete: 'Object.FindObjectOfType has been deprecated. Use Object.FindFirstObjectByType instead or if finding any instance is acceptable the faster Object.FindAnyObjectByType'

## input system
We use the unity new input system package

## inspector references
we prefer to slot prefabs and references in the inspector when needed. 

And we prefer to automatically configure reference (by instance for example) when possible (and thus not expose them in the inspector).