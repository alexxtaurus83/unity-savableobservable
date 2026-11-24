using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SavableObservable;

namespace SavableObservable.Tests {
    [TestFixture]
    public class ObservableEventTests {

        private class EventModel : BaseObservableDataModel {
            public ObservableVariable<int> score = new ObservableVariable<int>();
            [NonSaved] public ObservableVariable<int> transient = new ObservableVariable<int>();
            public ObservableList<int> items = new ObservableList<int>();
            public ObservableEvent<int> scored = new ObservableEvent<int>();
            public ObservableEvent<string> pinged;
        }

        private class EventSubscriber : MonoBehaviour {
            public readonly List<int> Scored = new List<int>();
            public int Pings;

            [ObservableHandler("score")] private void OnScore(int value) { }
            [ObservableHandler("transient")] private void OnTransient() { }
            [ObservableHandler("items")] private void OnItems() { }
            [ObservableHandler("scored")] private void OnScored(int value) => Scored.Add(value);
            [ObservableHandler("pinged")] private void OnPinged() => Pings++;
        }

        private class InvokeOnBindEventSubscriber : MonoBehaviour {
            public int Calls;
            [ObservableHandler("scored", InvokeOnBind = true)] private void OnScored(int value) => Calls++;
        }

        private GameObject _host;
        private EventModel _model;

        [SetUp]
        public void SetUp() {
            _host = new GameObject("EventHost");
            _model = _host.AddComponent<EventModel>();
            _model.EnsureFieldsInitialized();
        }

        [TearDown]
        public void TearDown() {
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
        }

        [Test]
        public void Fire_ReachesBoundHandlers_ZeroAndOneParameter() {
            var sub = _host.AddComponent<EventSubscriber>();
            Observable.SetListeners(sub, _model);

            _model.scored.Fire(3);
            _model.scored.Fire(3);
            _model.pinged.Fire("x");

            CollectionAssert.AreEqual(new[] { 3, 3 }, sub.Scored, "Equal payloads are delivered every time.");
            Assert.AreEqual(1, sub.Pings);
        }

        [Test]
        public void Bind_NeverReplaysEvents() {
            _model.scored.Fire(9);
            var sub = _host.AddComponent<EventSubscriber>();

            Observable.SetListeners(sub, _model);

            Assert.IsEmpty(sub.Scored);
            Assert.AreEqual(0, sub.Pings);
        }

        [Test]
        public void InvokeOnBind_OnEventHandler_LogsError_HandlerStaysSubscribed() {
            var sub = _host.AddComponent<InvokeOnBindEventSubscriber>();
            LogAssert.Expect(LogType.Error, new Regex("ObservableEvent, which is never replayed on bind"));

            Observable.SetListeners(sub, _model);
            Assert.AreEqual(0, sub.Calls, "Not replayed.");

            _model.scored.Fire(1);
            Assert.AreEqual(1, sub.Calls, "Still subscribed.");
        }

        [Test]
        public void ForceNotify_FiresDefault() {
            var seen = new List<int>();
            using (_model.scored.Subscribe(seen.Add)) {
                _model.scored.ForceNotify();
            }
            CollectionAssert.AreEqual(new[] { 0 }, seen);
        }

        [Test]
        public void Subscribe_IsNotCalledOnSubscribe_DisposeStops() {
            var seen = new List<int>();
            var handle = _model.scored.Subscribe(seen.Add);
            Assert.IsEmpty(seen);

            _model.scored.Fire(1);
            handle.Dispose();
            handle.Dispose();
            _model.scored.Fire(2);

            CollectionAssert.AreEqual(new[] { 1 }, seen);
            Assert.AreEqual(0, _model.scored.OnFired.HandlerCount);
        }

        [Test]
        public void ThrowingListener_IsLogged_OthersStillRun() {
            var seen = new List<int>();
            var bad = _model.scored.Subscribe(_ => throw new InvalidOperationException("listener boom"));
            var good = _model.scored.Subscribe(seen.Add);
            LogAssert.Expect(LogType.Exception, new Regex("listener boom"));

            _model.scored.Fire(5);

            CollectionAssert.AreEqual(new[] { 5 }, seen);
            bad.Dispose();
            good.Dispose();
        }

        [Test]
        public void NestedFire_IsQueued_DeliveredInOrderAfterCurrent() {
            var seen = new List<int>();
            var handle = _model.scored.Subscribe(v => {
                seen.Add(v);
                if (v == 1) _model.scored.Fire(2);
            });

            _model.scored.Fire(1);
            handle.Dispose();

            CollectionAssert.AreEqual(new[] { 1, 2 }, seen);
        }

        [Test]
        public void CleanupSubscriptions_RemovesBoundEventHandlers() {
            // Model.OnDestroy calls CleanupSubscriptions; EditMode DestroyImmediate does not run OnDestroy, so call it directly.
            var sub = _host.AddComponent<EventSubscriber>();
            Observable.SetListeners(sub, _model);
            var evt = _model.scored;
            Assert.AreEqual(1, evt.OnFired.HandlerCount);

            Observable.CleanupSubscriptions(_model);

            Assert.AreEqual(0, evt.OnFired.HandlerCount);
        }

        [Test]
        public void GetSavableObservableFields_ExcludesNonSavedAndEvents() {
            var names = _model.GetSavableObservableFields().Select(f => f.Name).ToArray();

            CollectionAssert.AreEquivalent(new[] { "score", "items" }, names);
            CollectionAssert.IsSubsetOf(new[] { "scored", "pinged", "transient" }, _model.GetCachedObservableFields().Select(f => f.Name).ToArray(),
                "Binding still sees events and [NonSaved] fields.");
        }

        [Test]
        public void LoadDataFromModel_SkipsEvents_AndCopiesState() {
            var sourceGo = new GameObject("EventSource");
            try {
                var source = sourceGo.AddComponent<EventModel>();
                source.EnsureFieldsInitialized();
                source.score.Value = 42;
                var targetEvent = _model.scored;
                int fired = 0;
                var handle = targetEvent.Subscribe(_ => fired++);

                _model.LoadDataFromModel(source);
                handle.Dispose();

                Assert.AreEqual(42, _model.score.Value);
                Assert.AreSame(targetEvent, _model.scored, "Event instance is not replaced by a load.");
                Assert.AreEqual(0, fired);
            } finally {
                UnityEngine.Object.DestroyImmediate(sourceGo);
            }
        }

        [Test]
        public void JsonUtility_DoesNotSerializeEvents() {
            var json = JsonUtility.ToJson(_model);
            StringAssert.DoesNotContain("scored", json);
            StringAssert.Contains("score", json);
        }
    }
}
