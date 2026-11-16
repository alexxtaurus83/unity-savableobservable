using System;
using System.Reflection;
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
            var presenterAttrs = typeof(BaseObservablePresenter<SamplePlayerModel>).GetCustomAttributes(typeof(DisallowMultipleComponent), true);
            Assert.IsTrue(presenterAttrs.Length > 0, "BaseObservablePresenter must have [DisallowMultipleComponent]");

            // Rule 3: One GameObject = one logic.
            var logicAttrs = typeof(BaseLogic).GetCustomAttributes(typeof(DisallowMultipleComponent), true);
            Assert.IsTrue(logicAttrs.Length > 0, "BaseLogic must have [DisallowMultipleComponent]");
        }

        [Test]
        public void ServiceLocatorAndPresenter_HaveExplicitDefaultExecutionOrderAttributes() {
            Assert.AreEqual(-200, typeof(ServiceLocator).GetCustomAttribute<DefaultExecutionOrder>()?.order,
                "ServiceLocator must declare [DefaultExecutionOrder(-200)]");
            Assert.AreEqual(-150, typeof(BaseObservablePresenter<>).GetCustomAttribute<DefaultExecutionOrder>()?.order,
                "BaseObservablePresenter<> must declare [DefaultExecutionOrder(-150)]");
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

        private static void InvokeReset(MonoBehaviour target) {
            var reset = target.GetType().GetMethod("Reset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            Assert.IsNotNull(reset, $"{target.GetType().Name} must expose a Reset hook that provisions its required components.");
            reset.Invoke(target, null);
        }

        [Test]
        public void ComponentAutoRequire_EnsureComponents_AddsEveryListedType_AndIsIdempotent() {
            var owner = _goA.AddComponent<MockTestLoader>();

            ComponentAutoRequire.EnsureComponents(owner, false, typeof(SamplePlayerModel), typeof(SamplePlayerLogic));
            ComponentAutoRequire.EnsureComponents(owner, false, typeof(SamplePlayerModel), typeof(SamplePlayerLogic));

            Assert.AreEqual(1, _goA.GetComponents<SamplePlayerModel>().Length);
            Assert.AreEqual(1, _goA.GetComponents<SamplePlayerLogic>().Length);
        }

        [Test]
        public void BaseLogic_Reset_ProvisionsModelAndPresenter() {
            var logic = _goA.AddComponent<SamplePlayerLogic>();

            InvokeReset(logic);

            Assert.IsNotNull(_goA.GetComponent<SamplePlayerModel>());
            Assert.IsNotNull(_goA.GetComponent<SamplePlayerPresenter>());
            Assert.AreEqual(1, _goA.GetComponents<SamplePlayerModel>().Length);
            Assert.AreEqual(1, _goA.GetComponents<SamplePlayerPresenter>().Length);
        }

        [Test]
        public void LoaderWithModel_Reset_ProvisionsModel() {
            var loader = _goA.AddComponent<MockTestLoader>();

            InvokeReset(loader);

            Assert.IsNotNull(_goA.GetComponent<SamplePlayerModel>());
        }

        [Test]
        public void LoaderWithModelAndLogic_Reset_ProvisionsModelAndLogic() {
            var loader = _goA.AddComponent<SamplePlayerLoader>();

            InvokeReset(loader);

            Assert.IsNotNull(_goA.GetComponent<SamplePlayerModel>());
            Assert.IsNotNull(_goA.GetComponent<SamplePlayerLogic>());
            Assert.AreEqual(1, _goA.GetComponents<SamplePlayerModel>().Length);
            Assert.AreEqual(1, _goA.GetComponents<SamplePlayerLogic>().Length);
        }

        [Test]
        public void InitializePresenter_RunsExactlyOnce_AcrossEveryEntryPath() {
            var model = _goA.AddComponent<SamplePlayerModel>();
            var presenter = _goA.AddComponent<SamplePlayerPresenter>();
            var logic = _goA.AddComponent<SamplePlayerLogic>();

            logic.GetModel();                             // Logic.GetModel -> presenter.EnsureInitialized
            presenter.EnsureInitialized();                // explicit
            logic.GetModel();                             // again
            Observable.SetListeners(presenter, model);    // explicit rebind must not re-run InitializePresenter

            Assert.AreEqual(1, presenter.InitializePresenterCount);
        }

        [Test]
        public void SetListeners_RunsInitializePresenter_BeforeAttachingHandlers_WhenCalledFirst() {
            var model = _goA.AddComponent<SamplePlayerModel>();
            var presenter = _goA.AddComponent<SamplePlayerPresenter>();

            Observable.SetListeners(presenter, model);

            Assert.AreEqual(1, presenter.InitializePresenterCount);
            model.health.Value = 10;
            Assert.AreEqual(10, presenter.LastObservedHealth, "Handlers are attached after InitializePresenter.");
            presenter.EnsureInitialized();
            Assert.AreEqual(1, presenter.InitializePresenterCount, "EnsureInitialized after an explicit bind must not re-run InitializePresenter.");
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
        public void ServiceLocator_TryRegister_Rejects_PlainClass() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            // Plain C# class implementing ISharedSingleton (not a MonoBehaviour) must be rejected
            var plainClass = new MockPlainSharedSingleton();
            LogAssert.Expect(LogType.Error, new Regex(@"is not a MonoBehaviour"));
            var resPlain = locator.TryRegister(plainClass);
            Assert.AreEqual(RegistrationResult.Rejected, resPlain);

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
        public void ServiceLocator_Duplicate_FirstWins_AndNamedInstanceIsUnaffected() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            // Distinct named service
            var logicNamed = _goA.AddComponent<SamplePlayerLogic>();
            logicNamed.SetSingletonName("NamedPlayer");

            // Two unnamed services sharing the default key: the first keeps it
            var logicB = _goB.AddComponent<SamplePlayerLogic>();
            var logicC = _goC.AddComponent<SamplePlayerLogic>();

            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicNamed));
            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicB));

            LogAssert.Expect(LogType.Error, new Regex(@"Duplicate registration of .*SamplePlayerLogic.* key 'SamplePlayerLogic'.*UnitB.*already holds this key.*UnitC.*NOT registered"));
            Assert.AreEqual(RegistrationResult.Duplicate, locator.TryRegister(logicC));

            // Named resolution succeeds, the default key resolves to the FIRST instance (no error, nothing displaced)
            Assert.AreSame(logicNamed, locator.Get<SamplePlayerLogic>("NamedPlayer"));
            Assert.AreSame(logicB, locator.Get<SamplePlayerLogic>(), "The first registration keeps the default key.");
            Assert.AreSame(logicB, locator.Get<SamplePlayerLogic>("SamplePlayerLogic"));

            // Models come from the registered Logic units: named lookup is exact
            Assert.AreSame(logicNamed.GetModel(), locator.GetModel<SamplePlayerModel>("NamedPlayer"));
            Assert.AreSame(logicB.GetModel(), locator.GetModel<SamplePlayerModel>("SamplePlayerLogic"));

            // Unnamed GetModel: two registered Logic units own this model type -> ambiguous
            LogAssert.Expect(LogType.Error, new Regex(@"Ambiguous GetModel request for model type.*SamplePlayerModel"));
            Assert.IsNull(locator.GetModel<SamplePlayerModel>());

            UnityEngine.Object.DestroyImmediate(locatorGo);
        }

        [Test]
        public void ServiceLocator_Unregister_OnlyRemoves_TargetUnitModel() {
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

            // Unregister PlayerA
            locator.Unregister(logicA);

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
        public void ServiceLocator_Duplicate_OnNamedKey_DoesNotAffectDefaultKey() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            // 1. One default unnamed service (key = "SamplePlayerLogic")
            var logicDefault = _goA.AddComponent<SamplePlayerLogic>();

            // 2. Two services sharing the named key "Secondary": the first keeps it
            var logicSec1 = _goB.AddComponent<SamplePlayerLogic>();
            logicSec1.SetSingletonName("Secondary");
            var logicSec2 = _goC.AddComponent<SamplePlayerLogic>();
            logicSec2.SetSingletonName("Secondary");

            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicDefault));
            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicSec1));

            LogAssert.Expect(LogType.Error, new Regex(@"Duplicate registration of .*SamplePlayerLogic.* key 'Secondary'"));
            Assert.AreEqual(RegistrationResult.Duplicate, locator.TryRegister(logicSec2));

            Assert.AreSame(logicDefault, locator.Get<SamplePlayerLogic>());
            Assert.AreSame(logicDefault.GetModel(), locator.GetModel<SamplePlayerModel>("SamplePlayerLogic"));

            // The duplicated key resolves to its FIRST holder (not null, no error)
            Assert.AreSame(logicSec1, locator.Get<SamplePlayerLogic>("Secondary"));
            Assert.AreSame(logicSec1.GetModel(), locator.GetModel<SamplePlayerModel>("Secondary"));

            UnityEngine.Object.DestroyImmediate(locatorGo);
        }

        [Test]
        public void ServiceLocator_Duplicate_FirstKeepsKey_RejectedInstanceNeverRegistered_And_KeyEmptiesWhenFirstDies() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            var logicA = _goA.AddComponent<SamplePlayerLogic>();
            var modelA = _goA.GetComponent<SamplePlayerModel>() ?? _goA.AddComponent<SamplePlayerModel>();
            var logicB = _goB.AddComponent<SamplePlayerLogic>();
            _goB.GetComponent<SamplePlayerModel>();

            // Both have default Name = "SamplePlayerLogic"
            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicA));
            Assert.AreSame(logicA, locator.Get<SamplePlayerLogic>());
            Assert.AreSame(modelA, locator.GetModel<SamplePlayerModel>());

            // Second instance with the same key: error at registration time, first instance keeps the key
            LogAssert.Expect(LogType.Error, new Regex(@"Duplicate registration of .*SamplePlayerLogic.*already holds this key"));
            Assert.AreEqual(RegistrationResult.Duplicate, locator.TryRegister(logicB));
            Assert.AreSame(logicA, locator.Get<SamplePlayerLogic>());
            Assert.AreSame(modelA, locator.GetModel<SamplePlayerModel>());

            // Unregistering the rejected duplicate must not displace the holder
            locator.Unregister(logicB);
            Assert.AreSame(logicA, locator.Get<SamplePlayerLogic>(), "Unregister of a never-registered instance is a no-op.");

            // Destroy the first holder: the key is empty (the second was never registered), no error, no recovery
            UnityEngine.Object.DestroyImmediate(logicA);
            Assert.IsNull(locator.Get<SamplePlayerLogic>(), "The rejected instance does not silently take over the key.");
            Assert.IsNull(locator.GetModel<SamplePlayerModel>(), "A destroyed Logic no longer resolves its model.");
            Assert.IsFalse(locator.IsRegistered<SamplePlayerLogic>());

            // A fresh registration of the surviving instance now succeeds
            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicB));
            Assert.AreSame(logicB, locator.Get<SamplePlayerLogic>());

            UnityEngine.Object.DestroyImmediate(locatorGo);
        }

        [Test]
        public void ServiceLocator_Get_Unnamed_SoleEntryFallback_And_AmbiguousAcrossKeys() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            var logicA = _goA.AddComponent<SamplePlayerLogic>();
            logicA.SetSingletonName("PlayerA");
            var logicB = _goB.AddComponent<SamplePlayerLogic>();
            logicB.SetSingletonName("PlayerB");

            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicA));

            // Default key is absent, exactly one instance of the type is registered: unnamed Get falls back to it
            Assert.AreSame(logicA, locator.Get<SamplePlayerLogic>());
            Assert.AreSame(logicA, locator.Get<SamplePlayerLogic>("PlayerA"));
            Assert.IsNull(locator.Get<SamplePlayerLogic>("Missing"));

            Assert.AreEqual(RegistrationResult.Added, locator.TryRegister(logicB));

            // Two instances under different keys and no default key: unnamed Get is ambiguous
            LogAssert.Expect(LogType.Error, new Regex(@"Ambiguous Get request for type.*SamplePlayerLogic.*PlayerA.*PlayerB|Ambiguous Get request for type.*SamplePlayerLogic.*PlayerB.*PlayerA"));
            Assert.IsNull(locator.Get<SamplePlayerLogic>());
            Assert.AreSame(logicB, locator.Get<SamplePlayerLogic>("PlayerB"));

            UnityEngine.Object.DestroyImmediate(locatorGo);
        }

        [Test]
        public void ServiceLocator_GetModel_Unique_Named_Ambiguous_AndNullAfterLogicDestroyed() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            var logicA = _goA.AddComponent<SamplePlayerLogic>();
            logicA.SetSingletonName("PlayerA");
            var modelA = _goA.GetComponent<SamplePlayerModel>();
            var logicB = _goB.AddComponent<SamplePlayerLogic>();
            logicB.SetSingletonName("PlayerB");
            var modelB = _goB.GetComponent<SamplePlayerModel>();

            Assert.IsNull(locator.GetModel<SamplePlayerModel>(), "Nothing registered: no model.");

            locator.TryRegister(logicA);
            Assert.AreSame(modelA, locator.GetModel<SamplePlayerModel>(), "A unique model resolves without a name.");

            locator.TryRegister(logicB);
            Assert.AreSame(modelA, locator.GetModel<SamplePlayerModel>("PlayerA"));
            Assert.AreSame(modelB, locator.GetModel<SamplePlayerModel>("PlayerB"));
            LogAssert.Expect(LogType.Error, new Regex(@"Ambiguous GetModel request for model type.*SamplePlayerModel"));
            Assert.IsNull(locator.GetModel<SamplePlayerModel>(), "Two Logic units own this model type: unnamed lookup is ambiguous.");

            UnityEngine.Object.DestroyImmediate(logicB);
            Assert.IsNull(locator.GetModel<SamplePlayerModel>("PlayerB"), "A destroyed Logic no longer resolves its model.");
            Assert.AreSame(modelA, locator.GetModel<SamplePlayerModel>(), "The surviving Logic's model is unique again.");

            UnityEngine.Object.DestroyImmediate(locatorGo);
        }

        [Test]
        public void ServiceLocator_Unregister_OfNonStoredInstance_IsNoOp() {
            var locatorGo = new GameObject("LocatorHost");
            var locator = locatorGo.AddComponent<ServiceLocator>();

            var holder = _goA.AddComponent<SamplePlayerLogic>();
            var stranger = _goB.AddComponent<SamplePlayerLogic>();
            locator.TryRegister(holder);

            locator.Unregister(stranger);
            Assert.AreSame(holder, locator.Get<SamplePlayerLogic>(), "A different instance with the same key must not remove the holder.");

            locator.Unregister(holder);
            Assert.IsFalse(locator.IsRegistered<SamplePlayerLogic>());

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

            // Loaders no longer rebind; an explicit Observable.SetListeners call still replaces subscriptions cleanly
            Observable.SetListeners(presenter, model);
            model.health.Value = 60;
            Assert.AreEqual(2, presenter.HealthHandlerCalls, "Observable.SetListeners replaces subscriptions cleanly without duplicating callbacks");
        }

        public class MockTestLoader : LoaderWithModel<SamplePlayerModel> { }

        [Test]
        public void MMVCExecutionOrderSynchronizer_GetTargetExecutionOrder_ReturnsExpectedExecutionOrders() {
            // Models, loaders and logic have no order dependency: they sit at 0 (this also resets an earlier non-default order)
            Assert.AreEqual(0, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(SamplePlayerModel)));
            Assert.AreEqual(0, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(MockTestLoader)));
            Assert.AreEqual(-200, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(ServiceLocator)));
            Assert.AreEqual(-150, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(SamplePlayerPresenter)));
            Assert.AreEqual(0, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(SamplePlayerLogic)));
            Assert.AreEqual(int.MinValue, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(MonoBehaviour)));
            Assert.AreEqual(int.MinValue, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(string)));
            Assert.AreEqual(int.MinValue, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(BaseObservableDataModel)), "The abstract base itself is not synchronized.");
        }

        [DefaultExecutionOrder(42)]
        private class CustomOrderModel : BaseObservableDataModel { }

        [DefaultExecutionOrder(-50)]
        private class CustomOrderLogic : BaseLogic<SamplePlayerModel, SamplePlayerPresenter> { }

        [DefaultExecutionOrder(100)]
        private class CustomOrderLoader : LoaderWithModel<SamplePlayerModel> { }

        [Test]
        public void MMVCExecutionOrderSynchronizer_GetTargetExecutionOrder_PreservesExplicitDefaultExecutionOrderOnUserScripts() {
            Assert.AreEqual(42, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(CustomOrderModel)));
            Assert.AreEqual(-50, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(CustomOrderLogic)));
            Assert.AreEqual(100, MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(typeof(CustomOrderLoader)));
        }

        [Test]
        public void ServiceLocator_Awake_ReplacesDeadOrDyingLocator_PreservingInstanceThroughDeferredOnDestroy() {
            var oldGo = new GameObject("OldLocatorGo");
            var oldLocator = oldGo.AddComponent<ServiceLocator>();
            var awakeMethod = typeof(ServiceLocator).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            awakeMethod.Invoke(oldLocator, null);
            Assert.AreSame(oldLocator, ServiceLocator.instance);

            // Simulate frame where oldLocator is Destroy()ed (not DestroyImmediate)
            // In the same frame before OnDestroy runs, a new ServiceLocator awakens (e.g. new scene or replacement)
            var newGo = new GameObject("NewLocatorGo");
            var newLocator = newGo.AddComponent<ServiceLocator>();
            awakeMethod.Invoke(newLocator, null);

            // New locator should have taken over instance
            Assert.AreSame(newLocator, ServiceLocator.instance);

            // Simulate deferred OnDestroy of oldLocator
            var onDestroyMethod = typeof(ServiceLocator).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(onDestroyMethod);
            onDestroyMethod.Invoke(oldLocator, null);

            // New locator instance must NOT have been wiped out by oldLocator.OnDestroy()
            Assert.AreSame(newLocator, ServiceLocator.instance, "Dying locator's deferred OnDestroy must not clear the new locator's instance");

            // Restore static state
            UnityEngine.Object.DestroyImmediate(oldGo);
            UnityEngine.Object.DestroyImmediate(newGo);
            ServiceLocator.instance = null;
        }

        [Test]
        public void FrameworkBaseClasses_DeclareOnlyServiceLocatorAndPresenterExecutionOrder() {
            Assert.IsEmpty(typeof(BaseObservableDataModel).GetCustomAttributes(typeof(DefaultExecutionOrder), false));
            Assert.IsEmpty(typeof(LoaderWithModel<SamplePlayerModel>).GetCustomAttributes(typeof(DefaultExecutionOrder), false));
            Assert.IsEmpty(typeof(BaseLogic).GetCustomAttributes(typeof(DefaultExecutionOrder), false));

            var locator = (DefaultExecutionOrder)typeof(ServiceLocator).GetCustomAttributes(typeof(DefaultExecutionOrder), false)[0];
            var presenter = (DefaultExecutionOrder)typeof(BaseObservablePresenter<SamplePlayerModel>).GetCustomAttributes(typeof(DefaultExecutionOrder), false)[0];
            Assert.AreEqual(-200, locator.order);
            Assert.AreEqual(-150, presenter.order);
        }

        public class DefaultOrderModel : BaseObservableDataModel {
            public ObservableVariable<int> level = new ObservableVariable<int>();
        }

        public class DefaultOrderPresenter : BaseObservablePresenter<DefaultOrderModel> {
            public int Changes { get; private set; }

            [ObservableHandler(nameof(DefaultOrderModel.level))]
            private void OnLevel(int _) { Changes++; }
        }

        public class DefaultOrderLoader : LoaderWithModel<DefaultOrderModel> { }

        [Test]
        public void Model_AtDefaultOrder_WithPresenterOnSameGameObject_PresenterBindsAndFieldsAreInitialized() {
            // Presenter first, model second: simulates the presenter's earlier Awake relative to the model's
            var presenter = _goA.AddComponent<DefaultOrderPresenter>();
            var model = _goA.AddComponent<DefaultOrderModel>();

            presenter.EnsureInitialized();
            model.level.Value = 3;

            Assert.IsNotNull(model.level, "Fields are initialized lazily through GetModel().");
            Assert.AreEqual(1, presenter.Changes, "The presenter binds regardless of the model's Awake order.");
        }

        [Test]
        public void Loader_LoadDataFromModel_BeforeModelAwake_WorksThroughLazyInit() {
            var loader = _goA.AddComponent<DefaultOrderLoader>(); // Reset auto-provisions the model in the Editor
            var model = _goA.GetComponent<DefaultOrderModel>();
            Assert.IsNotNull(model);
            model.level = null; // as if the model's Awake had not initialized its fields yet

            var sourceGo = new GameObject("Source");
            try {
                var source = sourceGo.AddComponent<DefaultOrderModel>();
                source.EnsureFieldsInitialized();
                source.level.Value = 9;

                loader.LoadDataFromModel(source);

                Assert.AreEqual(9, model.level.Value, "GetModel() initializes the fields lazily, so loading before Awake works.");
            } finally {
                UnityEngine.Object.DestroyImmediate(sourceGo);
            }
        }

        [Test]
        public void DynamicBaseLogic_AutoRegistersOnAwake_And_UnregistersOnDestroy_DuplicateNeverEvictsFirst() {
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

                // Dynamically instantiate a second BaseLogic instance with the same Name: duplicate = error, first wins
                var dynamicObj2 = new GameObject("DynamicUnit2");
                dynamicObj2.AddComponent<SamplePlayerModel>();
                dynamicObj2.AddComponent<SamplePlayerPresenter>();
                var logic2 = dynamicObj2.AddComponent<SamplePlayerLogic>();
                LogAssert.Expect(LogType.Error, new Regex(@"\[ServiceLocator\] Duplicate registration of .*SamplePlayerLogic"));
                awakeMethod.Invoke(logic2, null);

                Assert.AreSame(logic1, Services.Get<SamplePlayerLogic>(), "The first instance keeps the key; the duplicate is not registered.");

                // Destroy the duplicate: the holder must stay registered (OnDestroy only removes a stored instance)
                onDestroyMethod.Invoke(logic2, null);
                UnityEngine.Object.DestroyImmediate(dynamicObj2);

                Assert.IsTrue(locator.IsRegistered<SamplePlayerLogic>(), "Destroying the rejected duplicate must not unregister the holder");
                Assert.AreSame(logic1, Services.Get<SamplePlayerLogic>());

                // Destroy logic1: binding must be completely removed
                onDestroyMethod.Invoke(logic1, null);
                UnityEngine.Object.DestroyImmediate(dynamicObj1);

                Assert.IsFalse(locator.IsRegistered<SamplePlayerLogic>(), "Service must be unregistered after the holder is destroyed");
                Assert.IsNull(Services.Get<SamplePlayerLogic>());
            } finally {
                UnityEngine.Object.DestroyImmediate(locatorObj);
            }
        }

        [Test]
        public void InitializeSharedSingletons_ScopesStrictlyToOwnScene_EvenWhenSceneNotLoaded() {
            var sceneA = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var sceneB = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try {
                var locatorHostA = new GameObject("LocatorHostA");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(locatorHostA, sceneA);
                var locatorA = locatorHostA.AddComponent<ServiceLocator>();

                var unitA = new GameObject("UnitA");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(unitA, sceneA);
                unitA.AddComponent<SamplePlayerModel>();
                unitA.AddComponent<SamplePlayerPresenter>();
                var logicA = unitA.AddComponent<SamplePlayerLogic>();

                var unitB = new GameObject("UnitB");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(unitB, sceneB);
                unitB.AddComponent<SamplePlayerModel>();
                unitB.AddComponent<SamplePlayerPresenter>();
                var logicB = unitB.AddComponent<SamplePlayerLogic>();

                locatorA.InitializeSharedSingletons();

                Assert.IsTrue(locatorA.IsRegistered(logicA), "Logic in own scene should be registered");
                Assert.AreSame(logicA, locatorA.Get<SamplePlayerLogic>(), "Locator must find logic in its own scene");
                Assert.AreNotSame(logicB, locatorA.Get<SamplePlayerLogic>(), "Logic in different scene must not be registered; IsRegistered(instance) checks its type/key, not instance identity.");
            } finally {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(sceneA);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(sceneB);
            }
        }
    }
}
