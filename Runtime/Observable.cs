using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System.Runtime.CompilerServices;
using System.Linq.Expressions;

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
            public bool IsInCleanup { get; set; }

            // Fix A: Track UI→Model listener tokens so they can be removed during cleanup.
            // Key: subscriber object (e.g., presenter/Logic), Value: list of (uiComponent, token) pairs.
            // Using WeakReference for uiComponent to avoid preventing GC of destroyed Unity objects.
            public Dictionary<object, List<UiListenerToken>> UiListenerTokens { get; } = new Dictionary<object, List<UiListenerToken>>();
        }

        // Helper struct to store UI listener token information
        // Stored per subscriber to enable deterministic cleanup of UI→Model bindings
        private struct UiListenerToken {
            public UnityEngine.Object UiComponent; // Unity object reference (weak reference not needed for UnityEngine.Object)
            public object Token; // Opaque token returned by IUIAdapter.AddListener()

            public UiListenerToken(UnityEngine.Object uiComponent, object token) {
                UiComponent = uiComponent;
                Token = token;
            }
        }




        public static void SetListeners(object obj) {
            if (!(obj is MonoBehaviour monoBehaviour)) {
                Debug.LogWarning($"[SavableObservable] SetListeners called on non-MonoBehaviour object {obj?.GetType().Name}. Only MonoBehaviours are supported.");
                return;
            }
            var dataModel = monoBehaviour.GetComponent<BaseObservableDataModel>();
            if (dataModel == null) return;

            // Fix B/C: Remove existing subscriptions before adding new ones to ensure idempotent setup.
            // Calling SetListeners() multiple times will not duplicate Model→UI subscriptions.
            RemoveAllSubscriptions(dataModel, obj);

            dataModel.EnsureFieldsInitialized();

            var individualHandlers = new List<MethodInfo>();
            foreach (var method in obj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (method.GetCustomAttribute<ObservableHandlerAttribute>() != null)
                {
                    individualHandlers.Add(method);
                }
            }

            // Get cached observable fields once to avoid repeated reflection calls
            var observableFields = dataModel.GetCachedObservableFields();

            // Look for individual handlers with attributes.
            var individualHandlerMap = new Dictionary<string, MethodInfo>();
            foreach (var handler in individualHandlers)
            {
                var variableName = handler.GetCustomAttribute<ObservableHandlerAttribute>().VariableName;
                individualHandlerMap[variableName] = handler;
            }

            var autoBindTargetNames = new HashSet<string>(StringComparer.Ordinal);
            try {
                var autoBindFields = new List<FieldInfo>();
                foreach (var field in obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.GetCustomAttribute<AutoBindAttribute>() != null)
                    {
                        autoBindFields.Add(field);
                    }
                }

                foreach (var autoBindField in autoBindFields) {
                    var autoBind = autoBindField.GetCustomAttribute<AutoBindAttribute>();
                    var targetName = string.IsNullOrWhiteSpace(autoBind?.VariableName) ? autoBindField.Name : autoBind.VariableName;
                    if (!string.IsNullOrWhiteSpace(targetName)) {
                        autoBindTargetNames.Add(targetName);
                    }
                }
            } catch (Exception ex) {
                Debug.LogError($"[SavableObservable] Failed to scan [AutoBind] fields on {obj.GetType().Name}: {ex.Message}", monoBehaviour);
            }

            foreach (var field in observableFields) {
                if (individualHandlerMap.TryGetValue(field.Name, out var handlerMethod)) {
                    SubscribeIndividualHandler(obj, handlerMethod, field, dataModel);
                } else if (!autoBindTargetNames.Contains(field.Name)) {
                    // Warn only when not handled by either [ObservableHandler] or [AutoBind].
                    Debug.LogWarning($"[SavableObservable] ObservableVariable '{field.Name}' in {dataModel.GetType().Name} has no corresponding [ObservableHandler] method or [AutoBind] field in {obj.GetType().Name}.", (MonoBehaviour)obj);
                }
            }

            SetAutoBindListeners(obj);
        }

        private static void SubscribeIndividualHandler(object obj, MethodInfo handlerMethod, FieldInfo field, BaseObservableDataModel dataModel) {
            var observableVar = field.GetValue(dataModel);
            if (observableVar == null) return;

            // Use reflection to get the OnValueChanged ObservableTrackedAction property
            var onValueChangedProperty = field.FieldType.GetProperty("OnValueChanged");
            if (onValueChangedProperty == null) return;
            var trackedAction = onValueChangedProperty.GetValue(observableVar);
            if (trackedAction == null) return;

            // Create the handler based on the number of parameters
            var handlerParams = handlerMethod.GetParameters();
            var concreteObservableType = field.FieldType; // This is ObservableVariable<T>

            Delegate handler;
            if (handlerParams.Length == 0) {
                // Wrap the zero-parameter method in a delegate that ignores the ObservableVariable<T> argument
                var actionType = typeof(Action<>).MakeGenericType(concreteObservableType);
                // Create a lambda: (ObservableVariable<T> _) => handlerMethod()
                var param = Expression.Parameter(concreteObservableType, "_");
                var callExpression = Expression.Call(Expression.Constant(obj), handlerMethod);
                var lambda = Expression.Lambda(callExpression, param);
                handler = lambda.Compile();
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

        /// <summary>
        /// Sets up automatic UI bindings for fields marked with [AutoBind].
        /// </summary>
        public static void SetAutoBindListeners(object obj) {
            if (!(obj is MonoBehaviour monoBehaviour)) {
                Debug.LogWarning($"[SavableObservable] SetAutoBindListeners called on non-MonoBehaviour object {obj?.GetType().Name}. Only MonoBehaviours are supported.");
                return;
            }

            var dataModel = monoBehaviour.GetComponent<BaseObservableDataModel>();
            if (dataModel == null) return;

            // Note: Idempotent cleanup is performed at the SetListeners() entry point.
            // SetAutoBindListeners() is called from SetListeners() and should not perform
            // its own cleanup to avoid double-removal of handlers added by [ObservableHandler] methods.

            List<FieldInfo> autoBindFields;
            try {
                autoBindFields = new List<FieldInfo>();
                foreach (var field in obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.GetCustomAttribute<AutoBindAttribute>() != null)
                    {
                        autoBindFields.Add(field);
                    }
                }
            } catch (Exception ex) {
                Debug.LogError($"[SavableObservable] Failed to scan [AutoBind] fields on {obj.GetType().Name}: {ex.Message}", monoBehaviour);
                return;
            }

            if (autoBindFields.Count == 0) return;

            Dictionary<string, FieldInfo> observableFields;
            try {
                observableFields = new Dictionary<string, FieldInfo>();
                foreach (var field in GetCachedObservableFields(dataModel))
                {
                    observableFields[field.Name] = field;
                }
            } catch (Exception ex) {
                Debug.LogError($"[SavableObservable] Failed to discover ObservableVariable fields on {dataModel.GetType().Name}: {ex.Message}", monoBehaviour);
                return;
            }

            foreach (var uiField in autoBindFields) {
                try {
                    var attr = uiField.GetCustomAttribute<AutoBindAttribute>();
                    var targetName = string.IsNullOrWhiteSpace(attr?.VariableName) ? uiField.Name : attr.VariableName;

                    if (!observableFields.TryGetValue(targetName, out var observableField)) {
                        Debug.LogWarning($"[SavableObservable] [AutoBind] on '{uiField.Name}' could not find ObservableVariable '{targetName}' in {dataModel.GetType().Name}.", monoBehaviour);
                        continue;
                    }

                    var adapter = UIAdapterRegistry.GetAdapter(uiField.FieldType);
                    if (adapter == null) {
                        Debug.LogWarning($"[SavableObservable] No UI adapter found for type {uiField.FieldType.Name}. Register one via UIAdapterRegistry.RegisterAdapter().", monoBehaviour);
                        continue;
                    }

                    SubscribeAutoBindHandler(obj, uiField, observableField, dataModel, adapter);
                } catch (Exception ex) {
                    Debug.LogError($"[SavableObservable] Failed to wire [AutoBind] for field '{uiField.Name}' on {obj.GetType().Name}: {ex.Message}", monoBehaviour);
                }
            }
        }

        private static void SubscribeAutoBindHandler(
            object obj,
            FieldInfo uiField,
            FieldInfo observableField,
            BaseObservableDataModel dataModel,
            IUIAdapter adapter) {
            var observableVar = observableField.GetValue(dataModel);
            if (observableVar == null) return;

            // 1) Subscribe Model -> UI (One-way binding)
            // ----------------------------------------------------------------
            var onValueChangedProperty = observableField.FieldType.GetProperty("OnValueChanged");
            if (onValueChangedProperty != null) {
                var trackedAction = onValueChangedProperty.GetValue(observableVar);
                if (trackedAction != null) {
                    var genericArgs = observableField.FieldType.GetGenericArguments();
                    if (genericArgs.Length == 1) {
                        var valueType = genericArgs[0];
                        Action<object> modelToUiHandler = value => {
                            try {
                                var currentUiComponent = uiField.GetValue(obj);
                                if (currentUiComponent != null) {
                                    adapter.SetValue(currentUiComponent, value, valueType);
                                }
                            } catch (Exception ex) {
                                Debug.LogError($"[SavableObservable] [AutoBind] runtime update failed for UI field '{uiField.Name}': {ex.Message}", obj as MonoBehaviour);
                            }
                        };

                        var wrappedHandler = CreateWrappedHandler(observableField.FieldType, modelToUiHandler);
                        var addAction = trackedAction.GetType().GetMethod("Add");
                        addAction?.Invoke(trackedAction, new object[] { wrappedHandler, obj });
                    }
                }
            }

            // 2) Subscribe UI -> Model (Two-way binding, if supported)
            // ----------------------------------------------------------------
            try {
                var currentUiComponent = uiField.GetValue(obj);
                if (currentUiComponent != null) {
                    // Register the listener via the adapter (only if it supports listening)
                    var genericArgs = observableField.FieldType.GetGenericArguments();
                    var valueType = genericArgs.Length > 0 ? genericArgs[0] : typeof(object);
                    
                    // Use IUIListenerAdapter for two-way binding (only interactive components support this)
                    if (UIAdapterRegistry.TryGetListenerAdapter(currentUiComponent.GetType(), out var listenerAdapter)) {
                        // Create a callback that updates the ObservableVariable
                        Action<object> uiToModelHandler = (newValue) => {
                            try {
                                // Reflection: observableVar.Value = newValue
                                var valueProp = observableField.FieldType.GetProperty("Value");
                                if (valueProp != null && valueProp.CanWrite) {
                                    valueProp.SetValue(observableVar, newValue);
                                }
                            } catch (Exception ex) {
                                Debug.LogError($"[SavableObservable] Failed to update Observable '{observableField.Name}' from UI: {ex.Message}", obj as MonoBehaviour);
                            }
                        };

                        object token = listenerAdapter.AddListener(currentUiComponent, uiToModelHandler, valueType);

                        // Fix A: Store the UI listener token for later cleanup.
                        // Key by subscriber (obj) so we can remove all UI listeners when cleanup is needed.
                        if (token != null && currentUiComponent is UnityEngine.Object unityUiComponent) {
                            var instanceData = _instanceData.GetOrCreateValue(dataModel);
                            lock (instanceData.Lock) {
                                if (!instanceData.UiListenerTokens.ContainsKey(obj)) {
                                    instanceData.UiListenerTokens[obj] = new List<UiListenerToken>();
                                }
                                instanceData.UiListenerTokens[obj].Add(new UiListenerToken(unityUiComponent, token));
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                Debug.LogWarning($"[SavableObservable] Failed to setup two-way binding for '{uiField.Name}': {ex.Message}", obj as MonoBehaviour);
            }
        }

        private static Delegate CreateWrappedHandler(Type observableType, Action<object> handler) {
            var valueProperty = observableType.GetProperty("Value");
            if (valueProperty == null) {
                throw new InvalidOperationException($"Type {observableType.Name} does not expose a Value property.");
            }

            var param = Expression.Parameter(observableType, "obs");
            var valueExpression = Expression.Property(param, valueProperty);
            var boxedValue = Expression.Convert(valueExpression, typeof(object));
            var handlerConstant = Expression.Constant(handler);
            var invokeMethod = typeof(Action<object>).GetMethod(nameof(Action<object>.Invoke));
            var callHandler = Expression.Call(handlerConstant, invokeMethod, boxedValue);
            var lambda = Expression.Lambda(callHandler, param);
            return lambda.Compile();
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
            if (!field.FieldType.IsGenericType) return false;

            var genericDef = field.FieldType.GetGenericTypeDefinition();
            if (genericDef == typeof(ObservableVariable<>) || genericDef == typeof(ObservableList<>)) {
                return true;
            }

            // Allow subclasses of ObservableVariable<> / ObservableList<>
            var baseType = field.FieldType.BaseType;
            while (baseType != null) {
                if (baseType.IsGenericType) {
                    var baseGenericDef = baseType.GetGenericTypeDefinition();
                    if (baseGenericDef == typeof(ObservableVariable<>) || baseGenericDef == typeof(ObservableList<>)) {
                        return true;
                    }
                }

                baseType = baseType.BaseType;
            }

            return false;
        }

        /// <summary>
        /// Gets the cached observable fields for a data model instance.
        /// </summary>
        public static FieldInfo[] GetCachedObservableFields(BaseObservableDataModel dataModel) {
            var instanceData = _instanceData.GetOrCreateValue(dataModel);
            lock (instanceData.Lock) {
                if (instanceData.CachedObservableFields == null) {
                    var supportedFields = new List<FieldInfo>();
                    foreach (var field in dataModel.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (IsSupportedFieldType(field))
                        {
                            supportedFields.Add(field);
                        }
                    }
                    instanceData.CachedObservableFields = supportedFields.ToArray();
                }

                return instanceData.CachedObservableFields;
            }
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
                if (subscriber != null) {
                    if (instanceData.Subscriptions.ContainsKey(subscriber)) {
                        instanceData.Subscriptions[subscriber].Remove(subscription);
                        if (instanceData.Subscriptions[subscriber].Count == 0) {
                            instanceData.Subscriptions.Remove(subscriber);
                        }
                    }
                    return;
                }

                // During model destruction cleanup, tracked actions call Remove(), which calls UnregisterSubscription.
                // Skip mutation of the tracking dictionary in this phase because CleanupSubscriptions() will clear it at the end.
                if (instanceData.IsInCleanup) {
                    return;
                }

                // If subscriber is unknown, remove this subscription from any subscriber list that contains it.
                var subscribersToRemove = new List<object>();
                foreach (var kvp in instanceData.Subscriptions) {
                    kvp.Value.Remove(subscription);
                    if (kvp.Value.Count == 0) {
                        subscribersToRemove.Add(kvp.Key);
                    }
                }

                foreach (var emptySubscriber in subscribersToRemove) {
                    instanceData.Subscriptions.Remove(emptySubscriber);
                }
            }
        }

        /// <summary>
        /// Internal method to remove subscriptions for a specific subscriber from all observable variables
        /// </summary>
        private static void RemoveSubscriptionsForSubscriber(BaseObservableDataModel dataModel, object subscriber) {
            var instanceData = _instanceData.GetOrCreateValue(dataModel);
            lock (instanceData.Lock) {
                if (instanceData.Subscriptions.ContainsKey(subscriber)) {
                    var subscriptions = new List<Delegate>(instanceData.Subscriptions[subscriber]);
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
                RemoveSubscriptionsForSubscriber(dataModel, subscriber);
                instanceData.Subscriptions.Remove(subscriber);

                // Fix A: Also remove UI→Model listener tokens for this subscriber.
                RemoveUiListenersForSubscriber(dataModel, subscriber, instanceData);
            }
        }

        /// <summary>
        /// Cleans up all tracked subscriptions for a data model when it's destroyed.
        /// </summary>
        /// <param name="dataModel">The data model being destroyed</param>
        public static void CleanupSubscriptions(BaseObservableDataModel dataModel) {
            var instanceData = _instanceData.GetOrCreateValue(dataModel);
            lock (instanceData.Lock) {
                instanceData.IsInCleanup = true;
                try {
                    // Snapshot subscriptions to avoid collection mutation while trackedAction.Remove()
                    // triggers UnregisterSubscription internally.
                    var subscriptionsSnapshot = new List<Delegate>();
                    foreach (var subscriptionList in instanceData.Subscriptions.Values)
                    {
                        foreach (var subscription in subscriptionList)
                        {
                            subscriptionsSnapshot.Add(subscription);
                        }
                    }

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
                                        foreach (var subscription in subscriptionsSnapshot) {
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
                    instanceData.Subscriptions.Clear();

                    // Fix A: Clean up all UI→Model listener tokens for all subscribers.
                    foreach (var kvp in instanceData.UiListenerTokens) {
                        RemoveUiListenersForSubscriber(dataModel, kvp.Key, instanceData);
                    }
                    instanceData.UiListenerTokens.Clear();
                } finally {
                    instanceData.IsInCleanup = false;
                }
            }
        }

        /// <summary>
        /// Helper method to remove UI listener tokens for a specific subscriber.
        /// Called by RemoveAllSubscriptions and CleanupSubscriptions.
        /// </summary>
        /// <param name="dataModel">The data model instance</param>
        /// <param name="subscriber">The subscriber object</param>
        /// <param name="instanceData">The instance data (already locked)</param>
        private static void RemoveUiListenersForSubscriber(BaseObservableDataModel dataModel, object subscriber, InstanceData instanceData) {
            if (!instanceData.UiListenerTokens.ContainsKey(subscriber)) {
                return;
            }

            var tokens = instanceData.UiListenerTokens[subscriber];
            foreach (var uiToken in tokens) {
                // Skip if UI component was destroyed (Unity object null check handles destroyed objects gracefully)
                if (uiToken.UiComponent == null) {
                    continue;
                }

                // Get the listener adapter for this UI component type
                if (uiToken.Token != null && UIAdapterRegistry.TryGetListenerAdapter(uiToken.UiComponent.GetType(), out var listenerAdapter)) {
                    try {
                        listenerAdapter.RemoveListener(uiToken.UiComponent, uiToken.Token);
                    } catch (Exception ex) {
                        Debug.LogWarning($"[SavableObservable] Failed to remove UI listener token during cleanup: {ex.Message}");
                    }
                }
            }
            tokens.Clear();
        }
    }
}