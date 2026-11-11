using System;
using System.Collections;
using System.Reflection;
using UnityEditor;

namespace SavableObservable.Editor {
    /// <summary>
    /// Shared utility class for resolving objects from SerializedProperty paths.
    /// Used by editor drawers to access the actual runtime instances.
    /// </summary>
    public static class PropertyResolver {
        /// <summary>
        /// Gets the object instance that the SerializedProperty points to.
        /// Traverses the property path to find the actual target object.
        /// </summary>
        /// <param name="property">The serialized property to resolve</param>
        /// <returns>The object instance, or null if resolution fails</returns>
        public static object GetTargetObjectOfProperty(SerializedProperty property) {
            if (property == null || property.serializedObject == null) return null;
            return ResolvePathOnObject(property.serializedObject.targetObject, property.propertyPath);
        }

        /// <summary>
        /// Gets the object instances that the SerializedProperty points to across all target objects.
        /// Used for multi-object editing in property drawers.
        /// </summary>
        /// <param name="property">The serialized property to resolve</param>
        /// <returns>Array of resolved objects</returns>
        public static object[] GetTargetObjectsOfProperty(SerializedProperty property) {
            if (property == null || property.serializedObject == null) return Array.Empty<object>();
            var targets = property.serializedObject.targetObjects;
            if (targets == null || targets.Length == 0) {
                var single = property.serializedObject.targetObject;
                if (single == null) return Array.Empty<object>();
                var resolved = ResolvePathOnObject(single, property.propertyPath);
                return resolved != null ? new[] { resolved } : Array.Empty<object>();
            }

            var result = new object[targets.Length];
            for (int i = 0; i < targets.Length; i++) {
                result[i] = ResolvePathOnObject(targets[i], property.propertyPath);
            }
            return result;
        }

        private static object ResolvePathOnObject(object target, string propertyPath) {
            if (target == null) return null;
            var path = propertyPath.Replace(".Array.data[", "[");
            object obj = target;
            var elements = path.Split('.');

            foreach (var element in elements) {
                if (element.Contains("[")) {
                    var elementName = element.Substring(0, element.IndexOf("["));
                    var index = Convert.ToInt32(element.Substring(element.IndexOf("[")).Replace("[", "").Replace("]", ""));
                    obj = GetValueByPathSegment(obj, elementName, index);
                } else {
                    obj = GetValueByPathSegment(obj, element);
                }
                if (obj == null) return null;
            }

            return obj;
        }

        /// <summary>
        /// Gets a value by name from the source object using reflection.
        /// Searches fields and properties with public and non-public binding.
        /// </summary>
        /// <param name="source">The source object</param>
        /// <param name="name">The field or property name</param>
        /// <returns>The value, or null if not found</returns>
        public static object GetValueByPathSegment(object source, string name) {
            if (source == null) return null;
            var type = source.GetType();

            var current = type;
            while (current != null) {
                var f = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(source);
                current = current.BaseType;
            }

            current = type;
            while (current != null) {
                var p = current.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(source, null);
                current = current.BaseType;
            }

            return null;
        }

        /// <summary>
        /// Gets a value by name and index from the source object.
        /// Used for accessing array or list elements by index.
        /// </summary>
        /// <param name="source">The source object</param>
        /// <param name="name">The field or property name</param>
        /// <param name="index">The index to access</param>
        /// <returns>The value at the specified index, or null if not found</returns>
        public static object GetValueByPathSegment(object source, string name, int index) {
            var enumerable = GetValueByPathSegment(source, name) as IEnumerable;
            if (enumerable == null) return null;

            var enm = enumerable.GetEnumerator();
            try {
                for (int i = 0; i <= index; i++) {
                    if (!enm.MoveNext()) return null;
                }
                return enm.Current;
            } finally {
                (enm as IDisposable)?.Dispose();
            }
        }
    }
}
