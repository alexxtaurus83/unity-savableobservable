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
            if (property == null) return null;

            var path = property.propertyPath.Replace(".Array.data[", "[");
            object obj = property.serializedObject.targetObject;
            var elements = path.Split('.');

            foreach (var element in elements) {
                if (element.Contains("[")) {
                    var elementName = element.Substring(0, element.IndexOf("["));
                    var index = Convert.ToInt32(element.Substring(element.IndexOf("[")).Replace("[", "").Replace("]", ""));
                    obj = GetValueByPathSegment(obj, elementName, index);
                } else {
                    obj = GetValueByPathSegment(obj, element);
                }
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

            while (type != null) {
                var f = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (f != null) return f.GetValue(source);

                var p = type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p != null) return p.GetValue(source, null);

                type = type.BaseType;
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
            for (int i = 0; i <= index; i++) {
                if (!enm.MoveNext()) return null;
            }
            return enm.Current;
        }
    }
}
