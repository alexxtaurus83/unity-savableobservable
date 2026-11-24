using System;

namespace SavableObservable {

    /// <summary>
    /// Static facade over the scene's <see cref="ServiceLocator"/>. Lookup is by exact concrete type
    /// (<c>Get&lt;T&gt;</c> resolves <c>typeof(T)</c>, interfaces and base types are not resolved).
    /// Also supports direct model lookup via <see cref="GetModel{M}"/>.
    /// The facade reads <see cref="ServiceLocator.instance"/> directly, so a destroyed locator is never reused.
    /// </summary>
    public static partial class Services {

        /// <summary>Snapshot of the active locator, for tests that swap it temporarily.</summary>
        public struct TestState {
            public readonly ServiceLocator Locator;

            public TestState(ServiceLocator locator) {
                Locator = locator;
            }
        }

        public static TestState CaptureTestState() => new TestState(ServiceLocator.instance);

        public static void SetTestLocator(ServiceLocator locator) {
            ServiceLocator.instance = locator;
        }

        public static void RestoreTestState(TestState state) {
            ServiceLocator.instance = state.Locator;
        }

        /// <summary>The active locator, or <c>null</c> when there is none or it was destroyed (Unity-aware check).</summary>
        public static ServiceLocator instance {
            get {
                var locator = ServiceLocator.instance;
                return locator != null ? locator : null;
            }
        }

        public static bool IsRegistered<T>(string name = null) => instance != null && instance.IsRegistered<T>(name);
        public static bool IsRegistered<T>(T service, string name = null) => instance != null && instance.IsRegistered(service, name);
        public static bool IsRegistered(Type type, string name = null) => instance != null && instance.IsRegistered(type, name);
        public static T Get<T>(string name = null) => instance != null ? instance.Get<T>(name) : default;
        public static object Get(Type type, string name = null) => instance != null ? instance.Get(type, name) : null;
        public static M GetModel<M>(string name = null) where M : BaseObservableDataModel => instance != null ? instance.GetModel<M>(name) : null;

        public static RegistrationResult TryRegister(object service, string name = null) =>
            instance != null ? instance.TryRegister(service, name) : RegistrationResult.Rejected;

        public static void Unregister(ISharedSingleton service, string name = null) {
            if (instance != null) {
                instance.Unregister(service, name);
            }
        }

        /// <summary>Registers and binds all scene shared singletons and presenters.</summary>
        public static bool InitializeSharedSingletons() => instance != null && instance.InitializeSharedSingletons();

        /// <summary>
        /// Publishes <paramref name="evt"/> on the active locator's event bus (see <see cref="MmvcEventBus"/>).
        /// Call it from Logic or services only; presenters and models never publish (analyzer <c>MMVC016</c>).
        /// Without a live locator nothing is subscribed, so nothing happens.
        /// </summary>
        public static void Publish<TEvent>(in TEvent evt) where TEvent : struct, IMmvcEvent {
            var locator = instance;
            if (locator != null) locator.EventBus.Publish(in evt);
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> on behalf of <paramref name="subscriber"/> (Logic, service or presenter; never a model).
        /// Dispose the returned handle (e.g. through a <see cref="SubscriptionSet"/>) to unsubscribe; the subscription also ends when
        /// <paramref name="subscriber"/> is destroyed or the locator is destroyed. Without a live locator an error is logged and a
        /// no-op handle is returned.
        /// </summary>
        public static IDisposable Subscribe<TEvent>(UnityEngine.Component subscriber, Action<TEvent> handler) where TEvent : struct, IMmvcEvent {
            var locator = instance;
            if (locator == null) {
                UnityEngine.Debug.LogError($"[Services] Subscribe<{typeof(TEvent).Name}> called without a live ServiceLocator; the subscription was not created.", subscriber);
                return new Subscription(null);
            }
            return locator.EventBus.Subscribe(subscriber, handler);
        }

        /// <summary>Removes every subscription of <paramref name="handler"/> to <typeparamref name="TEvent"/>.</summary>
        public static void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : struct, IMmvcEvent {
            var locator = instance;
            if (locator != null) locator.EventBus.Unsubscribe(handler);
        }

        /// <summary>Removes every event-bus subscription of <paramref name="subscriber"/>, including its <see cref="EventHandlerAttribute"/> bindings.</summary>
        public static void Unsubscribe(UnityEngine.Component subscriber) {
            var locator = instance;
            if (locator != null) locator.EventBus.Unsubscribe(subscriber);
        }
    }
}
