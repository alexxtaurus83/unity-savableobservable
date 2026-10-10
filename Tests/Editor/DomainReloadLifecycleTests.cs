using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using SavableObservable;
using SavableObservable.Samples;

namespace SavableObservable.Tests {
    [TestFixture]
    public class DomainReloadLifecycleTests {
        private GameObject _host;

        [SetUp]
        public void SetUp() {
            _host = new GameObject("LifecycleHost");
        }

        [TearDown]
        public void TearDown() {
            if (_host != null) {
                UnityEngine.Object.DestroyImmediate(_host);
            }
        }

        [Test]
        public void CleanupSubscriptions_DetachesAllHandlersFromModel() {
            var model = _host.AddComponent<SamplePlayerModel>();
            var presenter = _host.AddComponent<SamplePlayerPresenter>();
            model.InitializeDefaults();

            Observable.SetListeners(presenter, model);
            model.health.Value = 60;
            Assert.AreEqual(60, presenter.LastObservedHealth);

            Observable.CleanupSubscriptions(model);

            model.health.Value = 20;
            Assert.AreEqual(60, presenter.LastObservedHealth, "Handler should be detached after CleanupSubscriptions");
        }

#if UNITY_6000_0_OR_NEWER || UNITY_6_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [Test]
        public void DomainReload_SubsystemRegistration_ResetInstanceData_ExecutesCleanly() {
            var method = typeof(Observable).GetMethod("ResetInstanceData", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "ResetInstanceData method should exist");

            // Invoke static reset
            method.Invoke(null, null);

            // Verify framework functions normally after static reset
            var model = _host.AddComponent<SamplePlayerModel>();
            var presenter = _host.AddComponent<SamplePlayerPresenter>();
            model.InitializeDefaults();

            Observable.SetListeners(presenter, model);
            model.health.Value = 55;
            Assert.AreEqual(55, presenter.LastObservedHealth);
        }

        [Test]
        public void DomainReload_SubsystemRegistration_ResetRegistry_ReinitializesAdaptersCleanly() {
            var method = typeof(UIAdapterRegistry).GetMethod("ResetRegistry", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "ResetRegistry method should exist");

            // Invoke static reset
            method.Invoke(null, null);

            // Check that adapters are immediately re-initialized and functional
            var toggleAdapter = UIAdapterRegistry.GetAdapter(typeof(Toggle));
            Assert.IsNotNull(toggleAdapter);

            var sliderAdapter = UIAdapterRegistry.GetAdapter(typeof(Slider));
            Assert.IsNotNull(sliderAdapter);
        }

        [Test]
        public void SimulateMultiPlayRuns_WithFastPlayMode_NoStaleSubscriptionsOrLeaks() {
            var resetObservable = typeof(Observable).GetMethod("ResetInstanceData", BindingFlags.NonPublic | BindingFlags.Static);
            var resetRegistry = typeof(UIAdapterRegistry).GetMethod("ResetRegistry", BindingFlags.NonPublic | BindingFlags.Static);

            // --- RUN 1 (Play mode run 1) ---
            var run1Host = new GameObject("Run1_Host");
            var model1 = run1Host.AddComponent<SamplePlayerModel>();
            var presenter1 = run1Host.AddComponent<SamplePlayerPresenter>();
            model1.InitializeDefaults();
            Observable.SetListeners(presenter1, model1);

            model1.health.Value = 75;
            Assert.AreEqual(75, presenter1.LastObservedHealth);

            // Teardown Run 1 (Scene unload / exit play mode)
            UnityEngine.Object.DestroyImmediate(run1Host);

            // Fast Play Mode entry: SubsystemRegistration reset is called by Unity runtime
            resetObservable?.Invoke(null, null);
            resetRegistry?.Invoke(null, null);

            // --- RUN 2 (Play mode run 2 without script recompilation) ---
            var run2Host = new GameObject("Run2_Host");
            var model2 = run2Host.AddComponent<SamplePlayerModel>();
            var presenter2 = run2Host.AddComponent<SamplePlayerPresenter>();
            model2.InitializeDefaults();
            Observable.SetListeners(presenter2, model2);

            model2.health.Value = 40;
            Assert.AreEqual(40, presenter2.LastObservedHealth);
            Assert.AreEqual(1, presenter2.HealthChangeEventsCount, "Run 2 must only have its own event invocations");

            UnityEngine.Object.DestroyImmediate(run2Host);
        }

        private class DummyCustomComponent : MonoBehaviour { }
        private class DummyCustomAdapter : IUIAdapter {
            public Type HandledType => typeof(DummyCustomComponent);
            public int Priority => 10;
            public void SetValue(object target, object value, Type valueType) { }
            public object GetValue(object target) => null;
        }

        [Test]
        public void CustomAdapterRegistration_IsClearedOnSubsystemRegistrationReset() {
            var customAdapter = new DummyCustomAdapter();
            UIAdapterRegistry.RegisterAdapter(customAdapter);

            Assert.AreSame(customAdapter, UIAdapterRegistry.GetAdapter(typeof(DummyCustomComponent)));

            // Reset registry (as occurs on domain reload / subsystem registration)
            var resetRegistry = typeof(UIAdapterRegistry).GetMethod("ResetRegistry", BindingFlags.NonPublic | BindingFlags.Static);
            resetRegistry?.Invoke(null, null);

            Assert.IsNull(UIAdapterRegistry.GetAdapter(typeof(DummyCustomComponent)), "Custom adapter must be cleared upon SubsystemRegistration reset");
        }
#endif

        [Test]
        public void DestroyedPresenter_BeforeModel_DoesNotThrowOnModelMutation() {
            var model = _host.AddComponent<SamplePlayerModel>();
            var presenter = _host.AddComponent<SamplePlayerPresenter>();
            model.InitializeDefaults();

            Observable.SetListeners(presenter, model);
            model.health.Value = 90;
            Assert.AreEqual(90, presenter.LastObservedHealth);

            // Destroy the presenter component while model lives
            UnityEngine.Object.DestroyImmediate(presenter);

            // Mutating model should execute safely without crashing or throwing
            Assert.DoesNotThrow(() => {
                model.health.Value = 30;
            });
        }
    }
}
