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
            LogAssert.Expect(LogType.Error, new Regex(@"Method 'OnInvalidSignature' has an invalid number of parameters"));

            Observable.SetListeners(invalidSub, _model);
        }

        [Test]
        public void Observable_SetListeners_Wires0_1_2_ParameterHandlers() {
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
            LogAssert.Expect(LogType.Warning, new Regex(@"\[AutoBind\] on 'missingDisplay'"));

            // Call SetListeners first time
            Observable.SetListeners(_subscriber, _model);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[AutoBind\] on 'missingDisplay'"));

            // Call SetListeners second time: existing subscriptions should be removed before re-adding
            Observable.SetListeners(_subscriber, _model);

            _model.health.Value = 80;

            Assert.AreEqual(1, _subscriber.ZeroParamCount, "Handlers should be invoked exactly once, not duplicated");
        }

        [Test]
        public void Observable_RemoveListeners_DetachesAllTrackedHandlers() {
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

        private static void ExpectMissingFieldWarning() {
            LogAssert.Expect(LogType.Warning, new Regex(@"\[AutoBind\] on 'missingDisplay' could not find ObservableVariable 'missingField'"));
        }

        private class OtherSubscriber : MonoBehaviour {
            public int HealthCalls;

            [ObservableHandler("health")]
            public void OnHealth() { HealthCalls++; }
        }

        [Test]
        public void RemoveListeners_RemovesOnlyThatSubscribersRecords() {
            var other = _host.AddComponent<OtherSubscriber>();
            ExpectMissingFieldWarning();
            {
                Observable.SetListeners(_subscriber, _model);
                Observable.SetListeners(other, _model);

                _model.health.Value = 50;
                Assert.AreEqual(1, _subscriber.ZeroParamCount);
                Assert.AreEqual(1, other.HealthCalls);

                Observable.RemoveListeners(_model, _subscriber);

                _model.health.Value = 40;
                Assert.AreEqual(1, _subscriber.ZeroParamCount, "Removed subscriber must not be notified.");
                Assert.AreEqual(2, other.HealthCalls, "Other subscribers must be unaffected.");
            }
        }

        [Test]
        public void SetListeners_ReplacesManagedBindings_ButKeepsManualSubscriptions() {
            int manual = 0;
            ExpectMissingFieldWarning();
            {
                _model.EnsureFieldsInitialized(); // links the field to its model, as Awake does in play mode
                _model.health.OnValueChanged.Add(_ => manual++, _subscriber);

                Observable.SetListeners(_subscriber, _model);
                Observable.SetListeners(_subscriber, _model);

                _model.health.Value = 33;

                Assert.AreEqual(1, manual, "Manual subscription survives rebinds and fires once.");
                Assert.AreEqual(1, _subscriber.ZeroParamCount, "Managed handler is replaced, not duplicated.");
            }
        }

        [Test]
        public void RemoveListeners_RemovesManualSubscriptionsToo() {
            int manual = 0;
            ExpectMissingFieldWarning();
            {
                _model.EnsureFieldsInitialized(); // links the field to its model, as Awake does in play mode
                _model.health.OnValueChanged.Add(_ => manual++, _subscriber);
                Observable.SetListeners(_subscriber, _model);

                Observable.RemoveListeners(_model, _subscriber);
                _model.health.Value = 12;

                Assert.AreEqual(0, manual);
            }
        }

        [Test]
        public void TwoWayUiListener_IsRemoved_WhenModelIsDestroyed() {
            var toggleObj = new GameObject("Toggle", typeof(Toggle));
            toggleObj.transform.SetParent(_host.transform, false);
            var toggle = toggleObj.GetComponent<Toggle>();
            var presenter = _host.AddComponent<SamplePlayerPresenter>();
            presenter.shieldToggle = toggle;

            Observable.SetListeners(presenter, _model);

            toggle.isOn = true;
            Assert.IsTrue(_model.isShieldActive.Value, "Two-way UI listener works before destruction.");

            var shield = _model.isShieldActive;
            Observable.CleanupSubscriptions(_model);

            toggle.isOn = false;
            Assert.IsTrue(shield.Value, "UI listener token must be removed on model cleanup/destroy.");
        }

        [Test]
        public void UnboundObservableField_NoWarningByDefault_WarningWhenStrictBinding() {
            Assert.IsFalse(ObservableSettings.StrictBinding, "StrictBinding defaults to false.");

            // Default: only the genuine [AutoBind] mismatch is reported.
            LogAssert.Expect(LogType.Warning, new Regex(@"\[AutoBind\] on 'missingDisplay' could not find ObservableVariable 'missingField'"));
            Observable.SetListeners(_subscriber, _model);
            LogAssert.NoUnexpectedReceived();

            ObservableSettings.StrictBinding = true;
            try {
                LogAssert.Expect(LogType.Warning, new Regex(@"\[AutoBind\] on 'missingDisplay' could not find ObservableVariable 'missingField'"));
                Observable.SetListeners(_subscriber, _model);
            } finally {
                ObservableSettings.StrictBinding = false;
            }
        }

        private class ListSubscriber : MonoBehaviour {
            public int ZeroCount, OneListCount, OneObservableCount, TwoCount;

            [ObservableHandler("items")] public void Zero() { ZeroCount++; }
            [ObservableHandler("items")] public void OneList(System.Collections.Generic.List<string> v) { OneListCount++; }
            [ObservableHandler("items")] public void OneObservable(ObservableList<string> v) { OneObservableCount++; }
            [ObservableHandler("items")] public void Two(System.Collections.Generic.List<string> cur, System.Collections.Generic.IReadOnlyList<string> prev) { TwoCount++; }
        }

        private class ListHandlerModel : BaseObservableDataModel {
            public ObservableList<string> items = new ObservableList<string>();
        }

        [Test]
        public void ObservableList_0_1_2_ParameterHandlers_WireViaSetListeners() {
            var go = new GameObject("ListBinding");
            try {
                var model = go.AddComponent<ListHandlerModel>();
                model.EnsureFieldsInitialized();
                var sub = go.AddComponent<ListSubscriber>();

                Observable.SetListeners(sub, model);
                model.items.Add("a");

                Assert.AreEqual(1, sub.ZeroCount);
                Assert.AreEqual(1, sub.OneListCount);
                Assert.AreEqual(1, sub.OneObservableCount);
                Assert.AreEqual(1, sub.TwoCount);

                Observable.RemoveListeners(model, sub);
                model.items.Add("b");
                Assert.AreEqual(1, sub.ZeroCount, "List handlers are removed by RemoveListeners.");
            } finally {
                UnityEngine.Object.DestroyImmediate(go);
            }
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
