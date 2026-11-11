using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using TMPro;
using SavableObservable;

namespace SavableObservable.Tests {
    #region Top-Level Fixture Components for Tests

    public class ClampingModel : BaseObservableDataModel {
        public ObservableVariable<int> depth = new ObservableVariable<int>();
        public ObservableVariable<bool> masterSwitch = new ObservableVariable<bool>();
        public ObservableVariable<bool> dependentSwitch = new ObservableVariable<bool>();
    }

    public class ClampingLogic : BaseLogic<ClampingModel, ClampingPresenter> {
        protected override bool RegisterAsSharedSingleton => false;

        public int LogicClampInvocationCount { get; private set; }

        public void BindLogicClamping() {
            var model = GetModel();
            model.depth.OnValueChanged.Add(OnDepthChanged, this);
            model.masterSwitch.OnValueChanged.Add(OnMasterSwitchChanged, this);
        }

        private void OnDepthChanged(ObservableVariable<int> variable) {
            LogicClampInvocationCount++;
            // Re-entrant clamp if value exceeds 10
            if (variable.Value > 10) {
                variable.Value = 10;
            } else if (variable.Value < 1) {
                variable.Value = 1;
            }
        }

        private void OnMasterSwitchChanged(ObservableVariable<bool> variable) {
            // Dependent mutation during OnValueChanged
            if (!variable.Value) {
                var model = GetModel();
                model.dependentSwitch.Value = false;
            }
        }
    }

    public class ClampingPresenter : BaseObservablePresenter<ClampingModel> {
        [AutoBind(nameof(ClampingModel.depth))]
        public Slider depthSlider;

        [AutoBind(nameof(ClampingModel.masterSwitch))]
        public Toggle masterToggle;

        [AutoBind(nameof(ClampingModel.dependentSwitch))]
        public Toggle dependentToggle;

        public int ObservedDepthCurrent { get; private set; }
        public int ObservedDepthPrevious { get; private set; }
        public int DepthHandlerCount { get; private set; }

        [ObservableHandler(nameof(ClampingModel.depth))]
        private void OnDepthHandler(int current, int previous) {
            DepthHandlerCount++;
            ObservedDepthCurrent = current;
            ObservedDepthPrevious = previous;
        }
    }

    public class ForeignSubscriberComponent : MonoBehaviour {
        public int NotificationCount { get; private set; }
        public int LastObservedValue { get; private set; }

        public void OnForeignValueChanged(ObservableVariable<int> variable) {
            NotificationCount++;
            LastObservedValue = variable.Value;
        }
    }

    public class ListHandlerSubscriber : MonoBehaviour {
        public int ZeroArgCount { get; private set; }
        public int OneArgObservableListCount { get; private set; }
        public int OneArgListCount { get; private set; }
        public int TwoArgCount { get; private set; }
        public int ObjectArgCount { get; private set; }

        public List<string> LastListContent { get; private set; }
        public IReadOnlyList<string> LastPreviousListContent { get; private set; }

        [ObservableHandler("items")]
        public void Handle0() {
            ZeroArgCount++;
        }

        [ObservableHandler("items")]
        public void Handle1ObservableList(ObservableList<string> list) {
            OneArgObservableListCount++;
        }

        [ObservableHandler("items")]
        public void Handle1List(List<string> list) {
            OneArgListCount++;
            LastListContent = list != null ? new List<string>(list) : null;
        }

        [ObservableHandler("items")]
        public void Handle2(List<string> current, IReadOnlyList<string> previous) {
            TwoArgCount++;
            LastListContent = current != null ? new List<string>(current) : null;
            LastPreviousListContent = previous != null ? new List<string>(previous) : null;
        }

        [ObservableHandler("items")]
        public void HandleObject(object obj) {
            ObjectArgCount++;
        }
    }

    public class ListTestModel : BaseObservableDataModel {
        public ObservableList<string> items = new ObservableList<string>();
    }

    #endregion

    [TestFixture]
    public class TrackedActionAndForeignSubTests {
        private GameObject _fixtureRoot;

        [SetUp]
        public void SetUp() {
            _fixtureRoot = new GameObject("TrackedActionAndForeignSubFixtureRoot");
        }

        [TearDown]
        public void TearDown() {
            if (_fixtureRoot != null) {
                UnityEngine.Object.DestroyImmediate(_fixtureRoot);
            }
        }

        #region ObservableTrackedAction Tests

        [Test]
        public void ReentrantClamping_TwoWayUIAndHandlers_ConvergeToClampedValue_AndDependentMutationPropagates() {
            var host = new GameObject("ClampingHost");
            host.transform.SetParent(_fixtureRoot.transform, false);

            var sliderObj = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderObj.transform.SetParent(host.transform, false);
            var slider = sliderObj.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 20f;

            var masterToggleObj = new GameObject("MasterToggle", typeof(RectTransform), typeof(Toggle));
            masterToggleObj.transform.SetParent(host.transform, false);
            var masterToggle = masterToggleObj.GetComponent<Toggle>();

            var dependentToggleObj = new GameObject("DependentToggle", typeof(RectTransform), typeof(Toggle));
            dependentToggleObj.transform.SetParent(host.transform, false);
            var dependentToggle = dependentToggleObj.GetComponent<Toggle>();

            var model = host.AddComponent<ClampingModel>();
            model.EnsureFieldsInitialized();
            model.depth.Value = 5;
            model.masterSwitch.Value = true;
            model.dependentSwitch.Value = true;

            var presenter = host.GetComponent<ClampingPresenter>() ?? host.AddComponent<ClampingPresenter>();
            var logic = host.GetComponent<ClampingLogic>() ?? host.AddComponent<ClampingLogic>();
            presenter.depthSlider = slider;
            presenter.masterToggle = masterToggle;
            presenter.dependentToggle = dependentToggle;

            slider.value = 5f;
            masterToggle.isOn = true;
            dependentToggle.isOn = true;

            Observable.SetListeners(presenter, model);
            logic.BindLogicClamping();

            // Initial state verification
            Assert.AreEqual(5, presenter.depthSlider.value);
            Assert.IsTrue(presenter.masterToggle.isOn);
            Assert.IsTrue(presenter.dependentToggle.isOn);

            // 1) Re-entrant clamping verification:
            // Simulate UI setting an out-of-range value (15 > 10)
            slider.value = 15f;

            // Clamping handler in Logic must clamp depth to 10
            Assert.AreEqual(10, model.depth.Value, "Model value must be clamped by Logic");
            Assert.AreEqual(10f, slider.value, "Slider UI must reflect the clamped value");
            Assert.AreEqual(10, presenter.ObservedDepthCurrent);
            Assert.AreEqual(15, presenter.ObservedDepthPrevious, "Previous value before clamping was 15");

            // 2) Dependent mutation during OnValueChanged:
            // Turning off master switch in UI should mutate dependent switch to false via Logic
            masterToggle.isOn = false;
            Assert.IsFalse(model.masterSwitch.Value);
            Assert.IsFalse(model.dependentSwitch.Value, "Dependent model value must be set to false by Logic");
            Assert.IsFalse(dependentToggle.isOn, "Dependent toggle UI must reflect dependent model state");
        }

        [Test]
        public void InfiniteLoop_ExceedingMaxIterations_StopsAndLogsError() {
            var modelVar = new ObservableVariable<int> { Value = 0 };

            // Add recursive handlers directly to modelVar.OnValueChanged
            modelVar.OnValueChanged.Add(v => {
                if (v.Value < 100) {
                    v.Value++;
                }
            }, null);

            modelVar.OnValueChanged.Add(v => {
                if (v.Value < 100) {
                    v.Value++;
                }
            }, null);

            LogAssert.Expect(LogType.Error, new Regex(@"ObservableTrackedAction\.Invoke exceeded maxIterations \(32\)"));

            modelVar.Value = 1;
        }

        [Test]
        public void ReentrantValueChange_MultipleSubscribers_DeliversEveryTransitionInFifoOrderWithoutDuplicateCalls() {
            var variable = new ObservableVariable<int> { Value = 0 };
            var transitions = new List<(int value, int prev)>();

            // H1: mutates Value to 2 when it sees Value == 1
            variable.OnValueChanged.Add(v => {
                if (v.Value == 1) {
                    v.Value = 2;
                }
            }, null);

            // H2: records every transition seen
            variable.OnValueChanged.Add(v => {
                transitions.Add((v.Value, v.PreviousValue));
            }, null);

            variable.Value = 1;

            Assert.AreEqual(2, transitions.Count, "H2 must receive exactly 2 transitions.");
            Assert.AreEqual((1, 0), transitions[0], "First transition must be (1, 0).");
            Assert.AreEqual((2, 1), transitions[1], "Second transition must be (2, 1).");
        }

        [Test]
        public void ExceptionIsolation_OneThrowingHandler_DoesNotPreventSubsequentHandlers() {
            var action = new ObservableTrackedAction<ObservableVariable<int>>();
            var modelVar = new ObservableVariable<int> { Value = 1 };

            bool handler2Executed = false;

            action.Add(v => throw new InvalidOperationException("Simulated test failure"), null);
            action.Add(v => handler2Executed = true, null);

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: Simulated test failure"));

            action.Invoke(modelVar);

            Assert.IsTrue(handler2Executed, "Subsequent handler must execute even if preceding handler threw exception");
        }

        [Test]
        public void MainThreadGuard_NonMainThreadAccess_ThrowsInvalidOperationException() {
            var action = new ObservableTrackedAction<ObservableVariable<int>>();

            // Calling Add from background thread must throw
            Assert.Throws<AggregateException>(() => {
                Task.Run(() => {
                    action.Add(_ => { }, null);
                }).Wait();
            });

            // Calling Invoke from background thread must throw
            Assert.Throws<AggregateException>(() => {
                Task.Run(() => {
                    action.Invoke(new ObservableVariable<int>());
                }).Wait();
            });
        }

        #endregion

        #region Foreign Subscription & Destruction Tests

        [Test]
        public void ForeignSubscriber_DestroyedBeforeModel_DoesNotThrowAndCleansUp() {
            var modelHost = new GameObject("ForeignModelHost");
            modelHost.transform.SetParent(_fixtureRoot.transform, false);
            var model = modelHost.AddComponent<ClampingModel>();
            model.EnsureFieldsInitialized();

            var subscriberHost = new GameObject("ForeignSubscriberHost");
            subscriberHost.transform.SetParent(_fixtureRoot.transform, false);
            var subscriber = subscriberHost.AddComponent<ForeignSubscriberComponent>();

            // 1) Instance method subscription
            model.depth.OnValueChanged.Add(subscriber.OnForeignValueChanged, subscriber);

            // 2) Lambda/closure subscription passing subscriber as tracked key
            int lambdaInvocations = 0;
            model.depth.OnValueChanged.Add(_ => lambdaInvocations++, subscriber);

            model.depth.Value = 42;
            Assert.AreEqual(1, subscriber.NotificationCount);
            Assert.AreEqual(1, lambdaInvocations);
            Assert.AreEqual(42, subscriber.LastObservedValue);

            // Destroy subscriber while model stays alive
            UnityEngine.Object.DestroyImmediate(subscriberHost);

            // Mutating model must NOT throw MissingReferenceException and must clean up dead handlers
            Assert.DoesNotThrow(() => {
                model.depth.Value = 84;
            });

            Assert.AreEqual(1, lambdaInvocations, "Dead subscriber lambda closure must not be invoked after destruction");
        }

        [Test]
        public void SetListeners_Rebind_PreservesManualTrackedSubscriptions() {
            var host = new GameObject("RebindHost");
            host.transform.SetParent(_fixtureRoot.transform, false);

            var model = host.AddComponent<ClampingModel>();
            model.EnsureFieldsInitialized();

            var presenter = host.AddComponent<ClampingPresenter>();

            // Register manual tracked subscription on presenter
            int manualInvocations = 0;
            model.depth.OnValueChanged.Add(_ => manualInvocations++, presenter);

            // First SetListeners call establishes attribute bindings
            Observable.SetListeners(presenter, model);

            model.depth.Value = 7;
            Assert.AreEqual(1, manualInvocations, "Manual subscription should fire");
            Assert.AreEqual(1, presenter.DepthHandlerCount, "Attribute handler should fire");

            // Re-bind via SetListeners (e.g. on reload)
            Observable.SetListeners(presenter, model);

            model.depth.Value = 12;
            Assert.AreEqual(2, manualInvocations, "Manual subscription must NOT be wiped out by SetListeners rebind");
            Assert.AreEqual(2, presenter.DepthHandlerCount, "Attribute handler must be invoked without duplicate");
        }

        [Test]
        public void SetListeners_Rebind_PreservesManualSubscriptionsAddedAfterBinding_OfPresenterAndForeignSubscriber() {
            var host = new GameObject("RebindLateManualHost");
            host.transform.SetParent(_fixtureRoot.transform, false);

            var model = host.AddComponent<ClampingModel>();
            model.EnsureFieldsInitialized();
            var presenter = host.AddComponent<ClampingPresenter>();
            var foreign = host.AddComponent<ForeignSubscriberComponent>();

            // The binding exists first; the manual subscriptions are added afterwards (a Logic subscribing to its model at runtime).
            Observable.SetListeners(presenter, model);

            int presenterManual = 0;
            model.depth.OnValueChanged.Add(_ => presenterManual++, presenter);
            model.depth.OnValueChanged.Add(foreign.OnForeignValueChanged, foreign);

            model.depth.Value = 3;
            Assert.AreEqual(1, presenterManual);
            Assert.AreEqual(1, foreign.NotificationCount);
            Assert.AreEqual(1, presenter.DepthHandlerCount);

            // Rebind (e.g. after a load): the attribute handler is replaced, never duplicated; both manual subscriptions stay.
            Observable.SetListeners(presenter, model);
            Observable.SetListeners(presenter, model);

            model.depth.Value = 8;
            Assert.AreEqual(2, presenterManual, "A manual subscription added after the first bind must survive rebinds.");
            Assert.AreEqual(2, foreign.NotificationCount, "A foreign subscriber's manual subscription must be untouched by the presenter's rebind.");
            Assert.AreEqual(2, presenter.DepthHandlerCount, "The attribute handler fires exactly once per change after rebinding.");
            Assert.AreEqual(8, foreign.LastObservedValue);
        }
        [Test]
        public void SetListeners_Repeated_IsIdempotent_AndPreservesHandlerBindings() {
            var host = new GameObject("AutoBindIdempotentHost");
            host.transform.SetParent(_fixtureRoot.transform, false);

            var toggleObj = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle));
            toggleObj.transform.SetParent(host.transform, false);
            var toggle = toggleObj.GetComponent<Toggle>();

            var sliderObj = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderObj.transform.SetParent(host.transform, false);
            var slider = sliderObj.GetComponent<Slider>();

            var model = host.AddComponent<ClampingModel>();
            model.EnsureFieldsInitialized();

            var presenter = host.AddComponent<ClampingPresenter>();
            presenter.masterToggle = toggle;
            presenter.depthSlider = slider;

            // First wire full listeners (methods + autobind), then rebind repeatedly
            Observable.SetListeners(presenter, model);
            Observable.SetListeners(presenter, model);
            Observable.SetListeners(presenter, model);

            // Verify AutoBind is idempotent: toggle UI change updates model exactly once
            int modelChangeCount = 0;
            model.masterSwitch.OnValueChanged.Add(_ => modelChangeCount++, presenter);

            toggle.isOn = true;
            Assert.AreEqual(1, modelChangeCount, "UI listener should not be registered twice");

            // Verify the method handler is intact and not duplicated after repeated SetListeners calls
            int handlerCountBefore = presenter.DepthHandlerCount;
            model.depth.Value = 9;
            Assert.AreEqual(handlerCountBefore + 1, presenter.DepthHandlerCount, "Method handler must remain intact (exactly once) after repeated SetListeners");
        }

        #endregion

        #region ObservableList Signatures & UI Numeric Tests

        [Test]
        public void ObservableList_AllHandlerSignatures_DispatchedCorrectly() {
            var host = new GameObject("ListHandlerHost");
            host.transform.SetParent(_fixtureRoot.transform, false);

            var model = host.AddComponent<ListTestModel>();
            model.EnsureFieldsInitialized();

            var subscriber = host.AddComponent<ListHandlerSubscriber>();
            Observable.SetListeners(subscriber, model);

            model.items.Add("Item1");

            Assert.AreEqual(1, subscriber.ZeroArgCount);
            Assert.AreEqual(1, subscriber.OneArgObservableListCount);
            Assert.AreEqual(1, subscriber.OneArgListCount);
            Assert.AreEqual(1, subscriber.TwoArgCount);
            Assert.AreEqual(1, subscriber.ObjectArgCount);

            Assert.IsNotNull(subscriber.LastListContent);
            Assert.AreEqual(1, subscriber.LastListContent.Count);
            Assert.AreEqual("Item1", subscriber.LastListContent[0]);

            // Test Remove missing item: returns false and must NOT fire OnChanged
            int countBefore = subscriber.ZeroArgCount;
            bool removed = model.items.Remove("MissingItem");
            Assert.IsFalse(removed);
            Assert.AreEqual(countBefore, subscriber.ZeroArgCount, "Remove(missing) must not fire notifications");
        }

        [Test]
        public void TwoWay_SliderWithInt_RoundtripsCorrectly() {
            var host = new GameObject("SliderIntHost");
            host.transform.SetParent(_fixtureRoot.transform, false);

            var sliderObj = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderObj.transform.SetParent(host.transform, false);
            var slider = sliderObj.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 100f;

            var model = new ObservableVariable<int> { Value = 25 };
            var adapter = UIAdapterRegistry.GetAdapter(typeof(Slider)) as IUIListenerAdapter;
            Assert.IsNotNull(adapter);

            // Model -> UI
            adapter.SetValue(slider, model.Value, typeof(int));
            Assert.AreEqual(25f, slider.value);

            // UI -> Model
            int observedModel = 0;
            object token = adapter.AddListener(slider, val => {
                observedModel = (int)val;
            }, typeof(int));

            slider.value = 75f;
            Assert.AreEqual(75, observedModel);

            adapter.RemoveListener(slider, token);
        }

        [Test]
        public void TwoWay_TMPInputField_WithNumericTypes_HandlesConversionsAndInvalidInput() {
            var host = new GameObject("TMPNumericHost");
            host.transform.SetParent(_fixtureRoot.transform, false);

            var inputObj = new GameObject("Input", typeof(RectTransform), typeof(TMP_InputField));
            inputObj.transform.SetParent(host.transform, false);
            var input = inputObj.GetComponent<TMP_InputField>();
            var textObj = new GameObject("Text", typeof(TextMeshProUGUI));
            textObj.transform.SetParent(inputObj.transform, false);
            input.textComponent = textObj.GetComponent<TextMeshProUGUI>();

            var adapter = UIAdapterRegistry.GetAdapter(typeof(TMP_InputField)) as IUIListenerAdapter;
            Assert.IsNotNull(adapter);

            int observedInt = -1;
            object token = adapter.AddListener(input, val => {
                observedInt = (int)val;
            }, typeof(int));

            // Valid numeric text
            input.text = "123";
            Assert.AreEqual(123, observedInt);

            // Empty text resets numeric to 0
            input.text = "";
            Assert.AreEqual(0, observedInt);

            // Invalid text is ignored (conversion caught, listener not dispatched)
            observedInt = 999;
            input.text = "invalid_text_abc";
            Assert.AreEqual(999, observedInt, "Invalid non-numeric input should be ignored for int binding");

            adapter.RemoveListener(input, token);
        }

        [Test]
        public void TwoWay_TMPInputField_WithFloatNumeric_HandlesConversionsAndScientificNotation() {
            var host = new GameObject("TMPFloatNumericHost");
            host.transform.SetParent(_fixtureRoot.transform, false);

            var inputObj = new GameObject("Input", typeof(RectTransform), typeof(TMP_InputField));
            inputObj.transform.SetParent(host.transform, false);
            var input = inputObj.GetComponent<TMP_InputField>();
            var textObj = new GameObject("Text", typeof(TextMeshProUGUI));
            textObj.transform.SetParent(inputObj.transform, false);
            input.textComponent = textObj.GetComponent<TextMeshProUGUI>();

            var adapter = UIAdapterRegistry.GetAdapter(typeof(TMP_InputField)) as IUIListenerAdapter;
            Assert.IsNotNull(adapter);

            float observedFloat = -1f;
            object token = adapter.AddListener(input, val => {
                observedFloat = (float)val;
            }, typeof(float));

            // Valid float numeric text
            input.text = "3.14159";
            Assert.AreEqual(3.14159f, observedFloat, 0.0001f);

            // Empty text resets numeric to 0f
            input.text = "";
            Assert.AreEqual(0f, observedFloat);

            // Invalid text is ignored
            observedFloat = 88.8f;
            input.text = "not_a_float";
            Assert.AreEqual(88.8f, observedFloat);

            adapter.RemoveListener(input, token);
        }

        [Test]
        public void ReentrantAction_HandlerRemoval_DuringInvocationPass_SnapshotIsSafe() {
            var action = new ObservableTrackedAction<ObservableVariable<int>>();
            var modelVar = new ObservableVariable<int> { Value = 1 };

            Action<ObservableVariable<int>> handler2 = null;
            bool handler1Called = false;
            bool handler2Called = false;
            bool handler3Called = false;

            action.Add(v => {
                handler1Called = true;
                // Dynamically remove handler2 while invoking handler1
                if (handler2 != null) action.Remove(handler2);
            }, null);

            handler2 = v => {
                handler2Called = true;
            };
            action.Add(handler2, null);

            action.Add(v => {
                handler3Called = true;
            }, null);

            // Invoke action
            action.Invoke(modelVar);

            Assert.IsTrue(handler1Called);
            Assert.IsTrue(handler3Called);

            // Next invoke pass: handler2 is permanently gone and won't be called
            handler1Called = false;
            handler2Called = false;
            handler3Called = false;
            action.Invoke(modelVar);

            Assert.IsTrue(handler1Called);
            Assert.IsFalse(handler2Called);
            Assert.IsTrue(handler3Called);
        }

        [Test]
        public void TwoWay_LegacyInputField_RoundtripsCorrectly() {
            var host = new GameObject("LegacyInputHost");
            host.transform.SetParent(_fixtureRoot.transform, false);

            var inputObj = new GameObject("LegacyInput", typeof(RectTransform), typeof(InputField));
            inputObj.transform.SetParent(host.transform, false);
            var input = inputObj.GetComponent<InputField>();
            var textObj = new GameObject("Text", typeof(Text));
            textObj.transform.SetParent(inputObj.transform, false);
            input.textComponent = textObj.GetComponent<Text>();

            var adapter = UIAdapterRegistry.GetAdapter(typeof(InputField)) as IUIListenerAdapter;
            Assert.IsNotNull(adapter);

            // Model -> UI
            adapter.SetValue(input, "LegacyInitial", typeof(string));
            Assert.AreEqual("LegacyInitial", input.text);

            // UI -> Model
            string observed = null;
            object token = adapter.AddListener(input, val => observed = (string)val, typeof(string));

            input.text = "LegacyUpdated";
            Assert.AreEqual("LegacyUpdated", observed);

            adapter.RemoveListener(input, token);
        }

        #endregion
    }
}
