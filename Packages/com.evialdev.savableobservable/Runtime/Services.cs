using System;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
using Unity.Scripting.LifecycleManagement;
#endif

namespace SavableObservable {

    /// <summary>
    /// Static facade over the scene's <see cref="ServiceLocator"/>. Lookup is by exact concrete type:
    /// <c>Add&lt;T&gt;</c> registers <c>service.GetType()</c>, not <c>typeof(T)</c>, and interfaces/base types are not resolved.
    /// Also supports direct model lookup via <see cref="GetModel{M}"/>.
    /// </summary>
    public static partial class Services {
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [AutoStaticsCleanup]
#endif
        private static ServiceLocator _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnSubsystemRegistration() {
            _instance = null;
        }

        public struct TestState {
            public readonly ServiceLocator Locator;
            public readonly ServiceLocator CachedLocator;

            public TestState(ServiceLocator locator, ServiceLocator cachedLocator) {
                Locator = locator;
                CachedLocator = cachedLocator;
            }
        }

        public static TestState CaptureTestState() => new TestState(ServiceLocator.instance, _instance);

        public static void SetTestLocator(ServiceLocator locator) {
            ServiceLocator.instance = locator;
            _instance = locator;
        }

        public static void RestoreTestState(TestState state) {
            ServiceLocator.instance = state.Locator;
            _instance = state.CachedLocator;
        }

        public static ServiceLocator instance {
            get {
                // Unity-aware check: a destroyed locator must not stay cached.
                if (_instance == null) {
                    var locator = ServiceLocator.instance;
                    _instance = locator != null ? locator : null;
                }
                return _instance;
            }
        }

        /// <summary>Called by a destroyed locator; clears the cache only when it points at that locator.</summary>
        internal static void ReleaseLocator(ServiceLocator locator) {
            if (ReferenceEquals(_instance, locator)) {
                _instance = null;
            }
        }

        public static bool IsRegistered<T>(string name = null) => instance != null && instance.IsRegistered<T>(name);
        public static bool IsRegistered<T>(T service, string name = null) => instance != null && instance.IsRegistered(service, name);
        public static bool IsRegistered(Type type, string name = null) => instance != null && instance.IsRegistered(type, name);
        public static T Get<T>(string name = null) => instance != null ? instance.Get<T>(name) : default;
        public static object Get(Type type, string name = null) => instance != null ? instance.Get(type, name) : null;
        public static M GetModel<M>(string name = null) where M : BaseObservableDataModel => instance != null ? instance.GetModel<M>(name) : null;

        /// <summary>Registers the service; returns it when registered (new or same instance), null when rejected.</summary>
        public static object Add<T>(T service, string name = null) => instance != null ? instance.Add(service, name) : null;
        public static RegistrationResult TryRegister(object service, string name = null) =>
            instance != null ? instance.TryRegister(service, name) : RegistrationResult.Rejected;

        public static void Remove<T>(string name = null) => instance?.Remove<T>(name);
        public static void Remove(Type type, string name = null) => instance?.Remove(type, name);
        public static void Unregister(ISharedSingleton service, string name = null) => instance?.Unregister(service, name);

        /// <summary>Registers and binds all scene shared singletons and presenters.</summary>
        public static bool InitializeSharedSingletons() => instance != null && instance.InitializeSharedSingletons();
    }
}
