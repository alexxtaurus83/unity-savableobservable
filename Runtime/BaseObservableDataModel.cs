using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Initializes a new instance of the <see cref="BaseObservableDataModel" /> class.
        /// Automatically creates instances of all fields with ObservableVariable type passing field name as parameter
        /// </summary>
        public void InitFields() {
            foreach (var field in GetCachedObservableFields()) {
                var instance = Activator.CreateInstance(field.FieldType, new object[] { field.Name });
                field.SetValue(this, instance);
                
                // Set the parent data model reference for cleanup purposes
                if (instance is IObservableVariable observableVar)
                {
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

            var type = GetType();
            // Cache fields and properties once
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var props  = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var prop in props) {
                prop.SetValue(this, prop.GetValue(model));
            }   

            foreach (var field in fields) {
                var modelValue = field.GetValue(model);
                if (Observable.IsSupportedFieldType(field)) {
                    var valueProp = field.FieldType.GetProperty("Value");
                    var thisValue = field.GetValue(this);
                    valueProp.SetValue(thisValue, valueProp.GetValue(modelValue));
                }
                else {
                    field.SetValue(this, modelValue);
                }
            }

        }
    }
}