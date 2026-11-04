using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SavableObservable;

namespace SavableObservable.Tests {
    [TestFixture]
    public class UIAdapterTests {
        private GameObject _host;

        [SetUp]
        public void SetUp() {
            _host = new GameObject("UIAdapterTestHost");
        }

        [TearDown]
        public void TearDown() {
            if (_host != null) {
                UnityEngine.Object.DestroyImmediate(_host);
            }
        }

        [Test]
        public void UIAdapterRegistry_ResolvesAllBuiltInAdapters() {
            Assert.IsNotNull(UIAdapterRegistry.GetAdapter(typeof(TextMeshProUGUI)));
            Assert.IsNotNull(UIAdapterRegistry.GetAdapter(typeof(TMP_Text)));
            Assert.IsNotNull(UIAdapterRegistry.GetAdapter(typeof(Text)));
            Assert.IsNotNull(UIAdapterRegistry.GetAdapter(typeof(Toggle)));
            Assert.IsNotNull(UIAdapterRegistry.GetAdapter(typeof(Slider)));
            Assert.IsNotNull(UIAdapterRegistry.GetAdapter(typeof(TMP_InputField)));
            Assert.IsNotNull(UIAdapterRegistry.GetAdapter(typeof(InputField)));
            Assert.IsNotNull(UIAdapterRegistry.GetAdapter(typeof(Image)));
        }

        [Test]
        public void TextMeshProAdapter_SetsTextFromAnyObject() {
            var tmp = _host.AddComponent<TextMeshProUGUI>();
            var adapter = UIAdapterRegistry.GetAdapter(typeof(TextMeshProUGUI));

            adapter.SetValue(tmp, 12345, typeof(int));
            Assert.AreEqual("12345", tmp.text);

            adapter.SetValue(tmp, null, typeof(object));
            Assert.AreEqual(string.Empty, tmp.text);
        }

        [Test]
        public void UnityTextAdapter_SetsText() {
            var text = _host.AddComponent<Text>();
            var adapter = UIAdapterRegistry.GetAdapter(typeof(Text));

            adapter.SetValue(text, "Hello Text", typeof(string));
            Assert.AreEqual("Hello Text", text.text);
        }

        [Test]
        public void ToggleAdapter_TwoWayBinding() {
            var toggle = _host.AddComponent<Toggle>();
            var adapter = UIAdapterRegistry.GetAdapter(typeof(Toggle)) as IUIListenerAdapter;
            Assert.IsNotNull(adapter);

            adapter.SetValue(toggle, true, typeof(bool));
            Assert.IsTrue(toggle.isOn);

            bool receivedVal = false;
            object token = adapter.AddListener(toggle, v => receivedVal = (bool)v, typeof(bool));
            Assert.IsNotNull(token);

            toggle.isOn = false;
            Assert.IsFalse(receivedVal);

            toggle.isOn = true;
            Assert.IsTrue(receivedVal);

            adapter.RemoveListener(toggle, token);
            toggle.isOn = false;
            Assert.IsTrue(receivedVal, "Listener should be removed");
        }

        [Test]
        public void SliderAdapter_TwoWayBinding() {
            var slider = _host.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 100f;
            var adapter = UIAdapterRegistry.GetAdapter(typeof(Slider)) as IUIListenerAdapter;
            Assert.IsNotNull(adapter);

            adapter.SetValue(slider, 75f, typeof(float));
            Assert.AreEqual(75f, slider.value);

            float receivedVal = 0f;
            object token = adapter.AddListener(slider, v => receivedVal = (float)v, typeof(float));

            slider.value = 25f;
            Assert.AreEqual(25f, receivedVal);

            adapter.RemoveListener(slider, token);
        }

        [Test]
        public void TMPInputFieldAdapter_TwoWayBinding() {
            var input = _host.AddComponent<TMP_InputField>();
            var textObj = new GameObject("Text", typeof(TextMeshProUGUI));
            textObj.transform.SetParent(_host.transform, false);
            input.textComponent = textObj.GetComponent<TextMeshProUGUI>();

            var adapter = UIAdapterRegistry.GetAdapter(typeof(TMP_InputField)) as IUIListenerAdapter;
            Assert.IsNotNull(adapter);

            adapter.SetValue(input, "Initial", typeof(string));
            Assert.AreEqual("Initial", input.text);

            string receivedVal = null;
            object token = adapter.AddListener(input, v => receivedVal = (string)v, typeof(string));

            input.text = "Updated";
            Assert.AreEqual("Updated", receivedVal);

            adapter.RemoveListener(input, token);
        }

        [Test]
        public void ImageAdapter_SetsSpriteAndHandlesNull() {
            var img = _host.AddComponent<Image>();
            var adapter = UIAdapterRegistry.GetAdapter(typeof(Image));
            Assert.IsNotNull(adapter);

            var tex = new Texture2D(2, 2);
            var testSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.zero);

            try {
                adapter.SetValue(img, testSprite, typeof(Sprite));
                Assert.AreEqual(testSprite, img.sprite);

                adapter.SetValue(img, null, typeof(Sprite));
                Assert.IsNull(img.sprite);
            } finally {
                UnityEngine.Object.DestroyImmediate(testSprite);
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void TwoWayBinding_DoesNotCauseInfiniteRecursionOrReentrancyLoop() {
            var toggle = _host.AddComponent<Toggle>();
            var modelVar = new ObservableVariable<bool> { Value = false };
            var adapter = UIAdapterRegistry.GetAdapter(typeof(Toggle)) as IUIListenerAdapter;
            Assert.IsNotNull(adapter);

            int uiListenerInvocations = 0;
            int modelListenerInvocations = 0;

            // UI -> Model listener
            object token = adapter.AddListener(toggle, val => {
                uiListenerInvocations++;
                if (modelVar.Value != (bool)val) {
                    modelVar.Value = (bool)val;
                }
            }, typeof(bool));

            // Model -> UI listener
            modelVar.OnValueChanged.Add(v => {
                modelListenerInvocations++;
                adapter.SetValue(toggle, v.Value, typeof(bool));
            }, null);

            // Trigger change from UI side
            toggle.isOn = true;

            Assert.AreEqual(1, uiListenerInvocations, "UI listener should only fire once per user change");
            Assert.AreEqual(1, modelListenerInvocations, "Model listener should only fire once");
            Assert.IsTrue(modelVar.Value);

            adapter.RemoveListener(toggle, token);
        }

        private class DynamicTestComponent : MonoBehaviour { }

        private class DynamicTestAdapter : IUIAdapter {
            public int Priority => 200;
            public bool CanHandle(Type uiComponentType) => uiComponentType == typeof(DynamicTestComponent);
            public void SetValue(object uiComponent, object value, Type valueType) { }
        }

        [Test]
        public void RegisterAdapter_AfterInitialization_RebuildsSnapshotAndResolvesAdapter() {
            // Ensure built-in adapters are initialized
            Assert.IsNotNull(UIAdapterRegistry.GetAdapter(typeof(TextMeshProUGUI)));

            var dynamicAdapter = new DynamicTestAdapter();
            try {
                UIAdapterRegistry.RegisterAdapter(dynamicAdapter);

                var resolved = UIAdapterRegistry.GetAdapter(typeof(DynamicTestComponent));
                Assert.AreSame(dynamicAdapter, resolved, "Dynamically registered adapter should resolve immediately after initialization");
            } finally {
                UIAdapterRegistry.UnregisterAdapter(dynamicAdapter);
                UIAdapterRegistry.ClearCustomAdapters();
                var resetRegistry = typeof(UIAdapterRegistry).GetMethod("ResetRegistry", BindingFlags.NonPublic | BindingFlags.Static);
                resetRegistry?.Invoke(null, null);
            }
        }

        [Test]
        public void TwoWayAdapters_ModelToUI_DoesNotBounceBackNotification() {
            // 1. Toggle
            var toggleObj = new GameObject("ToggleObj");
            toggleObj.transform.SetParent(_host.transform, false);
            var toggle = toggleObj.AddComponent<Toggle>();
            var toggleAdapter = UIAdapterRegistry.GetAdapter(typeof(Toggle)) as IUIListenerAdapter;
            var boolModel = new ObservableVariable<bool> { Value = false };

            int boolModelChangeCount = 0;
            boolModel.OnValueChanged.Add(v => {
                boolModelChangeCount++;
                toggleAdapter.SetValue(toggle, v.Value, typeof(bool));
            }, null);

            var toggleToken = toggleAdapter.AddListener(toggle, v => boolModel.Value = (bool)v, typeof(bool));

            // Set Model -> UI: must update toggle.isOn and invoke boolModel.OnValueChanged EXACTLY ONCE
            boolModel.Value = true;
            Assert.IsTrue(toggle.isOn);
            Assert.AreEqual(1, boolModelChangeCount, "Model update must invoke OnValueChanged exactly once without UI bounce-back");

            // UI -> Model: must update model
            toggle.isOn = false;
            Assert.IsFalse(boolModel.Value, "User toggling UI control must update Model");
            Assert.AreEqual(2, boolModelChangeCount);

            toggleAdapter.RemoveListener(toggle, toggleToken);

            // 2. Slider
            var sliderObj = new GameObject("SliderObj");
            sliderObj.transform.SetParent(_host.transform, false);
            var slider = sliderObj.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 100f;
            var sliderAdapter = UIAdapterRegistry.GetAdapter(typeof(Slider)) as IUIListenerAdapter;
            var floatModel = new ObservableVariable<float> { Value = 10f };

            int floatModelChangeCount = 0;
            floatModel.OnValueChanged.Add(v => {
                floatModelChangeCount++;
                sliderAdapter.SetValue(slider, v.Value, typeof(float));
            }, null);

            var sliderToken = sliderAdapter.AddListener(slider, v => floatModel.Value = (float)v, typeof(float));

            // Set Model -> UI
            floatModel.Value = 50f;
            Assert.AreEqual(50f, slider.value);
            Assert.AreEqual(1, floatModelChangeCount, "Slider Model update must invoke OnValueChanged exactly once without UI bounce-back");

            // UI -> Model
            slider.value = 75f;
            Assert.AreEqual(75f, floatModel.Value, "User sliding control must update Model");
            Assert.AreEqual(2, floatModelChangeCount);

            sliderAdapter.RemoveListener(slider, sliderToken);

            // 3. TMP_InputField
            var inputObj = new GameObject("InputObj");
            inputObj.transform.SetParent(_host.transform, false);
            var input = inputObj.AddComponent<TMP_InputField>();
            var textObj = new GameObject("TmpText", typeof(TextMeshProUGUI));
            textObj.transform.SetParent(inputObj.transform, false);
            input.textComponent = textObj.GetComponent<TextMeshProUGUI>();

            var inputAdapter = UIAdapterRegistry.GetAdapter(typeof(TMP_InputField)) as IUIListenerAdapter;
            var stringModel = new ObservableVariable<string> { Value = "A" };

            int stringModelChangeCount = 0;
            stringModel.OnValueChanged.Add(v => {
                stringModelChangeCount++;
                inputAdapter.SetValue(input, v.Value, typeof(string));
            }, null);

            var inputToken = inputAdapter.AddListener(input, v => stringModel.Value = (string)v, typeof(string));

            // Set Model -> UI
            stringModel.Value = "B";
            Assert.AreEqual("B", input.text);
            Assert.AreEqual(1, stringModelChangeCount, "InputField Model update must invoke OnValueChanged exactly once without UI bounce-back");

            // UI -> Model
            input.text = "C";
            Assert.AreEqual("C", stringModel.Value, "User typing in InputField must update Model");
            Assert.AreEqual(2, stringModelChangeCount);

            inputAdapter.RemoveListener(input, inputToken);
        }

        [Test]
        public void CustomAdapters_Survive_ResetRegistry_And_CanBeCleared() {
            UIAdapterRegistry.ClearCustomAdapters();
            var dynamicAdapter = new DynamicTestAdapter();
            try {
                UIAdapterRegistry.RegisterAdapter(dynamicAdapter);
                Assert.AreSame(dynamicAdapter, UIAdapterRegistry.GetAdapter(typeof(DynamicTestComponent)));

                // Simulate domain reload / Fast Play Mode subsystem registration
                var resetRegistry = typeof(UIAdapterRegistry).GetMethod("ResetRegistry", BindingFlags.NonPublic | BindingFlags.Static);
                resetRegistry?.Invoke(null, null);

                // Custom adapter must still resolve after reset!
                Assert.AreSame(dynamicAdapter, UIAdapterRegistry.GetAdapter(typeof(DynamicTestComponent)), "Custom adapter must survive ResetRegistry");
            } finally {
                UIAdapterRegistry.ClearCustomAdapters();
            }

            Assert.IsNull(UIAdapterRegistry.GetAdapter(typeof(DynamicTestComponent)), "Custom adapter should be cleared after ClearCustomAdapters");
        }
    }
}
