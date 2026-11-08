using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SavableObservable;
using SavableObservable.Tests.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SavableObservable.Tests {
    /// <summary>Model with one field observed through every binding path.</summary>
    [Serializable]
    public class ThreePathModel : BaseObservableDataModel {
        [SerializeField] public ObservableVariable<int> score = new ObservableVariable<int>();
        [SerializeField] public ObservableVariable<bool> flag = new ObservableVariable<bool>();
        [SerializeField] public ObservableVariable<string> label = new ObservableVariable<string>();
        [SerializeField] public ObservableList<string> items = new ObservableList<string>();
    }

    /// <summary>
    /// score is rendered by [AutoBind] controls (text and a two-way slider) AND by an [ObservableHandler] that drives a
    /// custom view state; the test adds the third path, a tracked manual subscription.
    /// </summary>
    [DisallowMultipleComponent]
    public class ThreePathPresenter : BaseObservablePresenter<ThreePathModel> {
        [AutoBind("score")] public TextMeshProUGUI scoreText;
        [AutoBind("score")] public Slider scoreSlider;
        [AutoBind("flag")] public Toggle flagToggle;
        [AutoBind("label")] public TMP_InputField labelInput;

        public TextMeshProUGUI handlerText;
        public Image highlight;
        public Transform itemsRoot;

        public int scoreHandlerCalls;
        public int lastCurrent;
        public int lastPrevious;
        public int itemsHandlerCalls;

        [ObservableHandler("score")]
        private void OnScore(int current, int previous) {
            scoreHandlerCalls++;
            lastCurrent = current;
            lastPrevious = previous;
            if (handlerText != null) handlerText.text = previous + "->" + current;
            if (highlight != null) {
                highlight.enabled = current >= 10;
                highlight.color = current >= 15 ? Color.red : Color.green;
            }
        }

        [ObservableHandler("items")]
        private void OnItems(ObservableList<string> list) {
            itemsHandlerCalls++;
            if (itemsRoot == null) return;
            for (int i = itemsRoot.childCount - 1; i >= 0; i--) Destroy(itemsRoot.GetChild(i).gameObject);
            for (int i = 0; i < list.Count; i++) {
                var go = new GameObject("Item_" + list[i], typeof(RectTransform));
                go.transform.SetParent(itemsRoot, false);
            }
        }
    }

    [TestFixture]
    [Category("LiveUi")]
    public class MmvcThreePathPlayModeTests {
        private GameObject _root;
        private GameObject _unit;
        private ThreePathModel _model;
        private ThreePathPresenter _presenter;
        private int _manualCalls;
        private int _manualLast;
        private readonly List<int> _manualSeen = new List<int>();
        private Action<ObservableVariable<int>> _manualHandler;
        private int _listManualCalls;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _root = new GameObject("ThreePathRoot");
            if (EventSystem.current == null) {
                new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform, false);
            }
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvas.transform.SetParent(_root.transform, false);

            TextMeshProUGUI MakeText(string n) {
                var go = new GameObject(n, typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(canvas.transform, false);
                return go.GetComponent<TextMeshProUGUI>();
            }

            var slider = new GameObject("Slider", typeof(RectTransform), typeof(Slider)).GetComponent<Slider>();
            slider.transform.SetParent(canvas.transform, false);
            slider.minValue = 0; slider.maxValue = 100; slider.wholeNumbers = true;

            var toggle = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle)).GetComponent<Toggle>();
            toggle.transform.SetParent(canvas.transform, false);

            var inputGo = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            inputGo.transform.SetParent(canvas.transform, false);
            var input = inputGo.GetComponent<TMP_InputField>();
            var textArea = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textArea.transform.SetParent(inputGo.transform, false);
            input.textComponent = textArea.GetComponent<TextMeshProUGUI>();

            var highlightGo = new GameObject("Highlight", typeof(RectTransform), typeof(Image));
            highlightGo.transform.SetParent(canvas.transform, false);
            var itemsRoot = new GameObject("Items", typeof(RectTransform));
            itemsRoot.transform.SetParent(canvas.transform, false);

            _unit = new GameObject("ThreePathUnit");
            _unit.SetActive(false);
            _unit.transform.SetParent(_root.transform, false);
            _model = _unit.AddComponent<ThreePathModel>();
            _presenter = _unit.AddComponent<ThreePathPresenter>();
            _presenter.scoreText = MakeText("ScoreText");
            _presenter.scoreSlider = slider;
            _presenter.flagToggle = toggle;
            _presenter.labelInput = input;
            _presenter.handlerText = MakeText("HandlerText");
            _presenter.highlight = highlightGo.GetComponent<Image>();
            _presenter.itemsRoot = itemsRoot.transform;

            _model.EnsureFieldsInitialized();

            // Third path: tracked manual subscriptions registered before the presenter binds.
            _manualCalls = 0; _manualLast = int.MinValue; _manualSeen.Clear(); _listManualCalls = 0;
            _manualHandler = v => { _manualCalls++; _manualLast = v.Value; _manualSeen.Add(v.Value); };
            _model.score.OnValueChanged.Add(_manualHandler, this);
            _model.items.OnChanged.Add(_ => _listManualCalls++, this);

            _presenter.EnsureInitialized();
            _unit.SetActive(true);
            yield return null;
            ResetCounters();
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            if (_model != null) Observable.RemoveListeners(_model, this);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            yield return null;
        }

        private void ResetCounters() {
            _manualCalls = 0; _manualSeen.Clear(); _listManualCalls = 0;
            _presenter.scoreHandlerCalls = 0; _presenter.itemsHandlerCalls = 0;
        }

        [UnityTest]
        public IEnumerator ModelChange_RendersThroughAutoBind_Handler_AndManualSubscription_ExactlyOnce() {
            _model.score.Value = 7;
            yield return null;

            Assert.AreEqual("7", _presenter.scoreText.text, "[AutoBind] text");
            Assert.AreEqual(7f, _presenter.scoreSlider.value, "[AutoBind] slider");
            Assert.AreEqual("0->7", _presenter.handlerText.text, "[ObservableHandler] rendering with previous value");
            Assert.AreEqual(1, _presenter.scoreHandlerCalls, "handler calls");
            Assert.AreEqual(1, _manualCalls, "manual calls");
            Assert.AreEqual(7, _manualLast);
            Assert.AreEqual(7, _presenter.lastCurrent);
            Assert.AreEqual(0, _presenter.lastPrevious);
        }

        [UnityTest]
        public IEnumerator HandlerDrivenViewState_FollowsThresholds() {
            var h = _presenter.highlight;
            _model.score.Value = 5;
            yield return null;
            Assert.IsFalse(h.enabled, "below threshold the highlight is disabled");

            _model.score.Value = 10;
            yield return null;
            Assert.IsTrue(h.enabled);
            Assert.AreEqual(Color.green, h.color);

            _model.score.Value = 15;
            yield return null;
            Assert.IsTrue(h.enabled);
            Assert.AreEqual(Color.red, h.color);

            _model.score.Value = 2;
            yield return null;
            Assert.IsFalse(h.enabled, "falling back below threshold disables the highlight again");
        }

        [UnityTest]
        public IEnumerator SliderUserInput_UpdatesModel_AndAllPaths_WithoutBounceBack() {
            yield return MmvcLiveUiAssert.SetSlider(_presenter.scoreSlider, 9f);

            Assert.AreEqual(9, _model.score.Value);
            Assert.AreEqual("9", _presenter.scoreText.text);
            Assert.AreEqual("0->9", _presenter.handlerText.text);
            Assert.AreEqual(1, _presenter.scoreHandlerCalls, "one user change = one handler call");
            Assert.AreEqual(1, _manualCalls, "one user change = one manual call");
            Assert.AreEqual(9f, _presenter.scoreSlider.value);
        }

        [UnityTest]
        public IEnumerator ToggleClick_UpdatesModel_AndModelChangeUpdatesToggleBack() {
            var toggle = _presenter.flagToggle;
            Assert.IsFalse(_model.flag.Value);

            yield return MmvcLiveUiAssert.SetToggle(toggle, true);
            Assert.IsTrue(toggle.isOn);
            Assert.IsTrue(_model.flag.Value, "UI click reaches the model");

            yield return MmvcLiveUiAssert.ModelToView(_model.flag, false, () => toggle.isOn, false);
            Assert.IsFalse(toggle.isOn, "model change reaches the toggle");
        }

        [UnityTest]
        public IEnumerator InputField_TypedText_UpdatesModel_AndModelUpdatesText() {
            yield return MmvcLiveUiAssert.Type(_presenter.labelInput, "typed");
            Assert.AreEqual("typed", _model.label.Value);

            yield return MmvcLiveUiAssert.ModelToView(_model.label, "from model", () => _presenter.labelInput.text, "from model");
        }

        [UnityTest]
        public IEnumerator ReentrantClamp_FromManualSubscription_ConvergesInModelAndEveryView() {
            Action<ObservableVariable<int>> clamp = v => { if (v.Value > 20) v.Value = 20; };
            _model.score.OnValueChanged.Add(clamp, this);

            yield return MmvcLiveUiAssert.SetSlider(_presenter.scoreSlider, 30f);

            Assert.AreEqual(20, _model.score.Value);
            Assert.AreEqual("20", _presenter.scoreText.text);
            Assert.AreEqual(20f, _presenter.scoreSlider.value);
            Assert.AreEqual(20, _presenter.lastCurrent, "the handler's final observed value is the clamped one");
            Assert.AreEqual(20, _manualLast);
            Assert.LessOrEqual(_presenter.scoreHandlerCalls, 2, "no notification storm");
        }

        [UnityTest]
        public IEnumerator Rebind_KeepsManualSubscription_AndDoesNotDuplicateBindings() {
            Observable.SetListeners(_presenter, _model);
            Observable.SetListeners(_presenter, _model);
            ResetCounters();

            _model.score.Value = 4;
            yield return null;

            Assert.AreEqual(1, _presenter.scoreHandlerCalls, "handler bound once after repeated SetListeners");
            Assert.AreEqual(1, _manualCalls, "manual subscription survives rebinding");
            Assert.AreEqual("4", _presenter.scoreText.text);

            yield return MmvcLiveUiAssert.SetSlider(_presenter.scoreSlider, 6f);
            Assert.AreEqual(2, _presenter.scoreHandlerCalls);
            Assert.AreEqual(2, _manualCalls);
            Assert.AreEqual(6, _model.score.Value);
        }

        [UnityTest]
        public IEnumerator DisableAndReenable_KeepsBindingsWithoutDuplicates() {
            _unit.SetActive(false);
            _model.score.Value = 3;
            yield return null;
            Assert.AreEqual("3", _presenter.scoreText.text, "binding survives deactivation");

            _unit.SetActive(true);
            yield return null;
            ResetCounters();

            _model.score.Value = 8;
            yield return null;
            Assert.AreEqual(1, _presenter.scoreHandlerCalls, "re-enable must not add a second handler");
            Assert.AreEqual(1, _manualCalls);
            Assert.AreEqual("8", _presenter.scoreText.text);
        }

        [UnityTest]
        public IEnumerator ListChanges_RenderThroughHandler_AndNotifyManualSubscription() {
            _model.items.Add("a");
            _model.items.Add("b");
            yield return null;
            Assert.AreEqual(2, _presenter.itemsHandlerCalls);
            Assert.AreEqual(2, _listManualCalls);
            Assert.AreEqual(2, CountLiveChildren(_presenter.itemsRoot));

            Assert.IsTrue(_model.items.Remove("a"));
            Assert.IsFalse(_model.items.Remove("missing"));
            yield return null;
            Assert.AreEqual(3, _presenter.itemsHandlerCalls, "removing a missing item must not notify");
            Assert.AreEqual(1, CountLiveChildren(_presenter.itemsRoot));
        }

        [UnityTest]
        public IEnumerator DestroyedPresenter_StopsReceivingNotifications_ManualSubscriptionKeepsWorking() {
            var text = _presenter.scoreText;
            UnityEngine.Object.Destroy(_presenter);
            yield return null;

            LogAssert.NoUnexpectedReceived();
            _model.score.Value = 12;
            yield return null;

            Assert.AreEqual(1, _manualCalls, "manual subscription is independent of the presenter");
            Assert.AreNotEqual("12", text.text, "destroyed presenter no longer renders");
        }

        private static int CountLiveChildren(Transform root) {
            int count = 0;
            for (int i = 0; i < root.childCount; i++) {
                // Children destroyed this frame are still listed until the frame ends; the handler destroys then recreates.
                if (root.GetChild(i).gameObject.activeInHierarchy) count++;
            }
            return count;
        }
    }
}
