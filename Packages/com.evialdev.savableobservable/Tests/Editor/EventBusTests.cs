using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using SavableObservable;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace SavableObservable.Tests {

    public readonly struct BusPing : IMmvcEvent {
        public readonly int Value;
        public BusPing(int value) { Value = value; }
    }

    public readonly struct BusPong : IMmvcEvent {
        public readonly int Value;
        public BusPong(int value) { Value = value; }
    }

    [TestFixture]
    public class EventBusTests {

        private class PingService : MonoBehaviour, ISharedSingleton {
            public readonly List<int> Seen = new List<int>();
            [EventHandler] private void OnPing(BusPing e) => Seen.Add(e.Value);
        }

        private class BusModel : BaseObservableDataModel {
            public ObservableVariable<int> value = new ObservableVariable<int>();
        }

        private class BusPresenter : BaseObservablePresenter<BusModel> {
            public readonly List<int> Seen = new List<int>();
            [ObservableHandler("value")] private void OnValue() { }
            [EventHandler] private void OnPing(in BusPing e) => Seen.Add(e.Value);
        }

        private class HandlerModel : BaseObservableDataModel {
            public ObservableVariable<int> value = new ObservableVariable<int>();
            [EventHandler] private void OnPing(BusPing e) { }
        }

        private class InvalidHandlerService : MonoBehaviour, ISharedSingleton {
            [EventHandler] private void TwoArgs(BusPing a, BusPing b) { }
            [EventHandler] private void NotAnEvent(int value) { }
        }

        private class Counter : MonoBehaviour {
            public int Count;
            public void OnPing(BusPing e) => Count++;
        }

        private readonly List<GameObject> _objects = new List<GameObject>();
        private Services.TestState _state;
        private ServiceLocator _locator;

        [SetUp]
        public void SetUp() {
            _state = Services.CaptureTestState();
            _locator = Create("BusLocator").AddComponent<ServiceLocator>();
            Services.SetTestLocator(_locator);
        }

        [TearDown]
        public void TearDown() {
            for (int i = _objects.Count - 1; i >= 0; i--) {
                if (_objects[i] != null) UnityEngine.Object.DestroyImmediate(_objects[i]);
            }
            _objects.Clear();
            Services.RestoreTestState(_state);
        }

        private GameObject Create(string name) {
            var go = new GameObject(name);
            _objects.Add(go);
            return go;
        }

        private MmvcEventBus Bus => _locator.EventBus;

        [Test]
        public void Subscribe_Publish_Dispose() {
            var owner = Create("Owner").AddComponent<Counter>();
            var seen = new List<int>();

            var handle = Services.Subscribe<BusPing>(owner, e => seen.Add(e.Value));
            Services.Publish(new BusPing(1));
            handle.Dispose();
            handle.Dispose();
            Services.Publish(new BusPing(2));

            CollectionAssert.AreEqual(new[] { 1 }, seen);
            Assert.AreEqual(0, Bus.GetHandlerCount<BusPing>());
        }

        [Test]
        public void UnsubscribeByHandler_AndBySubscriber() {
            var owner = Create("Owner").AddComponent<Counter>();
            Action<BusPing> handler = owner.OnPing;
            Services.Subscribe(owner, handler);
            Services.Subscribe<BusPong>(owner, _ => owner.Count += 100);

            Services.Unsubscribe(handler);
            Services.Publish(new BusPing(1));
            Assert.AreEqual(0, owner.Count);

            Services.Unsubscribe(owner);
            Services.Publish(new BusPong(1));
            Assert.AreEqual(0, owner.Count);
        }

        [Test]
        public void EventHandlerAttribute_OnRegisteredService_BindsOnRegister_UnbindsOnUnregister() {
            var service = Create("PingService").AddComponent<PingService>();

            Assert.AreEqual(RegistrationResult.Added, _locator.TryRegister(service));
            Assert.AreEqual(RegistrationResult.SameInstance, _locator.TryRegister(service));
            Services.Publish(new BusPing(5));
            CollectionAssert.AreEqual(new[] { 5 }, service.Seen, "Bound once; re-registering the same instance does not bind twice.");

            _locator.Unregister(service);
            Services.Publish(new BusPing(6));
            CollectionAssert.AreEqual(new[] { 5 }, service.Seen);
        }

        [Test]
        public void EventHandlerAttribute_OnPresenter_BindsOnSetListeners_UnbindsOnRemoveAndCleanup() {
            var go = Create("Unit");
            var model = go.AddComponent<BusModel>();
            var presenter = go.AddComponent<BusPresenter>();

            Observable.SetListeners(presenter, model);
            Observable.SetListeners(presenter, model);
            Services.Publish(new BusPing(1));
            CollectionAssert.AreEqual(new[] { 1 }, presenter.Seen, "Rebinding replaces the attributed binding (in-parameter handler supported).");

            Observable.RemoveAllSubscriptions(model, presenter);
            Services.Publish(new BusPing(2));
            CollectionAssert.AreEqual(new[] { 1 }, presenter.Seen);

            Observable.SetListeners(presenter, model);
            Observable.CleanupSubscriptions(model);
            Services.Publish(new BusPing(3));
            CollectionAssert.AreEqual(new[] { 1 }, presenter.Seen);
        }

        [Test]
        public void RemoveListenersFromForeignModel_KeepsPresenterBusBindings() {
            var go = Create("Unit");
            var model = go.AddComponent<BusModel>();
            var presenter = go.AddComponent<BusPresenter>();
            var foreign = Create("Foreign").AddComponent<BusModel>();
            foreign.EnsureFieldsInitialized();
            Observable.SetListeners(presenter, model);

            Observable.RemoveListeners(foreign, presenter);
            Services.Publish(new BusPing(7));

            CollectionAssert.AreEqual(new[] { 7 }, presenter.Seen);
        }

        [Test]
        public void Publish_WithoutSubscribers_CreatesNoChannel_AndDoesNotAllocate() {
            Services.Publish(new BusPong(1));
            Assert.AreEqual(0, Bus.GetHandlerCount<BusPong>());
            var evt = new BusPong(2);
            Assert.That(() => Services.Publish(evt), Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void InParameterEventHandler_PublishDoesNotAllocate() {
            var go = Create("Unit");
            var model = go.AddComponent<BusModel>();
            var presenter = go.AddComponent<BusPresenter>();
            Observable.SetListeners(presenter, model);
            presenter.Seen.Capacity = 16;
            var evt = new BusPing(1);
            Services.Publish(evt);

            Assert.That(() => Services.Publish(evt), Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void DestroyedSubscriber_IsPruned() {
            var owner = Create("Owner").AddComponent<Counter>();
            Services.Subscribe<BusPing>(owner, owner.OnPing);
            Assert.AreEqual(1, Bus.GetHandlerCount<BusPing>());

            UnityEngine.Object.DestroyImmediate(owner);
            Assert.AreEqual(0, Bus.GetHandlerCount<BusPing>(), "A destroyed subscriber is not live.");
            Assert.DoesNotThrow(() => Services.Publish(new BusPing(1)));
        }

        [Test]
        public void ThrowingHandler_IsLogged_OthersRun() {
            var owner = Create("Owner").AddComponent<Counter>();
            Services.Subscribe<BusPing>(owner, _ => throw new InvalidOperationException("bus boom"));
            Services.Subscribe<BusPing>(owner, owner.OnPing);
            LogAssert.Expect(LogType.Exception, new Regex("bus boom"));

            Services.Publish(new BusPing(1));

            Assert.AreEqual(1, owner.Count);
        }

        [Test]
        public void NestedPublish_IsQueued_AfterCurrentDispatch() {
            var owner = Create("Owner").AddComponent<Counter>();
            var log = new List<string>();
            Services.Subscribe<BusPing>(owner, e => {
                log.Add("ping-a");
                Services.Publish(new BusPong(e.Value));
            });
            Services.Subscribe<BusPing>(owner, _ => log.Add("ping-b"));
            Services.Subscribe<BusPong>(owner, _ => log.Add("pong"));

            Services.Publish(new BusPing(1));

            CollectionAssert.AreEqual(new[] { "ping-a", "ping-b", "pong" }, log);
        }

        [Test]
        public void ReentrantPublish_IsCappedAt32_WithError() {
            var owner = Create("Owner").AddComponent<Counter>();
            int deliveries = 0;
            Services.Subscribe<BusPing>(owner, e => {
                deliveries++;
                Services.Publish(new BusPing(e.Value + 1));
            });
            LogAssert.Expect(LogType.Error, new Regex("More than 32 events were published"));

            Services.Publish(new BusPing(0));

            Assert.AreEqual(33, deliveries, "The outer publish plus 32 queued ones.");
            Services.Publish(new BusPing(0));
            LogAssert.Expect(LogType.Error, new Regex("More than 32 events were published"));
            Assert.AreEqual(66, deliveries, "The cap resets for the next outer publish.");
        }

        [Test]
        public void Model_SubscribeIsRejected_AndModelEventHandlerIsNotBound() {
            var go = Create("ModelHost");
            var model = go.AddComponent<BusModel>();
            LogAssert.Expect(LogType.Error, new Regex("Model 'BusModel' must not subscribe"));

            var handle = Services.Subscribe<BusPing>(model, _ => Assert.Fail("Model handler must not run."));
            handle.Dispose();
            Services.Publish(new BusPing(1));
            Assert.AreEqual(0, Bus.GetHandlerCount<BusPing>());

            // Logged once per model type when its descriptor is built (Awake): evict the cache so the test does not depend on order.
            EvictCache(typeof(ModelDescriptor), "_cache", "_cacheLock", typeof(HandlerModel));
            LogAssert.Expect(LogType.Error, new Regex(@"\[EventHandler\] 'HandlerModel\.OnPing' is ignored: models"));
            var handlerModel = Create("HandlerModelHost").AddComponent<HandlerModel>();
            handlerModel.EnsureFieldsInitialized();
            Observable.BindEventHandlers(handlerModel, Bus);
            Assert.AreEqual(0, Bus.GetHandlerCount<BusPing>());
        }

        [Test]
        public void InvalidEventHandlerSignature_IsLoggedAndIgnored() {
            // Logged once per subscriber type when its metadata is built: evict the cache so the test does not depend on order.
            EvictCache(typeof(Observable), "_subscriberMetadataCache", "_subscriberMetadataLock", typeof(InvalidHandlerService));
            LogAssert.Expect(LogType.Error, new Regex("InvalidHandlerService.TwoArgs' is ignored: it has 2 parameters"));
            LogAssert.Expect(LogType.Error, new Regex("InvalidHandlerService.NotAnEvent' is ignored: parameter type 'Int32'"));
            var service = Create("Invalid").AddComponent<InvalidHandlerService>();

            _locator.TryRegister(service);

            Assert.AreEqual(0, Bus.GetHandlerCount<BusPing>());
        }

        private static void EvictCache(Type owner, string cacheName, string lockName, Type key) {
            const System.Reflection.BindingFlags Static = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var cacheField = owner.GetField(cacheName, Static);
            var lockField = owner.GetField(lockName, Static);
            Assert.IsNotNull(cacheField, $"{owner.Name}.{cacheName} was renamed: update EvictCache.");
            Assert.IsNotNull(lockField, $"{owner.Name}.{lockName} was renamed: update EvictCache.");
            lock (lockField.GetValue(null)) {
                ((System.Collections.IDictionary)cacheField.GetValue(null)).Remove(key);
            }
        }

        [Test]
        public void EachLocator_OwnsItsBus() {
            var owner = Create("Owner").AddComponent<Counter>();
            Services.Subscribe<BusPing>(owner, owner.OnPing);

            var second = Create("Locator2").AddComponent<ServiceLocator>();
            Services.SetTestLocator(second);
            Services.Publish(new BusPing(1));

            Assert.AreEqual(0, owner.Count, "A new locator (next Play session) starts with an empty bus.");
            Assert.AreEqual(0, second.EventBus.GetHandlerCount<BusPing>());
        }

        [Test]
        public void Publish_WithoutLocator_IsNoOp() {
            Services.SetTestLocator(null);
            Assert.DoesNotThrow(() => Services.Publish(new BusPing(1)));
        }

        [Test]
        public void Publish_DoesNotAllocate_AfterWarmup() {
            var owner = Create("Owner").AddComponent<Counter>();
            Services.Subscribe<BusPing>(owner, owner.OnPing);
            var evt = new BusPing(1);
            Services.Publish(evt);

            Assert.That(() => Services.Publish(evt), Is.Not.AllocatingGCMemory());
        }
    }
}
