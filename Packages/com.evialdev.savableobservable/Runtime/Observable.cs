using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System.Runtime.CompilerServices;
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
using Unity.Scripting.LifecycleManagement;
#endif

namespace SavableObservable {

    public partial class Observable {

        // Storage for per-instance data using ConditionalWeakTable to avoid memory leaks
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [AutoStaticsCleanup]
#endif
        private static ConditionalWeakTable<BaseObservableDataModel, InstanceData> _instanceData =
            new ConditionalWeakTable<BaseObservableDataModel, InstanceData>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstanceData() {
            _instanceData = new ConditionalWeakTable<BaseObservableDataModel, InstanceData>();
        }

        /// <summary>
        /// One subscription of a subscriber on a model: either a handler on an observable
        /// (<see cref="Source"/> + <see cref="Handler"/>) or a two-way UI listener (<see cref="Unbind"/>).
        /// </summary>
        private sealed class SubscriptionRecord {
            public IObservableVariable Source;
            public Delegate Handler;
            /// <summary>True for framework-created bindings (replaced by <c>SetListeners</c>); false for manual subscriptions.</summary>
            public bool Managed;
            /// <summary>Set for UI-listener records, which have no observable source.</summary>
            public Action Unbind;

            public void Remove() {
                try {
                    if (Unbind != null) {
                        Unbind();
                    } else {
                        Source?.RemoveHandler(Handler);
                    }
                } catch (Exception ex) {
                    Debug.LogWarning($"[SavableObservable] Failed to remove a subscription during cleanup: {ex.Message}");
                }
            }
        }

        // Per-model registry: subscriber -> its subscription records. Main-thread only
        // (the tracked actions assert the main thread), so it needs no locking.
        private sealed class InstanceData {
            public readonly Dictionary<object, List<SubscriptionRecord>> Subscriptions = new Dictionary<object, List<SubscriptionRecord>>();
        }

        #region Metadata Caching

        private class SubscriberMetadata {
            public readonly Dictionary<string, List<MethodInfo>> HandlerMethods = new Dictionary<string, List<MethodInfo>>();
            /// <summary>Handler methods whose attribute sets <see cref="ObservableHandlerAttribute.InvokeOnBind"/>.</summary>
            public readonly HashSet<MethodInfo> InvokeOnBindMethods = new HashSet<MethodInfo>();
            public readonly List<(FieldInfo field, string targetName)> AutoBindFields = new List<(FieldInfo, string)>();
            public readonly HashSet<string> AutoBindTargetNames = new HashSet<string>(StringComparer.Ordinal);

            public SubscriberMetadata(Type subscriberType) {
                var seenVirtualBaseDefs = new HashSet<MethodInfo>();

                foreach (var t in TypeUtil.GetUserHierarchy(subscriberType)) {
                    var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                    foreach (var method in methods) {
                        MethodInfo baseDef = null;
                        if (method.IsVirtual) {
                            baseDef = method.GetBaseDefinition();
                            if (seenVirtualBaseDefs.Contains(baseDef)) {
                                continue;
                            }
                        }

                        var attr = method.GetCustomAttribute<ObservableHandlerAttribute>();
                        if (attr == null && method.IsVirtual && baseDef != null) {
                            attr = baseDef.GetCustomAttribute<ObservableHandlerAttribute>();
                        }

                        if (method.IsVirtual && baseDef != null) {
                            seenVirtualBaseDefs.Add(baseDef);
                        }

                        if (attr != null && !string.IsNullOrWhiteSpace(attr.VariableName)) {
                            if (!HandlerMethods.TryGetValue(attr.VariableName, out var list)) {
                                list = new List<MethodInfo>();
                                HandlerMethods[attr.VariableName] = list;
                            }
                            list.Add(method);
                            if (attr.InvokeOnBind) {
                                InvokeOnBindMethods.Add(method);
                            }
                        }
                    }

                    var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    foreach (var field in fields) {
                        var attr = field.GetCustomAttribute<AutoBindAttribute>();
                        if (attr != null) {
                            var targetName = string.IsNullOrWhiteSpace(attr.VariableName) ? field.Name : attr.VariableName;
                            AutoBindFields.Add((field, targetName));
                            if (!string.IsNullOrWhiteSpace(targetName)) {
                                AutoBindTargetNames.Add(targetName);
                            }
                        }
                    }
                }
            }
        }

#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [NoAutoStaticsCleanup]
#endif
        private static readonly Dictionary<Type, SubscriberMetadata> _subscriberMetadataCache = new Dictionary<Type, SubscriberMetadata>();
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [NoAutoStaticsCleanup]
#endif
        private static readonly object _subscriberMetadataLock = new object();

        private static SubscriberMetadata GetSubscriberMetadata(Type type) {
            lock (_subscriberMetadataLock) {
                if (!_subscriberMetadataCache.TryGetValue(type, out var metadata)) {
                    metadata = new SubscriberMetadata(type);
                    _subscriberMetadataCache[type] = metadata;
                }
                return metadata;
            }
        }

        private static ModelDescriptor GetModelMetadata(Type type) => ModelDescriptor.Get(type);

        #endregion

        #region Binding

        /// <summary>
        /// Binds the <c>[ObservableHandler]</c> methods and <c>[AutoBind]</c> fields of <paramref name="subscriber"/>
        /// to the observable fields of <paramref name="model"/>. This is the only bind API; calling it again replaces the
        /// framework-created bindings of that subscriber (no duplicates) and keeps its manual subscriptions
        /// (<c>field.OnValueChanged.Add(handler, subscriber)</c>).
        /// The subscriber and the model must be on the same GameObject (one GameObject = one MMVC unit).
        /// </summary>
        public static void SetListeners(object subscriber, BaseObservableDataModel model) {
            if (!(subscriber is MonoBehaviour monoBehaviour)) {
                Debug.LogWarning($"[SavableObservable] SetListeners called on non-MonoBehaviour object {subscriber?.GetType().Name}. Only MonoBehaviours are supported.");
                return;
            }

            if (model == null) {
                Debug.LogError($"[SavableObservable] SetListeners called with null model on {subscriber.GetType().Name}.", monoBehaviour);
                return;
            }

            if (!ValidateSameGameObject(monoBehaviour, model, nameof(SetListeners))) {
                return;
            }

            ValidateSingleMmvc(monoBehaviour, model);
            SetListenersCore(monoBehaviour, subscriber, model);
        }

        private static void SetListenersCore(MonoBehaviour monoBehaviour, object subscriber, BaseObservableDataModel model) {
            model.EnsureFieldsInitialized();
            if (subscriber is IPresenterBindingState presenterState) {
                presenterState.EnsureInitializedForBinding();
            }

            // Replace framework-created bindings so repeated calls are idempotent,
            // while manual subscriptions on the same subscriber are preserved.
            RemoveRecords(model, subscriber, managedOnly: true);

            var subMetadata = GetSubscriberMetadata(subscriber.GetType());
            var descriptor = GetModelMetadata(model.GetType());

            // Handlers to call once with the current state after binding (SyncOnBind). Null when replay is off.
            var replay = ObservableSettings.SyncOnBind ? new List<(IObservableVariable source, Delegate handler)>() : null;

            foreach (var field in descriptor.ObservableFields) {
                if (subMetadata.HandlerMethods.TryGetValue(field.Name, out var handlerMethods)) {
                    foreach (var handlerMethod in handlerMethods) {
                        SubscribeIndividualHandler(subscriber, handlerMethod, field, model, subMetadata, replay);
                    }
                } else if (ObservableSettings.StrictBinding && !subMetadata.AutoBindTargetNames.Contains(field.Name)) {
                    Debug.LogWarning($"[SavableObservable] ObservableVariable '{field.Name}' in {model.GetType().Name} has no corresponding [ObservableHandler] method or [AutoBind] field in {subscriber.GetType().Name}.", monoBehaviour);
                }
            }

            BindAutoBinds(monoBehaviour, subscriber, model, subMetadata, descriptor, replay);

            if (subscriber is IPresenterBindingState bindingState) {
                bindingState.MarkBound();
            }

            // After MarkBound: a replayed handler that reaches back into the unit (logic.GetModel() -> presenter.EnsureInitialized())
            // finds the presenter bound and does not rebind.
            if (replay != null) {
                ReplayCurrentState(monoBehaviour, replay);
            }
        }

        // Pushes the current model state through the bindings created by one SetListeners call. A replay is not a change:
        // it does not notify other subscribers and does not touch PreviousValue. One failing handler does not stop the others.
        private static void ReplayCurrentState(MonoBehaviour monoBehaviour, List<(IObservableVariable source, Delegate handler)> replay) {
            for (int i = 0; i < replay.Count; i++) {
                try {
                    replay[i].source.InvokeHandler(replay[i].handler);
                } catch (Exception ex) {
                    Debug.LogError($"[SavableObservable] Replay of the current value on bind failed on {monoBehaviour.GetType().Name}: {ex.Message}", monoBehaviour);
                }
            }
        }

        private static void ValidateSingleMmvc(MonoBehaviour mb, BaseObservableDataModel chosenModel = null) {
            var models = mb.GetComponents<BaseObservableDataModel>();
            if (models.Length > 1) {
                var modelTypeNames = string.Join(", ", Array.ConvertAll(models, m => m.GetType().Name));
                var chosenName = chosenModel != null ? chosenModel.GetType().Name : (models.Length > 0 ? models[0].GetType().Name : "none");
                Debug.LogError(
                    $"[SavableObservable] GameObject '{mb.gameObject.name}' has multiple {nameof(BaseObservableDataModel)} components ({modelTypeNames}). " +
                    $"Rule: One GameObject = one MMVC unit (one model, at most one logic, at most one presenter). " +
                    $"Binding to '{chosenName}'.",
                    mb);
            }

            var presenters = mb.GetComponents<IObservablePresenter>();
            if (presenters.Length > 1) {
                var presenterTypeNames = string.Join(", ", Array.ConvertAll(presenters, p => p.GetType().Name));
                Debug.LogError(
                    $"[SavableObservable] GameObject '{mb.gameObject.name}' has multiple {nameof(IObservablePresenter)} components ({presenterTypeNames}). " +
                    $"Rule: One GameObject = one MMVC unit (one model, at most one logic, at most one presenter).",
                    mb);
            }
        }

        private static bool ValidateSameGameObject(MonoBehaviour mb, BaseObservableDataModel model, string api) {
            if (model.gameObject != mb.gameObject) {
                Debug.LogError(
                    $"[SavableObservable] Cross-GameObject binding rejected in {api}: subscriber '{mb.GetType().Name}' is on GameObject '{mb.gameObject.name}', " +
                    $"but model '{model.GetType().Name}' is on GameObject '{model.gameObject.name}'. " +
                    $"Rule: One GameObject = one MMVC unit. To access another unit's model, call Services.Get<TLogic>().GetModel() or Services.GetModel<TModel>() or subscribe to individual events manually.",
                    mb);
                return false;
            }
            return true;
        }

        private static void SubscribeIndividualHandler(
            object subscriber,
            MethodInfo handlerMethod,
            FieldInfo field,
            BaseObservableDataModel dataModel,
            SubscriberMetadata subMetadata,
            List<(IObservableVariable source, Delegate handler)> replay)
        {
            if (!(field.GetValue(dataModel) is IObservableVariable observableVar)) return;

            var handlerParams = handlerMethod.GetParameters();
            if (handlerParams.Length > 2) {
                Debug.LogError($"[SavableObservable] Method '{handlerMethod.Name}' has an invalid number of parameters for [ObservableHandler].", (MonoBehaviour)subscriber);
                return;
            }

            Delegate handler;
            try {
                handler = observableVar.CreateHandler(subscriber, handlerMethod, handlerParams.Length);
            } catch (Exception ex) {
                Debug.LogError($"[SavableObservable] Failed to create delegate for [ObservableHandler] method '{handlerMethod.Name}': {ex.Message}", (MonoBehaviour)subscriber);
                return;
            }

            if (handler == null) {
                Debug.LogError($"[SavableObservable] Method '{handlerMethod.Name}' has an invalid signature for [ObservableHandler].", (MonoBehaviour)subscriber);
                return;
            }

            // The observable records the subscription (managed) in the model's registry.
            observableVar.AddHandler(handler, subscriber);

            if (replay != null && subMetadata.InvokeOnBindMethods.Contains(handlerMethod)) {
                replay.Add((observableVar, handler));
            }
        }

        private static void BindAutoBinds(
            MonoBehaviour monoBehaviour,
            object obj,
            BaseObservableDataModel dataModel,
            SubscriberMetadata subMetadata,
            ModelDescriptor descriptor,
            List<(IObservableVariable source, Delegate handler)> replay)
        {
            if (subMetadata.AutoBindFields.Count == 0) return;

            foreach (var (uiField, targetName) in subMetadata.AutoBindFields) {
                try {
                    if (!descriptor.ObservableFieldsByName.TryGetValue(targetName, out var observableField)) {
                        Debug.LogWarning($"[SavableObservable] [AutoBind] on '{uiField.Name}' could not find ObservableVariable '{targetName}' in {dataModel.GetType().Name}.", monoBehaviour);
                        continue;
                    }

                    var adapter = UIAdapterRegistry.GetAdapter(uiField.FieldType);
                    if (adapter == null) {
                        Debug.LogWarning($"[SavableObservable] No UI adapter found for type {uiField.FieldType.Name}. Register one via UIAdapterRegistry.RegisterAdapter().", monoBehaviour);
                        continue;
                    }

                    SubscribeAutoBindHandler(obj, uiField, observableField, dataModel, adapter, replay);
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
            IUIAdapter adapter,
            List<(IObservableVariable source, Delegate handler)> replay)
        {
            if (!(observableField.GetValue(dataModel) is IObservableVariable observableVar)) return;

            // 1) Subscribe Model -> UI (One-way binding)
            // ----------------------------------------------------------------
            var genericArgs = observableField.FieldType.GetGenericArguments();
            var valueType = genericArgs.Length > 0 ? genericArgs[0] : typeof(object);

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

            var wrappedHandler = observableVar.CreateAutoBindHandler(modelToUiHandler);
            if (wrappedHandler != null) {
                observableVar.AddHandler(wrappedHandler, obj);
                // Model -> UI only: the adapters' SetValue use the control's "without notify" path, so the replay never writes back to the model.
                replay?.Add((observableVar, wrappedHandler));
            }

            // 2) Subscribe UI -> Model (Two-way binding, if supported)
            // ----------------------------------------------------------------
            try {
                var currentUiComponent = uiField.GetValue(obj);
                if (currentUiComponent != null) {
                    if (UIAdapterRegistry.TryGetListenerAdapter(currentUiComponent.GetType(), out var listenerAdapter)) {
                        var valueProp = observableField.FieldType.GetProperty("Value");
                        Action<object> uiToModelHandler = newValue => {
                            try {
                                if (valueProp != null && valueProp.CanWrite) {
                                    valueProp.SetValue(observableVar, newValue);
                                }
                            } catch (Exception ex) {
                                Debug.LogError($"[SavableObservable] Failed to update Observable '{observableField.Name}' from UI: {ex.Message}", obj as MonoBehaviour);
                            }
                        };

                        object token = listenerAdapter.AddListener(currentUiComponent, uiToModelHandler, valueType);

                        if (token != null && currentUiComponent is UnityEngine.Object unityUiComponent) {
                            RegisterUiListener(dataModel, obj, () => {
                                if (unityUiComponent != null) {
                                    listenerAdapter.RemoveListener(unityUiComponent, token);
                                }
                            });
                        }
                    }
                }
            } catch (Exception ex) {
                Debug.LogWarning($"[SavableObservable] Failed to setup two-way binding for '{uiField.Name}': {ex.Message}", obj as MonoBehaviour);
            }
        }

        #endregion

        #region Subscription registry

        /// <summary>
        /// Removes all subscriptions of a specific subscriber from the data model: framework-created bindings
        /// ([ObservableHandler] / [AutoBind]) and manual subscriptions. Other subscribers are not affected.
        /// A presenter is marked unbound, so the next <c>EnsureInitialized()</c> rebinds it.
        /// </summary>
        /// <param name="dataModel">The data model containing the observables</param>
        /// <param name="subscriber">The subscriber object to remove all subscriptions for</param>
        public static void RemoveListeners(object dataModel, object subscriber) {
            if (dataModel is BaseObservableDataModel model) {
                RemoveAllSubscriptions(model, subscriber);
            }
        }

        /// <summary>
        /// Removes all subscriptions for a specific subscriber.
        /// </summary>
        /// <param name="dataModel">The data model instance</param>
        /// <param name="subscriber">The subscriber object to remove all subscriptions for</param>
        public static void RemoveAllSubscriptions(BaseObservableDataModel dataModel, object subscriber) {
            if (dataModel == null || subscriber == null) return;
            RemoveRecords(dataModel, subscriber, managedOnly: false);

            if (subscriber is IPresenterBindingState bindingState) {
                bindingState.MarkUnbound();
            }
        }

        /// <summary>
        /// Cleans up all tracked subscriptions for a data model when it's destroyed.
        /// </summary>
        /// <param name="dataModel">The data model being destroyed</param>
        public static void CleanupSubscriptions(BaseObservableDataModel dataModel) {
            if (dataModel == null) return;
            if (!_instanceData.TryGetValue(dataModel, out var data)) return;

            var all = new List<SubscriptionRecord>();
            foreach (var list in data.Subscriptions.Values) {
                all.AddRange(list);
            }
            data.Subscriptions.Clear();

            for (int i = 0; i < all.Count; i++) {
                all[i].Remove();
            }
        }

        /// <summary>
        /// Records a subscription made through a tracked action of <paramref name="source"/>.
        /// Called by <see cref="ObservableTrackedAction{T}"/>; <paramref name="managed"/> is true for framework-created bindings.
        /// </summary>
        internal static void RegisterSubscription(BaseObservableDataModel dataModel, object subscriber, IObservableVariable source, Delegate handler, bool managed) {
            if (dataModel == null || subscriber == null || source == null || handler == null) return;

            var data = _instanceData.GetOrCreateValue(dataModel);
            if (!data.Subscriptions.TryGetValue(subscriber, out var list)) {
                list = new List<SubscriptionRecord>();
                data.Subscriptions[subscriber] = list;
            }

            for (int i = 0; i < list.Count; i++) {
                if (handler.Equals(list[i].Handler)) return;
            }

            list.Add(new SubscriptionRecord { Source = source, Handler = handler, Managed = managed });
        }

        /// <summary>Drops the record of a handler that was removed from a tracked action.</summary>
        internal static void UnregisterSubscription(BaseObservableDataModel dataModel, object subscriber, Delegate handler) {
            if (dataModel == null || subscriber == null || handler == null) return;
            if (!_instanceData.TryGetValue(dataModel, out var data)) return;
            if (!data.Subscriptions.TryGetValue(subscriber, out var list)) return;

            for (int i = list.Count - 1; i >= 0; i--) {
                if (handler.Equals(list[i].Handler)) {
                    list.RemoveAt(i);
                }
            }

            if (list.Count == 0) {
                data.Subscriptions.Remove(subscriber);
            }
        }

        private static void RegisterUiListener(BaseObservableDataModel dataModel, object subscriber, Action unbind) {
            var data = _instanceData.GetOrCreateValue(dataModel);
            if (!data.Subscriptions.TryGetValue(subscriber, out var list)) {
                list = new List<SubscriptionRecord>();
                data.Subscriptions[subscriber] = list;
            }

            list.Add(new SubscriptionRecord { Managed = true, Unbind = unbind });
        }

        // Removes the subscriber's records (only the framework-created ones when managedOnly) and unsubscribes them.
        // Records are detached from the registry first, so the tracked actions' own bookkeeping callbacks find nothing to mutate.
        private static void RemoveRecords(BaseObservableDataModel dataModel, object subscriber, bool managedOnly) {
            if (dataModel == null || subscriber == null) return;
            if (!_instanceData.TryGetValue(dataModel, out var data)) return;
            if (!data.Subscriptions.TryGetValue(subscriber, out var list)) return;

            var removed = new List<SubscriptionRecord>();
            for (int i = 0; i < list.Count; i++) {
                if (!managedOnly || list[i].Managed) {
                    removed.Add(list[i]);
                }
            }
            if (removed.Count == 0) return;

            list.RemoveAll(r => !managedOnly || r.Managed);
            if (list.Count == 0) {
                data.Subscriptions.Remove(subscriber);
            }

            for (int i = 0; i < removed.Count; i++) {
                removed[i].Remove();
            }
        }

        #endregion

        /// <summary>
        /// Determines whether field type is <see cref="ObservableVariable{T}" /> or <see cref="ObservableList{T}" /> (or a subclass of either).
        /// </summary>
        /// <param name="field">The observable field of the <see cref="BaseObservableDataModel" /> model.</param>
        /// <returns>
        ///   <c>true</c> if the field is a supported observable; otherwise, <c>false</c>.</returns>
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
            if (dataModel == null) return Array.Empty<FieldInfo>();
            return GetModelMetadata(dataModel.GetType()).ObservableFields;
        }
    }
}
