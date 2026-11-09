using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SavableObservable {

    /// <summary>Abstract DataModel class to keep observable keep data with ObservableVariable types</summary>    
    [Serializable]
    [DisallowMultipleComponent]
    public abstract partial class BaseObservableDataModel : MonoBehaviour {
        // No execution-order dependency: every reader (presenter, logic, loader, ServiceLocator) calls
        // EnsureFieldsInitialized() lazily through GetModel(), so Awake order between the unit's components does not matter.
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

            // Flag is only ever switched on here, so a runtime opt-in survives repeated initialization.
            foreach (var field in ModelDescriptor.Get(GetType()).NotifyOnSameValueFields) {
                if (field.GetValue(this) is ISameValuePolicy policy) {
                    policy.EnableNotifyOnSameValue();
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

            var descriptor = ModelDescriptor.Get(GetType());

            foreach (var prop in descriptor.LoadProperties) {
                try {
                    var modelValue = prop.GetValue(model);
                    modelValue = CloneIfListOrArray(modelValue, prop.PropertyType);
                    prop.SetValue(this, modelValue);
                }
                catch (Exception ex) {
                    Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: Failed to set property '{prop.Name}' on {GetType().Name}. Exception: {ex.Message}");
                }
            }

            foreach (var (field, valueProp, destType) in descriptor.LoadObservableFields) {
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

            foreach (var field in descriptor.LoadPlainFields) {
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
    }
}
