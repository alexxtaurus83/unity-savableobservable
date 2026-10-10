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
    public abstract partial class BaseObservableDataModel : MonoBehaviour {
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
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var props  = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var validProps = new List<PropertyInfo>();
                foreach (var prop in props) {
                    if (!prop.CanWrite || !prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                    if (IsUnityComponentProperty(prop)) continue;
                    validProps.Add(prop);
                }
                Properties = validProps.ToArray();

                var obsFields = new List<(FieldInfo, PropertyInfo, Type)>();
                var plainFields = new List<FieldInfo>();

                foreach (var field in fields) {
                    if (Observable.IsSupportedFieldType(field)) {
                        var valueProp = field.FieldType.GetProperty("Value");
                        if (valueProp != null && valueProp.CanRead && valueProp.CanWrite) {
                            obsFields.Add((field, valueProp, valueProp.PropertyType));
                        }
                    } else {
                        plainFields.Add(field);
                    }
                }

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

                    valueProp.SetValue(thisValue, sourceValue);
                }
                catch (Exception ex) {
                    Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: Failed to set field '{field.Name}' on {GetType().Name}. Exception: {ex.Message}");
                }
            }

            foreach (var field in descriptor.PlainFields) {
                try {
                    var modelValue = field.GetValue(model);
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