using System;
using System.Collections.Generic;
using UnityEngine;

namespace SavableObservable {

    /// <summary>
    /// Shared reflection helpers for walking user-declared type hierarchies.
    /// </summary>
    internal static class TypeUtil {

        /// <summary>
        /// Returns true when <paramref name="toCheck"/> is, or derives from, a closed or open form of the
        /// generic type definition <paramref name="generic"/> (e.g. <c>typeof(BaseObservablePresenter&lt;&gt;)</c>).
        /// </summary>
        internal static bool IsSubclassOfRawGeneric(Type generic, Type toCheck) {
            while (toCheck != null && toCheck != typeof(object)) {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (generic == cur) {
                    return true;
                }
                toCheck = toCheck.BaseType;
            }
            return false;
        }

        /// <summary>
        /// True for the engine / framework root types where a user-declared hierarchy walk must stop.
        /// </summary>
        internal static bool IsHierarchyRoot(Type type) {
            return type == null
                || type == typeof(BaseObservableDataModel)
                || type == typeof(MonoBehaviour)
                || type == typeof(Component)
                || type == typeof(UnityEngine.Object)
                || type == typeof(object);
        }

        /// <summary>
        /// Returns <paramref name="type"/> and its base types, most derived first, stopping before the
        /// framework / engine roots (<see cref="BaseObservableDataModel"/>, <see cref="MonoBehaviour"/>,
        /// <see cref="Component"/>, <see cref="UnityEngine.Object"/>, <see cref="object"/>).
        /// </summary>
        internal static List<Type> GetUserHierarchy(Type type) {
            var result = new List<Type>();
            for (var t = type; !IsHierarchyRoot(t); t = t.BaseType) {
                result.Add(t);
            }
            return result;
        }
    }
}
