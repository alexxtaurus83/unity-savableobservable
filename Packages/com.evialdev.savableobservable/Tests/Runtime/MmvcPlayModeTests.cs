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
            _model.health.Value = 100;
            yield return null;

            Assert.AreEqual("100", _hpText.text);

            _logic.TakeDamage(30);
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
    }
}
