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

        // Helper class to hold per-instance data
        private class InstanceData {
            public Dictionary<object, List<Delegate>> Subscriptions { get; } = new Dictionary<object, List<Delegate>>();
            public Dictionary<object, List<Action>> UnbindActions { get; } = new Dictionary<object, List<Action>>();
            public FieldInfo[] CachedObservableFields { get; set; }
            public readonly object Lock = new object(); // For thread safety
            public bool IsInCleanup { get; set; }

            // Track UI→Model listener tokens so they can be removed during cleanup.
            // Key: subscriber object (e.g., presenter/Logic), Value: list of (uiComponent, token) pairs.
            public Dictionary<object, List<UiListenerToken>> UiListenerTokens { get; } = new Dictionary<object, List<UiListenerToken>>();
        }

        // Helper struct to store UI listener token information
        // Stored per subscriber to enable deterministic cleanup of UI→Model bindings
        private struct UiListenerToken {
            public UnityEngine.Object UiComponent; // Unity object reference
            public object Token; // Opaque token returned by IUIAdapter.AddListener()

            public UiListenerToken(UnityEngine.Object uiComponent, object token) {
                UiComponent = uiComponent;
                Token = token;
            }
        }

        #region Metadata Caching

        private class SubscriberMetadata {
            public readonly Dictionary<string, List<MethodInfo>> HandlerMethods = new Dictionary<string, List<MethodInfo>>();
            public readonly List<(FieldInfo field, string targetName)> AutoBindFields = new List<(FieldInfo, string)>();
            public readonly HashSet<string> AutoBindTargetNames = new HashSet<string>(StringComparer.Ordinal);

            public SubscriberMetadata(Type subscriberType) {
                foreach (var method in subscriberType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)) {
                    var attr = method.GetCustomAttribute<ObservableHandlerAttribute>();
                    if (attr != null && !string.IsNullOrWhiteSpace(attr.VariableName)) {
                        if (!HandlerMethods.TryGetValue(attr.VariableName, out var list)) {
                            list = new List<MethodInfo>();
                            HandlerMethods[attr.VariableName] = list;
                        }
                        list.Add(method);
                    }
                }

                foreach (var field in subscriberType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
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

        private class ModelMetadata {
            public readonly FieldInfo[] ObservableFields;
            public readonly Dictionary<string, FieldInfo> ObservableFieldsByName;

            public ModelMetadata(Type modelType) {
                var supported = new List<FieldInfo>();
                var byName = new Dictionary<string, FieldInfo>();
                foreach (var field in modelType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                    if (IsSupportedFieldType(field)) {
                        supported.Add(field);
                        byName[field.Name] = field;
                    }
                }
                ObservableFields = supported.ToArray();
                ObservableFieldsByName = byName;
            }
        }

#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [NoAutoStaticsCleanup]
#endif
        private static readonly Dictionary<Type, ModelMetadata> _modelMetadataCache = new Dictionary<Type, ModelMetadata>();
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [NoAutoStaticsCleanup]
#endif
        private static readonly object _modelMetadataLock = new object();

        private static ModelMetadata GetModelMetadata(Type type) {
            lock (_modelMetadataLock) {
                if (!_modelMetadataCache.TryGetValue(type, out var metadata)) {
                    metadata = new ModelMetadata(type);
                    _modelMetadataCache[type] = metadata;
                }
                return metadata;
            }
        }

        #endregion

        #region Binding Bridges (AOT/IL2CPP compatible)

        private interface IObservableBinding {
            Delegate CreateHandler(object target, MethodInfo method, int paramCount);
            Delegate CreateAutoBindHandler(Action<object> modelToUiHandler);
            void AddHandler(object observableVar, Delegate handler, object subscriber);
            void RemoveHandler(object observableVar, Delegate handler);
        }

        private partial class ObservableVariableBinding<T> : IObservableBinding {
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
            [NoAutoStaticsCleanup]
#endif
            public static readonly ObservableVariableBinding<T> Instance = new ObservableVariableBinding<T>();

            public Delegate CreateHandler(object target, MethodInfo method, int paramCount) {
                switch (paramCount) {
                    case 0: {
                        var act = (Action)Delegate.CreateDelegate(typeof(Action), target, method);
                        Action<ObservableVariable<T>> handler = _ => act();
                        return handler;
                    }
                    case 1: {
                        try {
                            var act = (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), target, method);
                            Action<ObservableVariable<T>> handler = v => act(v.Value);
                            return handler;
                        } catch {
                            Action<ObservableVariable<T>> handler = v => method.Invoke(target, new object[] { v.Value });
                            return handler;
                        }
                    }
                    case 2: {
                        try {
                            var act = (Action<T, T>)Delegate.CreateDelegate(typeof(Action<T, T>), target, method);
                            Action<ObservableVariable<T>> handler = v => act(v.Value, v.PreviousValue);
                            return handler;
                        } catch {
                            Action<ObservableVariable<T>> handler = v => method.Invoke(target, new object[] { v.Value, v.PreviousValue });
                            return handler;
                        }
                    }
                    default:
                        return null;
                }
            }

            public Delegate CreateAutoBindHandler(Action<object> modelToUiHandler) {
                Action<ObservableVariable<T>> handler = v => modelToUiHandler(v.Value);
                return handler;
            }

            public void AddHandler(object observableVar, Delegate handler, object subscriber) {
                ((ObservableVariable<T>)observableVar).OnValueChanged.Add((Action<ObservableVariable<T>>)handler, subscriber);
            }

            public void RemoveHandler(object observableVar, Delegate handler) {
                ((ObservableVariable<T>)observableVar).OnValueChanged.Remove((Action<ObservableVariable<T>>)handler);
            }
        }

        private partial class ObservableListBinding<T> : IObservableBinding {
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
            [NoAutoStaticsCleanup]
#endif
            public static readonly ObservableListBinding<T> Instance = new ObservableListBinding<T>();

            public Delegate CreateHandler(object target, MethodInfo method, int paramCount) {
                switch (paramCount) {
                    case 0: {
                        var act = (Action)Delegate.CreateDelegate(typeof(Action), target, method);
                        Action<ObservableList<T>> handler = _ => act();
                        return handler;
                    }
                    case 1: {
                        var paramType = method.GetParameters()[0].ParameterType;
                        if (paramType.IsAssignableFrom(typeof(ObservableList<T>))) {
                            try {
                                var act = (Action<ObservableList<T>>)Delegate.CreateDelegate(typeof(Action<ObservableList<T>>), target, method);
                                Action<ObservableList<T>> handler = v => act(v);
                                return handler;
                            } catch {
                                Action<ObservableList<T>> handler = v => method.Invoke(target, new object[] { v });
                                return handler;
                            }
                        }

                        try {
                            var act = (Action<List<T>>)Delegate.CreateDelegate(typeof(Action<List<T>>), target, method);
                            Action<ObservableList<T>> handler = v => act(v.Value);
                            return handler;
                        } catch {
                            Action<ObservableList<T>> handler = v => method.Invoke(target, new object[] { v.Value });
                            return handler;
                        }
                    }
                    case 2: {
                        try {
                            var act = (Action<List<T>, IReadOnlyList<T>>)Delegate.CreateDelegate(typeof(Action<List<T>, IReadOnlyList<T>>), target, method);
                            Action<ObservableList<T>> handler = v => act(v.Value, v.PreviousValue);
                            return handler;
                        } catch {
                            Action<ObservableList<T>> handler = v => method.Invoke(target, new object[] { v.Value, v.PreviousValue });
                            return handler;
                        }
                    }
                    default:
                        return null;
                }
            }

            public Delegate CreateAutoBindHandler(Action<object> modelToUiHandler) {
                Action<ObservableList<T>> handler = v => modelToUiHandler(v.Value);
                return handler;
            }

            public void AddHandler(object observableVar, Delegate handler, object subscriber) {
                ((ObservableList<T>)observableVar).OnValueChanged.Add((Action<ObservableList<T>>)handler, subscriber);
            }

            public void RemoveHandler(object observableVar, Delegate handler) {
                ((ObservableList<T>)observableVar).OnValueChanged.Remove((Action<ObservableList<T>>)handler);
            }
        }

        private class ReflectionBinding : IObservableBinding {
            private readonly PropertyInfo _onValueChangedProp;
            private readonly PropertyInfo _valueProp;
            private readonly PropertyInfo _prevValueProp;
            private readonly MethodInfo _addMethod;
            private readonly MethodInfo _removeMethod;

            public ReflectionBinding(Type observableType) {
                _onValueChangedProp = observableType.GetProperty("OnValueChanged");
                _valueProp = observableType.GetProperty("Value");
                _prevValueProp = observableType.GetProperty("PreviousValue");
                if (_onValueChangedProp != null) {
                    var actionType = _onValueChangedProp.PropertyType;
                    _addMethod = actionType.GetMethod("Add");
                    _removeMethod = actionType.GetMethod("Remove");
                }
            }

            public Delegate CreateHandler(object target, MethodInfo method, int paramCount) {
                if (_addMethod == null) return null;
                var delegateType = _addMethod.GetParameters()[0].ParameterType;

                switch (paramCount) {
                    case 0: {
                        var act = (Action)Delegate.CreateDelegate(typeof(Action), target, method);
                        return CreateCustomAction(delegateType, _ => act());
                    }
                    case 1: {
                        return CreateCustomAction(delegateType, obs => {
                            var val = _valueProp?.GetValue(obs);
                            method.Invoke(target, new object[] { val });
                        });
                    }
                    case 2: {
                        return CreateCustomAction(delegateType, obs => {
                            var val = _valueProp?.GetValue(obs);
                            var prev = _prevValueProp?.GetValue(obs);
                            method.Invoke(target, new object[] { val, prev });
                        });
                    }
                    default:
                        return null;
                }
            }

            public Delegate CreateAutoBindHandler(Action<object> modelToUiHandler) {
                if (_addMethod == null) return null;
                var delegateType = _addMethod.GetParameters()[0].ParameterType;
                return CreateCustomAction(delegateType, obs => {
                    var val = _valueProp?.GetValue(obs);
                    modelToUiHandler(val);
                });
            }

            private static Delegate CreateCustomAction(Type actionType, Action<object> callback) {
                var invokeMethod = typeof(ReflectionBinding).GetMethod(nameof(ForwardAction), BindingFlags.NonPublic | BindingFlags.Static);
                var genericParam = actionType.GetGenericArguments()[0];
                var closedMethod = invokeMethod.MakeGenericMethod(genericParam);
                return Delegate.CreateDelegate(actionType, callback, closedMethod);
            }

            private static void ForwardAction<TObs>(object callbackObj, TObs obs) {
                ((Action<object>)callbackObj)(obs);
            }

            public void AddHandler(object observableVar, Delegate handler, object subscriber) {
                if (_onValueChangedProp == null || _addMethod == null) return;
                var trackedAction = _onValueChangedProp.GetValue(observableVar);
                if (trackedAction != null) {
                    _addMethod.Invoke(trackedAction, new object[] { handler, subscriber });
                }
            }

            public void RemoveHandler(object observableVar, Delegate handler) {
                if (_onValueChangedProp == null || _removeMethod == null) return;
                var trackedAction = _onValueChangedProp.GetValue(observableVar);
                if (trackedAction != null) {
                    _removeMethod.Invoke(trackedAction, new object[] { handler });
                }
            }
        }

#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [NoAutoStaticsCleanup]
#endif
        private static readonly Dictionary<Type, IObservableBinding> _bindingCache = new Dictionary<Type, IObservableBinding>();
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [NoAutoStaticsCleanup]
#endif
        private static readonly object _bindingCacheLock = new object();

        private static IObservableBinding GetBinding(Type observableType) {
            lock (_bindingCacheLock) {
                if (_bindingCache.TryGetValue(observableType, out var binding)) {
                    return binding;
                }

                Type targetType = observableType;
                while (targetType != null && (!targetType.IsGenericType || 
                       (targetType.GetGenericTypeDefinition() != typeof(ObservableVariable<>) && 
                        targetType.GetGenericTypeDefinition() != typeof(ObservableList<>)))) {
                    targetType = targetType.BaseType;
                }

                if (targetType != null && targetType.IsGenericType) {
                    var genericDef = targetType.GetGenericTypeDefinition();
                    var genericArg = targetType.GetGenericArguments()[0];

                    if (genericDef == typeof(ObservableVariable<>)) {
                        var bindingType = typeof(ObservableVariableBinding<>).MakeGenericType(genericArg);
                        binding = (IObservableBinding)bindingType.GetField("Instance").GetValue(null);
                    } else if (genericDef == typeof(ObservableList<>)) {
                        var bindingType = typeof(ObservableListBinding<>).MakeGenericType(genericArg);
                        binding = (IObservableBinding)bindingType.GetField("Instance").GetValue(null);
                    }
                }

                if (binding == null) {
                    binding = new ReflectionBinding(observableType);
                }

                _bindingCache[observableType] = binding;
                return binding;
            }
        }

        #endregion

        public static void SetListeners(object obj) {
            if (!(obj is MonoBehaviour monoBehaviour)) {
                Debug.LogWarning($"[SavableObservable] SetListeners called on non-MonoBehaviour object {obj?.GetType().Name}. Only MonoBehaviours are supported.");
                return;
            }

            var dataModel = ResolveModel(monoBehaviour, obj);
            if (dataModel == null) return;

            SetListenersCore(monoBehaviour, obj, dataModel);
        }

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
            // Remove existing subscriptions before adding new ones to ensure idempotent setup.
            RemoveAllSubscriptions(model, subscriber);

            model.EnsureFieldsInitialized();

            var subMetadata = GetSubscriberMetadata(subscriber.GetType());
            var modelMetadata = GetModelMetadata(model.GetType());
            var instanceData = _instanceData.GetOrCreateValue(model);

            foreach (var field in modelMetadata.ObservableFields) {
                if (subMetadata.HandlerMethods.TryGetValue(field.Name, out var handlerMethods)) {
                    foreach (var handlerMethod in handlerMethods) {
                        SubscribeIndividualHandler(subscriber, handlerMethod, field, model, instanceData);
                    }
                } else if (!subMetadata.AutoBindTargetNames.Contains(field.Name)) {
                    // Warn only when not handled by either [ObservableHandler] or [AutoBind].
                    Debug.LogWarning($"[SavableObservable] ObservableVariable '{field.Name}' in {model.GetType().Name} has no corresponding [ObservableHandler] method or [AutoBind] field in {subscriber.GetType().Name}.", monoBehaviour);
                }
            }

            SetAutoBindListenersInternal(monoBehaviour, subscriber, model, subMetadata, modelMetadata, instanceData);
        }

        private static BaseObservableDataModel ResolveModel(MonoBehaviour mb, object subscriber) {
            BaseObservableDataModel dataModel = null;
            if (subscriber is IObservablePresenter presenter) {
                dataModel = presenter.GetObservableModel();
            }

            if (dataModel == null) {
                var models = mb.GetComponents<BaseObservableDataModel>();
                if (models.Length > 0) {
                    dataModel = models[0];
                }
            }

            ValidateSingleMmvc(mb, dataModel);
            return dataModel;
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
                    $"Rule: One GameObject = one MMVC unit. To access another unit's model, get its presenter and call presenter.GetModel() or subscribe to individual events manually.",
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
            InstanceData instanceData) 
        {
            var observableVar = field.GetValue(dataModel);
            if (observableVar == null) return;

            var binding = GetBinding(field.FieldType);
            if (binding == null) return;

            var handlerParams = handlerMethod.GetParameters();
            if (handlerParams.Length > 2) {
                Debug.LogError($"[SavableObservable] Method '{handlerMethod.Name}' has an invalid number of parameters for [ObservableHandler].", (MonoBehaviour)subscriber);
                return;
            }

            Delegate handler;
            try {
                handler = binding.CreateHandler(subscriber, handlerMethod, handlerParams.Length);
            } catch (Exception ex) {
                Debug.LogError($"[SavableObservable] Failed to create delegate for [ObservableHandler] method '{handlerMethod.Name}': {ex.Message}", (MonoBehaviour)subscriber);
                return;
            }

            if (handler == null) {
                Debug.LogError($"[SavableObservable] Method '{handlerMethod.Name}' has an invalid signature for [ObservableHandler].", (MonoBehaviour)subscriber);
                return;
            }

            binding.AddHandler(observableVar, handler, subscriber);

            lock (instanceData.Lock) {
                if (!instanceData.UnbindActions.ContainsKey(subscriber)) {
                    instanceData.UnbindActions[subscriber] = new List<Action>();
                }
                instanceData.UnbindActions[subscriber].Add(() => binding.RemoveHandler(observableVar, handler));
            }
        }

        /// <summary>
        /// Sets up automatic UI bindings for fields marked with [AutoBind].
        /// </summary>
        public static void SetAutoBindListeners(object obj) {
            if (!(obj is MonoBehaviour monoBehaviour)) {
                Debug.LogWarning($"[SavableObservable] SetAutoBindListeners called on non-MonoBehaviour object {obj?.GetType().Name}. Only MonoBehaviours are supported.");
                return;
            }

            var dataModel = ResolveModel(monoBehaviour, obj);
            if (dataModel == null) return;

            SetAutoBindListenersCore(monoBehaviour, obj, dataModel);
        }

        public static void SetAutoBindListeners(object obj, BaseObservableDataModel dataModel) {
            if (!(obj is MonoBehaviour monoBehaviour)) {
                Debug.LogWarning($"[SavableObservable] SetAutoBindListeners called on non-MonoBehaviour object {obj?.GetType().Name}. Only MonoBehaviours are supported.");
                return;
            }

            if (dataModel == null) {
                Debug.LogError($"[SavableObservable] SetAutoBindListeners called with null model on {obj.GetType().Name}.", monoBehaviour);
                return;
            }

            if (!ValidateSameGameObject(monoBehaviour, dataModel, nameof(SetAutoBindListeners))) {
                return;
            }

            ValidateSingleMmvc(monoBehaviour, dataModel);
            SetAutoBindListenersCore(monoBehaviour, obj, dataModel);
        }

        private static void SetAutoBindListenersCore(MonoBehaviour monoBehaviour, object obj, BaseObservableDataModel dataModel) {
            var subMetadata = GetSubscriberMetadata(obj.GetType());
            var modelMetadata = GetModelMetadata(dataModel.GetType());
            var instanceData = _instanceData.GetOrCreateValue(dataModel);
            SetAutoBindListenersInternal(monoBehaviour, obj, dataModel, subMetadata, modelMetadata, instanceData);
        }

        private static void SetAutoBindListenersInternal(
            MonoBehaviour monoBehaviour, 
            object obj, 
            BaseObservableDataModel dataModel,
            SubscriberMetadata subMetadata,
            ModelMetadata modelMetadata,
            InstanceData instanceData) 
        {
            if (subMetadata.AutoBindFields.Count == 0) return;

            foreach (var (uiField, targetName) in subMetadata.AutoBindFields) {
                try {
                    if (!modelMetadata.ObservableFieldsByName.TryGetValue(targetName, out var observableField)) {
                        Debug.LogWarning($"[SavableObservable] [AutoBind] on '{uiField.Name}' could not find ObservableVariable '{targetName}' in {dataModel.GetType().Name}.", monoBehaviour);
                        continue;
                    }

                    var adapter = UIAdapterRegistry.GetAdapter(uiField.FieldType);
                    if (adapter == null) {
                        Debug.LogWarning($"[SavableObservable] No UI adapter found for type {uiField.FieldType.Name}. Register one via UIAdapterRegistry.RegisterAdapter().", monoBehaviour);
                        continue;
                    }

                    SubscribeAutoBindHandler(obj, uiField, observableField, dataModel, adapter, instanceData);
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
            InstanceData instanceData) 
        {
            var observableVar = observableField.GetValue(dataModel);
            if (observableVar == null) return;

            var binding = GetBinding(observableField.FieldType);
            if (binding == null) return;

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

            var wrappedHandler = binding.CreateAutoBindHandler(modelToUiHandler);
            if (wrappedHandler != null) {
                binding.AddHandler(observableVar, wrappedHandler, obj);

                lock (instanceData.Lock) {
                    if (!instanceData.UnbindActions.ContainsKey(obj)) {
                        instanceData.UnbindActions[obj] = new List<Action>();
                    }
                    instanceData.UnbindActions[obj].Add(() => binding.RemoveHandler(observableVar, wrappedHandler));
                }
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
                            lock (instanceData.Lock) {
                                if (!instanceData.UnbindActions.ContainsKey(obj)) {
                                    instanceData.UnbindActions[obj] = new List<Action>();
                                }
                                instanceData.UnbindActions[obj].Add(() => {
                                    if (unityUiComponent != null) {
                                        try {
                                            listenerAdapter.RemoveListener(unityUiComponent, token);
                                        } catch (Exception ex) {
                                            Debug.LogWarning($"[SavableObservable] Failed to remove UI listener token during cleanup: {ex.Message}");
                                        }
                                    }
                                });

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
        ///   <c>true</c> if field of type <see cref="ObservableVariable" /> otherwise, <c>false</c>.</returns>
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

        /// <summary>
        /// Registers a subscription for automatic cleanup when the GameObject is destroyed.
        /// </summary>
        /// <param name="dataModel">The data model instance</param>
        /// <param name="subscriber">The subscriber object (e.g., Presenter or Logic)</param>
        /// <param name="subscription">The delegate subscription to be cleaned up</param>
        public static void RegisterSubscription(BaseObservableDataModel dataModel, object subscriber, Delegate subscription) {
            if (dataModel == null || subscriber == null || subscription == null) return;
            var instanceData = _instanceData.GetOrCreateValue(dataModel);
            lock (instanceData.Lock) {
                if (!instanceData.Subscriptions.ContainsKey(subscriber)) {
                    instanceData.Subscriptions[subscriber] = new List<Delegate>();
                }
                if (!instanceData.Subscriptions[subscriber].Contains(subscription)) {
                    instanceData.Subscriptions[subscriber].Add(subscription);
                }
            }
        }

        /// <summary>
        /// Removes a subscription from the tracking list.
        /// </summary>
        /// <param name="dataModel">The data model instance</param>
        /// <param name="subscriber">The subscriber object</param>
        /// <param name="subscription">The delegate subscription to remove</param>
        public static void UnregisterSubscription(BaseObservableDataModel dataModel, object subscriber, Delegate subscription) {
            if (dataModel == null || subscription == null) return;
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
        /// Internal fallback to remove subscriptions for a specific subscriber from all observable variables.
        /// </summary>
        private static void RemoveSubscriptionsForSubscriber(BaseObservableDataModel dataModel, object subscriber, InstanceData instanceData) {
            if (!instanceData.Subscriptions.TryGetValue(subscriber, out var subscriptions) || subscriptions.Count == 0) {
                return;
            }

            var subscriptionsList = new List<Delegate>(subscriptions);
            var observableFields = GetCachedObservableFields(dataModel);
            foreach (var field in observableFields) {
                var observableVar = field.GetValue(dataModel);
                if (observableVar == null) continue;

                var binding = GetBinding(field.FieldType);
                foreach (var sub in subscriptionsList) {
                    try {
                        binding.RemoveHandler(observableVar, sub);
                    } catch {
                        // Subscription was not found on this event or type did not match, continue
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
            if (dataModel == null || subscriber == null) return;
            var instanceData = _instanceData.GetOrCreateValue(dataModel);
            lock (instanceData.Lock) {
                // 1) Direct unbind closures
                if (instanceData.UnbindActions.TryGetValue(subscriber, out var unbinds)) {
                    foreach (var unbind in unbinds) {
                        try {
                            unbind();
                        } catch (Exception ex) {
                            Debug.LogWarning($"[SavableObservable] Failed to unbind subscriber during cleanup: {ex.Message}");
                        }
                    }
                    unbinds.Clear();
                    instanceData.UnbindActions.Remove(subscriber);
                }

                // 2) Manual subscriptions fallback
                if (instanceData.Subscriptions.ContainsKey(subscriber)) {
                    RemoveSubscriptionsForSubscriber(dataModel, subscriber, instanceData);
                    instanceData.Subscriptions.Remove(subscriber);
                }

                // 3) UI listener tokens cleanup
                RemoveUiListenersForSubscriber(dataModel, subscriber, instanceData);
            }
        }

        /// <summary>
        /// Cleans up all tracked subscriptions for a data model when it's destroyed.
        /// </summary>
        /// <param name="dataModel">The data model being destroyed</param>
        public static void CleanupSubscriptions(BaseObservableDataModel dataModel) {
            if (dataModel == null) return;
            var instanceData = _instanceData.GetOrCreateValue(dataModel);
            lock (instanceData.Lock) {
                instanceData.IsInCleanup = true;
                try {
                    // 1) Direct unbind closures across all subscribers
                    foreach (var unbindList in instanceData.UnbindActions.Values) {
                        foreach (var unbind in unbindList) {
                            try {
                                unbind();
                            } catch (Exception ex) {
                                Debug.LogWarning($"[SavableObservable] Failed to unbind subscriber during cleanup: {ex.Message}");
                            }
                        }
                        unbindList.Clear();
                    }
                    instanceData.UnbindActions.Clear();

                    // 2) Manual subscriptions fallback
                    if (instanceData.Subscriptions.Count > 0) {
                        var allSubscribers = new List<object>(instanceData.Subscriptions.Keys);
                        foreach (var sub in allSubscribers) {
                            RemoveSubscriptionsForSubscriber(dataModel, sub, instanceData);
                        }
                        instanceData.Subscriptions.Clear();
                    }

                    // 3) UI listener tokens cleanup
                    foreach (var kvp in instanceData.UiListenerTokens) {
                        RemoveUiTokens(kvp.Value);
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
        private static void RemoveUiListenersForSubscriber(BaseObservableDataModel dataModel, object subscriber, InstanceData instanceData) {
            if (!instanceData.UiListenerTokens.TryGetValue(subscriber, out var tokens)) {
                return;
            }

            RemoveUiTokens(tokens);
            tokens.Clear();
            instanceData.UiListenerTokens.Remove(subscriber);
        }

        private static void RemoveUiTokens(List<UiListenerToken> tokens) {
            foreach (var uiToken in tokens) {
                if (uiToken.UiComponent == null) {
                    continue;
                }

                if (uiToken.Token != null && UIAdapterRegistry.TryGetListenerAdapter(uiToken.UiComponent.GetType(), out var listenerAdapter)) {
                    try {
                        listenerAdapter.RemoveListener(uiToken.UiComponent, uiToken.Token);
                    } catch (Exception ex) {
                        Debug.LogWarning($"[SavableObservable] Failed to remove UI listener token during cleanup: {ex.Message}");
                    }
                }
            }
        }
    }
}
