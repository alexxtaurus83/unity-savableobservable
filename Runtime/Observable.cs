using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Linq.Expressions;
#if UNITY_EDITOR
using UnityEditor;
using System.Text.RegularExpressions;
using System.Collections;
#endif

namespace SavableObservable {

    public class Observable {

        // Storage for per-instance data using ConditionalWeakTable to avoid memory leaks
        private static readonly ConditionalWeakTable<BaseObservableDataModel, InstanceData> _instanceData =
            new ConditionalWeakTable<BaseObservableDataModel, InstanceData>();

        // Helper class to hold per-instance data
        private class InstanceData {
            public Dictionary<object, List<Delegate>> Subscriptions { get; } = new Dictionary<object, List<Delegate>>();
            public FieldInfo[] CachedObservableFields { get; set; }
            public readonly object Lock = new object(); // For thread safety
        }

        private static readonly HashSet<object> _initializedListeners = new HashSet<object>();

        /// <summary>
        /// Checks if SetListeners has been called for a specific object (typically a Presenter or Logic).
        /// </summary>
        public static bool AreListenersInitialized(object obj) => _initializedListeners.Contains(obj);



        public static void SetListeners(object obj) {
            _initializedListeners.Add(obj);
            var dataModel = ((MonoBehaviour)obj).GetComponent<BaseObservableDataModel>();
            if (dataModel == null) return;

            var initMethod = dataModel.GetType().GetMethod("InitFields");
            initMethod?.Invoke(dataModel, null);

            var universalHandler = obj.GetType().GetMethod("OnModelValueChanged", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            
            bool isUniversalHandlerOverridden = universalHandler != null && universalHandler.DeclaringType == obj.GetType();

            var individualHandlers = obj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Where(m => m.GetCustomAttribute<ObservableHandlerAttribute>() != null).ToList();

            // Warn if a universal handler and attribute-based handlers are used together.
            if (isUniversalHandlerOverridden && individualHandlers.Any()) {
                Debug.LogError($"[SavableObservable] Presenter '{obj.GetType().Name}' uses both a universal 'OnModelValueChanged' handler and specific [ObservableHandler] attributes. This is not supported. The universal handler will be used, and attribute-based handlers will be ignored.", (MonoBehaviour)obj);
            }

            // Get cached observable fields once to avoid repeated reflection calls
            var observableFields = dataModel.GetCachedObservableFields();

            // If a universal handler is overridden, subscribe all variables to it and stop processing.
            if (isUniversalHandlerOverridden) {
                foreach (var field in observableFields) {
                    SubscribeUniversalHandler(obj, universalHandler, field, dataModel);
                }
                return;
            }

            // If no universal handler, look for individual handlers with attributes.
            var individualHandlerMap = individualHandlers.ToDictionary(x => x.GetCustomAttribute<ObservableHandlerAttribute>().VariableName, x => x);

            foreach (var field in observableFields) {
                if (individualHandlerMap.TryGetValue(field.Name, out var handlerMethod)) {
                    SubscribeIndividualHandler(obj, handlerMethod, field, dataModel);
                } else {
                    // Warn if an observable variable has no corresponding handler.
                    Debug.LogWarning($"[SavableObservable] ObservableVariable '{field.Name}' in {dataModel.GetType().Name} has no corresponding [ObservableHandler] method in {obj.GetType().Name}.", (MonoBehaviour)obj);
                }
            }
        }

        private static void SubscribeUniversalHandler(object obj, MethodInfo universalHandler, FieldInfo field, BaseObservableDataModel dataModel) {
            var observableVar = field.GetValue(dataModel);

            // Use reflection to get the OnValueChanged ObservableTrackedAction property and add the handler
            var onValueChangedProperty = field.FieldType.GetProperty("OnValueChanged");
            var trackedAction = onValueChangedProperty.GetValue(observableVar);

            if (trackedAction != null) {
                // Set the parent data model for the tracked action
                var parentDataModelProperty = trackedAction.GetType().GetProperty("ParentDataModel");
                parentDataModelProperty?.SetValue(trackedAction, dataModel);

                // Create the handler delegate
                var handler = Delegate.CreateDelegate(
                    typeof(Action<>).MakeGenericType(field.FieldType),
                    obj,
                    universalHandler
                );

                // Add the handler to the tracked action
                var addAction = trackedAction.GetType().GetMethod("Add");
                addAction.Invoke(trackedAction, new object[] { handler, obj });
            }
        }

        private static void SubscribeIndividualHandler(object obj, MethodInfo handlerMethod, FieldInfo field, BaseObservableDataModel dataModel) {
            var observableVar = field.GetValue(dataModel);
            if (observableVar == null) return;

            // Use reflection to get the OnValueChanged ObservableTrackedAction property
            var onValueChangedProperty = field.FieldType.GetProperty("OnValueChanged");
            var trackedAction = onValueChangedProperty.GetValue(observableVar);

            if (trackedAction != null) {
                // Set the parent data model for the tracked action
                var parentDataModelProperty = trackedAction.GetType().GetProperty("ParentDataModel");
                parentDataModelProperty?.SetValue(trackedAction, dataModel);

                // Create the handler based on the number of parameters
                var handlerParams = handlerMethod.GetParameters();
                var concreteObservableType = field.FieldType; // This is ObservableVariable<T>

                Delegate handler;
                if (handlerParams.Length == 0) {
                    var actionType = typeof(Action<>).MakeGenericType(concreteObservableType);
                    handler = Delegate.CreateDelegate(actionType, obj, handlerMethod);
                } else if (handlerParams.Length == 1) {
                    // Create a lambda that extracts the Value property
                    var param = Expression.Parameter(concreteObservableType, "var");
                    var valueProperty = Expression.Property(param, "Value");
                    var callExpression = Expression.Call(Expression.Constant(obj), handlerMethod, valueProperty);
                    var lambda = Expression.Lambda(callExpression, param);
                    handler = lambda.Compile();
                } else if (handlerParams.Length == 2) {
                    // Create a lambda that extracts both Value and PreviousValue properties
                    var param = Expression.Parameter(concreteObservableType, "var");
                    var valueProperty = Expression.Property(param, "Value");
                    var prevValueProperty = Expression.Property(param, "PreviousValue");
                    var callExpression = Expression.Call(Expression.Constant(obj), handlerMethod, valueProperty, prevValueProperty);
                    var lambda = Expression.Lambda(callExpression, param);
                    handler = lambda.Compile();
                } else {
                    Debug.LogError($"[SavableObservable] Method '{handlerMethod.Name}' has an invalid number of parameters for [ObservableHandler].", (MonoBehaviour)obj);
                    return;
                }

                // Add the handler to the tracked action
                var addAction = trackedAction.GetType().GetMethod("Add");
                addAction.Invoke(trackedAction, new object[] { handler, obj });
            }
        }

        /// <summary>
        /// Removes all subscriptions for a specific subscriber from the data model.
        /// </summary>
        /// <param name="dataModel">The data model containing the observables</param>
        /// <param name="subscriber">The subscriber object to remove all subscriptions for</param>
        public static void RemoveListeners(object dataModel, object subscriber) {
            if (dataModel is BaseObservableDataModel model) {
                RemoveAllSubscriptions(model, subscriber);
            }
        }

        /// <summary>
        /// Determines whether field type is <see cref="ObservableVariable" /> field
        /// </summary>
        /// <param name="field">The <see cref="ObservableVariable" /> field of the <see cref="BaseObservableDataModel" /> model.</param>
        /// <returns>
        ///   <c>true</c> if filed of type <see cref="ObservableVariable" /> otherwise, <c>false</c>.</returns>
        internal static bool IsSupportedFieldType(FieldInfo field) {
            return field.FieldType.IsGenericType &&
                   field.FieldType.GetGenericTypeDefinition() == typeof(ObservableVariable<>);
        }

        /// <summary>
        /// Gets the cached observable fields for a data model instance.
        /// </summary>
        public static FieldInfo[] GetCachedObservableFields(BaseObservableDataModel dataModel) {
            var instanceData = _instanceData.GetOrCreateValue(dataModel);
            if (instanceData.CachedObservableFields == null) {
                instanceData.CachedObservableFields = dataModel.GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(f => IsSupportedFieldType(f))
                    .ToArray();
            }
            return instanceData.CachedObservableFields;
        }

        /// <summary>
        /// Registers a subscription for automatic cleanup when the GameObject is destroyed.
        /// </summary>
        /// <param name="dataModel">The data model instance</param>
        /// <param name="subscriber">The subscriber object (e.g., Presenter or Logic)</param>
        /// <param name="subscription">The delegate subscription to be cleaned up</param>
        public static void RegisterSubscription(BaseObservableDataModel dataModel, object subscriber, Delegate subscription) {
            var instanceData = _instanceData.GetOrCreateValue(dataModel);
            lock (instanceData.Lock) {
                if (!instanceData.Subscriptions.ContainsKey(subscriber)) {
                    instanceData.Subscriptions[subscriber] = new List<Delegate>();
                }
                instanceData.Subscriptions[subscriber].Add(subscription);
            }
        }

        /// <summary>
        /// Removes a subscription from the tracking list.
        /// </summary>
        /// <param name="dataModel">The data model instance</param>
        /// <param name="subscriber">The subscriber object</param>
        /// <param name="subscription">The delegate subscription to remove</param>
        public static void UnregisterSubscription(BaseObservableDataModel dataModel, object subscriber, Delegate subscription) {
            var instanceData = _instanceData.GetOrCreateValue(dataModel);
            lock (instanceData.Lock) {
                if (instanceData.Subscriptions.ContainsKey(subscriber)) {
                    instanceData.Subscriptions[subscriber].Remove(subscription);
                    if (instanceData.Subscriptions[subscriber].Count == 0) {
                        instanceData.Subscriptions.Remove(subscriber);
                    }
                }
            }
        }

        /// <summary>
        /// Removes all subscriptions for a specific subscriber.
        /// </summary>
        /// <param name="dataModel">The data model instance</param>
        /// <param name="subscriber">The subscriber object to remove all subscriptions for</param>
        public static void RemoveAllSubscriptions(BaseObservableDataModel dataModel, object subscriber) {
            var instanceData = _instanceData.GetOrCreateValue(dataModel);
            lock (instanceData.Lock) {
                if (instanceData.Subscriptions.ContainsKey(subscriber)) {
                    var subscriptions = instanceData.Subscriptions[subscriber];
                    // Actually unsubscribe from each observable variable
                    var observableFields = GetCachedObservableFields(dataModel);
                    foreach (var field in observableFields) {
                        var observableVar = field.GetValue(dataModel);
                        if (observableVar != null) {
                            // Use reflection to get the OnValueChanged ObservableTrackedAction property and remove the handler
                            var onValueChangedProperty = field.FieldType.GetProperty("OnValueChanged");
                            if (onValueChangedProperty != null) {
                                var trackedAction = onValueChangedProperty.GetValue(observableVar);
                                if (trackedAction != null) {
                                    var removeMethod = trackedAction.GetType().GetMethod("Remove");
                                    if (removeMethod != null) {
                                        foreach (var subscription in subscriptions) {
                                            try {
                                                removeMethod.Invoke(trackedAction, new object[] { subscription });
                                            } catch (System.ArgumentException) {
                                                // Subscription was not found on this event, continue
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    instanceData.Subscriptions.Remove(subscriber);
                }
            }
        }

        /// <summary>
        /// Cleans up all tracked subscriptions for a data model when it's destroyed.
        /// </summary>
        /// <param name="dataModel">The data model being destroyed</param>
        public static void CleanupSubscriptions(BaseObservableDataModel dataModel) {
            var instanceData = _instanceData.GetOrCreateValue(dataModel);
            lock (instanceData.Lock) {
                // Clean up tracked subscriptions by iterating through each observable variable once
                var observableFields = GetCachedObservableFields(dataModel);
                foreach (var field in observableFields) {
                    var observableVar = field.GetValue(dataModel);
                    if (observableVar != null) {
                        // Use reflection to get the OnValueChanged ObservableTrackedAction property and remove all handlers
                        var onValueChangedProperty = field.FieldType.GetProperty("OnValueChanged");
                        if (onValueChangedProperty != null) {
                            var trackedAction = onValueChangedProperty.GetValue(observableVar);
                            if (trackedAction != null) {
                                var removeMethod = trackedAction.GetType().GetMethod("Remove");
                                if (removeMethod != null) {
                                    // Remove all subscriptions for this observable variable
                                    foreach (var kvp in instanceData.Subscriptions) {
                                        var subscriptions = kvp.Value;
                                        foreach (var subscription in subscriptions) {
                                            try {
                                                removeMethod.Invoke(trackedAction, new object[] { subscription });
                                            } catch (System.ArgumentException) {
                                                // Subscription was not found on this event, continue
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                instanceData.Subscriptions.Clear();
            }
        }
    }
}