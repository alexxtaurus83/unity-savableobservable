using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
#if (UNITY_6000_0_OR_NEWER || UNITY_6_OR_NEWER) && HAS_UNITY_PIPELINE
using Unity.Pipeline.Commands;
#endif
using SavableObservable;
using SavableObservable.Samples;

namespace SavableObservable.Editor {
    /// <summary>
    /// Comprehensive MMVC CLI Pipeline self-test runner.
    /// Exposes Unity Pipeline command 'mmvc_selftest' on Unity 6+ with Unity Pipeline.
    /// </summary>
    public static class MmvcSelfTest {
        private class TestRunnerContext {
            public int Passed;
            public int Failed;
            public readonly List<string> FailureMessages = new List<string>();

            public void Assert(bool condition, string testName, string details = "") {
                if (condition) {
                    Passed++;
                } else {
                    Failed++;
                    string msg = $"FAIL: {testName}" + (string.IsNullOrEmpty(details) ? "" : $" -> {details}");
                    FailureMessages.Add(msg);
                    Debug.LogWarning("[MmvcSelfTest] " + msg);
                }
            }
        }

#if (UNITY_6000_0_OR_NEWER || UNITY_6_OR_NEWER) && HAS_UNITY_PIPELINE
        [CliCommand("mmvc_selftest", "Run complete SavableObservable verification: deterministic C# checks, active scene MMVC validation, and live reactive execution.")]
        public static string RunSelfTest([CliArg("mode", "all | fast | scene")] string mode = "all") {
#else
        public static string RunSelfTest(string mode = "all") {
#endif
            var ctx = new TestRunnerContext();
            var sb = new StringBuilder();

            sb.AppendLine("==================================================");
            sb.AppendLine($" SavableObservable MMVC Self-Test (Mode: {mode})");
            sb.AppendLine("==================================================");

            try {
                // Phase 1: In-memory framework primitives
                if (mode == "all" || mode == "fast") {
                    RunPhase1Deterministic(ctx);
                }

                // Phase 2 & 3: Scene, hierarchy & reactive execution
                if (mode == "all" || mode == "scene") {
                    RunPhase2And3SceneReactive(ctx);
                }

                // Phase 4: Architectural guards & domain reload lifecycle
                if (mode == "all" || mode == "fast") {
                    RunPhase4GuardsAndLifecycle(ctx);
                }
            } catch (Exception ex) {
                ctx.Failed++;
                ctx.FailureMessages.Add($"CRITICAL EXCEPTION during self-test: {ex.Message}\n{ex.StackTrace}");
            }

            int total = ctx.Passed + ctx.Failed;
            sb.AppendLine("--------------------------------------------------");
            if (ctx.Failed == 0) {
                sb.AppendLine($"RESULT: PASS {ctx.Passed}/{total}");
            } else {
                sb.AppendLine($"RESULT: FAILED ({ctx.Passed} passed, {ctx.Failed} failed out of {total})");
                sb.AppendLine("Failure Details:");
                foreach (var fail in ctx.FailureMessages) {
                    sb.AppendLine("  - " + fail);
                }
            }
            sb.AppendLine("==================================================");

            return sb.ToString();
        }

        private static void RunPhase1Deterministic(TestRunnerContext ctx) {
            // 1.1 ObservableVariable value assignment & previous value
            var intVar = new ObservableVariable<int> { Value = 10 };
            int notifiedVal = 0;
            intVar.OnValueChanged.Add(v => notifiedVal = v.Value, null);

            intVar.Value = 25;
            ctx.Assert(notifiedVal == 25, "Phase 1.1: ObservableVariable assigns new value and fires notification", $"Got {notifiedVal}");
            ctx.Assert(intVar.PreviousValue == 10, "Phase 1.1: ObservableVariable tracks PreviousValue", $"Got {intVar.PreviousValue}");

            // 1.2 Identical value assignment fires notification (current framework behavior)
            bool identicalNotified = false;
            intVar.OnValueChanged.Add(_ => identicalNotified = true, null);
            intVar.Value = 25;
            ctx.Assert(identicalNotified, "Phase 1.2: ObservableVariable fires notification on identical value assignment");

            // 1.3 ForceNotify fires notification
            bool forceNotified = false;
            intVar.OnValueChanged.Add(_ => forceNotified = true, null);
            intVar.ForceNotify();
            ctx.Assert(forceNotified, "Phase 1.3: ObservableVariable ForceNotify fires notification");

            // 1.4 ObservableList Add, Remove, PreviousValue
            var list = new ObservableList<string>();
            bool listChanged = false;
            list.OnChanged.Add(_ => listChanged = true, null);

            list.Add("Alpha");
            ctx.Assert(listChanged && list.Count == 1 && list[0] == "Alpha", "Phase 1.4: ObservableList Add");
            ctx.Assert(list.PreviousValue != null && list.PreviousValue.Count == 0, "Phase 1.4: ObservableList PreviousValue snapshot on Add");

            listChanged = false;
            list.Add("Beta");
            ctx.Assert(list.PreviousValue != null && list.PreviousValue.Count == 1 && list.PreviousValue[0] == "Alpha", "Phase 1.4: ObservableList PreviousValue snapshot before second Add");

            listChanged = false;
            list[0] = "Omega";
            ctx.Assert(listChanged && list[0] == "Omega", "Phase 1.5: ObservableList indexer assignment triggers change");
            ctx.Assert(list.PreviousValue[0] == "Alpha", "Phase 1.5: ObservableList indexer records PreviousValue snapshot");

            listChanged = false;
            list.Remove("Omega");
            ctx.Assert(listChanged && list.Count == 1 && list[0] == "Beta", "Phase 1.4: ObservableList Remove");

            list.Clear();
            ctx.Assert(list.Count == 0, "Phase 1.4: ObservableList Clear");

            // 1.6 ObservableTrackedAction operators & count
            var action = new ObservableTrackedAction<ObservableVariable<int>>();
            Action<ObservableVariable<int>> handlerA = _ => { };
            Action<ObservableVariable<int>> handlerB = _ => { };

            action += handlerA;
            ctx.Assert(action.HandlerCount == 1, "Phase 1.6: TrackedAction += adds handler");
            action += handlerB;
            ctx.Assert(action.HandlerCount == 2, "Phase 1.6: TrackedAction += adds second handler");
            action -= handlerA;
            ctx.Assert(action.HandlerCount == 1, "Phase 1.6: TrackedAction -= removes handler");
            action -= handlerB;
            ctx.Assert(action.HandlerCount == 0, "Phase 1.6: TrackedAction -= removes all handlers");

            // 1.7 UIAdapterRegistry lookup
            var tmpAdapter = UIAdapterRegistry.GetAdapter(typeof(TextMeshProUGUI));
            var sliderAdapter = UIAdapterRegistry.GetAdapter(typeof(Slider));
            var toggleAdapter = UIAdapterRegistry.GetAdapter(typeof(Toggle));
            var inputAdapter = UIAdapterRegistry.GetAdapter(typeof(TMP_InputField));
            var imgAdapter = UIAdapterRegistry.GetAdapter(typeof(Image));

            ctx.Assert(tmpAdapter != null, "Phase 1.7: UIAdapterRegistry resolves TextMeshProUGUI adapter");
            ctx.Assert(sliderAdapter != null, "Phase 1.7: UIAdapterRegistry resolves Slider adapter");
            ctx.Assert(toggleAdapter != null, "Phase 1.7: UIAdapterRegistry resolves Toggle adapter");
            ctx.Assert(inputAdapter != null, "Phase 1.7: UIAdapterRegistry resolves TMP_InputField adapter");
            ctx.Assert(imgAdapter != null, "Phase 1.7: UIAdapterRegistry resolves Image adapter");
        }

        private static void RunPhase2And3SceneReactive(TestRunnerContext ctx) {
            // Create a clean in-memory test root
            var root = new GameObject("[MmvcSelfTest_Host]");
            try {
                // UI Canvas & Controls
                var canvasObj = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                canvasObj.transform.SetParent(root.transform, false);

                var nameTextObj = new GameObject("NameText", typeof(RectTransform), typeof(TextMeshProUGUI));
                nameTextObj.transform.SetParent(canvasObj.transform, false);
                var nameText = nameTextObj.GetComponent<TextMeshProUGUI>();

                var hpTextObj = new GameObject("HpText", typeof(RectTransform), typeof(TextMeshProUGUI));
                hpTextObj.transform.SetParent(canvasObj.transform, false);
                var hpText = hpTextObj.GetComponent<TextMeshProUGUI>();

                var sliderObj = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
                sliderObj.transform.SetParent(canvasObj.transform, false);
                var slider = sliderObj.GetComponent<Slider>();
                slider.minValue = 0f;
                slider.maxValue = 1f;

                var toggleObj = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle));
                toggleObj.transform.SetParent(canvasObj.transform, false);
                var toggle = toggleObj.GetComponent<Toggle>();

                var inputObj = new GameObject("InputField", typeof(RectTransform), typeof(TMP_InputField));
                inputObj.transform.SetParent(canvasObj.transform, false);
                var inputField = inputObj.GetComponent<TMP_InputField>();
                var textObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                textObj.transform.SetParent(inputObj.transform, false);
                inputField.textComponent = textObj.GetComponent<TextMeshProUGUI>();

                var invContainerObj = new GameObject("InventoryContainer", typeof(RectTransform));
                invContainerObj.transform.SetParent(canvasObj.transform, false);

                // MMVC Triad GameObject
                var unitObj = new GameObject("PlayerUnit");
                unitObj.transform.SetParent(root.transform, false);

                var model = unitObj.AddComponent<SamplePlayerModel>();
                var logic = unitObj.AddComponent<SamplePlayerLogic>();
                var presenter = unitObj.AddComponent<SamplePlayerPresenter>();
                var loader = unitObj.AddComponent<SamplePlayerLoader>();

                // Phase 2: Hierarchy validation
                var models = unitObj.GetComponents<BaseObservableDataModel>();
                var presenters = unitObj.GetComponents<IObservablePresenter>();
                ctx.Assert(models.Length == 1, "Phase 2.1: Exactly one BaseObservableDataModel on unit");
                ctx.Assert(presenters.Length == 1, "Phase 2.1: Exactly one IObservablePresenter on unit");

                // Assign UI references to Presenter
                presenter.playerNameText = nameText;
                presenter.healthText = hpText;
                presenter.energySlider = slider;
                presenter.shieldToggle = toggle;
                presenter.nameInputField = inputField;
                presenter.inventoryContainer = invContainerObj.transform;

                ctx.Assert(presenter.playerNameText != null && presenter.healthText != null, "Phase 2.2: Presenter UI references assigned");

                // Phase 3: Live reactive execution
                // 3.1 Initial bind
                model.InitializeDefaults();
                Observable.SetListeners(presenter, model);

                ctx.Assert(presenter.GetModel() == model, "Phase 3.1: Presenter linked to Model");

                // 3.2 Model -> UI Text updates
                model.health.Value = 85;
                ctx.Assert(hpText.text == "85", "Phase 3.2: Model health change updates TextMeshPro text via AutoBind", $"hpText.text is '{hpText.text}'");
                ctx.Assert(presenter.LastObservedHealth == 85, "Phase 3.2: [ObservableHandler] method invoked for health", $"LastObservedHealth: {presenter.LastObservedHealth}");

                model.playerName.Value = "Warrior";
                ctx.Assert(nameText.text == "Warrior", "Phase 3.2: Model playerName change updates TextMeshPro text via AutoBind", $"nameText.text is '{nameText.text}'");

                // 3.3 UI -> Model Two-way binding (Toggle)
                toggle.isOn = true;
                ctx.Assert(model.isShieldActive.Value == true, "Phase 3.3: Toggle.isOn change propagates to Model.isShieldActive via two-way binding");

                // 3.4 UI -> Model Two-way binding (Slider)
                slider.value = 0.65f;
                ctx.Assert(Mathf.Approximately(model.energy.Value, 0.65f), "Phase 3.4: Slider.value change propagates to Model.energy via two-way binding", $"energy: {model.energy.Value}");

                // 3.5 UI -> Model Two-way binding (TMP_InputField)
                inputField.text = "Champion";
                ctx.Assert(model.playerName.Value == "Champion", "Phase 3.5: TMP_InputField change propagates to Model.playerName via two-way binding", $"playerName: '{model.playerName.Value}'");

                // 3.6 Logic execution -> Model & UI reaction
                logic.TakeDamage(20); // Shield is active, so takes 10 damage: 85 - 10 = 75
                ctx.Assert(model.health.Value == 75, "Phase 3.6: Logic.TakeDamage() calculates correct damage with shield", $"health: {model.health.Value}");
                ctx.Assert(hpText.text == "75", "Phase 3.6: Logic mutation reflects in UI text", $"hpText.text: '{hpText.text}'");

                logic.Heal(10); // 75 + 10 = 85
                ctx.Assert(model.health.Value == 85, "Phase 3.6: Logic.Heal() updates Model", $"health: {model.health.Value}");

                // 3.7 ObservableList and Presenter inventory reaction
                model.inventory.Add("ShieldItem");
                ctx.Assert(presenter.InventoryChangeEventsCount > 0, "Phase 3.7: ObservableList mutation invokes Presenter handler");
                ctx.Assert(presenter.CurrentInventoryItems.Contains("ShieldItem"), "Phase 3.7: Presenter inventory list updated");

                // 3.8 Loader persistence roundtrip
                var savedDto = loader.SaveCurrentState();
                ctx.Assert(savedDto != null && savedDto.playerName == "Champion" && savedDto.health == 85, "Phase 3.8: Loader saves accurate DTO");

                // Modify model, then restore via loader
                model.playerName.Value = "MutatedName";
                model.health.Value = 10;
                ctx.Assert(model.health.Value == 10, "Phase 3.8: Model state mutated before restore");

                loader.LoadSampleState(savedDto);
                ctx.Assert(model.playerName.Value == "Champion", "Phase 3.8: Loader restored playerName", $"Got '{model.playerName.Value}'");
                ctx.Assert(model.health.Value == 85, "Phase 3.8: Loader restored health", $"Got {model.health.Value}");

                // Also test BaseObservableDataModel.LoadDataFromModel with another model instance
                var sourceUnit = new GameObject("SourceUnit");
                try {
                    var sourceModel = sourceUnit.AddComponent<SamplePlayerModel>();
                    sourceModel.playerName.Value = "ClonedHero";
                    sourceModel.health.Value = 99;
                    sourceModel.score = 500;

                    model.LoadDataFromModel(sourceModel);
                    ctx.Assert(model.playerName.Value == "ClonedHero", "Phase 3.8: BaseObservableDataModel.LoadDataFromModel copies ObservableVariable");
                    ctx.Assert(model.health.Value == 99, "Phase 3.8: BaseObservableDataModel.LoadDataFromModel copies health Value");
                    ctx.Assert(model.score == 500, "Phase 3.8: BaseObservableDataModel.LoadDataFromModel copies plain fields");
                } finally {
                    UnityEngine.Object.DestroyImmediate(sourceUnit);
                }

                // Clean up listeners
                Observable.RemoveListeners(model, presenter);
            } finally {
                if (root != null) {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static void RunPhase4GuardsAndLifecycle(TestRunnerContext ctx) {
            // 4.1 Cross-GameObject binding rejection
            var goA = new GameObject("SubscriberGO");
            var goB = new GameObject("ModelGO");
            try {
                var modelB = goB.AddComponent<SamplePlayerModel>();
                var presenterA = goA.AddComponent<SamplePlayerPresenter>();

                // SetListeners across GameObjects should be safely rejected
                Observable.SetListeners(presenterA, modelB);
                // Verify no subscriptions were registered
                modelB.health.Value = 42;
                ctx.Assert(presenterA.LastObservedHealth != 42, "Phase 4.1: Cross-GameObject binding is rejected and not invoked");
            } finally {
                UnityEngine.Object.DestroyImmediate(goA);
                UnityEngine.Object.DestroyImmediate(goB);
            }

            // 4.2 Lifecycle cleanup upon destruction
            var tempHost = new GameObject("TempLifecycleUnit");
            try {
                var tempModel = tempHost.AddComponent<SamplePlayerModel>();
                var tempPresenter = tempHost.AddComponent<SamplePlayerPresenter>();
                tempModel.InitializeDefaults();

                Observable.SetListeners(tempPresenter, tempModel);
                tempModel.health.Value = 99;
                ctx.Assert(tempPresenter.LastObservedHealth == 99, "Phase 4.2: Subscriptions active before cleanup");

                // Cleanup subscriptions
                Observable.CleanupSubscriptions(tempModel);
                tempModel.health.Value = 50;
                ctx.Assert(tempPresenter.LastObservedHealth == 99, "Phase 4.2: CleanupSubscriptions detaches all handlers");
            } finally {
                UnityEngine.Object.DestroyImmediate(tempHost);
            }

            // 4.3 Domain Reload static reset verification
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
            try {
                // Call ResetInstanceData and ResetRegistry via reflection or direct method if accessible
                var resetMethod = typeof(Observable).GetMethod("ResetInstanceData", BindingFlags.NonPublic | BindingFlags.Static);
                resetMethod?.Invoke(null, null);

                var resetRegMethod = typeof(UIAdapterRegistry).GetMethod("ResetRegistry", BindingFlags.NonPublic | BindingFlags.Static);
                resetRegMethod?.Invoke(null, null);

                // Re-verify that UIAdapterRegistry can still retrieve adapters after static reset
                var adapter = UIAdapterRegistry.GetAdapter(typeof(Slider));
                ctx.Assert(adapter != null, "Phase 4.3: UIAdapterRegistry re-initializes cleanly after SubsystemRegistration static reset");
            } catch (Exception ex) {
                ctx.Assert(false, "Phase 4.3: Domain reload static reset execution", ex.Message);
            }
#endif
        }
    }
}
