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

#if UNITY_EDITOR
        protected virtual void OnValidate() {
            EnsureFieldsInitialized();
            foreach (var field in GetCachedObservableFields()) {
                if (field.GetValue(this) is IObservableVariable observableVar) {
                    observableVar.OnValidate();
                }
            }
        }
#endif

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

        private static bool TryGetMemberValue(object source, Type sourceType, string name, Type expectedType, out object value) {
            value = null;
            if (source == null || sourceType == null) return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            var prop = sourceType.GetProperty(name, flags);
            if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0) {
                try {
                    var raw = prop.GetValue(source);
                    if (raw == null || expectedType.IsAssignableFrom(raw.GetType())) {
                        value = raw;
                        return true;
                    }
                } catch { }
            }

            var field = sourceType.GetField(name, flags);
            if (field != null) {
                try {
                    var raw = field.GetValue(source);
                    if (raw == null || expectedType.IsAssignableFrom(raw.GetType())) {
                        value = raw;
                        return true;
                    }
                } catch { }
            }

            if (name.StartsWith("_") && name.Length > 1) {
                var stripped = name.Substring(1);
                return TryGetMemberValue(source, sourceType, stripped, expectedType, out value);
            }

            return false;
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
            bool isDirectModel = GetType().IsAssignableFrom(model.GetType());

            foreach (var prop in descriptor.LoadProperties) {
                try {
                    object modelValue = null;
                    if (isDirectModel) {
                        modelValue = prop.GetValue(model);
                    } else if (TryGetMemberValue(model, model.GetType(), prop.Name, prop.PropertyType, out var val)) {
                        modelValue = val;
                    } else {
                        continue;
                    }

                    modelValue = CloneIfListOrArray(modelValue, prop.PropertyType);
                    prop.SetValue(this, modelValue);
                }
                catch (Exception ex) {
                    Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: Failed to set property '{prop.Name}' on {GetType().Name}. Exception: {ex.Message}");
                }
            }

            foreach (var (field, valueProp, destType) in descriptor.LoadObservableFields) {
                try {
                    var thisValue = field.GetValue(this);
                    if (thisValue == null) {
                        Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: Field '{field.Name}' on {GetType().Name} is null. EnsureFieldsInitialized() was not called or field initialization failed. Skipping this field.");
                        continue;
                    }

                    object sourceValue = null;
                    if (isDirectModel) {
                        var sourceFieldObj = field.GetValue(model);
                        if (sourceFieldObj == null) {
                            Debug.LogWarning($"[BaseObservableDataModel] LoadDataFromModel: Field '{field.Name}' on {GetType().Name} has null value in source model. Skipping Value copy for this field.");
                            continue;
                        }
                        sourceValue = valueProp.GetValue(sourceFieldObj);
                    } else {
                        var sourceType = model.GetType();
                        if (TryGetMemberValue(model, sourceType, field.Name, typeof(object), out var memberObj) && memberObj != null) {
                            var srcValProp = memberObj.GetType().GetProperty("Value");
                            if (srcValProp != null && srcValProp.CanRead) {
                                sourceValue = srcValProp.GetValue(memberObj);
                            } else if (destType.IsAssignableFrom(memberObj.GetType())) {
                                sourceValue = memberObj;
                            } else {
                                continue;
                            }
                        } else if (TryGetMemberValue(model, sourceType, field.Name, destType, out var rawVal)) {
                            sourceValue = rawVal;
                        } else {
                            continue;
                        }
                    }

                    var sourceTypeActual = sourceValue?.GetType() ?? typeof(object);
                    if (sourceValue != null && !destType.IsAssignableFrom(sourceTypeActual)) {
                        Debug.LogError($"[BaseObservableDataModel] LoadDataFromModel: Type mismatch for field '{field.Name}'. Destination type: {destType.Name}, Source type: {sourceTypeActual.Name}. Skipping this field.");
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
                    object modelValue = null;
                    if (isDirectModel) {
                        modelValue = field.GetValue(model);
                    } else if (TryGetMemberValue(model, model.GetType(), field.Name, field.FieldType, out var val)) {
                        modelValue = val;
                    } else {
                        continue;
                    }

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
