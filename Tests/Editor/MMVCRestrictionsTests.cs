using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SavableObservable;
using SavableObservable.Editor;
using SavableObservable.Samples;

namespace SavableObservable.Tests {
    [TestFixture]
    public class MMVCRestrictionsTests {
        private GameObject _goA;
        private GameObject _goB;
        private GameObject _goC;

        [SetUp]
        public void SetUp() {
            _goA = new GameObject("UnitA");
            _goB = new GameObject("UnitB");
            _goC = new GameObject("UnitC");
        }

        [TearDown]
        public void TearDown() {
            if (_goA != null) UnityEngine.Object.DestroyImmediate(_goA);
            if (_goB != null) UnityEngine.Object.DestroyImmediate(_goB);
            if (_goC != null) UnityEngine.Object.DestroyImmediate(_goC);
        }

        [Test]
        public void CrossGameObject_Binding_IsRejectedWithLogError() {
            var modelA = _goA.AddComponent<SamplePlayerModel>();
            var presenterB = _goB.AddComponent<SamplePlayerPresenter>();

            LogAssert.Expect(LogType.Error, new Regex(@"Cross-GameObject binding rejected in SetListeners"));

            Observable.SetListeners(presenterB, modelA);
        }

        [Test]
        public void ValidateSingleMmvc_Rules_EnforcedViaDisallowMultipleComponent() {
            // Rule 1: One GameObject = one model.
            var modelAttrs = typeof(BaseObservableDataModel).GetCustomAttributes(typeof(DisallowMultipleComponent), true);
            Assert.IsTrue(modelAttrs.Length > 0, "BaseObservableDataModel must have [DisallowMultipleComponent]");

            // Rule 2: One GameObject = one presenter.
            var presenterAttrs = typeof(BasePresenter<SamplePlayerModel>).GetCustomAttributes(typeof(DisallowMultipleComponent), true);
            Assert.IsTrue(presenterAttrs.Length > 0, "BasePresenter must have [DisallowMultipleComponent]");

            // Rule 3: One GameObject = one logic.
            var logicAttrs = typeof(BaseLogic).GetCustomAttributes(typeof(DisallowMultipleComponent), true);
            Assert.IsTrue(logicAttrs.Length > 0, "BaseLogic must have [DisallowMultipleComponent]");
        }

        [Test]
        public void BaseLogicGeneric_AutoRequires_ModelAndPresenter() {
            // BaseLogic<M, P> auto-requires both M and P via EnsureComponent in Reset/OnValidate
            var logic = _goA.AddComponent<SamplePlayerLogic>();
            Assert.IsNotNull(logic);

            var autoRequireType = typeof(BaseObservableDataModel).Assembly.GetType("SavableObservable.ComponentAutoRequire");
            var ensureMethod = autoRequireType?.GetMethod("EnsureComponent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static, null, new[] { typeof(MonoBehaviour), typeof(Type) }, null);

            ensureMethod?.Invoke(null, new object[] { logic, typeof(SamplePlayerModel) });
            ensureMethod?.Invoke(null, new object[] { logic, typeof(SamplePlayerPresenter) });

            Assert.IsNotNull(_goA.GetComponent<SamplePlayerModel>());
            Assert.IsNotNull(_goA.GetComponent<SamplePlayerPresenter>());
        }

        public class MockInitializingPresenter : BaseObservablePresenter<SamplePlayerModel> {
            public bool InitializePresenterCalled { get; private set; }
            protected override void InitializePresenter() {
                base.InitializePresenter();
                InitializePresenterCalled = true;
            }
        }

        [Test]
        public void EnsureInitialized_Invokes_InitializePresenter_And_ObservableBinding() {
            var model = _goA.GetComponent<SamplePlayerModel>() ?? _goA.AddComponent<SamplePlayerModel>();
            var presenter = _goA.AddComponent<MockInitializingPresenter>();

            Assert.IsFalse(presenter.InitializePresenterCalled);
            presenter.EnsureInitialized();
            Assert.IsTrue(presenter.InitializePresenterCalled);

            // Test idempotent call
            presenter.EnsureInitialized();
            Assert.IsTrue(presenter.InitializePresenterCalled);
        }

        [Test]
        public void Observable_SetListenersCore_Guarantees_InitializePresenter_BeforeBinding() {
            var model = _goA.AddComponent<SamplePlayerModel>();
            var presenter = _goA.AddComponent<MockInitializingPresenter>();

            Assert.IsFalse(presenter.InitializePresenterCalled);
            Observable.SetListeners(presenter, model);
            Assert.IsTrue(presenter.InitializePresenterCalled);
        }

        public class MockPlainSharedSingleton : ISharedSingleton {
            public string Name => "MockPlainSharedSingleton";
        }

        public class MockRawNonObservablePresenter : BasePresenter<SamplePlayerModel> {
        }

        [Test]
        public void ServiceLocator_TryRegister_Rejects_PresentersAndModels() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            var model = _goA.AddComponent<SamplePlayerModel>();
            var presenter = _goA.AddComponent<SamplePlayerPresenter>();

            LogAssert.Expect(LogType.Error, new Regex(@"is rejected from ServiceLocator registration"));
            var resPres = locator.TryRegister(presenter);
            Assert.AreEqual(RegistrationResult.Rejected, resPres);

            LogAssert.Expect(LogType.Error, new Regex(@"is rejected from ServiceLocator registration"));
            var resMod = locator.TryRegister(model);
            Assert.AreEqual(RegistrationResult.Rejected, resMod);

            UnityEngine.Object.DestroyImmediate(locatorGo);
        }

        [Test]
        public void ServiceLocator_TryRegister_Rejects_PlainClass_And_RawBasePresenter() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            // 1. Plain C# class implementing ISharedSingleton (not a MonoBehaviour) must be rejected
            var plainClass = new MockPlainSharedSingleton();
            LogAssert.Expect(LogType.Error, new Regex(@"is not a MonoBehaviour"));
            var resPlain = locator.TryRegister(plainClass);
            Assert.AreEqual(RegistrationResult.Rejected, resPlain);

            // 2. Raw non-reactive BasePresenter<M> (does not implement IObservablePresenter) must be rejected
            var rawPres = _goA.AddComponent<MockRawNonObservablePresenter>();
            LogAssert.Expect(LogType.Error, new Regex(@"is rejected from ServiceLocator registration"));
            var resRaw = locator.TryRegister(rawPres);
            Assert.AreEqual(RegistrationResult.Rejected, resRaw);

            UnityEngine.Object.DestroyImmediate(locatorGo);
        }

        [Test]
        public void ServiceLocator_DestroyedLogic_With_SurvivingModel_PrunedOnGetModel() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            var logic = _goA.AddComponent<SamplePlayerLogic>();
            var model = _goA.GetComponent<SamplePlayerModel>();

            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logic));
            Assert.AreSame(model, locator.GetModel<SamplePlayerModel>());

            // Destroy the Logic component ONLY, leaving Model intact on the GameObject
            UnityEngine.Object.DestroyImmediate(logic);

            // GetModel must prune owning Logic and return null
            Assert.IsNull(locator.GetModel<SamplePlayerModel>(), "Destroyed Logic with surviving Model must not resolve via GetModel");

            UnityEngine.Object.DestroyImmediate(locatorGo);
        }

        [Test]
        public void ServiceLocator_Collision_With_RemainingDistinctNamedService_BlocksUnnamedFallback() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            // Distinct named service
            var logicNamed = _goA.AddComponent<SamplePlayerLogic>();
            logicNamed.SetSingletonName("NamedPlayer");

            // Two colliding unnamed services
            var logicB = _goB.AddComponent<SamplePlayerLogic>();
            var logicC = _goC.AddComponent<SamplePlayerLogic>();

            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicNamed));
            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicB));
            Assert.AreEqual(RegistrationResult.EvictedDuplicate, locator.TryRegister(logicC));

            // Named resolution succeeds
            Assert.AreSame(logicNamed, locator.Get<SamplePlayerLogic>("NamedPlayer"));

            // Unnamed resolution must detect that a collision exists and block fallback (return null + log error)
            LogAssert.Expect(LogType.Error, new Regex(@"Ambiguous Get request for type.*evicted from singleton lookup"));
            Assert.IsNull(locator.Get<SamplePlayerLogic>(), "Collided unnamed instances must block unnamed single fallback even when a distinct named instance exists");

            // Models are provisioned by the Logic components above.
            LogAssert.Expect(LogType.Error, new Regex(@"Ambiguous GetModel request for model type.*evicted from singleton lookup"));
            Assert.IsNull(locator.GetModel<SamplePlayerModel>(), "Collided unnamed model instances must block unnamed single fallback");

            UnityEngine.Object.DestroyImmediate(locatorGo);
        }

        [Test]
        public void ServiceLocator_Remove_OnlyRemoves_TargetUnitModel() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            var logicA = _goA.AddComponent<SamplePlayerLogic>();
            logicA.SetSingletonName("PlayerA");
            var modelA = _goA.GetComponent<SamplePlayerModel>() ?? _goA.AddComponent<SamplePlayerModel>();

            var logicB = _goB.AddComponent<SamplePlayerLogic>();
            logicB.SetSingletonName("PlayerB");
            var modelB = _goB.GetComponent<SamplePlayerModel>() ?? _goB.AddComponent<SamplePlayerModel>();

            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicA));
            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicB));

            Assert.AreSame(modelA, locator.GetModel<SamplePlayerModel>("PlayerA"));
            Assert.AreSame(modelB, locator.GetModel<SamplePlayerModel>("PlayerB"));

            // Remove PlayerA
            locator.Remove<SamplePlayerLogic>("PlayerA");

            // PlayerA model should be removed; PlayerB model must remain intact!
            Assert.IsNull(locator.GetModel<SamplePlayerModel>("PlayerA"));
            Assert.AreSame(modelB, locator.GetModel<SamplePlayerModel>("PlayerB"));

            UnityEngine.Object.DestroyImmediate(locatorGo);
        }

        public class MockPass1Pass2Logic : BaseLogic<SamplePlayerModel, MockPass1Pass2Presenter> { }

        public class MockPass1Pass2Presenter : BaseObservablePresenter<SamplePlayerModel> {
            public MockPass1Pass2Logic ResolvedLogicInInit { get; private set; }
            protected override void InitializePresenter() {
                base.InitializePresenter();
                // Test that Services.Get resolves in InitializePresenter during Pass 2
                ResolvedLogicInInit = Services.Get<MockPass1Pass2Logic>();
            }
        }

        [Test]
        public void ServiceLocator_InitializeSharedSingletons_TwoPass_GuaranteesServicesReadyInInitializePresenter() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            // Setup Unit on _goA: Model, Logic, and MockPass1Pass2Presenter
            _goA.AddComponent<SamplePlayerModel>();
            _goA.AddComponent<MockPass1Pass2Logic>();
            var presenter = _goA.GetComponent<MockPass1Pass2Presenter>();

            Services.SetTestLocator(locator);
            try {
                Assert.IsNull(presenter.ResolvedLogicInInit);

                locator.InitializeSharedSingletons();

                Assert.IsNotNull(presenter.ResolvedLogicInInit, "Services.Get<SamplePlayerLogic>() must be populated inside InitializePresenter during Pass 2");
            } finally {
                Services.SetTestLocator(null);
                UnityEngine.Object.DestroyImmediate(locatorGo);
            }
        }

        [Test]
        public void ServiceLocator_Collision_OnDistinctNamedService_DoesNotBlockDefaultUnnamedService() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            // 1. One default unnamed service (key = "SamplePlayerLogic")
            var logicDefault = _goA.AddComponent<SamplePlayerLogic>();

            // 2. Two colliding named services (key = "Secondary")
            var logicSec1 = _goB.AddComponent<SamplePlayerLogic>();
            logicSec1.SetSingletonName("Secondary");
            var logicSec2 = _goC.AddComponent<SamplePlayerLogic>();
            logicSec2.SetSingletonName("Secondary");

            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicDefault));
            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicSec1));
            Assert.AreEqual(RegistrationResult.EvictedDuplicate, locator.TryRegister(logicSec2));

            // Default unnamed resolution must succeed because the requested key "SamplePlayerLogic" did NOT collide!
            Assert.AreSame(logicDefault, locator.Get<SamplePlayerLogic>());
            Assert.AreSame(logicDefault.GetModel(), locator.GetModel<SamplePlayerModel>("SamplePlayerLogic"));

            // Collided named key "Secondary" must be blocked
            LogAssert.Expect(LogType.Error, new Regex(@"Ambiguous Get request for type.*Secondary.*evicted from singleton lookup"));
            Assert.IsNull(locator.Get<SamplePlayerLogic>("Secondary"));

            LogAssert.Expect(LogType.Error, new Regex(@"Ambiguous GetModel request for model type.*Secondary.*evicted"));
            Assert.IsNull(locator.GetModel<SamplePlayerModel>("Secondary"));

            UnityEngine.Object.DestroyImmediate(locatorGo);
        }

        [Test]
        public void ServiceLocator_CollisionEviction_And_Recovery_Lifecycle() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            var logicA = _goA.AddComponent<SamplePlayerLogic>();
            var modelA = _goA.GetComponent<SamplePlayerModel>() ?? _goA.AddComponent<SamplePlayerModel>();

            var logicB = _goB.AddComponent<SamplePlayerLogic>();
            var modelB = _goB.GetComponent<SamplePlayerModel>() ?? _goB.AddComponent<SamplePlayerModel>();

            // Both have default Name = "SamplePlayerLogic"
            var res1 = locator.TryRegister(logicA);
            Assert.AreEqual(RegistrationResult.Added, res1);
            Assert.AreSame(logicA, locator.Get<SamplePlayerLogic>());
            Assert.AreSame(modelA, locator.GetModel<SamplePlayerModel>());

            // Second instance with same Name collides: evicted from lookup
            var res2 = locator.TryRegister(logicB);
            Assert.AreEqual(RegistrationResult.EvictedDuplicate, res2);

            // Get<SamplePlayerLogic>() logs error and returns null
            LogAssert.Expect(LogType.Error, new Regex(@"Ambiguous Get request for type.*SamplePlayerLogic.*evicted from singleton lookup"));
            Assert.IsNull(locator.Get<SamplePlayerLogic>());

            // GetModel<SamplePlayerModel>() logs error and returns null
            LogAssert.Expect(LogType.Error, new Regex(@"Ambiguous GetModel request for model type.*SamplePlayerModel.*evicted from singleton lookup"));
            Assert.IsNull(locator.GetModel<SamplePlayerModel>());

            // Destroy colliding instance logicB
            UnityEngine.Object.DestroyImmediate(logicB);
            UnityEngine.Object.DestroyImmediate(_goB);

            // Pruning restores single remaining instance logicA and modelA
            Assert.AreSame(logicA, locator.Get<SamplePlayerLogic>());
            Assert.AreSame(modelA, locator.GetModel<SamplePlayerModel>());

            UnityEngine.Object.DestroyImmediate(locatorGo);
        }

        [Test]
        public void ServiceLocator_NamedLookup_And_GetModelWithKey() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            var logicA = _goA.AddComponent<SamplePlayerLogic>();
            logicA.SetSingletonName("PlayerA");
            var modelA = _goA.GetComponent<SamplePlayerModel>() ?? _goA.AddComponent<SamplePlayerModel>();

            var logicB = _goB.AddComponent<SamplePlayerLogic>();
            logicB.SetSingletonName("PlayerB");
            var modelB = _goB.GetComponent<SamplePlayerModel>() ?? _goB.AddComponent<SamplePlayerModel>();

            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicA));
            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicB));

            Assert.AreSame(logicA, locator.Get<SamplePlayerLogic>("PlayerA"));
            Assert.AreSame(logicB, locator.Get<SamplePlayerLogic>("PlayerB"));

            Assert.AreSame(modelA, locator.GetModel<SamplePlayerModel>("PlayerA"));
            Assert.AreSame(modelB, locator.GetModel<SamplePlayerModel>("PlayerB"));

            UnityEngine.Object.DestroyImmediate(locatorGo);
        }

        public class MockModelWithNonSaved : BaseObservableDataModel {
            public ObservableVariable<int> persistentValue;
            [NonSaved]
            public ObservableVariable<string> transientSessionToken;
        }

        [Test]
        public void NonSavedAttribute_IsSkipped_During_LoadDataFromModel() {
            var source = _goA.AddComponent<MockModelWithNonSaved>();
            source.EnsureFieldsInitialized();
            source.persistentValue.Value = 42;
            source.transientSessionToken.Value = "SESSION_123";

            var target = _goB.AddComponent<MockModelWithNonSaved>();
            target.EnsureFieldsInitialized();
            target.persistentValue.Value = 0;
            target.transientSessionToken.Value = "ORIGINAL_TOKEN";

            target.LoadDataFromModel(source);

            Assert.AreEqual(42, target.persistentValue.Value);
            Assert.AreEqual("ORIGINAL_TOKEN", target.transientSessionToken.Value, "Fields with [NonSaved] must not be overwritten by LoadDataFromModel");
        }

        [Test]
        public void ComponentAutoRequire_RejectsNonComponentTypes() {
            var dummy = _goA.AddComponent<SamplePlayerModel>();

            LogAssert.Expect(LogType.Error, new Regex(@"Cannot auto-add 'String' to 'SamplePlayerModel'. Type must inherit from UnityEngine.Component"));

            var autoRequireType = typeof(BaseObservableDataModel).Assembly.GetType("SavableObservable.ComponentAutoRequire");
            var ensureMethod = autoRequireType?.GetMethod("EnsureComponent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static, null, new[] { typeof(MonoBehaviour), typeof(Type) }, null);
            ensureMethod?.Invoke(null, new object[] { dummy, typeof(string) });
        }

        public class MockFailingPresenter : BaseObservablePresenter<SamplePlayerModel> {
            public int InitializeAttempts { get; set; }
            public bool ShouldThrow { get; set; } = true;

            protected override void InitializePresenter() {
                base.InitializePresenter();
                InitializeAttempts++;
                if (ShouldThrow) {
                    throw new InvalidOperationException("Simulated InitializePresenter failure");
                }
            }
        }

        public class MockFailingLogic : BaseLogic<SamplePlayerModel, MockFailingPresenter> { }

        [Test]
        public void EnsureInitialized_OnFailure_DoesNotPermanentlyLatch_AndAllowsRetry() {
            var model = _goA.AddComponent<SamplePlayerModel>();
            var presenter = _goA.AddComponent<MockFailingPresenter>();
            var logic = _goA.AddComponent<MockFailingLogic>();

            // 1. Initial attempt fails
            presenter.ShouldThrow = true;
            Assert.Throws<InvalidOperationException>(() => logic.GetModel());
            Assert.AreEqual(1, presenter.InitializeAttempts);

            // 2. Second attempt still retries and throws (not permanently latched as initialized)
            Assert.Throws<InvalidOperationException>(() => logic.GetModel());
            Assert.AreEqual(2, presenter.InitializeAttempts);

            // 3. Fix condition: subsequent attempt succeeds cleanly
            presenter.ShouldThrow = false;
            var retrievedModel = logic.GetModel();
            Assert.AreSame(model, retrievedModel);
            Assert.AreEqual(3, presenter.InitializeAttempts);

            // 4. Repeated EnsureInitialized does not call InitializePresenter again
            presenter.EnsureInitialized();
            Assert.AreEqual(3, presenter.InitializeAttempts);
        }

        public class MockSameUnitReentrantPresenter : BaseObservablePresenter<SamplePlayerModel> {
            public int InitializeCalls { get; private set; }
            public int ReentrantCallCount { get; private set; }

            protected override void InitializePresenter() {
                base.InitializePresenter();
                InitializeCalls++;
                // Reentrant call to own EnsureInitialized during InitializePresenter
                EnsureInitialized();
                ReentrantCallCount++;
            }
        }

        public class MockSameUnitReentrantLogic : BaseLogic<SamplePlayerModel, MockSameUnitReentrantPresenter> { }

        public class MockCrossUnitTargetPresenter : BaseObservablePresenter<SamplePlayerModel> {
            public int InitializeCalls { get; private set; }

            protected override void InitializePresenter() {
                base.InitializePresenter();
                InitializeCalls++;
            }
        }

        public class MockCrossUnitTargetLogic : BaseLogic<SamplePlayerModel, MockCrossUnitTargetPresenter> { }

        public class MockCrossUnitSourcePresenter : BaseObservablePresenter<SamplePlayerModel> {
            public MockCrossUnitTargetLogic TargetLogic { get; set; }
            public int InitializeCalls { get; private set; }

            protected override void InitializePresenter() {
                base.InitializePresenter();
                InitializeCalls++;
                if (TargetLogic != null) {
                    // Call GetModel on another unit during own InitializePresenter
                    TargetLogic.GetModel();
                }
            }
        }

        public class MockCrossUnitSourceLogic : BaseLogic<SamplePlayerModel, MockCrossUnitSourcePresenter> { }

        [Test]
        public void Reentrancy_SameUnit_And_CrossUnit_DistinguishesInitializingFromCompleted() {
            // Same-unit reentrant check
            var modelA = _goA.AddComponent<SamplePlayerModel>();
            var presenterA = _goA.AddComponent<MockSameUnitReentrantPresenter>();
            var logicA = _goA.AddComponent<MockSameUnitReentrantLogic>();

            Assert.DoesNotThrow(() => logicA.GetModel());
            Assert.AreEqual(1, presenterA.InitializeCalls, "Same-unit reentrant EnsureInitialized must not re-execute InitializePresenter");
            Assert.AreEqual(1, presenterA.ReentrantCallCount);

            // Cross-unit reentrant check
            var modelB = _goB.AddComponent<SamplePlayerModel>();
            var presenterB = _goB.AddComponent<MockCrossUnitTargetPresenter>();
            var logicB = _goB.AddComponent<MockCrossUnitTargetLogic>();

            var modelC = _goC.AddComponent<SamplePlayerModel>();
            var presenterC = _goC.AddComponent<MockCrossUnitSourcePresenter>();
            var logicC = _goC.AddComponent<MockCrossUnitSourceLogic>();
            presenterC.TargetLogic = logicB;

            Assert.DoesNotThrow(() => logicC.GetModel());
            Assert.AreEqual(1, presenterC.InitializeCalls);
            Assert.AreEqual(1, presenterB.InitializeCalls, "Target unit must initialize cleanly when called from source unit's InitializePresenter");
        }

        [Test]
        public void InactivePrefab_LogicGetModel_InitializesAndBindsPresenter_BeforeModelMutation() {
            _goA.SetActive(false);

            var model = _goA.AddComponent<SamplePlayerModel>();
            model.InitializeDefaults();
            var logic = _goA.AddComponent<SamplePlayerLogic>();
            var presenter = _goA.GetComponent<SamplePlayerPresenter>();

            // Unit is inactive: Awake has not run on presenter or logic
            Assert.IsFalse(_goA.activeSelf);
            Assert.AreEqual(0, presenter.HealthChangeEventsCount);

            // Accessing GetModel on inactive logic must trigger presenter initialization & binding
            var retrievedModel = logic.GetModel();
            Assert.AreSame(model, retrievedModel);

            // Mutate model: presenter must receive reactive handler notifications
            logic.TakeDamage(25);
            Assert.AreEqual(75, retrievedModel.health.Value);
            Assert.AreEqual(1, presenter.HealthChangeEventsCount);
            Assert.AreEqual(75, presenter.LastObservedHealth);
        }

        public class MockCountingHandlerPresenter : BaseObservablePresenter<SamplePlayerModel> {
            public int HealthHandlerCalls { get; private set; }

            [ObservableHandler("health")]
            private void OnHealthChanged(int val) {
                HealthHandlerCalls++;
            }
        }

        [Test]
        public void EnsureInitialized_IsIdempotent_DoesNotCauseDuplicateCallbacks() {
            var model = _goA.AddComponent<SamplePlayerModel>();
            model.InitializeDefaults();
            var presenter = _goA.AddComponent<MockCountingHandlerPresenter>();

            // First EnsureInitialized wires listeners
            presenter.EnsureInitialized();
            Assert.AreEqual(0, presenter.HealthHandlerCalls);

            // Repeated EnsureInitialized calls must be idempotent no-ops
            presenter.EnsureInitialized();
            presenter.EnsureInitialized();

            // Mutate value
            model.health.Value = 80;
            Assert.AreEqual(1, presenter.HealthHandlerCalls, "Exactly one callback invocation expected; repeated EnsureInitialized must not duplicate listeners");

            // Explicit loader rebind via Observable.SetListeners cleans up and rebinds without duplicate callbacks
            Observable.SetListeners(presenter, model);
            model.health.Value = 60;
            Assert.AreEqual(2, presenter.HealthHandlerCalls, "Observable.SetListeners replaces subscriptions cleanly without duplicating callbacks");
        }

        public class MockTestLoader : LoaderWithModel<SamplePlayerModel> { }

        [Test]
        public void MMVCExecutionOrderSynchronizer_GetTargetExecutionOrder_ReturnsExpectedExecutionOrders() {
            Assert.AreEqual(-300, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(SamplePlayerModel)));
            Assert.AreEqual(-250, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(MockTestLoader)));
            Assert.AreEqual(-200, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(ServiceLocator)));
            Assert.AreEqual(-150, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(SamplePlayerPresenter)));
            Assert.AreEqual(0, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(SamplePlayerLogic)));
            Assert.AreEqual(int.MinValue, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(MonoBehaviour)));
            Assert.AreEqual(int.MinValue, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(string)));
        }

        [Test]
        public void DynamicBaseLogic_AutoRegistersOnAwake_And_UnregistersOnDestroy_WithCollisionRecovery() {
            var locatorObj = new GameObject("LocatorHost");
            var locator = locatorObj.AddComponent<ServiceLocator>();
            Services.SetTestLocator(locator);

            var awakeMethod = typeof(BaseLogic).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var onDestroyMethod = typeof(BaseLogic).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            try {
                // Dynamically instantiate a new GameObject with BaseLogic
                var dynamicObj1 = new GameObject("DynamicUnit1");
                dynamicObj1.AddComponent<SamplePlayerModel>();
                dynamicObj1.AddComponent<SamplePlayerPresenter>();
                var logic1 = dynamicObj1.AddComponent<SamplePlayerLogic>();
                awakeMethod.Invoke(logic1, null);

                // Logic1 should have auto-registered in Awake()
                Assert.IsTrue(locator.IsRegistered<SamplePlayerLogic>(), "Dynamically created BaseLogic must auto-register with ServiceLocator in Awake");
                Assert.AreSame(logic1, Services.Get<SamplePlayerLogic>());
                Assert.IsNotNull(Services.GetModel<SamplePlayerModel>(), "Model of dynamically created BaseLogic must be resolvable");

                // Dynamically instantiate a second colliding BaseLogic instance (same Name)
                var dynamicObj2 = new GameObject("DynamicUnit2");
                dynamicObj2.AddComponent<SamplePlayerModel>();
                dynamicObj2.AddComponent<SamplePlayerPresenter>();
                var logic2 = dynamicObj2.AddComponent<SamplePlayerLogic>();
                awakeMethod.Invoke(logic2, null);

                // Key should now be evicted due to collision (> 1 live instance with same Name)
                LogAssert.Expect(LogType.Error, new Regex(@"\[ServiceLocator\] Ambiguous Get request for type"));
                Assert.IsNull(Services.Get<SamplePlayerLogic>(), "Colliding instances should be evicted from lookup");

                // Destroy logic2: logic1 should recover lookup cleanly
                onDestroyMethod.Invoke(logic2, null);
                UnityEngine.Object.DestroyImmediate(dynamicObj2);

                Assert.IsTrue(locator.IsRegistered<SamplePlayerLogic>(), "Surviving instance must recover lookup after colliding instance is destroyed");
                Assert.AreSame(logic1, Services.Get<SamplePlayerLogic>(), "Surviving instance must resolve correctly");

                // Destroy logic1: binding must be completely removed
                onDestroyMethod.Invoke(logic1, null);
                UnityEngine.Object.DestroyImmediate(dynamicObj1);

                Assert.IsFalse(locator.IsRegistered<SamplePlayerLogic>(), "Service must be unregistered after all instances are destroyed");
                Assert.IsNull(Services.Get<SamplePlayerLogic>());
            } finally {
                UnityEngine.Object.DestroyImmediate(locatorObj);
            }
        }
    }
}
