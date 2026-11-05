using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
using Unity.Scripting.LifecycleManagement;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SavableObservable {

    /// <summary>Outcome of a registration attempt.</summary>
    public enum RegistrationResult {
        /// <summary>The service was stored under its binding key.</summary>
        Added,
        /// <summary>The very same live object was already stored under that key; nothing changed.</summary>
        SameInstance,
        /// <summary>Two or more live instances share the same key; the key was evicted from singleton lookup.</summary>
        EvictedDuplicate,
        /// <summary>The service is null, destroyed, or of a forbidden type (e.g. Presenter or Model).</summary>
        Rejected
    }

    /// <summary>
    /// Instance-owned service registry.
    /// Lookup is by exact concrete type: registers <c>service.GetType()</c>, not base types or interfaces.
    /// Manages shared singletons (<see cref="BaseLogic"/> and custom non-MMVC <see cref="MonoBehaviour"/> services).
    /// Models (<see cref="BaseObservableDataModel"/>) of registered <see cref="BaseLogic"/> instances are also indexed
    /// by model type and logic name, resolvable via <see cref="Services.GetModel{M}"/>.
    /// Implements live-instance collision-eviction: if multiple live instances share a key without distinct names,
    /// the key is evicted from lookup and logs an error upon resolution attempt.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public partial class ServiceLocator : MonoBehaviour {

        [SerializeField] private bool enableLogging = true;

#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [AutoStaticsCleanup]
#endif
        public static ServiceLocator instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnSubsystemRegistration() {
            instance = null;
        }

        // Service bindings: Type -> Key -> Service Instance
        private readonly Dictionary<Type, Dictionary<string, object>> _serviceBindings = new Dictionary<Type, Dictionary<string, object>>();
        // Model bindings: ModelType -> Key (Logic Name) -> Model Instance
        private readonly Dictionary<Type, Dictionary<string, object>> _modelBindings = new Dictionary<Type, Dictionary<string, object>>();
        // Live service instances tracked per (Type, Key): Type -> Key -> List of live service instances
        private readonly Dictionary<Type, Dictionary<string, List<object>>> _liveInstances = new Dictionary<Type, Dictionary<string, List<object>>>();
        // Live model instances tracked per (ModelType, Key): ModelType -> Key -> List of live model instances
        private readonly Dictionary<Type, Dictionary<string, List<object>>> _liveModelInstances = new Dictionary<Type, Dictionary<string, List<object>>>();
        // Map from Logic instance -> (ModelType, Key, ModelInstance) to enable accurate targeted removal/pruning
        private readonly Dictionary<object, (Type ModelType, string Key, object ModelInstance)> _logicToModelMap = new Dictionary<object, (Type, string, object)>();

        // Main-thread scratch buffers for pruning.
        private readonly List<string> _pruneScratch = new List<string>();
        private readonly List<object> _livePruneScratch = new List<object>();

        private void Awake() {
            if (instance == null) {
                instance = this;
            }
            InitializeSharedSingletons();
        }

        private void OnDestroy() {
            _serviceBindings.Clear();
            _modelBindings.Clear();
            _liveInstances.Clear();
            _liveModelInstances.Clear();
            _logicToModelMap.Clear();
            if (ReferenceEquals(instance, this)) {
                instance = null;
            }
            Services.ReleaseLocator(this);
        }

        /// <summary>Unity-aware liveness: destroyed UnityEngine.Object instances are not live even when typed as object/interface.</summary>
        internal static bool IsLive(object service) {
            if (service is UnityEngine.Object u) return u != null;
            return service != null;
        }

        private static string ResolveKey(Type type, object service, string name) {
            if (string.IsNullOrEmpty(name) && service is ISharedSingleton ss) {
                name = ss.Name;
            }
            return string.IsNullOrEmpty(name) ? type.Name : name;
        }

        /// <summary>
        /// Prunes destroyed instances from tracking dictionaries for the given service type.
        /// Re-evaluates collision-eviction status: if dead duplicates leave exactly 1 live instance,
        /// that single remaining instance is restored to lookup.
        /// </summary>
        private void PruneType(Type type) {
            if (!_liveInstances.TryGetValue(type, out var keyDict)) {
                _serviceBindings.Remove(type);
                return;
            }

            _pruneScratch.Clear();
            foreach (var kvp in keyDict) {
                var key = kvp.Key;
                var list = kvp.Value;

                _livePruneScratch.Clear();
                for (int i = 0; i < list.Count; i++) {
                    var item = list[i];
                    if (IsLive(item)) {
                        _livePruneScratch.Add(item);
                    } else {
                        // Logic was destroyed: unregister its tracked model mapping
                        if (_logicToModelMap.TryGetValue(item, out var mapped)) {
                            RemoveTrackedModel(mapped.ModelType, mapped.Key, mapped.ModelInstance);
                            _logicToModelMap.Remove(item);
                        }
                    }
                }
                list.Clear();
                list.AddRange(_livePruneScratch);
                _livePruneScratch.Clear();

                if (list.Count == 0) {
                    _pruneScratch.Add(key);
                    if (_serviceBindings.TryGetValue(type, out var sInner)) {
                        sInner.Remove(key);
                    }
                } else if (list.Count == 1) {
                    if (!_serviceBindings.TryGetValue(type, out var sInner)) {
                        sInner = new Dictionary<string, object>();
                        _serviceBindings[type] = sInner;
                    }
                    sInner[key] = list[0];

                    // Recover model index if this single live instance is BaseLogic
                    if (list[0] is BaseLogic logic) {
                        var model = logic.GetUntypedModel();
                        if (IsLive(model)) {
                            SyncLiveModel(model.GetType(), key, model);
                            _logicToModelMap[logic] = (model.GetType(), key, model);
                        }
                    }
                } else {
                    // Still collided (> 1 live instance): evicted from lookup
                    if (_serviceBindings.TryGetValue(type, out var sInner)) {
                        sInner.Remove(key);
                    }
                }
            }

            for (int i = 0; i < _pruneScratch.Count; i++) {
                keyDict.Remove(_pruneScratch[i]);
            }
            _pruneScratch.Clear();

            if (keyDict.Count == 0) {
                _liveInstances.Remove(type);
                _serviceBindings.Remove(type);
            }
        }

        private void PruneModelType(Type modelType) {
            // First, prune all registered Logic instances so destroyed Logic units clean up their models!
            PruneAllLogicOwners();

            if (!_liveModelInstances.TryGetValue(modelType, out var keyDict)) {
                _modelBindings.Remove(modelType);
                return;
            }

            _pruneScratch.Clear();
            foreach (var kvp in keyDict) {
                var key = kvp.Key;
                var list = kvp.Value;

                _livePruneScratch.Clear();
                for (int i = 0; i < list.Count; i++) {
                    if (IsLive(list[i])) {
                        _livePruneScratch.Add(list[i]);
                    }
                }
                list.Clear();
                list.AddRange(_livePruneScratch);
                _livePruneScratch.Clear();

                if (list.Count == 0) {
                    _pruneScratch.Add(key);
                    if (_modelBindings.TryGetValue(modelType, out var mInner)) {
                        mInner.Remove(key);
                    }
                } else if (list.Count == 1) {
                    if (!_modelBindings.TryGetValue(modelType, out var mInner)) {
                        mInner = new Dictionary<string, object>();
                        _modelBindings[modelType] = mInner;
                    }
                    mInner[key] = list[0];
                } else {
                    // Collided: evicted from model lookup
                    if (_modelBindings.TryGetValue(modelType, out var mInner)) {
                        mInner.Remove(key);
                    }
                }
            }

            for (int i = 0; i < _pruneScratch.Count; i++) {
                keyDict.Remove(_pruneScratch[i]);
            }
            _pruneScratch.Clear();

            if (keyDict.Count == 0) {
                _liveModelInstances.Remove(modelType);
                _modelBindings.Remove(modelType);
            }
        }

        private void PruneAllLogicOwners() {
            var deadLogics = new List<object>();
            foreach (var kvp in _logicToModelMap) {
                if (!IsLive(kvp.Key)) {
                    deadLogics.Add(kvp.Key);
                }
            }
            for (int i = 0; i < deadLogics.Count; i++) {
                var dead = deadLogics[i];
                var mapped = _logicToModelMap[dead];
                RemoveTrackedModel(mapped.ModelType, mapped.Key, mapped.ModelInstance);
                _logicToModelMap.Remove(dead);
            }
        }

        private void SyncLiveModel(Type modelType, string key, object model) {
            if (!_liveModelInstances.TryGetValue(modelType, out var mKeyDict)) {
                mKeyDict = new Dictionary<string, List<object>>();
                _liveModelInstances[modelType] = mKeyDict;
            }
            if (!mKeyDict.TryGetValue(key, out var mList)) {
                mList = new List<object>();
                mKeyDict[key] = mList;
            }
            bool exists = false;
            for (int i = 0; i < mList.Count; i++) {
                if (ReferenceEquals(mList[i], model)) {
                    exists = true;
                    break;
                }
            }
            if (!exists) {
                mList.Add(model);
            }

            if (!_modelBindings.TryGetValue(modelType, out var mInner)) {
                mInner = new Dictionary<string, object>();
                _modelBindings[modelType] = mInner;
            }

            if (mList.Count == 1) {
                mInner[key] = model;
            } else {
                mInner.Remove(key);
            }
        }

        private void RemoveTrackedModel(Type modelType, string key, object model) {
            if (_liveModelInstances.TryGetValue(modelType, out var mKeyDict)) {
                if (mKeyDict.TryGetValue(key, out var mList)) {
                    for (int i = mList.Count - 1; i >= 0; i--) {
                        if (ReferenceEquals(mList[i], model) || !IsLive(mList[i])) {
                            mList.RemoveAt(i);
                        }
                    }

                    if (!_modelBindings.TryGetValue(modelType, out var mInner)) {
                        mInner = new Dictionary<string, object>();
                        _modelBindings[modelType] = mInner;
                    }

                    if (mList.Count == 0) {
                        mKeyDict.Remove(key);
                        mInner.Remove(key);
                    } else if (mList.Count == 1) {
                        mInner[key] = mList[0];
                    } else {
                        mInner.Remove(key);
                    }
                }
                if (mKeyDict.Count == 0) {
                    _liveModelInstances.Remove(modelType);
                    _modelBindings.Remove(modelType);
                }
            }
        }

        public bool IsRegistered<T>(string name = null) => IsRegistered(typeof(T), name);

        public bool IsRegistered<T>(T service, string name = null) {
            if (service == null) return false;
            var type = service.GetType();
            return IsRegistered(type, ResolveKey(type, service, name));
        }

        public bool IsRegistered(Type type, string name = null) {
            PruneType(type);
            if (!_serviceBindings.TryGetValue(type, out var inner)) return false;
            string key = string.IsNullOrEmpty(name) ? type.Name : name;
            return inner.TryGetValue(key, out var value) && IsLive(value);
        }

        public object Get(Type bindingType, string name = null) {
            PruneType(bindingType);

            string effectiveKey = !string.IsNullOrEmpty(name) ? name : bindingType.Name;

            // Collision check on specific key: if the target key has multiple live instances, it was evicted!
            if (_liveInstances.TryGetValue(bindingType, out var keyDict) &&
                keyDict.TryGetValue(effectiveKey, out var liveList) &&
                liveList.Count > 1) {
                if (enableLogging) {
                    Debug.LogError($"[ServiceLocator] Ambiguous Get request for type '{bindingType.FullName}' with key '{effectiveKey}'. Multiple live instances ({liveList.Count}) shared this Name and were evicted from singleton lookup. Assign distinct singletonName values on each BaseLogic instance in the Inspector.");
                }
                return null;
            }

            if (!string.IsNullOrEmpty(name)) {
                if (_serviceBindings.TryGetValue(bindingType, out var innerNamed) &&
                    innerNamed.TryGetValue(name, out object named) && IsLive(named)) {
                    return named;
                }
                return null;
            }

            // Unnamed Get request:
            if (!_serviceBindings.TryGetValue(bindingType, out var inner) || inner.Count == 0) {
                return null;
            }

            string defaultKey = bindingType.Name;
            if (inner.TryGetValue(defaultKey, out object obj) && IsLive(obj)) {
                return obj;
            }

            // Fallback: if no default key was registered, but exactly 1 instance is registered across all keys, return it.
            // Check if any key for this type has collided (> 1 live instance); if so, unnamed fallback is ambiguous and must be blocked.
            if (_liveInstances.TryGetValue(bindingType, out var allKeyDict)) {
                foreach (var kvp in allKeyDict) {
                    if (kvp.Value.Count > 1) {
                        if (enableLogging) {
                            Debug.LogError($"[ServiceLocator] Ambiguous Get request for type '{bindingType.FullName}'. Key '{kvp.Key}' has multiple live instances ({kvp.Value.Count}) and was evicted from singleton lookup. Assign distinct singletonName values on each BaseLogic instance in the Inspector.");
                        }
                        return null;
                    }
                }
            }

            if (inner.Count == 1) {
                foreach (var single in inner.Values) {
                    if (IsLive(single)) return single;
                }
            }

            if (enableLogging) {
                Debug.LogError($"[ServiceLocator] Ambiguous Get request for type '{bindingType.FullName}' with no name. Multiple instances are registered ({inner.Count}).");
            }
            return null;
        }

        public T Get<T>(string name = null) {
            var obj = Get(typeof(T), name);
            return obj == null ? default : (T)obj;
        }

        public M GetModel<M>(string name = null) where M : BaseObservableDataModel {
            Type modelType = typeof(M);
            PruneModelType(modelType);

            if (!string.IsNullOrEmpty(name)) {
                // Collision check on named model key
                if (_liveModelInstances.TryGetValue(modelType, out var keyDict) &&
                    keyDict.TryGetValue(name, out var liveList) &&
                    liveList.Count > 1) {
                    if (enableLogging) {
                        Debug.LogError($"[ServiceLocator] Ambiguous GetModel request for model type '{modelType.FullName}' with key '{name}'. Multiple live models ({liveList.Count}) shared this logic Name and were evicted. Assign distinct singletonName values on each BaseLogic instance in the Inspector.");
                    }
                    return null;
                }

                if (_modelBindings.TryGetValue(modelType, out var inner) &&
                    inner.TryGetValue(name, out var named) && IsLive(named)) {
                    return (M)named;
                }
                return null;
            }

            // Unnamed GetModel request:
            // Check if ANY key for this model type has collided (> 1 live instance)
            if (_liveModelInstances.TryGetValue(modelType, out var allKeyDict)) {
                foreach (var kvp in allKeyDict) {
                    if (kvp.Value.Count > 1) {
                        if (enableLogging) {
                            Debug.LogError($"[ServiceLocator] Ambiguous GetModel request for model type '{modelType.FullName}'. Key '{kvp.Key}' has multiple live instances ({kvp.Value.Count}) and was evicted from singleton lookup. Assign distinct singletonName values on each BaseLogic instance in the Inspector.");
                        }
                        return null;
                    }
                }
            }

            if (_modelBindings.TryGetValue(modelType, out var modelInner) && modelInner.Count > 0) {
                if (modelInner.Count == 1) {
                    foreach (var single in modelInner.Values) {
                        if (IsLive(single)) return (M)single;
                    }
                }

                if (enableLogging) {
                    Debug.LogError($"[ServiceLocator] Ambiguous GetModel request for model type '{modelType.FullName}' with no name. Multiple models of this type are registered across distinct logic units ({modelInner.Count}). Provide the logic Name.");
                }
                return null;
            }

            return null;
        }

        public void Remove<T>(string name = null) {
            Remove(typeof(T), name);
        }

        public void Remove(Type bindingType, string name = null) {
            string key = string.IsNullOrEmpty(name) ? bindingType.Name : name;

            if (_liveInstances.TryGetValue(bindingType, out var keyDict)) {
                if (keyDict.TryGetValue(key, out var list)) {
                    for (int i = 0; i < list.Count; i++) {
                        var item = list[i];
                        if (_logicToModelMap.TryGetValue(item, out var mapped)) {
                            RemoveTrackedModel(mapped.ModelType, mapped.Key, mapped.ModelInstance);
                            _logicToModelMap.Remove(item);
                        }
                    }
                    keyDict.Remove(key);
                }
                if (keyDict.Count == 0) _liveInstances.Remove(bindingType);
            }

            if (_serviceBindings.TryGetValue(bindingType, out var inner)) {
                inner.Remove(key);
                if (inner.Count == 0) _serviceBindings.Remove(bindingType);
            }
        }

        /// <summary>
        /// Unregisters a specific <see cref="ISharedSingleton"/> instance and reconciles its binding key.
        /// If multiple colliding instances existed and one is unregistered, any single surviving instance
        /// recovers lookup cleanly. If the last instance is unregistered, the binding is completely removed.
        /// </summary>
        public void Unregister(ISharedSingleton instance, string name = null) {
            if (instance == null) return;
            Type type = instance.GetType();
            string key = ResolveKey(type, instance, name);

            if (_liveInstances.TryGetValue(type, out var keyDict)) {
                // If not found under resolved key, search other keys in case it was registered with an explicit name
                if (!keyDict.ContainsKey(key)) {
                    foreach (var kvp in keyDict) {
                        for (int i = 0; i < kvp.Value.Count; i++) {
                            if (ReferenceEquals(kvp.Value[i], instance)) {
                                key = kvp.Key;
                                break;
                            }
                        }
                    }
                }

                if (keyDict.TryGetValue(key, out var list)) {
                    for (int i = list.Count - 1; i >= 0; i--) {
                        var item = list[i];
                        if (ReferenceEquals(item, instance) || !IsLive(item)) {
                            if (item is BaseLogic l && _logicToModelMap.TryGetValue(l, out var mapped)) {
                                RemoveTrackedModel(mapped.ModelType, mapped.Key, mapped.ModelInstance);
                                _logicToModelMap.Remove(l);
                            }
                            list.RemoveAt(i);
                        }
                    }

                    if (!_serviceBindings.TryGetValue(type, out var sInner)) {
                        sInner = new Dictionary<string, object>();
                        _serviceBindings[type] = sInner;
                    }

                    if (list.Count == 0) {
                        keyDict.Remove(key);
                        sInner.Remove(key);
                    } else if (list.Count == 1) {
                        sInner[key] = list[0];

                        // Recover model index if this single live instance is BaseLogic
                        if (list[0] is BaseLogic survivingLogic) {
                            var model = survivingLogic.GetUntypedModel();
                            if (IsLive(model)) {
                                SyncLiveModel(model.GetType(), key, model);
                                _logicToModelMap[survivingLogic] = (model.GetType(), key, model);
                            }
                        }
                    } else {
                        // Still collided (> 1 live instance)
                        sInner.Remove(key);
                    }

                    if (sInner.Count == 0) {
                        _serviceBindings.Remove(type);
                    }
                }

                if (keyDict.Count == 0) {
                    _liveInstances.Remove(type);
                    _serviceBindings.Remove(type);
                }
            } else {
                if (instance is BaseLogic l && _logicToModelMap.TryGetValue(l, out var mapped)) {
                    RemoveTrackedModel(mapped.ModelType, mapped.Key, mapped.ModelInstance);
                    _logicToModelMap.Remove(l);
                }
            }
        }

        /// <summary>
        /// Registers <paramref name="service"/> under its runtime type and <paramref name="name"/>.
        /// Only concrete MonoBehaviour classes implementing <see cref="ISharedSingleton"/> may be registered.
        /// Rejects Presenters, Models, interfaces, plain C# classes, and null/destroyed objects.
        /// Handles collision eviction when multiple live instances share the same key.
        /// Also indexes the model of any <see cref="BaseLogic"/> instance into model bindings.
        /// </summary>
        public RegistrationResult TryRegister(object service, string name = null) {
            if (!IsLive(service)) return RegistrationResult.Rejected;

            if (service is IObservablePresenter || service is BaseObservableDataModel ||
                IsSubclassOfRawGeneric(typeof(BasePresenter<>), service.GetType())) {
                if (enableLogging) {
                    Debug.LogError($"[ServiceLocator] Type '{service.GetType().FullName}' is rejected from ServiceLocator registration. Presenters and Models must never register as shared singletons; register BaseLogic or custom MonoBehaviour services instead.");
                }
                return RegistrationResult.Rejected;
            }

            if (!(service is MonoBehaviour)) {
                if (enableLogging) {
                    Debug.LogError($"[ServiceLocator] Type '{service.GetType().FullName}' is not a MonoBehaviour. Only concrete MonoBehaviour classes implementing ISharedSingleton may be registered in ServiceLocator.");
                }
                return RegistrationResult.Rejected;
            }

            if (!(service is ISharedSingleton)) {
                if (enableLogging) {
                    Debug.LogError($"[ServiceLocator] Type '{service.GetType().FullName}' does not implement ISharedSingleton and cannot be registered in ServiceLocator. Only BaseLogic or custom MonoBehaviour services implementing ISharedSingleton may register.");
                }
                return RegistrationResult.Rejected;
            }

            Type type = service.GetType();
            if (type.IsInterface) {
                if (enableLogging) {
                    Debug.LogError($"[ServiceLocator] Interface '{type.FullName}' cannot be registered. Only concrete MonoBehaviour classes may be shared singletons.");
                }
                return RegistrationResult.Rejected;
            }

            string key = ResolveKey(type, service, name);

            PruneType(type);

            if (!_liveInstances.TryGetValue(type, out var keyDict)) {
                keyDict = new Dictionary<string, List<object>>();
                _liveInstances[type] = keyDict;
            }

            if (!keyDict.TryGetValue(key, out var liveList)) {
                liveList = new List<object>();
                keyDict[key] = liveList;
            }

            // Check if this exact live instance is already in liveList
            for (int i = 0; i < liveList.Count; i++) {
                if (ReferenceEquals(liveList[i], service)) {
                    return RegistrationResult.SameInstance;
                }
            }

            liveList.Add(service);

            if (!_serviceBindings.TryGetValue(type, out var sInner)) {
                sInner = new Dictionary<string, object>();
                _serviceBindings[type] = sInner;
            }

            if (liveList.Count == 1) {
                sInner[key] = service;

                // Index model if this service is BaseLogic
                if (service is BaseLogic logic) {
                    var model = logic.GetUntypedModel();
                    if (model != null) {
                        SyncLiveModel(model.GetType(), key, model);
                        _logicToModelMap[logic] = (model.GetType(), key, model);
                    }
                }

                return RegistrationResult.Added;
            } else {
                // Collision: 2 or more live instances share the same key. Evict from lookup!
                sInner.Remove(key);

                if (service is BaseLogic logic) {
                    var model = logic.GetUntypedModel();
                    if (model != null) {
                        SyncLiveModel(model.GetType(), key, model);
                        _logicToModelMap[logic] = (model.GetType(), key, model);
                    }
                }

                return RegistrationResult.EvictedDuplicate;
            }
        }

        private static bool IsSubclassOfRawGeneric(Type generic, Type toCheck) {
            while (toCheck != null && toCheck != typeof(object)) {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (generic == cur) {
                    return true;
                }
                toCheck = toCheck.BaseType;
            }
            return false;
        }

        public T Add<T>(T service, string name = null) {
            var result = TryRegister(service, name);
            return (result == RegistrationResult.Added || result == RegistrationResult.SameInstance) ? service : default;
        }

        private void ValidateSharedSingletonTypes() {
#if UNITY_EDITOR
            foreach (var t in TypeCache.GetTypesDerivedFrom<ISharedSingleton>()) {
                if (t.IsInterface) {
                    if (enableLogging) {
                        Debug.LogError($"[ServiceLocator] Interface '{t.FullName}' must not derive from ISharedSingleton. Only concrete classes may be shared singletons.");
                    }
                } else if (typeof(IObservablePresenter).IsAssignableFrom(t) || IsSubclassOfRawGeneric(typeof(BasePresenter<>), t)) {
                    if (enableLogging) {
                        Debug.LogError($"[ServiceLocator] Presenter class '{t.FullName}' must not implement ISharedSingleton. Presenters are rejected from ServiceLocator; only BaseLogic or custom MonoBehaviour services may be shared singletons.");
                    }
                } else if (typeof(BaseObservableDataModel).IsAssignableFrom(t)) {
                    if (enableLogging) {
                        Debug.LogError($"[ServiceLocator] Model class '{t.FullName}' must not implement ISharedSingleton. Models are rejected from ServiceLocator; access models via Services.GetModel<M>() or owning Logic.");
                    }
                }
            }
#endif
        }

        /// <summary>
        /// Registers every scene <see cref="ISharedSingleton"/> in Pass 1 and then binds all scene
        /// <see cref="IObservablePresenter"/> components in Pass 2.
        /// Because Pass 1 finishes before Pass 2 starts, <see cref="Services.Get{T}"/> is guaranteed
        /// ready inside every <see cref="BaseObservablePresenter{M}.InitializePresenter"/>.
        /// </summary>
        public bool InitializeSharedSingletons() {
            ValidateSharedSingletonTypes();

            var allMonoBehaviours = new List<MonoBehaviour>();
            var scene = gameObject.scene;
            if (scene.IsValid() && scene.isLoaded) {
                var roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++) {
                    if (roots[r] != null) {
                        allMonoBehaviours.AddRange(roots[r].GetComponentsInChildren<MonoBehaviour>(true));
                    }
                }
            } else {
                var found = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
                if (found != null) {
                    for (int f = 0; f < found.Length; f++) {
                        var item = found[f];
                        if (item != null) allMonoBehaviours.Add(item);
                    }
                }
            }

            // Pass 1: Register all ISharedSingleton services & Logics FIRST
            for (int i = 0; i < allMonoBehaviours.Count; i++) {
                var mb = allMonoBehaviours[i];
                if (mb == null) continue;

                if (mb is ISharedSingleton service) {
                    TryRegister(service);
                }
            }

            // Pass 2: Initialize and bind all scene IObservablePresenter components SECOND
            for (int i = 0; i < allMonoBehaviours.Count; i++) {
                var mb = allMonoBehaviours[i];
                if (mb == null) continue;

                if (mb is IObservablePresenter) {
                    BindPresenter(mb);
                }
            }

            return true;
        }

        /// <summary>Calls EnsureInitialized on the presenter component once.</summary>
        private bool BindPresenter(MonoBehaviour mb) {
            if (!(mb is IObservablePresenter p)) return false;

            var models = mb.GetComponents<BaseObservableDataModel>();
            if (models.Length > 1) {
                if (enableLogging) {
                    Debug.LogError($"[ServiceLocator] GameObject '{mb.gameObject.name}' has multiple BaseObservableDataModel components ({models.Length}). Exactly one model per presenter GameObject is allowed.", mb);
                }
            }

            p.EnsureInitialized();
            return true;
        }
    }
}
