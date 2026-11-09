using System;
using UnityEngine;

namespace SavableObservable {
    /// <summary>
    /// Editor-time helper that auto-adds required components to keep MMVC setup friction low.
    /// </summary>
    internal static class ComponentAutoRequire {
        /// <summary>
        /// Ensures a component of type <typeparamref name="T"/> exists on the same GameObject.
        /// </summary>
        internal static void EnsureComponent<T>(MonoBehaviour owner, bool delayed = false) {
            EnsureComponent(owner, typeof(T), delayed);
        }

        /// <summary>
        /// Ensures every listed component type exists on the same GameObject (Editor, non-play mode).
        /// Used by <c>BaseLogic&lt;M,P&gt;</c>, <c>LoaderWithModel&lt;M&gt;</c> and <c>LoaderWithModelAndLogic&lt;M,LO&gt;</c>
        /// from <c>Reset</c> (<paramref name="delayed"/> = false) and <c>OnValidate</c> (<paramref name="delayed"/> = true).
        /// </summary>
        internal static void EnsureComponents(MonoBehaviour owner, bool delayed, params Type[] componentTypes) {
            if (componentTypes == null) return;
            for (int i = 0; i < componentTypes.Length; i++) {
                EnsureComponent(owner, componentTypes[i], delayed);
            }
        }

        /// <summary>
        /// Ensures a component of <paramref name="componentType"/> exists on the same GameObject.
        /// Adds it automatically in the Unity Editor (non-play mode).
        /// </summary>
        internal static void EnsureComponent(MonoBehaviour owner, Type componentType) {
            EnsureComponent(owner, componentType, false);
        }

        /// <summary>
        /// Ensures a component of <paramref name="componentType"/> exists on the same GameObject.
        /// Adds it automatically in the Unity Editor (non-play mode).
        /// When <paramref name="delayed"/> is true (e.g. during <c>OnValidate</c>), defers addition
        /// via <c>EditorApplication.delayCall</c> to avoid SendMessage warnings.
        /// </summary>
        internal static void EnsureComponent(MonoBehaviour owner, Type componentType, bool delayed) {
            if (owner == null || componentType == null) return;

            if (!typeof(Component).IsAssignableFrom(componentType)) {
                Debug.LogError($"[SavableObservable] Cannot auto-add '{componentType.Name}' to '{owner.GetType().Name}'. Type must inherit from UnityEngine.Component.", owner);
                return;
            }

            if (owner.GetComponent(componentType) != null) return;

#if UNITY_EDITOR
            if (!Application.isPlaying) {
                if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(owner)) {
                    return;
                }

                if (delayed) {
                    UnityEditor.EditorApplication.delayCall += () => {
                        if (owner == null || owner.gameObject == null) return;
                        if (owner.GetComponent(componentType) != null) return;

                        owner.gameObject.AddComponent(componentType);
                        Debug.Log($"[SavableObservable] Auto-added required component '{componentType.Name}' to '{owner.gameObject.name}' for '{owner.GetType().Name}'.", owner);
                    };
                } else {
                    owner.gameObject.AddComponent(componentType);
                    Debug.Log($"[SavableObservable] Auto-added required component '{componentType.Name}' to '{owner.gameObject.name}' for '{owner.GetType().Name}'.", owner);
                }
            }
#endif
        }
    }
}
