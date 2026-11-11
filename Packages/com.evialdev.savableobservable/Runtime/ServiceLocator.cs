using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Scripting.LifecycleManagement;
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
        /// <summary>Another live instance already holds the key. The first registration keeps it; this one was not registered and an error was logged.</summary>
        Duplicate,
        /// <summary>The service is null, destroyed, or of a forbidden type (e.g. Presenter or Model).</summary>
        Rejected
    }

    /// <summary>
    /// Instance-owned service registry.
    /// Lookup is by exact concrete type and key: registers <c>service.GetType()</c>, not base types or interfaces.
    /// Manages shared singletons (<see cref="BaseLogic"/> and custom non-MMVC <see cref="MonoBehaviour"/> services).
    /// The model of a registered <see cref="BaseLogic"/> is resolvable via <see cref="GetModel{M}"/> (derived from the
    /// registered Logic, never stored separately).
    /// A key is held by one live instance: a second live instance with the same key is an error, it is not registered
    /// and the first instance keeps the key. Multi-instance units (pooled prefabs, board cells) either override
    /// <c>RegisterAsSharedSingleton =&gt; false</c> or get distinct <c>singletonName</c> values.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public partial class ServiceLocator : MonoBehaviour {

        [SerializeField] private bool enableLogging = true;

        [AutoStaticsCleanup]
        public static ServiceLocator instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnSubsystemRegistration() {
            instance = null;
        }

        // The only store: (concrete type, key) -> service instance.
        private readonly Dictionary<(Type Type, string Key), object> _services = new Dictionary<(Type, string), object>();

        // Cache of Type -> Type.Name to avoid allocating strings on unnamed lookups.
        private readonly Dictionary<Type, string> _defaultKeyCache = new Dictionary<Type, string>();

        // Instances already reported as duplicates, so scene Pass 1 and the later Awake of the same unit log once.
        private readonly HashSet<object> _reportedDuplicates = new HashSet<object>();

        private void Awake() {
            if (instance == null) {
                instance = this;
            }
            InitializeSharedSingletons();
        }

        private void OnDestroy() {
            _services.Clear();
            _defaultKeyCache.Clear();
            _reportedDuplicates.Clear();
            if (ReferenceEquals(instance, this)) {
                instance = null;
            }
        }

        private string GetDefaultKey(Type type) {
            if (!_defaultKeyCache.TryGetValue(type, out var key)) {
                key = type.Name;
                _defaultKeyCache[type] = key;
            }
            return key;
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

        private static string Describe(object service) {
            if (service is Component c && c != null) return $"GameObject '{c.gameObject.name}'";
            return service?.GetType().Name ?? "null";
        }

        /// <summary>Returns the live service stored under (type, key); a destroyed entry is dropped.</summary>
        private object Lookup(Type type, string key) {
            if (!_services.TryGetValue((type, key), out var value)) return null;
            if (IsLive(value)) return value;
            _services.Remove((type, key));
            return null;
        }

        public bool IsRegistered<T>(string name = null) => IsRegistered(typeof(T), name);

        public bool IsRegistered<T>(T service, string name = null) {
            if (service == null) return false;
            var type = service.GetType();
            return IsRegistered(type, ResolveKey(type, service, name));
        }

        public bool IsRegistered(Type type, string name = null) {
            string key = string.IsNullOrEmpty(name) ? GetDefaultKey(type) : name;
            return Lookup(type, key) != null;
        }

        /// <summary>
        /// Resolves a service by exact concrete type. With a <paramref name="name"/>, only that key is considered.
        /// Without a name, the type's default key is used; if it is not registered, the sole registered instance of the
        /// type is returned. Several instances under different keys and no name is ambiguous: an error is logged and
        /// <c>null</c> is returned.
        /// </summary>
        public object Get(Type bindingType, string name = null) {
            if (!string.IsNullOrEmpty(name)) {
                return Lookup(bindingType, name);
            }

            var byDefaultKey = Lookup(bindingType, GetDefaultKey(bindingType));
            if (byDefaultKey != null) return byDefaultKey;

            object only = null;
            int count = 0;
            List<(Type, string)> dead = null;
            foreach (var kvp in _services) {
                if (kvp.Key.Type != bindingType) continue;
                if (!IsLive(kvp.Value)) {
                    (dead ?? (dead = new List<(Type, string)>())).Add(kvp.Key);
                    continue;
                }
                only = kvp.Value;
                count++;
            }
            if (dead != null) {
                for (int i = 0; i < dead.Count; i++) _services.Remove(dead[i]);
            }

            if (count == 1) return only;

            if (count > 1 && enableLogging) {
                var keys = new List<string>(count);
                foreach (var kvp in _services) {
                    if (kvp.Key.Type == bindingType && IsLive(kvp.Value)) {
                        keys.Add(kvp.Key.Key);
                    }
                }
                Debug.LogError($"[ServiceLocator] Ambiguous Get request for type '{bindingType.FullName}' with no name. {keys.Count} instances are registered under different keys ({string.Join(", ", keys)}). Pass the name of the instance you want.");
            }
            return null;
        }

        public T Get<T>(string name = null) {
            var obj = Get(typeof(T), name);
            return obj == null ? default : (T)obj;
        }

        /// <summary>
        /// Resolves the model of a registered <see cref="BaseLogic"/> by exact model type.
        /// With a <paramref name="name"/> (the Logic's registration key) only that Logic is considered; without a name
        /// the model must be unique among registered Logic units, otherwise an error is logged and <c>null</c> is returned.
        /// Returns <c>null</c> when the owning Logic is destroyed or not registered.
        /// </summary>
        public M GetModel<M>(string name = null) where M : BaseObservableDataModel {
            Type modelType = typeof(M);
            M found = null;
            int count = 0;
            List<(Type, string)> dead = null;

            foreach (var kvp in _services) {
                if (!(kvp.Value is BaseLogic logic)) continue;
                if (!IsLive(logic)) {
                    (dead ?? (dead = new List<(Type, string)>())).Add(kvp.Key);
                    continue;
                }
                if (logic.ModelType != modelType) continue;

                var model = logic.GetUntypedModel();
                if (!IsLive(model)) continue;

                if (!string.IsNullOrEmpty(name)) {
                    if (kvp.Key.Key == name) {
                        found = (M)model;
                        break;
                    }
                    continue;
                }

                found = (M)model;
                count++;
            }

            if (dead != null) {
                for (int i = 0; i < dead.Count; i++) _services.Remove(dead[i]);
            }

            if (!string.IsNullOrEmpty(name)) return found;
            if (count <= 1) return found;

            if (enableLogging) {
                var keys = new List<string>(count);
                foreach (var kvp in _services) {
                    if (kvp.Value is BaseLogic logic && IsLive(logic) && logic.ModelType == modelType && IsLive(logic.GetUntypedModel())) {
                        keys.Add(kvp.Key.Key);
                    }
                }
                Debug.LogError($"[ServiceLocator] Ambiguous GetModel request for model type '{modelType.FullName}' with no name. {keys.Count} registered Logic units own a model of this type ({string.Join(", ", keys)}). Pass the Logic name.");
            }
            return null;
        }

        /// <summary>
        /// Unregisters a specific <see cref="ISharedSingleton"/> instance. The entry is removed only if the stored
        /// value for its key is that very instance; an instance that was never registered (for example a rejected
        /// duplicate) is a no-op and never evicts the instance that holds the key.
        /// </summary>
        public void Unregister(ISharedSingleton instance, string name = null) {
            if (instance == null) return;
            _reportedDuplicates.Remove(instance);

            Type type = instance.GetType();
            string key = ResolveKey(type, instance, name);

            if (_services.TryGetValue((type, key), out var stored)) {
                if (ReferenceEquals(stored, instance)) {
                    _services.Remove((type, key));
                }
                return;
            }

            // Registered with an explicit name that differs from the resolved key: find it by reference.
            if (!string.IsNullOrEmpty(name)) return;
            (Type, string)? match = null;
            foreach (var kvp in _services) {
                if (kvp.Key.Type == type && ReferenceEquals(kvp.Value, instance)) {
                    match = kvp.Key;
                    break;
                }
            }
            if (match.HasValue) _services.Remove(match.Value);
        }

        /// <summary>
        /// Registers <paramref name="service"/> under its runtime type and <paramref name="name"/>.
        /// Only concrete MonoBehaviour classes implementing <see cref="ISharedSingleton"/> may be registered.
        /// Rejects Presenters, Models, interfaces, plain C# classes, and null/destroyed objects.
        /// If another live instance already holds the key, an error is logged, the service is NOT registered and the
        /// first instance keeps the key (<see cref="RegistrationResult.Duplicate"/>). A destroyed holder is replaced.
        /// </summary>
        public RegistrationResult TryRegister(object service, string name = null) {
            if (!IsLive(service)) return RegistrationResult.Rejected;

            if (service is IObservablePresenter || service is BaseObservableDataModel) {
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

            if (_services.TryGetValue((type, key), out var existing) && IsLive(existing)) {
                if (ReferenceEquals(existing, service)) {
                    return RegistrationResult.SameInstance;
                }

                if (enableLogging && _reportedDuplicates.Add(service)) {
                    Debug.LogError($"[ServiceLocator] Duplicate registration of '{type.FullName}' with key '{key}': {Describe(existing)} already holds this key, {Describe(service)} was NOT registered. " +
                        "Assign a distinct singletonName to each instance, or override RegisterAsSharedSingleton => false for multi-instance (pooled / repeated) units.");
                }
                return RegistrationResult.Duplicate;
            }

            _services[(type, key)] = service;
            return RegistrationResult.Added;
        }

        private void ValidateSharedSingletonTypes() {
#if UNITY_EDITOR
            foreach (var t in TypeCache.GetTypesDerivedFrom<ISharedSingleton>()) {
                if (t.IsInterface) {
                    if (enableLogging) {
                        Debug.LogError($"[ServiceLocator] Interface '{t.FullName}' must not derive from ISharedSingleton. Only concrete classes may be shared singletons.");
                    }
                } else if (typeof(IObservablePresenter).IsAssignableFrom(t)) {
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
            bool rootsAcquired = false;
            if (scene.IsValid() && (Application.isPlaying || scene.isLoaded)) {
                try {
                    var roots = scene.GetRootGameObjects();
                    if (roots != null) {
                        for (int r = 0; r < roots.Length; r++) {
                            if (roots[r] != null) {
                                allMonoBehaviours.AddRange(roots[r].GetComponentsInChildren<MonoBehaviour>(true));
                            }
                        }
                        rootsAcquired = true;
                    }
                } catch (ArgumentException) {
                    rootsAcquired = false;
                }
            }

            if (!rootsAcquired) {
                var found = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
                if (found != null) {
                    for (int f = 0; f < found.Length; f++) {
                        var item = found[f];
                        if (item != null && (!scene.IsValid() || item.gameObject.scene == scene)) {
                            allMonoBehaviours.Add(item);
                        }
                    }
                }
            }

            // Pass 1: Register all ISharedSingleton services & Logics FIRST
            for (int i = 0; i < allMonoBehaviours.Count; i++) {
                var mb = allMonoBehaviours[i];
                if (mb == null) continue;
                if (mb is BaseLogic logic && !logic.ShouldRegisterAsSharedSingleton) continue;

                if (mb is ISharedSingleton service) {
                    TryRegister(service);
                }
            }

            // Pass 2: Initialize and bind all scene IObservablePresenter components SECOND.
            // The single-model / single-presenter rule is validated once, by Observable.SetListeners.
            for (int i = 0; i < allMonoBehaviours.Count; i++) {
                var mb = allMonoBehaviours[i];
                if (mb == null) continue;

                if (mb is IObservablePresenter p) {
                    p.EnsureInitialized();
                }
            }

            return true;
        }
    }
}
