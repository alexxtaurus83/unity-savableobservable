using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SavableObservable;

namespace SavableObservable.Tests {

    public readonly struct PlayBusEvent : IMmvcEvent {
        public readonly int Value;
        public PlayBusEvent(int value) { Value = value; }
    }

    public class PlayBusModel : BaseObservableDataModel {
        public ObservableVariable<int> received = new ObservableVariable<int>();
    }

    public class PlayBusPresenter : BaseObservablePresenter<PlayBusModel> {
        public readonly List<int> Seen = new List<int>();
        [ObservableHandler("received")] private void OnReceived() { }
        [EventHandler] private void OnEvent(PlayBusEvent e) => Seen.Add(e.Value);
    }

    public class PlayBusLogic : BaseLogic<PlayBusModel, PlayBusPresenter> {
        [EventHandler] private void OnEvent(PlayBusEvent e) => GetModel().received.Value = e.Value;
    }

    [TestFixture]
    public class EventBusPlayModeTests {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private Services.TestState _state;

        [SetUp]
        public void SetUp() {
            _state = Services.CaptureTestState();
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            foreach (var go in _objects) if (go != null) Object.Destroy(go);
            _objects.Clear();
            yield return null;
            Services.RestoreTestState(_state);
        }

        private PlayBusLogic CreateUnit() {
            var go = new GameObject("PlayBusUnit");
            go.SetActive(false);
            go.AddComponent<PlayBusModel>();
            go.AddComponent<PlayBusPresenter>();
            var logic = go.AddComponent<PlayBusLogic>();
            _objects.Add(go);
            return logic;
        }

        private ServiceLocator CreateLocator() {
            var go = new GameObject("PlayBusLocator");
            _objects.Add(go);
            return go.AddComponent<ServiceLocator>();
        }

        [UnityTest]
        public IEnumerator SceneUnit_LogicAndPresenterHandlers_AutoBound_ByLocator() {
            var logic = CreateUnit();
            var locator = CreateLocator();
            logic.gameObject.SetActive(true);
            yield return null;

            Services.Publish(new PlayBusEvent(4));

            Assert.AreEqual(4, logic.GetModel().received.Value);
            CollectionAssert.AreEqual(new[] { 4 }, logic.GetComponent<PlayBusPresenter>().Seen);
            Assert.AreEqual(2, locator.EventBus.GetHandlerCount<PlayBusEvent>());
        }

        [UnityTest]
        public IEnumerator DestroyedUnit_IsRemovedFromBus() {
            var logic = CreateUnit();
            var locator = CreateLocator();
            logic.gameObject.SetActive(true);
            yield return null;

            Object.Destroy(logic.gameObject);
            yield return null;

            Assert.AreEqual(0, locator.EventBus.GetHandlerCount<PlayBusEvent>());
            Assert.DoesNotThrow(() => Services.Publish(new PlayBusEvent(1)));
        }

        [UnityTest]
        public IEnumerator SecondSession_FreshLocator_HasNoStaleListeners() {
            var firstUnit = CreateUnit();
            var first = CreateLocator();
            firstUnit.gameObject.SetActive(true);
            yield return null;
            Object.Destroy(first.gameObject);
            Object.Destroy(firstUnit.gameObject);
            yield return null;

            var secondUnit = CreateUnit();
            var second = CreateLocator();
            secondUnit.gameObject.SetActive(true);
            yield return null;

            Assert.AreEqual(2, second.EventBus.GetHandlerCount<PlayBusEvent>(), "Exactly one Logic and one Presenter handler in the new session.");
            Services.Publish(new PlayBusEvent(9));
            CollectionAssert.AreEqual(new[] { 9 }, secondUnit.GetComponent<PlayBusPresenter>().Seen);
        }
    }
}
