using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SavableObservable;

namespace SavableObservable.Tests {

    public class BootPlayModel : BaseObservableDataModel {
        public ObservableVariable<int> loaded = new ObservableVariable<int>();
    }

    public class BootPlayPresenter : BaseObservablePresenter<BootPlayModel> {
        public readonly List<int> Seen = new List<int>();

        [ObservableHandler("loaded", InvokeOnBind = true)]
        private void OnLoaded(int value) => Seen.Add(value);
    }

    public class BootPlayLogic : BaseLogic<BootPlayModel, BootPlayPresenter> { }

    public class BootPlayService : MonoBehaviour, ISharedSingleton, IBootstrapLoad, IBootstrapStart {
        public BootPlayLogic target;
        public int loadCalls, startCalls, presenterValuesAtStart = -1;
        public int BootstrapOrder => 0;

        public void BootstrapLoad() {
            loadCalls++;
            // Services.GetModel writes without binding the presenter (target.GetModel() would bind it right here).
            Services.GetModel<BootPlayModel>().loaded.Value = 7;
        }

        public void BootstrapStart() {
            startCalls++;
            presenterValuesAtStart = target.GetComponent<BootPlayPresenter>().Seen.Count;
        }
    }

    [TestFixture]
    public class BootstrapPlayModeTests {
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

        private (BootPlayLogic unit, BootPlayService service) CreateSceneObjects() {
            var unitGo = new GameObject("BootUnit");
            unitGo.SetActive(false);
            unitGo.AddComponent<BootPlayModel>();
            unitGo.AddComponent<BootPlayPresenter>();
            var unit = unitGo.AddComponent<BootPlayLogic>();
            _objects.Add(unitGo);

            var serviceGo = new GameObject("BootService");
            serviceGo.SetActive(false);
            var service = serviceGo.AddComponent<BootPlayService>();
            service.target = unit;
            _objects.Add(serviceGo);
            return (unit, service);
        }

        private ServiceLocator CreateLocator() {
            var go = new GameObject("BootLocator");
            _objects.Add(go);
            return go.AddComponent<ServiceLocator>(); // Awake runs now: register -> load -> bind.
        }

        [UnityTest]
        public IEnumerator Locator_AwakeLoadsBeforeBind_StartRunsNextFrame_AndSceneAwakeDoesNotRebind() {
            var (unit, service) = CreateSceneObjects();
            CreateLocator();
            var presenter = unit.GetComponent<BootPlayPresenter>();

            Assert.AreEqual(1, service.loadCalls);
            CollectionAssert.AreEqual(new[] { 7 }, presenter.Seen, "Presenter bound after BootstrapLoad and replayed the loaded value once.");
            Assert.AreEqual(0, service.startCalls, "BootstrapStart waits for the locator's Start.");

            unit.gameObject.SetActive(true);
            service.gameObject.SetActive(true);
            yield return null;

            Assert.AreEqual(1, service.startCalls);
            Assert.AreEqual(1, service.presenterValuesAtStart, "Presenters are bound before BootstrapStart.");
            CollectionAssert.AreEqual(new[] { 7 }, presenter.Seen, "Presenter Awake is a no-op for an already bound scene presenter.");
        }

        [UnityTest]
        public IEnumerator RuntimeInstantiatedPresenter_BindsInItsOwnAwake() {
            CreateLocator();
            yield return null;

            var go = new GameObject("RuntimeUnit");
            _objects.Add(go);
            var model = go.AddComponent<BootPlayModel>();
            model.loaded.Value = 3;
            var presenter = go.AddComponent<BootPlayPresenter>();

            CollectionAssert.AreEqual(new[] { 3 }, presenter.Seen);
        }

        [UnityTest]
        public IEnumerator SecondLocatorSession_RegistersAndRunsHooksOnceEach() {
            var (_, service) = CreateSceneObjects();
            var first = CreateLocator();
            yield return null;
            Object.Destroy(first.gameObject);
            yield return null;

            var second = CreateLocator();
            yield return null;

            Assert.AreEqual(2, service.loadCalls, "One load per locator session.");
            Assert.AreEqual(2, service.startCalls, "One start per locator session.");
            Assert.AreSame(service, Services.Get<BootPlayService>());
            Assert.AreSame(second, ServiceLocator.instance);
        }

        [UnityTest]
        public IEnumerator DuplicateActiveLocatorsInScene_LogsErrorAndRunsBootstrapStartOnce() {
            var (_, service) = CreateSceneObjects();
            var first = CreateLocator();
            var second = CreateLocator();

            LogAssert.Expect(LogType.Error, new Regex(@"\[ServiceLocator\] Duplicate active ServiceLocator"));
            yield return null;

            Assert.AreSame(second, ServiceLocator.instance);
            Assert.AreEqual(1, service.startCalls, "Superseded duplicate locator must not run BootstrapStart a second time.");
            Assert.IsTrue(first != null);
        }
    }
}
