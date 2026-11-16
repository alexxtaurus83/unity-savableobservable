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
        public void DomainReload_SubsystemRegistration_ServicesAndServiceLocator_ResetCleanly() {
            var testState = Services.CaptureTestState();
            try {
                var locatorGo = new GameObject("TestLocator");
                var locator = locatorGo.AddComponent<ServiceLocator>();
                Services.SetTestLocator(locator);

                Assert.AreSame(locator, ServiceLocator.instance);
                Assert.AreSame(locator, Services.instance);

                // Invoke SubsystemRegistration reset hook on ServiceLocator
                var resetLocatorMethod = typeof(ServiceLocator).GetMethod("ResetStaticsOnSubsystemRegistration", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(resetLocatorMethod, "ResetStaticsOnSubsystemRegistration should exist on ServiceLocator");
                resetLocatorMethod.Invoke(null, null);

                Assert.IsNull(ServiceLocator.instance, "ServiceLocator.instance must be null after SubsystemRegistration reset");

                // Services holds no state of its own: it reads ServiceLocator.instance directly
                Assert.IsNull(Services.instance, "Services.instance must be null after the locator was reset");
                Assert.IsNull(typeof(Services).GetMethod("ResetStaticsOnSubsystemRegistration", BindingFlags.NonPublic | BindingFlags.Static), "Services must not cache a locator that needs its own reset.");

                UnityEngine.Object.DestroyImmediate(locatorGo);
            } finally {
                Services.RestoreTestState(testState);
            }
        }

        [Test]
        public void Services_DestroyedCachedLocator_IsNotReused() {
            var testState = Services.CaptureTestState();
            try {
                var locatorGo = new GameObject("TestLocator");
                var locator = locatorGo.AddComponent<ServiceLocator>();

                Services.SetTestLocator(locator);
                Assert.AreSame(locator, Services.instance);

                // Destroy the locator GameObject
                UnityEngine.Object.DestroyImmediate(locatorGo);

                // Services.instance reads the locator directly and must treat a destroyed Unity object as null
                Assert.IsNull(Services.instance, "A destroyed ServiceLocator must not be returned by Services.instance");
                Assert.IsTrue(ServiceLocator.instance == null, "The static instance is a destroyed Unity object (Unity null) after the locator is destroyed");
            } finally {
                Services.RestoreTestState(testState);
            }
        }

        [Test]
        public void SimulateMultiPlayRuns_WithFastPlayMode_NoStaleSubscriptionsOrLeaks() {
            var resetObservable = typeof(Observable).GetMethod("ResetInstanceData", BindingFlags.NonPublic | BindingFlags.Static);
            var resetRegistry = typeof(UIAdapterRegistry).GetMethod("ResetRegistry", BindingFlags.NonPublic | BindingFlags.Static);
            var resetLocator = typeof(ServiceLocator).GetMethod("ResetStaticsOnSubsystemRegistration", BindingFlags.NonPublic | BindingFlags.Static);

            var testState = Services.CaptureTestState();
            try {
                // --- RUN 1 (Play mode run 1) ---
                var run1Host = new GameObject("Run1_Host");
                var locator1 = run1Host.AddComponent<ServiceLocator>();
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
                resetLocator?.Invoke(null, null);

                Assert.IsNull(ServiceLocator.instance);
                Assert.IsNull(Services.instance);

                // --- RUN 2 (Play mode run 2 without script recompilation) ---
                var run2Host = new GameObject("Run2_Host");
                var locator2 = run2Host.AddComponent<ServiceLocator>();
                var model2 = run2Host.AddComponent<SamplePlayerModel>();
                var presenter2 = run2Host.AddComponent<SamplePlayerPresenter>();
                model2.InitializeDefaults();
                Observable.SetListeners(presenter2, model2);

                model2.health.Value = 40;
                Assert.AreEqual(40, presenter2.LastObservedHealth);
                Assert.AreEqual(1, presenter2.HealthChangeEventsCount, "Run 2 must only have its own event invocations");

                UnityEngine.Object.DestroyImmediate(run2Host);
            } finally {
                Services.RestoreTestState(testState);
            }
        }

        private class DummyCustomComponent : MonoBehaviour { }
        private class DummyCustomAdapter : IUIAdapter {
            public bool CanHandle(Type uiComponentType) => uiComponentType == typeof(DummyCustomComponent);
            public int Priority => 10;
            public void SetValue(object uiComponent, object value, Type valueType) { }
        }

        [Test]
        public void CustomAdapterRegistration_SurvivesSubsystemRegistrationReset_And_CanBeClearedExplicitly() {
            UIAdapterRegistry.ClearCustomAdapters();
            var customAdapter = new DummyCustomAdapter();
            try {
                UIAdapterRegistry.RegisterAdapter(customAdapter);

                Assert.IsNotNull(UIAdapterRegistry.GetAdapter(typeof(DummyCustomComponent)));
                Assert.AreSame(customAdapter, UIAdapterRegistry.GetAdapter(typeof(DummyCustomComponent)));

                // Reset registry (as occurs on domain reload / subsystem registration in Fast Play Mode)
                var resetRegistry = typeof(UIAdapterRegistry).GetMethod("ResetRegistry", BindingFlags.NonPublic | BindingFlags.Static);
                resetRegistry?.Invoke(null, null);

                // Custom adapters survive Fast Play Mode SubsystemRegistration reset
                Assert.AreSame(customAdapter, UIAdapterRegistry.GetAdapter(typeof(DummyCustomComponent)), "Custom adapter must survive SubsystemRegistration reset");

                // Explicit clear cleans up custom adapters
                UIAdapterRegistry.ClearCustomAdapters();
                Assert.IsNull(UIAdapterRegistry.GetAdapter(typeof(DummyCustomComponent)), "Custom adapter must be cleared upon ClearCustomAdapters");
            } finally {
                UIAdapterRegistry.ClearCustomAdapters();
            }
        }

        [Test]
        public void CustomAdapterRegistration_ReplacesPreviousInstanceAcrossFastPlayModeRuns() {
            UIAdapterRegistry.ClearCustomAdapters();
            try {
                var resetRegistry = typeof(UIAdapterRegistry).GetMethod("ResetRegistry", BindingFlags.NonPublic | BindingFlags.Static);

                var adapterRun1 = new DummyCustomAdapter();
                UIAdapterRegistry.RegisterAdapter(adapterRun1);
                Assert.AreSame(adapterRun1, UIAdapterRegistry.GetAdapter(typeof(DummyCustomComponent)));

                // Simulate Play Mode exit / reload
                resetRegistry?.Invoke(null, null);

                // Simulate Play Mode start: bootstrap registers a new instance of DummyCustomAdapter
                var adapterRun2 = new DummyCustomAdapter();
                UIAdapterRegistry.RegisterAdapter(adapterRun2);

                Assert.AreSame(adapterRun2, UIAdapterRegistry.GetAdapter(typeof(DummyCustomComponent)));

                // Inspect internal _customAdapters list to verify count is exactly 1 (no accumulation)
                var customAdaptersField = typeof(UIAdapterRegistry).GetField("_customAdapters", BindingFlags.NonPublic | BindingFlags.Static);
                var customList = customAdaptersField?.GetValue(null) as System.Collections.IList;
                Assert.IsNotNull(customList);
                Assert.AreEqual(1, customList.Count, "Repeated registration of the same adapter type must replace, not accumulate.");
            } finally {
                UIAdapterRegistry.ClearCustomAdapters();
            }
        }

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
