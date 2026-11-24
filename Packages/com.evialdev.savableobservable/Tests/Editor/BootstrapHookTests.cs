using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SavableObservable;

namespace SavableObservable.Tests {
    [TestFixture]
    public class BootstrapHookTests {

        // Per-test log; reset in SetUp (test-only static, never used at runtime).
        private static readonly List<string> Log = new List<string>();

        private class BootModel : BaseObservableDataModel {
            public ObservableVariable<int> loaded = new ObservableVariable<int>();
        }

        private class BootPresenter : BaseObservablePresenter<BootModel> {
            public int SeenOnBind = -1;

            protected override void InitializePresenter() {
                base.InitializePresenter();
                Log.Add("bind");
            }

            [ObservableHandler("loaded", InvokeOnBind = true)]
            private void OnLoaded(int value) {
                if (SeenOnBind < 0) SeenOnBind = value;
            }
        }

        private class BootLogic : BaseLogic<BootModel, BootPresenter> { }

        private class LoaderService : MonoBehaviour, ISharedSingleton, IBootstrapLoad, IBootstrapStart {
            public int Order;
            public string Tag = "a";
            public BootLogic Target;
            public int LoadCalls, StartCalls;
            public int BootstrapOrder => Order;
            public string Name => "Loader_" + Tag; // distinct registration keys for several loaders

            public void BootstrapLoad() {
                LoadCalls++;
                Log.Add($"load:{Tag}:registered={Services.IsRegistered(this)}");
                // Services.GetModel writes without binding the presenter (Logic.GetModel() would bind it right here).
                if (Target != null) Services.GetModel<BootModel>().loaded.Value = 42;
            }

            public void BootstrapStart() {
                StartCalls++;
                Log.Add($"start:{Tag}");
            }
        }

        private class ThrowingLoad : MonoBehaviour, IBootstrapLoad {
            public int BootstrapOrder => 0;
            public void BootstrapLoad() => throw new InvalidOperationException("load failed");
        }

        private readonly List<GameObject> _objects = new List<GameObject>();
        private Services.TestState _state;
        private ServiceLocator _locator;

        [SetUp]
        public void SetUp() {
            Log.Clear();
            _state = Services.CaptureTestState();
            _locator = Create("Locator").AddComponent<ServiceLocator>();
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

        private LoaderService CreateLoader(string tag, int order) {
            var loader = Create("Loader_" + tag).AddComponent<LoaderService>();
            loader.Tag = tag;
            loader.Order = order;
            return loader;
        }

        private BootLogic CreateUnit() {
            var go = Create("Unit");
            go.AddComponent<BootModel>();
            go.AddComponent<BootPresenter>();
            return go.AddComponent<BootLogic>();
        }

        [Test]
        public void Order_Register_Load_Bind_Start() {
            var unit = CreateUnit();
            var loader = CreateLoader("a", 0);

            _locator.InitializeSharedSingletons();
            _locator.RunBootstrapStart();

            CollectionAssert.AreEqual(new[] { "load:a:registered=True", "bind", "start:a" }, Log);
            Assert.AreEqual(1, loader.LoadCalls);
            Assert.AreEqual(1, loader.StartCalls);
            Assert.IsTrue(Services.IsRegistered(unit));
        }

        [Test]
        public void ScenePresenter_SeesBootstrapLoadedData_OnBind() {
            var unit = CreateUnit();
            var loader = CreateLoader("a", 0);
            loader.Target = unit;

            _locator.InitializeSharedSingletons();

            Assert.AreEqual(42, unit.GetComponent<BootPresenter>().SeenOnBind);
        }

        [Test]
        public void Hooks_AreSortedByOrder_TiesKeepSceneOrder() {
            CreateLoader("late", 10);
            CreateLoader("tie1", 0);
            CreateLoader("early", -5);
            CreateLoader("tie2", 0);

            _locator.InitializeSharedSingletons();
            _locator.RunBootstrapStart();

            CollectionAssert.AreEqual(new[] {
                "load:early:registered=True", "load:tie1:registered=True", "load:tie2:registered=True", "load:late:registered=True",
                "start:early", "start:tie1", "start:tie2", "start:late"
            }, Log);
        }

        [Test]
        public void RepeatedInitialize_DoesNotRerunHooks() {
            var loader = CreateLoader("a", 0);

            _locator.InitializeSharedSingletons();
            _locator.InitializeSharedSingletons();
            _locator.RunBootstrapStart();
            _locator.RunBootstrapStart();

            Assert.AreEqual(1, loader.LoadCalls);
            Assert.AreEqual(1, loader.StartCalls);
        }

        [Test]
        public void LoadException_Propagates_AndPresentersAreNotBound() {
            CreateUnit();
            Create("Throwing").AddComponent<ThrowingLoad>();

            var ex = Assert.Throws<InvalidOperationException>(() => _locator.InitializeSharedSingletons());

            Assert.AreEqual("load failed", ex.Message);
            CollectionAssert.DoesNotContain(Log, "bind");
        }

        [Test]
        public void LoadException_SkipsBootstrapStart() {
            var loader = CreateLoader("a", 5);
            Create("Throwing").AddComponent<ThrowingLoad>();

            Assert.Throws<InvalidOperationException>(() => _locator.InitializeSharedSingletons());
            _locator.RunBootstrapStart();

            Assert.AreEqual(0, loader.StartCalls, "No BootstrapStart after a failed BootstrapLoad.");
        }

        [Test]
        public void DestroyedStartHook_IsSkipped() {
            var keep = CreateLoader("keep", 0);
            var gone = CreateLoader("gone", 1);

            _locator.InitializeSharedSingletons();
            UnityEngine.Object.DestroyImmediate(gone.gameObject);
            _locator.RunBootstrapStart();

            Assert.AreEqual(1, keep.StartCalls);
            CollectionAssert.DoesNotContain(Log, "start:gone");
        }

        [Test]
        public void NewLocator_RunsHooksAgain_PerLocatorState() {
            var loader = CreateLoader("a", 0);
            _locator.InitializeSharedSingletons();

            var second = Create("Locator2").AddComponent<ServiceLocator>();
            Services.SetTestLocator(second);
            UnityEngine.Object.DestroyImmediate(_locator.gameObject);
            second.InitializeSharedSingletons();
            second.RunBootstrapStart();

            Assert.AreEqual(2, loader.LoadCalls, "A fresh locator (next Play session) runs the hooks once more.");
            Assert.AreEqual(1, loader.StartCalls);
        }

        [Test]
        public void DuplicateFlaggedLocator_SkipsInitializeAndBootstrapStart() {
            var loader = CreateLoader("a", 0);
            var flag = typeof(ServiceLocator).GetField("_isDuplicate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(flag);
            flag.SetValue(_locator, true);

            Assert.IsFalse(_locator.InitializeSharedSingletons());
            _locator.RunBootstrapStart();

            Assert.AreEqual(0, loader.LoadCalls);
            Assert.AreEqual(0, loader.StartCalls);
        }
    }
}
