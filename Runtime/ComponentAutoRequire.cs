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
        internal static void EnsureComponent<T>(MonoBehaviour owner) {
            EnsureComponent(owner, typeof(T));
        }

        /// <summary>
        /// Ensures a component of <paramref name="componentType"/> exists on the same GameObject.
        /// Adds it automatically in the Unity Editor (non-play mode).
        /// </summary>
        internal static void EnsureComponent(MonoBehaviour owner, Type componentType) {
            if (owner == null || componentType == null) return;

            if (!typeof(Component).IsAssignableFrom(componentType)) {
                Debug.LogError($"[SavableObservable] Cannot auto-add '{componentType.Name}' to '{owner.GetType().Name}'. Type must inherit from UnityEngine.Component.", owner);
                return;
            }

            if (owner.GetComponent(componentType) != null) return;

#if UNITY_EDITOR
            if (!Application.isPlaying) {
                owner.gameObject.AddComponent(componentType);
                Debug.Log($"[SavableObservable] Auto-added required component '{componentType.Name}' to '{owner.gameObject.name}' for '{owner.GetType().Name}'.", owner);
            }
#endif
        }
    }
}
