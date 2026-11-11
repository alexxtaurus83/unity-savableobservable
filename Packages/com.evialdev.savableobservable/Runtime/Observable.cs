using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Runtime.CompilerServices;
using Unity.Scripting.LifecycleManagement;

namespace SavableObservable {

    public partial class Observable {

        // Storage for per-instance data using ConditionalWeakTable to avoid memory leaks
        [AutoStaticsCleanup]
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
            public readonly List<(FieldInfo field, string targetName, string commandName)> AutoBindFields = new List<(FieldInfo, string, string)>();
            public readonly HashSet<string> AutoBindTargetNames = new HashSet<string>(StringComparer.Ordinal);
            public readonly List<(FieldInfo field, string commandName)> ClickCommandFields = new List<(FieldInfo, string)>();

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
                        var clickAttr = field.GetCustomAttribute<ClickCommandAttribute>();
                        var autoBindAttr = field.GetCustomAttribute<AutoBindAttribute>();

                        if (clickAttr != null && autoBindAttr != null) {
                            Debug.LogError($"[SavableObservable] Field '{field.Name}' on '{subscriberType.Name}' cannot have both [ClickCommand] and [AutoBind].", null);
                        } else if (clickAttr != null) {
                            if (!typeof(Button).IsAssignableFrom(field.FieldType)) {
                                Debug.LogError($"[SavableObservable] Invalid [ClickCommand] on field '{field.Name}' ({subscriberType.Name}): field type '{field.FieldType.Name}' is not UnityEngine.UI.Button or a subclass.", null);
                            } else if (string.IsNullOrWhiteSpace(clickAttr.MethodName)) {
                                Debug.LogError($"[SavableObservable] [ClickCommand] on field '{field.Name}' ({subscriberType.Name}) has an empty method name.", null);
                            } else {
                                ClickCommandFields.Add((field, clickAttr.MethodName.Trim()));
                            }
                        } else if (autoBindAttr != null) {
                            var targetName = string.IsNullOrWhiteSpace(autoBindAttr.VariableName) ? field.Name : autoBindAttr.VariableName;
                            var commandName = !string.IsNullOrWhiteSpace(autoBindAttr.Command) ? autoBindAttr.Command.Trim() : null;
                            AutoBindFields.Add((field, targetName, commandName));
                            if (!string.IsNullOrWhiteSpace(targetName)) {
                                AutoBindTargetNames.Add(targetName);
                            }
                        }
                    }
                }
            }
        }

        [NoAutoStaticsCleanup]
        private static readonly Dictionary<Type, SubscriberMetadata> _subscriberMetadataCache = new Dictionary<Type, SubscriberMetadata>();
        [NoAutoStaticsCleanup]
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
                PresenterLifetimeTracker.Track(presenterState);
            }
            PresenterLifetimeTracker.SweepDeadPresenters();

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

            foreach (var (uiField, targetName, commandName) in subMetadata.AutoBindFields) {
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

                    SubscribeAutoBindHandler(obj, uiField, observableField, commandName, dataModel, adapter, replay);
                } catch (Exception ex) {
                    Debug.LogError($"[SavableObservable] Failed to wire [AutoBind] for field '{uiField.Name}' on {obj.GetType().Name}: {ex.Message}", monoBehaviour);
                }
            }
        }

        private static bool IsTargetAlive(object obj) => obj != null && !(obj is UnityEngine.Object uo && uo == null);

        private static void SubscribeAutoBindHandler(
            object obj,
            FieldInfo uiField,
            FieldInfo observableField,
            string commandName,
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
                    if (IsTargetAlive(currentUiComponent)) {
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

            // 2) Subscribe UI -> Model or UI -> Logic (Two-way / Command binding)
            // ----------------------------------------------------------------
            try {
                var presenterMb = obj as MonoBehaviour;
                var currentUiComponent = uiField.GetValue(obj);
                if (!IsTargetAlive(currentUiComponent)) return;

                if (!UIAdapterRegistry.TryGetListenerAdapter(currentUiComponent.GetType(), out var listenerAdapter)) {
                    if (!string.IsNullOrEmpty(commandName)) {
                        Debug.LogError($"[SavableObservable] [AutoBind] on field '{uiField.Name}' ({obj.GetType().Name}) declares Command '{commandName}', but UI component type '{currentUiComponent.GetType().Name}' does not support input events.", presenterMb);
                    }
                    return;
                }

                if (string.IsNullOrEmpty(commandName)) {
                    var valueProp = observableField.FieldType.GetProperty("Value");
                    Action<object> uiToModelHandler = newValue => {
                        try {
                            if (valueProp != null && valueProp.CanWrite) {
                                valueProp.SetValue(observableVar, newValue);
                            }
                        } catch (Exception ex) {
                            Debug.LogError($"[SavableObservable] Failed to update Observable '{observableField.Name}' from UI: {ex.Message}", presenterMb);
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
                } else {
                    if (IsObservableListType(observableField.FieldType)) {
                        Debug.LogError($"[SavableObservable] [AutoBind] on field '{uiField.Name}' ({obj.GetType().Name}) declares Command '{commandName}' for '{observableField.Name}', but command binding is not supported for ObservableList. Expected ObservableVariable.", presenterMb);
                        return;
                    }

                    if (!(obj is IAutoBindCommandTarget commandTarget)) {
                        Debug.LogError($"[SavableObservable] [AutoBind] on field '{uiField.Name}' ({obj.GetType().Name}) declares Command '{commandName}', but presenter does not implement IAutoBindCommandTarget.", presenterMb);
                        return;
                    }

                    var logic = commandTarget.GetAutoBindCommandTarget();
                    if (!IsTargetAlive(logic)) {
                        Debug.LogError($"[SavableObservable] [AutoBind] on field '{uiField.Name}' ({obj.GetType().Name}) declares Command '{commandName}', but paired Logic target is null or destroyed.", presenterMb);
                        return;
                    }

                    if (presenterMb != null && logic.gameObject != presenterMb.gameObject) {
                        Debug.LogError($"[SavableObservable] [AutoBind] on field '{uiField.Name}' ({obj.GetType().Name}) declares Command '{commandName}', but paired Logic is on a different GameObject ('{logic.gameObject.name}'). Must be on the same GameObject.", presenterMb);
                        return;
                    }

                    if (!ReferenceEquals(logic.GetUntypedModel(), dataModel)) {
                        Debug.LogError($"[SavableObservable] [AutoBind] on field '{uiField.Name}' ({obj.GetType().Name}) declares Command '{commandName}', but paired Logic does not own model '{dataModel.GetType().Name}'.", presenterMb);
                        return;
                    }

                    if (!GetOrValidateCommandMethod(logic.GetType(), commandName, valueType, out var methodInfo, out var error)) {
                        Debug.LogError($"[SavableObservable] Invalid [AutoBind] Command '{commandName}' on field '{uiField.Name}' ({obj.GetType().Name}): {error} Expected signature on {logic.GetType().Name}: 'public void {commandName}({valueType.Name} value)'", presenterMb);
                        return;
                    }

                    bool isProcessingCommand = false;
                    Action<object> uiToCommandHandler = newValue => {
                        try {
                            if (isProcessingCommand) return;
                            var currentUi = uiField.GetValue(obj);
                            if (!IsTargetAlive(currentUi) || !IsTargetAlive(obj) || !IsTargetAlive(logic) || !IsTargetAlive(dataModel)) return;
                            if (UIAdapterRegistry.IsSettingValue(currentUi)) return;

                            isProcessingCommand = true;
                            try {
                                methodInfo.Invoke(logic, new object[] { newValue });
                            } catch (TargetInvocationException tie) {
                                Debug.LogError($"[SavableObservable] Command '{methodInfo.Name}' on '{logic.GetType().Name}' threw an exception: {tie.InnerException?.Message ?? tie.Message}", presenterMb);
                            } catch (Exception ex) {
                                Debug.LogError($"[SavableObservable] Command '{methodInfo.Name}' on '{logic.GetType().Name}' threw an exception: {ex.Message}", presenterMb);
                            } finally {
                                try {
                                    if (IsTargetAlive(currentUi) && IsTargetAlive(dataModel)) {
                                        observableVar.InvokeHandler(wrappedHandler);
                                    }
                                } catch (Exception rex) {
                                    Debug.LogError($"[SavableObservable] Failed to reconcile UI field '{uiField.Name}' from '{observableField.Name}': {rex.Message}", presenterMb);
                                }
                                isProcessingCommand = false;
                            }
                        } catch (Exception ex) {
                            Debug.LogError($"[SavableObservable] Failed to process UI command for '{uiField.Name}': {ex.Message}", presenterMb);
                        }
                    };

                    object token = listenerAdapter.AddListener(currentUiComponent, uiToCommandHandler, valueType);

                    if (token != null && currentUiComponent is UnityEngine.Object unityUiComponent) {
                        RegisterUiListener(dataModel, obj, () => {
                            if (unityUiComponent != null) {
                                listenerAdapter.RemoveListener(unityUiComponent, token);
                            }
                        });
                    }
                }
            } catch (Exception ex) {
                Debug.LogWarning($"[SavableObservable] Failed to setup two-way binding for '{uiField.Name}': {ex.Message}", obj as MonoBehaviour);
            }
        }

        internal static void WireClickCommands(MonoBehaviour presenter, ref ListenerSet clickListeners) {
            if (presenter == null) return;
            var subMetadata = GetSubscriberMetadata(presenter.GetType());
            if (subMetadata.ClickCommandFields.Count == 0) return;

            if (!(presenter is IAutoBindCommandTarget commandTarget)) {
                for (int i = 0; i < subMetadata.ClickCommandFields.Count; i++) {
                    var (field, commandName) = subMetadata.ClickCommandFields[i];
                    Debug.LogError($"[SavableObservable] [ClickCommand] on field '{field.Name}' ({presenter.GetType().Name}) requires a paired Logic component, but {presenter.GetType().Name} does not implement IAutoBindCommandTarget.", presenter);
                }
                return;
            }

            var logic = commandTarget.GetAutoBindCommandTarget();
            if (!IsTargetAlive(logic)) {
                for (int i = 0; i < subMetadata.ClickCommandFields.Count; i++) {
                    var (field, commandName) = subMetadata.ClickCommandFields[i];
                    Debug.LogError($"[SavableObservable] [ClickCommand] on field '{field.Name}' ({presenter.GetType().Name}): paired Logic target is null or destroyed.", presenter);
                }
                return;
            }

            if (logic.gameObject != presenter.gameObject) {
                for (int i = 0; i < subMetadata.ClickCommandFields.Count; i++) {
                    var (field, commandName) = subMetadata.ClickCommandFields[i];
                    Debug.LogError($"[SavableObservable] [ClickCommand] on field '{field.Name}' ({presenter.GetType().Name}): paired Logic is on a different GameObject ('{logic.gameObject.name}'). Must be on the same GameObject.", presenter);
                }
                return;
            }

            var model = (presenter as IObservablePresenter)?.GetObservableModel();
            if (model != null && !ReferenceEquals(logic.GetUntypedModel(), model)) {
                for (int i = 0; i < subMetadata.ClickCommandFields.Count; i++) {
                    var (field, commandName) = subMetadata.ClickCommandFields[i];
                    Debug.LogError($"[SavableObservable] [ClickCommand] on field '{field.Name}' ({presenter.GetType().Name}): paired Logic does not own model '{model.GetType().Name}'.", presenter);
                }
                return;
            }

            for (int i = 0; i < subMetadata.ClickCommandFields.Count; i++) {
                var (buttonField, commandName) = subMetadata.ClickCommandFields[i];
                try {
                    var button = buttonField.GetValue(presenter) as Button;
                    if (!IsTargetAlive(button)) {
                        continue;
                    }

                    if (!GetOrValidateClickCommandMethod(logic.GetType(), commandName, out var methodInfo, out var error)) {
                        Debug.LogError($"[SavableObservable] Invalid [ClickCommand] '{commandName}' on field '{buttonField.Name}' ({presenter.GetType().Name}): {error} Expected signature on {logic.GetType().Name}: 'public void {commandName}()'", presenter);
                        continue;
                    }

                    clickListeners ??= new ListenerSet();
                    var cachedMethod = methodInfo;
                    UnityAction clickAction = () => {
                        if (!IsTargetAlive(presenter) || !IsTargetAlive(logic) || !IsTargetAlive(button)) return;
                        try {
                            cachedMethod.Invoke(logic, null);
                        } catch (TargetInvocationException tie) {
                            Debug.LogError($"[SavableObservable] ClickCommand '{cachedMethod.Name}' on button '{buttonField.Name}' ({presenter.GetType().Name}) threw an exception: {tie.InnerException?.Message ?? tie.Message}", presenter);
                        } catch (Exception ex) {
                            Debug.LogError($"[SavableObservable] ClickCommand '{cachedMethod.Name}' on button '{buttonField.Name}' ({presenter.GetType().Name}) threw an exception: {ex.Message}", presenter);
                        }
                    };

                    clickListeners.Listen(button.onClick, clickAction);
                } catch (Exception ex) {
                    Debug.LogError($"[SavableObservable] Failed to wire [ClickCommand] on field '{buttonField.Name}' ({presenter.GetType().Name}): {ex.Message}", presenter);
                }
            }
        }

        [NoAutoStaticsCleanup]
        private static readonly Dictionary<(Type, string), (MethodInfo method, string error)> _clickCommandMethodCache =
            new Dictionary<(Type, string), (MethodInfo, string)>();
        [NoAutoStaticsCleanup]
        private static readonly object _clickCommandMethodLock = new object();

        private static bool GetOrValidateClickCommandMethod(Type logicType, string commandName, out MethodInfo methodInfo, out string error) {
            var key = (logicType, commandName);
            lock (_clickCommandMethodLock) {
                if (_clickCommandMethodCache.TryGetValue(key, out var cached)) {
                    methodInfo = cached.method;
                    error = cached.error;
                    return methodInfo != null;
                }
            }

            var result = ResolveClickCommandMethod(logicType, commandName);
            lock (_clickCommandMethodLock) {
                _clickCommandMethodCache[key] = result;
            }

            methodInfo = result.method;
            error = result.error;
            return methodInfo != null;
        }

        private static (MethodInfo method, string error) ResolveClickCommandMethod(Type logicType, string commandName) {
            var methods = logicType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var allMethods = new List<MethodInfo>();
            for (int i = 0; i < methods.Length; i++) {
                if (methods[i].Name == commandName) {
                    allMethods.Add(methods[i]);
                }
            }

            if (allMethods.Count == 0) {
                return (null, $"Method '{commandName}' not found on '{logicType.Name}'.");
            }

            bool allStatic = true;
            for (int i = 0; i < allMethods.Count; i++) {
                if (!allMethods[i].IsStatic) {
                    allStatic = false;
                    break;
                }
            }
            if (allStatic) {
                return (null, $"Method '{commandName}' on '{logicType.Name}' is static. Commands must be public instance methods.");
            }

            bool allNonPublic = true;
            for (int i = 0; i < allMethods.Count; i++) {
                if (allMethods[i].IsPublic) {
                    allNonPublic = false;
                    break;
                }
            }
            if (allNonPublic) {
                return (null, $"Method '{commandName}' on '{logicType.Name}' is not public. Commands must be public instance methods.");
            }

            var publicInstance = new List<MethodInfo>();
            for (int i = 0; i < allMethods.Count; i++) {
                var m = allMethods[i];
                if (m.IsPublic && !m.IsStatic) {
                    publicInstance.Add(m);
                }
            }
            var candidates = new List<MethodInfo>();
            string firstError = null;

            foreach (var m in publicInstance) {
                if (m.IsGenericMethod || m.ContainsGenericParameters) {
                    firstError ??= $"Method '{commandName}' on '{logicType.Name}' is generic. Generic methods are not supported.";
                    continue;
                }

                if (m.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>() != null) {
                    firstError ??= $"Method '{commandName}' on '{logicType.Name}' is async void. Commands must be synchronous.";
                    continue;
                }

                if (typeof(System.Threading.Tasks.Task).IsAssignableFrom(m.ReturnType) ||
                    m.ReturnType.Name == "ValueTask" ||
                    typeof(System.Collections.IEnumerator).IsAssignableFrom(m.ReturnType)) {
                    firstError ??= $"Method '{commandName}' on '{logicType.Name}' returns '{m.ReturnType.Name}'. Commands must be synchronous with void return.";
                    continue;
                }

                if (m.ReturnType != typeof(void)) {
                    firstError ??= $"Method '{commandName}' on '{logicType.Name}' returns '{m.ReturnType.Name}'. Commands must return void.";
                    continue;
                }

                var parameters = m.GetParameters();
                if (parameters.Length != 0) {
                    firstError ??= $"Method '{commandName}' on '{logicType.Name}' has {parameters.Length} parameters. Click commands must have zero parameters.";
                    continue;
                }

                candidates.Add(m);
            }

            if (candidates.Count == 0) {
                return (null, firstError ?? $"Method '{commandName}' on '{logicType.Name}' does not have the expected signature.");
            }

            if (candidates.Count > 1) {
                var filtered = new List<MethodInfo>();
                for (int i = 0; i < candidates.Count; i++) {
                    var c = candidates[i];
                    bool hasDerivedOverride = false;
                    for (int j = 0; j < candidates.Count; j++) {
                        var other = candidates[j];
                        if (!ReferenceEquals(other, c) && other.DeclaringType != c.DeclaringType && c.DeclaringType.IsAssignableFrom(other.DeclaringType)) {
                            hasDerivedOverride = true;
                            break;
                        }
                    }
                    if (!hasDerivedOverride) {
                        filtered.Add(c);
                    }
                }
                if (filtered.Count == 1) {
                    return (filtered[0], null);
                }
                return (null, $"Ambiguous matches found for command '{commandName}' on '{logicType.Name}'.");
            }

            return (candidates[0], null);
        }

        [NoAutoStaticsCleanup]
        private static readonly Dictionary<(Type, string, Type), (MethodInfo method, string error)> _commandMethodCache =
            new Dictionary<(Type, string, Type), (MethodInfo, string)>();
        [NoAutoStaticsCleanup]
        private static readonly object _commandMethodLock = new object();

        private static bool GetOrValidateCommandMethod(Type logicType, string commandName, Type valueType, out MethodInfo methodInfo, out string error) {
            var key = (logicType, commandName, valueType);
            lock (_commandMethodLock) {
                if (_commandMethodCache.TryGetValue(key, out var cached)) {
                    methodInfo = cached.method;
                    error = cached.error;
                    return methodInfo != null;
                }
            }

            var result = ResolveCommandMethod(logicType, commandName, valueType);
            lock (_commandMethodLock) {
                _commandMethodCache[key] = result;
            }

            methodInfo = result.method;
            error = result.error;
            return methodInfo != null;
        }

        private static (MethodInfo method, string error) ResolveCommandMethod(Type logicType, string commandName, Type valueType) {
            var methods = logicType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var allMethods = new List<MethodInfo>();
            for (int i = 0; i < methods.Length; i++) {
                if (methods[i].Name == commandName) {
                    allMethods.Add(methods[i]);
                }
            }

            if (allMethods.Count == 0) {
                return (null, $"Method '{commandName}' not found on '{logicType.Name}'.");
            }

            bool allStatic = true;
            for (int i = 0; i < allMethods.Count; i++) {
                if (!allMethods[i].IsStatic) {
                    allStatic = false;
                    break;
                }
            }
            if (allStatic) {
                return (null, $"Method '{commandName}' on '{logicType.Name}' is static. Commands must be public instance methods.");
            }

            bool allNonPublic = true;
            for (int i = 0; i < allMethods.Count; i++) {
                if (allMethods[i].IsPublic) {
                    allNonPublic = false;
                    break;
                }
            }
            if (allNonPublic) {
                return (null, $"Method '{commandName}' on '{logicType.Name}' is not public. Commands must be public instance methods.");
            }

            var publicInstance = new List<MethodInfo>();
            for (int i = 0; i < allMethods.Count; i++) {
                var m = allMethods[i];
                if (m.IsPublic && !m.IsStatic) {
                    publicInstance.Add(m);
                }
            }
            var candidates = new List<MethodInfo>();
            string firstError = null;

            foreach (var m in publicInstance) {
                if (m.IsGenericMethod || m.ContainsGenericParameters) {
                    firstError ??= $"Method '{commandName}' on '{logicType.Name}' is generic. Generic methods are not supported.";
                    continue;
                }

                if (m.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>() != null) {
                    firstError ??= $"Method '{commandName}' on '{logicType.Name}' is async void. Commands must be synchronous.";
                    continue;
                }

                if (typeof(System.Threading.Tasks.Task).IsAssignableFrom(m.ReturnType) ||
                    m.ReturnType.Name == "ValueTask" ||
                    typeof(System.Collections.IEnumerator).IsAssignableFrom(m.ReturnType)) {
                    firstError ??= $"Method '{commandName}' on '{logicType.Name}' returns '{m.ReturnType.Name}'. Commands must be synchronous with void return.";
                    continue;
                }

                if (m.ReturnType != typeof(void)) {
                    firstError ??= $"Method '{commandName}' on '{logicType.Name}' returns '{m.ReturnType.Name}'. Commands must return void.";
                    continue;
                }

                var parameters = m.GetParameters();
                if (parameters.Length != 1) {
                    firstError ??= $"Method '{commandName}' on '{logicType.Name}' has {parameters.Length} parameters. Commands must have exactly one parameter matching the value type '{valueType.Name}'.";
                    continue;
                }

                var p = parameters[0];
                if (p.ParameterType.IsByRef || p.IsOut) {
                    firstError ??= $"Method '{commandName}' on '{logicType.Name}' parameter '{p.Name}' has ref/out modifier. By-ref parameters are not supported.";
                    continue;
                }

                if (p.ParameterType != valueType) {
                    firstError ??= $"Method '{commandName}' on '{logicType.Name}' parameter type '{p.ParameterType.Name}' does not match expected value type '{valueType.Name}'.";
                    continue;
                }

                candidates.Add(m);
            }

            if (candidates.Count == 0) {
                return (null, firstError ?? $"Method '{commandName}' on '{logicType.Name}' does not have the expected signature.");
            }

            if (candidates.Count > 1) {
                var filtered = new List<MethodInfo>();
                for (int i = 0; i < candidates.Count; i++) {
                    var c = candidates[i];
                    bool hasDerivedOverride = false;
                    for (int j = 0; j < candidates.Count; j++) {
                        var other = candidates[j];
                        if (!ReferenceEquals(other, c) && other.DeclaringType != c.DeclaringType && c.DeclaringType.IsAssignableFrom(other.DeclaringType)) {
                            hasDerivedOverride = true;
                            break;
                        }
                    }
                    if (!hasDerivedOverride) {
                        filtered.Add(c);
                    }
                }
                if (filtered.Count == 1) {
                    return (filtered[0], null);
                }
                return (null, $"Ambiguous matches found for command '{commandName}' on '{logicType.Name}'.");
            }

            return (candidates[0], null);
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
            foreach (var kvp in data.Subscriptions) {
                if (kvp.Key is IPresenterBindingState bindingState) {
                    bindingState.CleanupPresenterLifetime();
                    PresenterLifetimeTracker.Untrack(bindingState);
                }
                all.AddRange(kvp.Value);
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

        private static bool IsObservableListType(Type type) {
            while (type != null && type != typeof(object)) {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ObservableList<>)) {
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }

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
