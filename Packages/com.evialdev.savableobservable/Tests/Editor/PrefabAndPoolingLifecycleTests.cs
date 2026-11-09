using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SavableObservable;
using SavableObservable.Samples;

namespace SavableObservable.Tests {
    #region Top-Level Fixture Components for Tests

    public class PrefabTestModel : BaseObservableDataModel {
        public ObservableVariable<int> score = new ObservableVariable<int>();
        public ObservableVariable<string> playerName = new ObservableVariable<string>();
        public ObservableVariable<bool> isReady = new ObservableVariable<bool>();
        public ObservableVariable<float> progress = new ObservableVariable<float>();
        public ObservableVariable<int> level = new ObservableVariable<int>();
    }

    public class NonSingletonPrefabLogic : BaseLogic<PrefabTestModel, PrefabTestPresenter> {
        protected override bool RegisterAsSharedSingleton => false;
        public int customData = 42;
    }

    public class PrefabTestPresenter : BaseObservablePresenter<PrefabTestModel> {
        [AutoBind(nameof(PrefabTestModel.playerName))]
        public TextMeshProUGUI nameDisplay;

        [AutoBind(nameof(PrefabTestModel.isReady))]
        public Toggle readyToggle;

        [AutoBind(nameof(PrefabTestModel.progress))]
        public Slider progressSlider;

        [AutoBind(nameof(PrefabTestModel.score))]
        public TMP_InputField scoreInput;

        public int ScoreChangedCount { get; private set; }
        public int LastScoreCurrent { get; private set; }
        public int LastScorePrevious { get; private set; }

        [ObservableHandler(nameof(PrefabTestModel.score))]
        private void OnScoreChanged(int current, int previous) {
            ScoreChangedCount++;
            LastScoreCurrent = current;
            LastScorePrevious = previous;
        }

        public int ReadyChangedCount { get; private set; }
        [ObservableHandler(nameof(PrefabTestModel.isReady))]
        private void OnReadyChanged(bool ready) {
            ReadyChangedCount++;
        }
    }

    public abstract class BaseVirtualHandlerPresenter<M> : BaseObservablePresenter<M>
        where M : BaseObservableDataModel
    {
        public int BaseInvocationCount { get; protected set; }

        [ObservableHandler("isOpen")]
        protected virtual void OnIsOpenChanged(bool open) {
            BaseInvocationCount++;
        }
    }

    public class VirtualHandlerModel : BaseObservableDataModel {
        public ObservableVariable<bool> isOpen = new ObservableVariable<bool>();
    }

    public class VirtualHandlerLogic : BaseLogic<VirtualHandlerModel, DerivedVirtualHandlerPresenter> {
        protected override bool RegisterAsSharedSingleton => false;
    }

    public class DerivedVirtualHandlerPresenter : BaseVirtualHandlerPresenter<VirtualHandlerModel> {
        public int DerivedInvocationCount { get; private set; }

        // Deliberately does NOT repeat [ObservableHandler("isOpen")] attribute to test virtual method deduplication
        protected override void OnIsOpenChanged(bool open) {
            base.OnIsOpenChanged(open);
            DerivedInvocationCount++;
        }
    }

    public class ReentrantInitModel : BaseObservableDataModel {
        public ObservableVariable<int> score = new ObservableVariable<int>();
        public ObservableVariable<bool> isReady = new ObservableVariable<bool>();
    }

    public class ReentrantInitLogic : BaseLogic<ReentrantInitModel, ReentrantInitPresenter> {
        protected override bool RegisterAsSharedSingleton => false;
    }

    public class ReentrantInitPresenter : ObservablePresenterWithLogic<ReentrantInitModel, ReentrantInitLogic> {
        [AutoBind(nameof(ReentrantInitModel.isReady))]
        public Toggle readyToggle;

        public int ScoreHandlerCountInsideInit { get; private set; } = -1;
        public int ScoreChangedCount { get; private set; }

        protected override void InitializePresenter() {
            // Reentrant path: logic.GetModel() triggers presenter.EnsureInitialized() while initializing.
            var model = GetLogic().GetModel();
            ScoreHandlerCountInsideInit = model.score.OnValueChanged.HandlerCount;

            // The UI control is created AFTER the reentrant call; its two-way listener must still be attached.
            var toggleObject = new GameObject("ReentrantToggle", typeof(RectTransform), typeof(Toggle));
            toggleObject.transform.SetParent(transform, false);
            readyToggle = toggleObject.GetComponent<Toggle>();
        }

        [ObservableHandler(nameof(ReentrantInitModel.score))]
        private void OnScoreChanged(int current) {
            ScoreChangedCount++;
        }
    }

    #endregion

    [TestFixture]
    public class PrefabAndPoolingLifecycleTests {
        private GameObject _fixtureRoot;

        [SetUp]
        public void SetUp() {
            _fixtureRoot = new GameObject("PrefabAndPoolingFixtureRoot");
        }

        [TearDown]
        public void TearDown() {
            if (_fixtureRoot != null) {
                UnityEngine.Object.DestroyImmediate(_fixtureRoot);
            }
        }

        #region Helper Methods

        private GameObject CreatePrefabTemplate(bool activeByDefault) {
            var template = new GameObject("PrefabTemplate");
            template.transform.SetParent(_fixtureRoot.transform, false);
            template.SetActive(false);

            var canvas = new GameObject("Canvas", typeof(RectTransform));
            canvas.transform.SetParent(template.transform, false);

            var textObj = new GameObject("NameDisplay", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(canvas.transform, false);
            var textComp = textObj.GetComponent<TextMeshProUGUI>();

            var toggleObj = new GameObject("ReadyToggle", typeof(RectTransform), typeof(Toggle));
            toggleObj.transform.SetParent(canvas.transform, false);
            var toggleComp = toggleObj.GetComponent<Toggle>();

            var sliderObj = new GameObject("ProgressSlider", typeof(RectTransform), typeof(Slider));
            sliderObj.transform.SetParent(canvas.transform, false);
            var sliderComp = sliderObj.GetComponent<Slider>();
            sliderComp.minValue = 0f;
            sliderComp.maxValue = 100f;

            var inputObj = new GameObject("ScoreInput", typeof(RectTransform), typeof(TMP_InputField));
            inputObj.transform.SetParent(canvas.transform, false);
            var inputComp = inputObj.GetComponent<TMP_InputField>();
            var inputTextObj = new GameObject("Text", typeof(TextMeshProUGUI));
            inputTextObj.transform.SetParent(inputObj.transform, false);
            inputComp.textComponent = inputTextObj.GetComponent<TextMeshProUGUI>();

            var model = template.AddComponent<PrefabTestModel>();
            model.EnsureFieldsInitialized();
            model.score.Value = 100;
            model.playerName.Value = "Hero";
            model.isReady.Value = true;
            model.progress.Value = 50f;

            var presenter = template.GetComponent<PrefabTestPresenter>() ?? template.AddComponent<PrefabTestPresenter>();
            var logic = template.GetComponent<NonSingletonPrefabLogic>() ?? template.AddComponent<NonSingletonPrefabLogic>();

            presenter.nameDisplay = textComp;
            presenter.readyToggle = toggleComp;
            presenter.progressSlider = sliderComp;
            presenter.scoreInput = inputComp;

            toggleComp.isOn = true;
            sliderComp.value = 50f;

            if (activeByDefault) {
                template.SetActive(true);
            }

            return template;
        }

        #endregion

        [Test]
        public void ObjectInstantiate_ActivePrefabHierarchy_InitializesAndBindsReactivity() {
            var template = CreatePrefabTemplate(activeByDefault: true);

            var clone = UnityEngine.Object.Instantiate(template, _fixtureRoot.transform);
            try {
                var model = clone.GetComponent<PrefabTestModel>();
                var logic = clone.GetComponent<NonSingletonPrefabLogic>();
                var presenter = clone.GetComponent<PrefabTestPresenter>();

                Assert.IsNotNull(model);
                Assert.IsNotNull(logic);
                Assert.IsNotNull(presenter);

                // Note: In Editor edit-mode tests, MonoBehaviour Awake/Start does not execute synchronously on Instantiate without [ExecuteAlways].
                // Explicitly calling EnsureInitialized() models the explicit defensive initialization contract expected at runtime and edit-mode.
                presenter.EnsureInitialized();

                // Mutate model and verify reactive UI updates and handler dispatch
                model.score.Value = 250;
                Assert.AreEqual("250", presenter.scoreInput.text);
                Assert.AreEqual(250, presenter.LastScoreCurrent);
                Assert.AreEqual(100, presenter.LastScorePrevious);

                model.playerName.Value = "Legend";
                Assert.AreEqual("Legend", presenter.nameDisplay.text);

                // Two-way UI mutation to model
                presenter.readyToggle.isOn = false;
                Assert.IsFalse(model.isReady.Value);
            } finally {
                UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        [Test]
        public void ObjectInstantiate_InactivePrefab_GetModelEarlyInitializes_AndSetActiveDoesNotDoubleBind() {
            var template = CreatePrefabTemplate(activeByDefault: false);

            var clone = UnityEngine.Object.Instantiate(template, _fixtureRoot.transform);
            try {
                Assert.IsFalse(clone.activeSelf);

                var logic = clone.GetComponent<NonSingletonPrefabLogic>();
                var presenter = clone.GetComponent<PrefabTestPresenter>();

                // Configure properties while inactive
                logic.customData = 999;

                // Call GetModel() while inactive: initializes model & presenter early
                var model = logic.GetModel();
                Assert.IsNotNull(model);
                Assert.AreEqual(100, model.score.Value);

                // Mutate model while still inactive
                model.score.Value = 300;
                Assert.AreEqual(1, presenter.ScoreChangedCount);
                Assert.AreEqual(300, presenter.LastScoreCurrent);

                // Re-bind or activation check: calling EnsureInitialized() simulates activation lifecycle and must not double-bind
                presenter.EnsureInitialized();

                int countBefore = presenter.ScoreChangedCount;
                model.score.Value = 400;

                Assert.AreEqual(countBefore + 1, presenter.ScoreChangedCount, "Handler must be called exactly once per change after activation/re-bind");
                Assert.AreEqual(400, presenter.LastScoreCurrent);
                Assert.AreEqual(300, presenter.LastScorePrevious);
            } finally {
                UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        [Test]
        public void PoolingLifecycle_SetActiveCycles_RetainsBindingAndUpdatesCorrectly() {
            var template = CreatePrefabTemplate(activeByDefault: true);
            var clone = UnityEngine.Object.Instantiate(template, _fixtureRoot.transform);

            try {
                var model = clone.GetComponent<PrefabTestModel>();
                var presenter = clone.GetComponent<PrefabTestPresenter>();
                presenter.EnsureInitialized();

                // Simulate pooling checkout 1
                model.score.Value = 10;
                Assert.AreEqual("10", presenter.scoreInput.text);

                // Recycle back to pool: SetActive(false)
                clone.SetActive(false);

                // Model mutation while in pool
                model.score.Value = 50;

                // Checkout again: SetActive(true) and EnsureInitialized()
                clone.SetActive(true);
                presenter.EnsureInitialized();

                // Verify bindings are still active and no duplicates exist
                int invocations = 0;
                model.score.OnValueChanged.Add(_ => invocations++, clone);

                model.score.Value = 99;
                Assert.AreEqual("99", presenter.scoreInput.text);
                Assert.AreEqual(1, invocations);

                // Two-way interaction after reuse
                presenter.readyToggle.isOn = false;
                Assert.IsFalse(model.isReady.Value);
            } finally {
                UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        private ReentrantInitPresenter CreateReentrantHost(out ReentrantInitModel model) {
            var host = new GameObject("ReentrantInitHost");
            host.transform.SetParent(_fixtureRoot.transform, false);
            model = host.AddComponent<ReentrantInitModel>();
            var presenter = host.AddComponent<ReentrantInitPresenter>();
            host.AddComponent<ReentrantInitLogic>();
            return presenter;
        }

        [Test]
        public void ReentrantInitialize_UiCreatedAfterGetModel_TwoWayBindingIsAttached() {
            var presenter = CreateReentrantHost(out var model);

            presenter.EnsureInitialized();

            Assert.IsNotNull(presenter.readyToggle);

            presenter.readyToggle.isOn = true;
            Assert.IsTrue(model.isReady.Value, "UI -> Model listener must be attached for a control assigned in InitializePresenter().");

            model.isReady.Value = false;
            Assert.IsFalse(presenter.readyToggle.isOn);

            model.score.Value = 5;
            Assert.AreEqual(1, presenter.ScoreChangedCount, "Handler must fire exactly once per change.");
            model.score.Value = 6;
            Assert.AreEqual(2, presenter.ScoreChangedCount);
        }

        [Test]
        public void ReentrantInitialize_InitializePresenterRunsBeforeHandlerRegistration() {
            var presenter = CreateReentrantHost(out var model);

            presenter.EnsureInitialized();

            Assert.AreEqual(0, presenter.ScoreHandlerCountInsideInit,
                "No managed handler may be registered while InitializePresenter() is still running.");
            Assert.AreEqual(1, model.score.OnValueChanged.HandlerCount);
        }

        [Test]
        public void RemoveListeners_ThenEnsureInitialized_RebindsExactlyOnce() {
            var template = CreatePrefabTemplate(activeByDefault: true);
            var clone = UnityEngine.Object.Instantiate(template, _fixtureRoot.transform);
            try {
                var model = clone.GetComponent<PrefabTestModel>();
                var presenter = clone.GetComponent<PrefabTestPresenter>();
                presenter.EnsureInitialized();

                model.score.Value = 1;
                Assert.AreEqual(1, presenter.ScoreChangedCount);

                Observable.RemoveListeners(model, presenter);

                model.score.Value = 2;
                Assert.AreEqual(1, presenter.ScoreChangedCount, "No handler may fire after RemoveListeners.");

                presenter.EnsureInitialized();

                model.score.Value = 3;
                Assert.AreEqual(2, presenter.ScoreChangedCount, "Handler must fire exactly once after rebind.");
                Assert.AreEqual("3", presenter.scoreInput.text);

                model.playerName.Value = "Rebound";
                Assert.AreEqual("Rebound", presenter.nameDisplay.text);

                model.isReady.Value = false;
                presenter.readyToggle.isOn = true;
                Assert.IsTrue(model.isReady.Value, "Two-way UI -> Model must work after rebind.");
            } finally {
                UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        [Test]
        public void EnsureInitialized_ExplicitSetListeners_DoesNotDoubleBind() {
            var template = CreatePrefabTemplate(activeByDefault: true);
            var clone = UnityEngine.Object.Instantiate(template, _fixtureRoot.transform);
            try {
                var model = clone.GetComponent<PrefabTestModel>();
                var presenter = clone.GetComponent<PrefabTestPresenter>();

                presenter.EnsureInitialized();
                Observable.SetListeners(presenter, model);
                presenter.EnsureInitialized();

                model.score.Value = 7;
                Assert.AreEqual(1, presenter.ScoreChangedCount, "Exactly one invocation per change.");
                model.score.Value = 8;
                Assert.AreEqual(2, presenter.ScoreChangedCount);
            } finally {
                UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        [Test]
        public void VirtualObservableHandler_OverrideWithoutAttribute_InvokedOnlyOnce() {
            var host = new GameObject("VirtualHandlerHost");
            host.transform.SetParent(_fixtureRoot.transform, false);

            var model = host.AddComponent<VirtualHandlerModel>();
            model.EnsureFieldsInitialized();

            var presenter = host.AddComponent<DerivedVirtualHandlerPresenter>();
            Observable.SetListeners(presenter, model);

            model.isOpen.Value = true;

            Assert.AreEqual(1, presenter.DerivedInvocationCount, "Derived override must be invoked exactly once");
            Assert.AreEqual(1, presenter.BaseInvocationCount, "Base method called via base.OnIsOpenChanged must be invoked once");

            model.isOpen.Value = false;

            Assert.AreEqual(2, presenter.DerivedInvocationCount);
            Assert.AreEqual(2, presenter.BaseInvocationCount);
        }

        [Test]
        public void NonSingletonLogic_DoesNotCollideOrRegisterInServiceLocator() {
            var host1 = new GameObject("NonSingleton1");
            host1.transform.SetParent(_fixtureRoot.transform, false);
            var model1 = host1.AddComponent<PrefabTestModel>();
            model1.EnsureFieldsInitialized();
            var logic1 = host1.AddComponent<NonSingletonPrefabLogic>();

            var host2 = new GameObject("NonSingleton2");
            host2.transform.SetParent(_fixtureRoot.transform, false);
            var model2 = host2.AddComponent<PrefabTestModel>();
            model2.EnsureFieldsInitialized();
            var logic2 = host2.AddComponent<NonSingletonPrefabLogic>();

            // ServiceLocator.instance should NOT have registered either instance
            if (ServiceLocator.instance != null) {
                Assert.IsFalse(ServiceLocator.instance.IsRegistered<NonSingletonPrefabLogic>());
            }
        }

        [Test]
        public void TopLevelSamplePrefab_SerializedReferences_PersistAndRebindCorrectly() {
            // Test with established top-level SamplePlayerPresenter
            var host = new GameObject("SamplePrefabHost");
            host.transform.SetParent(_fixtureRoot.transform, false);

            var model = host.AddComponent<SamplePlayerModel>();
            model.InitializeDefaults();

            var presenter = host.AddComponent<SamplePlayerPresenter>();

            var textObj = new GameObject("PlayerNameText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(host.transform, false);
            presenter.playerNameText = textObj.GetComponent<TextMeshProUGUI>();

            var toggleObj = new GameObject("ShieldToggle", typeof(RectTransform), typeof(Toggle));
            toggleObj.transform.SetParent(host.transform, false);
            presenter.shieldToggle = toggleObj.GetComponent<Toggle>();

            // Wire bindings via SetListeners
            Observable.SetListeners(presenter, model);

            model.playerName.Value = "TopLevelHero";
            Assert.AreEqual("TopLevelHero", presenter.playerNameText.text);

            // Two-way UI -> Model
            presenter.shieldToggle.isOn = true;
            Assert.IsTrue(model.isShieldActive.Value);

            // Re-bind: must be idempotent and preserve handlers
            Observable.SetListeners(presenter, model);

            model.playerName.Value = "ReboundHero";
            Assert.AreEqual("ReboundHero", presenter.playerNameText.text);
        }

        [Test]
        public void SceneRegistration_HonorsMultiInstanceLogicOptOut() {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var host = new GameObject("NonSingletonSceneUnit");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
            host.SetActive(false);
            try {
                host.AddComponent<PrefabTestModel>();
                host.AddComponent<PrefabTestPresenter>();
                var logic = host.AddComponent<NonSingletonPrefabLogic>();
                var locator = host.AddComponent<ServiceLocator>();
                locator.InitializeSharedSingletons();
                Assert.IsFalse(locator.IsRegistered(logic),
                    "The scene scan must honor the same opt-out as BaseLogic.Awake.");
            } finally {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void PooledUnits_WithRegisterAsSharedSingletonFalse_ProduceNoDuplicateErrors() {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var created = new List<GameObject>();
            try {
                var locatorHost = new GameObject("LocatorHost");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(locatorHost, scene);
                var locator = locatorHost.AddComponent<ServiceLocator>();

                // 49 board-cell style copies of the same Logic type, all with the default key
                for (int i = 0; i < 49; i++) {
                    var cell = new GameObject("Cell" + i);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cell, scene);
                    cell.SetActive(false);
                    cell.AddComponent<PrefabTestModel>();
                    cell.AddComponent<PrefabTestPresenter>();
                    cell.AddComponent<NonSingletonPrefabLogic>();
                    created.Add(cell);
                }

                // Any error log fails the test: the opt-out units must be skipped silently
                locator.InitializeSharedSingletons();

                Assert.IsFalse(locator.IsRegistered<NonSingletonPrefabLogic>());
            } finally {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public class DuplicateKeyLogic : BaseLogic<PrefabTestModel, PrefabTestPresenter> { }

        [Test]
        public void SceneRegistration_UnnamedDuplicates_LogOneErrorAtInitialize_FirstWins() {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try {
                var locatorHost = new GameObject("LocatorHost");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(locatorHost, scene);
                var locator = locatorHost.AddComponent<ServiceLocator>();

                var first = new GameObject("First");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(first, scene);
                first.SetActive(false);
                first.AddComponent<PrefabTestModel>();
                first.AddComponent<PrefabTestPresenter>();
                var firstLogic = first.AddComponent<DuplicateKeyLogic>();

                var second = new GameObject("Second");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(second, scene);
                second.SetActive(false);
                second.AddComponent<PrefabTestModel>();
                second.AddComponent<PrefabTestPresenter>();
                second.AddComponent<DuplicateKeyLogic>();

                UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"Duplicate registration of .*DuplicateKeyLogic.*already holds this key"));
                locator.InitializeSharedSingletons();

                var previous = ServiceLocator.instance;
                ServiceLocator.instance = locator;
                try {
                    Assert.AreSame(firstLogic, Services.Get<DuplicateKeyLogic>(), "Services.Get returns the first registered instance.");
                } finally {
                    ServiceLocator.instance = previous;
                }
            } finally {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void SavedPrefab_Instantiate_RemapsSerializedUIReferencesToClone() {
            var host = new GameObject("SerializedSamplePrefab");
            host.transform.SetParent(_fixtureRoot.transform, false);
            host.SetActive(false);
            host.AddComponent<SamplePlayerModel>();
            var presenter = host.GetComponent<SamplePlayerPresenter>() ?? host.AddComponent<SamplePlayerPresenter>();
            var logic = host.GetComponent<SamplePlayerLogic>() ?? host.AddComponent<SamplePlayerLogic>();
            var label = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(host.transform, false);
            presenter.playerNameText = label.GetComponent<TextMeshProUGUI>();

            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/MmvcSerializedReferenceTest.prefab");
            GameObject clone = null;
            try {
                var prefab = PrefabUtility.SaveAsPrefabAsset(host, path);
                Assert.IsNotNull(prefab);
                clone = UnityEngine.Object.Instantiate(prefab, _fixtureRoot.transform);
                var clonedPresenter = clone.GetComponent<SamplePlayerPresenter>();
                Assert.IsNotNull(clonedPresenter.playerNameText);
                Assert.IsTrue(clonedPresenter.playerNameText.transform.IsChildOf(clone.transform),
                    "The serialized reference must point to the clone, not the source asset.");
                Assert.AreNotSame(presenter.playerNameText, clonedPresenter.playerNameText);
                clonedPresenter.EnsureInitialized();
                clonedPresenter.GetModel().playerName.Value = "Serialized clone";
                Assert.AreEqual("Serialized clone", clonedPresenter.playerNameText.text);
            } finally {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
