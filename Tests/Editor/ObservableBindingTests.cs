using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using TMPro;
using SavableObservable;
using SavableObservable.Samples;

namespace SavableObservable.Tests {
    [TestFixture]
    public class ObservableBindingTests {
        private GameObject _host;
        private SamplePlayerModel _model;
        private DummySubscriber _subscriber;

        private class DummySubscriber : MonoBehaviour {
            [AutoBind("playerName")]
            public TextMeshProUGUI nameDisplay;

            [AutoBind("missingField")]
            public TextMeshProUGUI missingDisplay;

            public int ZeroParamCount;
            public int OneParamValue;
            public int TwoParamCurrent;
            public int TwoParamPrev;

            [ObservableHandler("health")]
            public void OnHealthChanged0() {
                ZeroParamCount++;
            }

            [ObservableHandler("health")]
            public void OnHealthChanged1(int current) {
                OneParamValue = current;
            }

            [ObservableHandler("health")]
            public void OnHealthChanged2(int current, int prev) {
                TwoParamCurrent = current;
                TwoParamPrev = prev;
            }
        }

        private class InvalidSubscriber : MonoBehaviour {
            [ObservableHandler("health")]
            public void OnInvalidSignature(int a, int b, int c) {
                // Invalid: 3 parameters
            }
        }

        [SetUp]
        public void SetUp() {
            _host = new GameObject("TestHost");
            _model = _host.AddComponent<SamplePlayerModel>();
            _model.InitializeDefaults();
            _subscriber = _host.AddComponent<DummySubscriber>();

            var textObj = new GameObject("TextObj", typeof(TextMeshProUGUI));
            textObj.transform.SetParent(_host.transform, false);
            _subscriber.nameDisplay = textObj.GetComponent<TextMeshProUGUI>();

            var missingObj = new GameObject("MissingObj", typeof(TextMeshProUGUI));
            missingObj.transform.SetParent(_host.transform, false);
            _subscriber.missingDisplay = missingObj.GetComponent<TextMeshProUGUI>();
        }

        [TearDown]
        public void TearDown() {
            if (_host != null) {
                UnityEngine.Object.DestroyImmediate(_host);
            }
        }

        [Test]
        public void Observable_SetListeners_InvalidSignature_LogsError() {
            var invalidSub = _host.AddComponent<InvalidSubscriber>();
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'playerName'"));
            LogAssert.Expect(LogType.Error, new Regex(@"Method 'OnInvalidSignature' has an invalid number of parameters"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'energy'"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'isShieldActive'"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'inventory'"));

            Observable.SetListeners(invalidSub, _model);
        }

        [Test]
        public void Observable_SetListeners_Wires0_1_2_ParameterHandlers() {
            // Expected log warnings
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'energy' in SamplePlayerModel has no corresponding"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'isShieldActive' in SamplePlayerModel has no corresponding"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'inventory' in SamplePlayerModel has no corresponding"));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[AutoBind\] on 'missingDisplay' could not find ObservableVariable 'missingField'"));

            Observable.SetListeners(_subscriber, _model);

            _model.health.Value = 45;

            Assert.AreEqual(1, _subscriber.ZeroParamCount);
            Assert.AreEqual(45, _subscriber.OneParamValue);
            Assert.AreEqual(45, _subscriber.TwoParamCurrent);
            Assert.AreEqual(100, _subscriber.TwoParamPrev);
        }

        [Test]
        public void Observable_SetListeners_IsIdempotent_DoesNotDuplicateHandlers() {
            // First call expected logs
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'energy'"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'isShieldActive'"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'inventory'"));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[AutoBind\] on 'missingDisplay'"));

            // Call SetListeners first time
            Observable.SetListeners(_subscriber, _model);

            // Second call expected logs
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'energy'"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'isShieldActive'"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'inventory'"));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[AutoBind\] on 'missingDisplay'"));

            // Call SetListeners second time: existing subscriptions should be removed before re-adding
            Observable.SetListeners(_subscriber, _model);

            _model.health.Value = 80;

            Assert.AreEqual(1, _subscriber.ZeroParamCount, "Handlers should be invoked exactly once, not duplicated");
        }

        [Test]
        public void Observable_RemoveListeners_DetachesAllTrackedHandlers() {
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'energy'"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'isShieldActive'"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'inventory'"));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[AutoBind\] on 'missingDisplay'"));

            Observable.SetListeners(_subscriber, _model);

            _model.health.Value = 70;
            Assert.AreEqual(1, _subscriber.ZeroParamCount);

            Observable.RemoveListeners(_model, _subscriber);

            _model.health.Value = 30;
            Assert.AreEqual(1, _subscriber.ZeroParamCount, "Handler count should not increase after RemoveListeners");
        }

        [Test]
        public void AutoBind_PropagatesModelValueToUI() {
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'energy'"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'isShieldActive'"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'inventory'"));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[AutoBind\] on 'missingDisplay'"));

            Observable.SetListeners(_subscriber, _model);

            _model.playerName.Value = "Aragorn";
            Assert.AreEqual("Aragorn", _subscriber.nameDisplay.text);
        }

        [Test]
        public void AutoBind_TwoWayBinding_CleansUpOnRemoveListeners() {
            var toggleObj = new GameObject("Toggle", typeof(Toggle));
            toggleObj.transform.SetParent(_host.transform, false);
            var toggle = toggleObj.GetComponent<Toggle>();

            var presenter = _host.AddComponent<SamplePlayerPresenter>();
            presenter.shieldToggle = toggle;

            Observable.SetListeners(presenter, _model);

            // Toggle change should update model (UI -> Model two-way)
            toggle.isOn = true;
            Assert.IsTrue(_model.isShieldActive.Value);

            // Remove listeners
            Observable.RemoveListeners(_model, presenter);

            // Subsequent toggle change should no longer mutate model
            toggle.isOn = false;
            Assert.IsTrue(_model.isShieldActive.Value, "Model should retain value after RemoveListeners detaches two-way UI listener");
        }

        public abstract class BaseModalPresenter<M, L> : BaseObservablePresenter<M>
            where M : BaseObservableDataModel
            where L : BaseLogic
        {
            [AutoBind("title")]
            private TextMeshProUGUI _titleText;

            public int BasePrivateHandlerCount { get; private set; }

            [ObservableHandler("isOpen")]
            private void OnIsOpenChanged(bool open) {
                BasePrivateHandlerCount++;
            }

            public TextMeshProUGUI GetTitleText() => _titleText;
            public void SetTitleText(TextMeshProUGUI text) => _titleText = text;
        }

        public class ConcreteModalModel : BaseObservableDataModel {
            public ObservableVariable<bool> isOpen = new ObservableVariable<bool>();
            public ObservableVariable<string> title = new ObservableVariable<string>();
        }

        public class ConcreteModalLogic : BaseLogic<ConcreteModalModel, ConcreteModalPresenter> { }

        public class ConcreteModalPresenter : BaseModalPresenter<ConcreteModalModel, ConcreteModalLogic> {
            public int DerivedHandlerCount { get; private set; }

            [ObservableHandler("isOpen")]
            private void OnDerivedIsOpen(bool open) {
                DerivedHandlerCount++;
            }
        }

        [Test]
        public void Observable_SetListeners_DiscoversPrivateBaseClassHandlersAndAutoBindFields() {
            var modalHost = new GameObject("ModalHost");
            try {
                var model = modalHost.AddComponent<ConcreteModalModel>();
                model.EnsureFieldsInitialized();

                var presenter = modalHost.AddComponent<ConcreteModalPresenter>();
                var textObj = new GameObject("TitleText", typeof(TextMeshProUGUI));
                textObj.transform.SetParent(modalHost.transform, false);
                var textComp = textObj.GetComponent<TextMeshProUGUI>();
                presenter.SetTitleText(textComp);

                Observable.SetListeners(presenter, model);

                // AutoBind on private base field should populate the UI text when model changes
                model.title.Value = "Initial Title";
                Assert.AreEqual("Initial Title", textComp.text);

                // Changing isOpen should invoke both private base handler and derived handler
                model.isOpen.Value = true;
                Assert.AreEqual(1, presenter.BasePrivateHandlerCount, "Private [ObservableHandler] on generic base presenter must be invoked");
                Assert.AreEqual(1, presenter.DerivedHandlerCount, "Handler on derived presenter must be invoked");

                // AutoBind UI updates when title changes again
                model.title.Value = "Updated Title";
                Assert.AreEqual("Updated Title", textComp.text);
            } finally {
                UnityEngine.Object.DestroyImmediate(modalHost);
            }
        }
    }
}
