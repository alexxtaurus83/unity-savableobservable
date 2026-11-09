using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
using Unity.Scripting.LifecycleManagement;
#endif

namespace SavableObservable {

    /// <summary>
    /// Cached reflection description of a <see cref="BaseObservableDataModel"/> type.
    /// Single source of truth for binding (observable fields by name, same-value flags)
    /// and persistence (properties, observable fields, plain fields).
    /// </summary>
    internal sealed class ModelDescriptor {

        /// <summary>All supported observable fields (<see cref="ObservableVariable{T}"/> / <see cref="ObservableList{T}"/>), including <c>[NonSaved]</c> ones.</summary>
        public readonly FieldInfo[] ObservableFields;

        /// <summary>Supported observable fields by name (first declaration in the hierarchy wins).</summary>
        public readonly Dictionary<string, FieldInfo> ObservableFieldsByName;

        /// <summary>Observable-variable fields marked with <see cref="NotifyOnSameValueAttribute"/>.</summary>
        public readonly FieldInfo[] NotifyOnSameValueFields;

        /// <summary>Writable instance properties copied by <c>LoadDataFromModel</c>.</summary>
        public readonly PropertyInfo[] LoadProperties;

        /// <summary>Saved (not <c>[NonSaved]</c>) observable fields together with their <c>Value</c> property.</summary>
        public readonly (FieldInfo field, PropertyInfo valueProp, Type destType)[] LoadObservableFields;

        /// <summary>Saved non-observable fields copied by <c>LoadDataFromModel</c>.</summary>
        public readonly FieldInfo[] LoadPlainFields;

        private ModelDescriptor(Type modelType) {
            var supported = new List<FieldInfo>();
            var byName = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            var sameValue = new List<FieldInfo>();

            var props = new List<PropertyInfo>();
            var seenPropNames = new HashSet<string>(StringComparer.Ordinal);
            var loadObs = new List<(FieldInfo, PropertyInfo, Type)>();
            var loadPlain = new List<FieldInfo>();
            var seenFieldNames = new HashSet<string>(StringComparer.Ordinal);

            const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            foreach (var t in TypeUtil.GetUserHierarchy(modelType)) {
                foreach (var prop in t.GetProperties(Declared)) {
                    if (!prop.CanWrite || !prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                    if (IsUnityComponentProperty(prop)) continue;
                    if (seenPropNames.Add(prop.Name)) {
                        props.Add(prop);
                    }
                }

                foreach (var field in t.GetFields(Declared)) {
                    var isObservable = Observable.IsSupportedFieldType(field);

                    if (isObservable && !byName.ContainsKey(field.Name)) {
                        supported.Add(field);
                        byName[field.Name] = field;
                    }

                    if (field.IsDefined(typeof(NotifyOnSameValueAttribute), true)) {
                        if (typeof(ISameValuePolicy).IsAssignableFrom(field.FieldType)) {
                            sameValue.Add(field);
                        } else {
                            // Logged once per model type: descriptors are cached.
                            Debug.LogWarning($"[SavableObservable] [{nameof(NotifyOnSameValueAttribute)}] on '{field.Name}' in {modelType.Name} has no effect: it only applies to ObservableVariable<T> fields.");
                        }
                    }

                    if (field.IsDefined(typeof(NonSavedAttribute), true)) continue;
                    if (!seenFieldNames.Add(field.Name)) continue;

                    if (isObservable) {
                        var valueProp = field.FieldType.GetProperty("Value");
                        if (valueProp != null && valueProp.CanRead && valueProp.CanWrite) {
                            loadObs.Add((field, valueProp, valueProp.PropertyType));
                        }
                    } else {
                        loadPlain.Add(field);
                    }
                }
            }

            ObservableFields = supported.ToArray();
            ObservableFieldsByName = byName;
            NotifyOnSameValueFields = sameValue.ToArray();
            LoadProperties = props.ToArray();
            LoadObservableFields = loadObs.ToArray();
            LoadPlainFields = loadPlain.ToArray();
        }

#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [NoAutoStaticsCleanup]
#endif
        private static readonly Dictionary<Type, ModelDescriptor> _cache = new Dictionary<Type, ModelDescriptor>();
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [NoAutoStaticsCleanup]
#endif
        private static readonly object _cacheLock = new object();

        /// <summary>Returns the cached descriptor for <paramref name="modelType"/>, building it on first use.</summary>
        internal static ModelDescriptor Get(Type modelType) {
            lock (_cacheLock) {
                if (!_cache.TryGetValue(modelType, out var descriptor)) {
                    descriptor = new ModelDescriptor(modelType);
                    _cache[modelType] = descriptor;
                }
                return descriptor;
            }
        }

        private static bool IsUnityComponentProperty(PropertyInfo prop) {
            var declaringType = prop.DeclaringType;
            if (declaringType == null || string.IsNullOrEmpty(declaringType.Namespace)) {
                return false;
            }

            return declaringType.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal);
        }
    }
}
