using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
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
        public void Observable_SetListeners_Wires0_1_2_ParameterHandlers() {
            // Expected log warnings/errors in exact occurrence order:
            // 1. Error for invalid signature during handler subscription
            LogAssert.Expect(LogType.Error, new Regex(@"Method 'OnInvalidSignature' has an invalid number of parameters"));
            // 2. Warnings for unhandled observable fields
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'energy' in SamplePlayerModel has no corresponding"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'isShieldActive' in SamplePlayerModel has no corresponding"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'inventory' in SamplePlayerModel has no corresponding"));
            // 3. Warning for missing AutoBind target field
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
            LogAssert.Expect(LogType.Error, new Regex(@"Method 'OnInvalidSignature' has an invalid number of parameters"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'energy'"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'isShieldActive'"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'inventory'"));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[AutoBind\] on 'missingDisplay'"));

            // Call SetListeners first time
            Observable.SetListeners(_subscriber, _model);

            // Second call expected logs
            LogAssert.Expect(LogType.Error, new Regex(@"Method 'OnInvalidSignature' has an invalid number of parameters"));
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
            LogAssert.Expect(LogType.Error, new Regex(@"Method 'OnInvalidSignature' has an invalid number of parameters"));
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
            LogAssert.Expect(LogType.Error, new Regex(@"Method 'OnInvalidSignature' has an invalid number of parameters"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'energy'"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'isShieldActive'"));
            LogAssert.Expect(LogType.Warning, new Regex("ObservableVariable 'inventory'"));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[AutoBind\] on 'missingDisplay'"));

            Observable.SetListeners(_subscriber, _model);

            _model.playerName.Value = "Aragorn";
            Assert.AreEqual("Aragorn", _subscriber.nameDisplay.text);
        }
    }
}
