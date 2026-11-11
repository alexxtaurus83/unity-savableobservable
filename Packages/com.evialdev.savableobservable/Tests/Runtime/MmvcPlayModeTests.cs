using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using TMPro;
using SavableObservable;
using SavableObservable.Samples;

namespace SavableObservable.Tests {
    [TestFixture]
    public class MmvcPlayModeTests {
        private GameObject _root;
        private SamplePlayerModel _model;
        private SamplePlayerLogic _logic;
        private SamplePlayerPresenter _presenter;
        private Toggle _toggle;
        private Slider _slider;
        private TextMeshProUGUI _hpText;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _root = new GameObject("PlayModeTestRoot");

            var canvasObj = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasObj.transform.SetParent(_root.transform, false);

            var hpTextObj = new GameObject("HpText", typeof(RectTransform), typeof(TextMeshProUGUI));
            hpTextObj.transform.SetParent(canvasObj.transform, false);
            _hpText = hpTextObj.GetComponent<TextMeshProUGUI>();

            var toggleObj = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle));
            toggleObj.transform.SetParent(canvasObj.transform, false);
            _toggle = toggleObj.GetComponent<Toggle>();

            var sliderObj = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderObj.transform.SetParent(canvasObj.transform, false);
            _slider = sliderObj.GetComponent<Slider>();
            _slider.minValue = 0f;
            _slider.maxValue = 1f;

            var unitObj = new GameObject("PlayerUnit");
            unitObj.SetActive(false);
            unitObj.transform.SetParent(_root.transform, false);

            _model = unitObj.AddComponent<SamplePlayerModel>();
            _model.InitializeDefaults();
            _logic = unitObj.AddComponent<SamplePlayerLogic>();
            _presenter = unitObj.GetComponent<SamplePlayerPresenter>();
            if (_presenter == null) {
                _presenter = unitObj.AddComponent<SamplePlayerPresenter>();
            }

            _presenter.healthText = _hpText;
            _presenter.shieldToggle = _toggle;
            _presenter.energySlider = _slider;

            _presenter.EnsureInitialized();
            unitObj.SetActive(true);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            if (_root != null) {
                Object.Destroy(_root);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayMode_LogicTakeDamage_UpdatesModelAndUIInPlayMode() {
            // Default health is 100 (InitializeDefaults): an equal assignment is silent, so the UI is not touched.
            var textBefore = _hpText.text;
            _model.health.Value = 100;
            yield return null;
            Assert.AreEqual(textBefore, _hpText.text, "Equal assignment must not notify or update the UI.");

            _model.health.Value = 90;
            yield return null;
            Assert.AreEqual("90", _hpText.text);

            _logic.TakeDamage(20);
            yield return null;

            Assert.AreEqual(70, _model.health.Value);
            Assert.AreEqual("70", _hpText.text);
        }

        [UnityTest]
        public IEnumerator PlayMode_TwoWayBinding_ToggleStatePropagatesToModel() {
            Assert.IsFalse(_model.isShieldActive.Value);

            _toggle.isOn = true;
            yield return null;

            Assert.IsTrue(_model.isShieldActive.Value);

            _toggle.isOn = false;
            yield return null;

            Assert.IsFalse(_model.isShieldActive.Value);
        }

        [UnityTest]
        public IEnumerator PlayMode_TwoWayBinding_SliderValuePropagatesToModel() {
            _slider.value = 0.85f;
            yield return null;

            Assert.AreEqual(0.85f, _model.energy.Value, 0.001f);

            _slider.value = 0.25f;
            yield return null;

            Assert.AreEqual(0.25f, _model.energy.Value, 0.001f);
        }

        public class DefaultOrderPlayModelModel : BaseObservableDataModel {
            public ObservableVariable<int> value = new ObservableVariable<int>();
        }

        public class DefaultOrderPlayPresenter : BaseObservablePresenter<DefaultOrderPlayModelModel> {
            public int Changes { get; private set; }
            public bool FieldsInitializedInAwake { get; private set; }

            protected override void Awake() {
                // Presenter Awake (-150) runs BEFORE the model's Awake (default order 0): fields must already be usable.
                FieldsInitializedInAwake = GetModel() != null && GetModel().value != null;
                base.Awake();
            }

            [ObservableHandler(nameof(DefaultOrderPlayModelModel.value))]
            private void OnValue(int _) { Changes++; }
        }

        [UnityTest]
        public IEnumerator PlayMode_ModelAtDefaultOrder_PresenterOnSameGameObject_BindsAndInitializesFields() {
            var unit = new GameObject("DefaultOrderUnit");
            unit.SetActive(false);
            unit.transform.SetParent(_root.transform, false);
            var model = unit.AddComponent<DefaultOrderPlayModelModel>();
            var presenter = unit.AddComponent<DefaultOrderPlayPresenter>();

            unit.SetActive(true);
            yield return null;

            Assert.IsTrue(presenter.FieldsInitializedInAwake, "GetModel() initializes the fields lazily even though the model's Awake has not run yet.");

            model.value.Value = 4;
            Assert.AreEqual(1, presenter.Changes, "The presenter is bound exactly once at the default model order.");
        }

        public class CustomDynamicPlayModeLogic : BaseLogic<SamplePlayerModel, SamplePlayerPresenter> { }

        [UnityTest]
        public IEnumerator PlayMode_DynamicBaseLogic_AutoRegistersOnAwake_And_UnregistersOnDestroy() {
            var locatorObj = new GameObject("PlayModeLocatorHost");
            locatorObj.transform.SetParent(_root.transform, false);
            var locator = locatorObj.AddComponent<ServiceLocator>();
            Services.SetTestLocator(locator);

            var dynamicUnit = new GameObject("DynamicPlayModeUnit");
            dynamicUnit.transform.SetParent(_root.transform, false);
            dynamicUnit.AddComponent<SamplePlayerModel>();
            dynamicUnit.AddComponent<SamplePlayerPresenter>();

            // In PlayMode, AddComponent executes Awake immediately
            var dynamicLogic = dynamicUnit.AddComponent<CustomDynamicPlayModeLogic>();
            yield return null;

            Assert.IsTrue(locator.IsRegistered<CustomDynamicPlayModeLogic>(), "Dynamic BaseLogic must auto-register via Awake in PlayMode");
            Assert.AreSame(dynamicLogic, Services.Get<CustomDynamicPlayModeLogic>());
            Assert.IsNotNull(Services.GetModel<SamplePlayerModel>("CustomDynamicPlayModeLogic"));

            // In PlayMode, Destroy executes OnDestroy at end of frame
            Object.Destroy(dynamicUnit);
            yield return null;

            Assert.IsFalse(locator.IsRegistered<CustomDynamicPlayModeLogic>(), "Dynamic BaseLogic must auto-unregister via OnDestroy in PlayMode");
            Assert.IsNull(Services.Get<CustomDynamicPlayModeLogic>());

            Services.SetTestLocator(null);
        }

        [UnityTest]
        public IEnumerator PlayMode_ServiceLocator_SameFrameReplacement_PreservesNewInstanceAfterOldDestroy() {
            var oldLocatorObj = new GameObject("OldPlayModeLocatorHost");
            oldLocatorObj.transform.SetParent(_root.transform, false);
            var oldLocator = oldLocatorObj.AddComponent<ServiceLocator>();
            Assert.AreSame(oldLocator, ServiceLocator.instance);

            // Destroy old locator asynchronously (deferred to end of frame)
            Object.Destroy(oldLocatorObj);

            // In the same frame before end-of-frame deferred OnDestroy, create a new ServiceLocator
            var newLocatorObj = new GameObject("NewPlayModeLocatorHost");
            newLocatorObj.transform.SetParent(_root.transform, false);
            var newLocator = newLocatorObj.AddComponent<ServiceLocator>();

            Assert.AreSame(newLocator, ServiceLocator.instance, "New ServiceLocator Awake must take over instance immediately when previous locator is destroyed");

            // Yield to let end of frame run Object.Destroy and OnDestroy on oldLocatorObj
            yield return null;

            Assert.AreSame(newLocator, ServiceLocator.instance, "Deferred OnDestroy of old ServiceLocator must not clear the new live instance");

            Object.Destroy(newLocatorObj);
            yield return null;
            Assert.IsNull(ServiceLocator.instance);
        }
    }
}
