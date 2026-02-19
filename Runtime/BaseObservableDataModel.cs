using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SavableObservable {

    /// <summary>Abstract DataModel class to keep observable keep data with ObservableVariable types</summary>    
    [Serializable]
    public abstract class BaseObservableDataModel : MonoBehaviour {
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

        /// <summary>
        /// Loads the data from saved model of <see cref="BaseObservableDataModel" /> to the <see cref="ObservableVariable" /> types at current <see cref="BaseObservableDataModel" /> model. Do not change name of the Method as it used in reflection.
        /// </summary>
        /// <param name="model">The model from save of the type <see cref="BaseObservableDataModel" /></param>
        public void LoadDataFromModel(object model) {
            // Check if model is null
            if (model == null) {
                Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: model is null. Cannot load data into {GetType().Name}.");
                return;
            }

            var type = GetType();
            // Cache fields and properties once
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var props  = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var prop in props) {
                // Skip indexers and properties that cannot be written
                if (!prop.CanWrite || prop.GetIndexParameters().Length > 0) continue;
                // Skip Unity Component/Object properties (e.g., transform, gameObject, tag, name)
                if (IsUnityComponentProperty(prop)) continue;

                try {
                    var modelValue = prop.GetValue(model);
                    prop.SetValue(this, modelValue);
                }
                catch (Exception ex) {
                    Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: Failed to set property '{prop.Name}' on {GetType().Name}. Exception: {ex.Message}");
                }
            }

            foreach (var field in fields) {
                try {
                    var modelValue = field.GetValue(model);
                    
                    if (Observable.IsSupportedFieldType(field)) {
                        // Check if thisValue is null
                        var thisValue = field.GetValue(this);
                        if (thisValue == null) {
                            Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: Field '{field.Name}' on {GetType().Name} is null. EnsureFieldsInitialized() was not called or field initialization failed. Skipping this field.");
                            continue;
                        }

                        // Check if modelValue is null
                        if (modelValue == null) {
                            Debug.LogWarning($"[BaseObservableDataModel] LoadDataFromModel: Field '{field.Name}' on {GetType().Name} has null value in source model. Skipping Value copy for this field.");
                            continue;
                        }

                        var valueProp = field.FieldType.GetProperty("Value");
                        
                        // Check if valueProp is null or not readable/writable
                        if (valueProp == null) {
                            Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: 'Value' property not found on field '{field.Name}' of type {field.FieldType.Name}. Skipping this field.");
                            continue;
                        }
                        
                        if (!valueProp.CanRead) {
                            Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: 'Value' property on field '{field.Name}' is not readable. Skipping this field.");
                            continue;
                        }
                        
                        if (!valueProp.CanWrite) {
                            Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: 'Value' property on field '{field.Name}' is not writable. Skipping this field.");
                            continue;
                        }

                        var sourceValue = valueProp.GetValue(modelValue);
                        var destType = valueProp.PropertyType;
                        var sourceType = sourceValue?.GetType() ?? typeof(object);
                        
                        // Check for type mismatch
                        if (sourceValue != null && !destType.IsAssignableFrom(sourceType)) {
                            Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: Type mismatch for field '{field.Name}'. Destination type: {destType.Name}, Source type: {sourceType.Name}. Skipping this field.");
                            continue;
                        }

                        valueProp.SetValue(thisValue, sourceValue);
                    }
                    else {
                        field.SetValue(this, modelValue);
                    }
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