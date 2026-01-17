using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SavableObservable {

    /// <summary>Abstract DataModel class to keep observable keep data with ObservableVariable types</summary>    
    [Serializable]
    public abstract class BaseObservableDataModel : MonoBehaviour {
        // Dictionary to track subscriptions per subscriber for automatic cleanup
        private Dictionary<object, List<Delegate>> _subscriptions = new Dictionary<object, List<Delegate>>();

        // Cached observable fields to avoid repeated reflection calls
        private FieldInfo[] _cachedObservableFields;

        /// <summary>Determines whether field type is <see cref="ObservableVariable" /> field</summary>
        /// <param name="field">The <see cref="ObservableVariable" /> field of the <see cref="BaseObservableDataModel" /> model.</param>
        /// <returns>
        ///   <c>true</c> if filed of type <see cref="ObservableVariable" /> otherwise, <c>false</c>.</returns>
        private static bool IsSupportedFieldType(FieldInfo field) {
            return field.FieldType.IsGenericType &&
                   field.FieldType.GetGenericTypeDefinition() == typeof(ObservableVariable<>);
        }

        /// <summary>
        /// Gets the cached observable fields for this data model.
        /// </summary>
        public FieldInfo[] GetCachedObservableFields() {
            return _cachedObservableFields ??= this.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => Observable.IsSupportedFieldType(f))
                .ToArray();
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
        /// Registers a subscription for automatic cleanup when the GameObject is destroyed.
        /// </summary>
        /// <param name="subscriber">The subscriber object (e.g., Presenter or Logic)</param>
        /// <param name="subscription">The delegate subscription to be cleaned up</param>
        internal void RegisterSubscription(object subscriber, Delegate subscription) {
            if (!_subscriptions.ContainsKey(subscriber)) {
                _subscriptions[subscriber] = new List<Delegate>();
            }
            _subscriptions[subscriber].Add(subscription);
        }

        /// <summary>
        /// Removes a subscription from the tracking list.
        /// </summary>
        /// <param name="subscriber">The subscriber object</param>
        /// <param name="subscription">The delegate subscription to remove</param>
        internal void UnregisterSubscription(object subscriber, Delegate subscription) {
            if (_subscriptions.ContainsKey(subscriber)) {
                _subscriptions[subscriber].Remove(subscription);
                if (_subscriptions[subscriber].Count == 0) {
                    _subscriptions.Remove(subscriber);
                }
            }
        }

        /// <summary>
        /// Removes all subscriptions for a specific subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber object to remove all subscriptions for</param>
        internal void RemoveAllSubscriptions(object subscriber) {
            if (_subscriptions.ContainsKey(subscriber)) {
                _subscriptions.Remove(subscriber);
            }
        }

        /// <summary>
        /// Called when the GameObject is destroyed to clean up all subscriptions.
        /// </summary>
        protected virtual void OnDestroy() {
            // Clean up tracked subscriptions
            foreach (var kvp in _subscriptions) {
                var subscriber = kvp.Key;
                var subscriptions = kvp.Value;
                
                // Clear all subscriptions for each subscriber
                foreach (var subscription in subscriptions) {
                    // Find all ObservableVariable instances in this model and unsubscribe
                    var observableFields = GetCachedObservableFields();
                    foreach (var field in observableFields)
                    {
                        var observableVar = field.GetValue(this);
                        if (observableVar != null)
                        {
                            // Use reflection to get the OnValueChanged ObservableTrackedAction property and remove the handler
                            var onValueChangedProperty = field.FieldType.GetProperty("OnValueChanged");
                            if (onValueChangedProperty != null)
                            {
                                var trackedAction = onValueChangedProperty.GetValue(observableVar);
                                if (trackedAction != null)
                                {
                                    var removeMethod = trackedAction.GetType().GetMethod("Remove");
                                    if (removeMethod != null)
                                    {
                                        try
                                        {
                                            removeMethod.Invoke(trackedAction, new object[] { subscription });
                                        }
                                        catch (System.ArgumentException)
                                        {
                                            // Subscription was not found on this event, continue
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            _subscriptions.Clear();
            
            // For any remaining untracked subscriptions, we can't automatically clean them
            // The user is responsible for cleaning up direct subscriptions to OnValueChanged
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