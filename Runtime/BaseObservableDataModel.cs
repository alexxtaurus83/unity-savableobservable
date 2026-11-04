using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
using Unity.Scripting.LifecycleManagement;
#endif

namespace SavableObservable {

    /// <summary>Abstract DataModel class to keep observable keep data with ObservableVariable types</summary>    
    [Serializable]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-300)]
    public abstract partial class BaseObservableDataModel : MonoBehaviour {
        protected virtual void Awake() {
            EnsureFieldsInitialized();
        }

        /// <summary>
        /// Gets the cached observable fields for this data model.
        /// </summary>
        public FieldInfo[] GetCachedObservableFields() {
            return Observable.GetCachedObservableFields(this);
        }

        /// <summary>
        /// Ensures all observable fields are initialized and linked to this data model.
        /// </summary>
        public void EnsureFieldsInitialized() {
            foreach (var field in GetCachedObservableFields()) {
                var instance = field.GetValue(this);
                if (instance == null) {
                    instance = Activator.CreateInstance(field.FieldType);
                    field.SetValue(this, instance);
                }

                // Set the parent data model reference for cleanup purposes
                if (instance is IObservableVariable observableVar) {
                    observableVar.SetParentDataModel(this);
                }
            }
        }


        /// <summary>
        /// Called when the GameObject is destroyed to clean up all subscriptions.
        /// </summary>
        protected virtual void OnDestroy() {
            // Clean up all tracked subscriptions using the static cleanup method
            Observable.CleanupSubscriptions(this);
        }

        private class ModelLoadDescriptor {
            public readonly PropertyInfo[] Properties;
            public readonly (FieldInfo field, PropertyInfo valueProp, Type destType)[] ObservableFields;
            public readonly FieldInfo[] PlainFields;

            public ModelLoadDescriptor(Type type) {
                var validProps = new List<PropertyInfo>();
                var seenPropNames = new HashSet<string>(StringComparer.Ordinal);

                var obsFields = new List<(FieldInfo, PropertyInfo, Type)>();
                var plainFields = new List<FieldInfo>();
                var seenFieldNames = new HashSet<string>(StringComparer.Ordinal);

                for (var t = type; t != null && t != typeof(BaseObservableDataModel) && t != typeof(MonoBehaviour) && t != typeof(Component) && t != typeof(UnityEngine.Object) && t != typeof(object); t = t.BaseType) {
                    var props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    foreach (var prop in props) {
                        if (!prop.CanWrite || !prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                        if (IsUnityComponentProperty(prop)) continue;
                        if (seenPropNames.Add(prop.Name)) {
                            validProps.Add(prop);
                        }
                    }

                    var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    foreach (var field in fields) {
                        if (field.IsDefined(typeof(NonSavedAttribute), true)) {
                            continue;
                        }

                        if (!seenFieldNames.Add(field.Name)) {
                            continue;
                        }

                        if (Observable.IsSupportedFieldType(field)) {
                            var valueProp = field.FieldType.GetProperty("Value");
                            if (valueProp != null && valueProp.CanRead && valueProp.CanWrite) {
                                obsFields.Add((field, valueProp, valueProp.PropertyType));
                            }
                        } else {
                            plainFields.Add(field);
                        }
                    }
                }

                Properties = validProps.ToArray();
                ObservableFields = obsFields.ToArray();
                PlainFields = plainFields.ToArray();
            }
        }

#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [NoAutoStaticsCleanup]
#endif
        private static readonly Dictionary<Type, ModelLoadDescriptor> _loadDescriptors = new Dictionary<Type, ModelLoadDescriptor>();
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [NoAutoStaticsCleanup]
#endif
        private static readonly object _loadDescriptorsLock = new object();

        private static ModelLoadDescriptor GetLoadDescriptor(Type type) {
            lock (_loadDescriptorsLock) {
                if (!_loadDescriptors.TryGetValue(type, out var descriptor)) {
                    descriptor = new ModelLoadDescriptor(type);
                    _loadDescriptors[type] = descriptor;
                }
                return descriptor;
            }
        }

        private static object CloneIfListOrArray(object value, Type targetType) {
            if (value == null) return null;
            if (value is Array arr) {
                return arr.Clone();
            }
            if (value is System.Collections.IList list) {
                var listType = targetType;
                if (listType.IsGenericType && listType.GetGenericTypeDefinition() == typeof(List<>)) {
                    var itemType = listType.GetGenericArguments()[0];
                    var constructed = typeof(List<>).MakeGenericType(itemType);
                    var clonedList = (System.Collections.IList)Activator.CreateInstance(constructed);
                    for (int i = 0; i < list.Count; i++) {
                        clonedList.Add(list[i]);
                    }
                    return clonedList;
                } else if (value is ICloneable cloneable) {
                    return cloneable.Clone();
                } else {
                    try {
                        var clonedList = (System.Collections.IList)Activator.CreateInstance(value.GetType());
                        for (int i = 0; i < list.Count; i++) {
                            clonedList.Add(list[i]);
                        }
                        return clonedList;
                    } catch {
                        return value;
                    }
                }
            }
            return value;
        }

        /// <summary>
        /// Loads the data from saved model of <see cref="BaseObservableDataModel" /> to the <see cref="ObservableVariable" /> types at current <see cref="BaseObservableDataModel" /> model.
        /// </summary>
        /// <param name="model">The model from save of the type <see cref="BaseObservableDataModel" /></param>
        public void LoadDataFromModel(object model) {
            // Check if model is null
            if (model == null) {
                Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: model is null. Cannot load data into {GetType().Name}.");
                return;
            }

            var descriptor = GetLoadDescriptor(GetType());

            foreach (var prop in descriptor.Properties) {
                try {
                    var modelValue = prop.GetValue(model);
                    modelValue = CloneIfListOrArray(modelValue, prop.PropertyType);
                    prop.SetValue(this, modelValue);
                }
                catch (Exception ex) {
                    Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: Failed to set property '{prop.Name}' on {GetType().Name}. Exception: {ex.Message}");
                }
            }

            foreach (var (field, valueProp, destType) in descriptor.ObservableFields) {
                try {
                    var sourceFieldObj = field.GetValue(model);
                    if (sourceFieldObj == null) {
                        Debug.LogWarning($"[BaseObservableDataModel] LoadDataFromModel: Field '{field.Name}' on {GetType().Name} has null value in source model. Skipping Value copy for this field.");
                        continue;
                    }

                    var thisValue = field.GetValue(this);
                    if (thisValue == null) {
                        Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: Field '{field.Name}' on {GetType().Name} is null. EnsureFieldsInitialized() was not called or field initialization failed. Skipping this field.");
                        continue;
                    }

                    var sourceValue = valueProp.GetValue(sourceFieldObj);
                    var sourceType = sourceValue?.GetType() ?? typeof(object);
                    
                    // Check for type mismatch
                    if (sourceValue != null && !destType.IsAssignableFrom(sourceType)) {
                        Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: Type mismatch for field '{field.Name}'. Destination type: {destType.Name}, Source type: {sourceType.Name}. Skipping this field.");
                        continue;
                    }

                    sourceValue = CloneIfListOrArray(sourceValue, destType);
                    valueProp.SetValue(thisValue, sourceValue);
                }
                catch (Exception ex) {
                    Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: Failed to set field '{field.Name}' on {GetType().Name}. Exception: {ex.Message}");
                }
            }

            foreach (var field in descriptor.PlainFields) {
                try {
                    var modelValue = field.GetValue(model);
                    modelValue = CloneIfListOrArray(modelValue, field.FieldType);
                    field.SetValue(this, modelValue);
                }
                catch (Exception ex) {
                    Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: Failed to set field '{field.Name}' on {GetType().Name}. Exception: {ex.Message}");
                }
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
