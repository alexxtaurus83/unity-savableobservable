using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TMPro;
using SavableObservable;

namespace SavableObservable.Tests {

    [TestFixture]
    public class CommandAutoBindTests {

        private GameObject _host;
        private SettingsModel _model;
        private SettingsLogic _logic;
        private SettingsPresenter _presenter;

        public class SettingsModel : BaseObservableDataModel {
            public class IntObservable : ObservableVariable<int> { }
            public ObservableVariable<float> musicVolume = new ObservableVariable<float>();
            public ObservableVariable<bool> musicEnabled = new ObservableVariable<bool>();
            public ObservableVariable<string> serverUrl = new ObservableVariable<string>();
            public IntObservable difficulty = new IntObservable();
            public ObservableList<string> tags = new ObservableList<string>();
        }

        public class BaseSettingsLogic : BaseLogic<SettingsModel, SettingsPresenter> {
            public int InheritedCallCount;

            [UnityEngine.Scripting.Preserve]
            public virtual void SetInheritedValue(float val) {
                InheritedCallCount++;
            }
        }

        public class SettingsLogic : BaseSettingsLogic {
            public int VolumeCallCount;
            public int ToggleCallCount;
            public int UrlCallCount;
            public int DifficultyCallCount;
            public bool RejectToggle;
            public bool ThrowOnVolume;

            [UnityEngine.Scripting.Preserve]
            public void SetMusicVolume(float val) {
                VolumeCallCount++;
                if (ThrowOnVolume) throw new InvalidOperationException("Simulated volume failure");
                // Clamp to [0, 1]
                var clamped = Mathf.Clamp(val, 0f, 1f);
                GetModel().musicVolume.Value = clamped;
            }

            [UnityEngine.Scripting.Preserve]
            public void SetMusicEnabled(bool val) {
                ToggleCallCount++;
                if (RejectToggle) {
                    // Reject: do not update model
                    return;
                }
                GetModel().musicEnabled.Value = val;
            }

            [UnityEngine.Scripting.Preserve]
            public void SetServerUrl(string val) {
                UrlCallCount++;
                var normalized = val != null ? val.Trim() : string.Empty;
                GetModel().serverUrl.Value = normalized;
            }

            [UnityEngine.Scripting.Preserve]
            public void SetDifficulty(int val) {
                DifficultyCallCount++;
                GetModel().difficulty.Value = val;
            }

            public async void AsyncCommand(float v) => await Task.Yield();
            public Task TaskCommand(float v) => Task.CompletedTask;
            public void WrongParamType(string s) { }
            public void ExtraParam(float v, int x) { }
            private void PrivateCommand(float v) { }
        }

        public class SettingsPresenter : ObservablePresenterWithLogic<SettingsModel, SettingsLogic> {
            [AutoBind(nameof(SettingsModel.musicVolume), Command = nameof(SettingsLogic.SetMusicVolume))]
            public Slider volumeSlider;

            [AutoBind(nameof(SettingsModel.musicEnabled), Command = nameof(SettingsLogic.SetMusicEnabled))]
            public Toggle enabledToggle;

            [AutoBind(nameof(SettingsModel.serverUrl), Command = nameof(SettingsLogic.SetServerUrl))]
            public TMP_InputField urlInput;

            [AutoBind(nameof(SettingsModel.difficulty), Command = nameof(SettingsLogic.SetDifficulty))]
            public TMP_Dropdown difficultyDropdown;

            [AutoBind(nameof(SettingsModel.difficulty), Command = nameof(SettingsLogic.SetDifficulty))]
            public InputField difficultyInput;

            [AutoBind(nameof(SettingsModel.musicVolume))]
            public Slider legacySlider;

            [AutoBind(nameof(SettingsModel.musicVolume), Command = nameof(SettingsLogic.SetInheritedValue))]
            public Slider inheritedSlider;
        }

        public class InvalidLogic : BaseLogic<SettingsModel, InvalidDeclarationsPresenter> {
            public async void AsyncCommand(float v) => await Task.Yield();
            public void WrongParamType(string s) { }
            public void SetMusicVolume(float val) { }
        }

        public class InvalidDeclarationsPresenter : ObservablePresenterWithLogic<SettingsModel, InvalidLogic> {
            [AutoBind(nameof(SettingsModel.musicVolume), Command = nameof(InvalidLogic.SetMusicVolume))]
            public TextMeshProUGUI displayOnlyWithCommand;

            [AutoBind(nameof(SettingsModel.tags), Command = "SetTags")]
            public TMP_InputField listWithCommand;

            [AutoBind(nameof(SettingsModel.musicVolume), Command = "NonExistentCommand")]
            public Slider missingMethodSlider;

            [AutoBind(nameof(SettingsModel.musicVolume), Command = nameof(InvalidLogic.AsyncCommand))]
            public Slider asyncMethodSlider;

            [AutoBind(nameof(SettingsModel.musicVolume), Command = nameof(InvalidLogic.WrongParamType))]
            public Slider wrongParamSlider;
        }

        public class PlainPresenterWithoutLogicTarget : BaseObservablePresenter<SettingsModel> {
            [AutoBind(nameof(SettingsModel.musicVolume), Command = "SomeCommand")]
            public Slider sliderWithoutTarget;
        }

        [SetUp]
        public void SetUp() {
            _host = new GameObject("SettingsHost");
            _host.SetActive(false);
            _model = _host.AddComponent<SettingsModel>();
            _logic = _host.AddComponent<SettingsLogic>();
            _presenter = _host.GetComponent<SettingsPresenter>() ?? _host.AddComponent<SettingsPresenter>();

            GameObject CreateChild(string name) {
                var c = new GameObject(name);
                c.transform.SetParent(_host.transform);
                return c;
            }

            // Setup controls on child objects (Selectable rule: max 1 per GameObject)
            _presenter.volumeSlider = CreateChild("Volume").AddComponent<Slider>();
            _presenter.volumeSlider.minValue = -2f;
            _presenter.volumeSlider.maxValue = 2f;

            _presenter.enabledToggle = CreateChild("Toggle").AddComponent<Toggle>();
            _presenter.urlInput = CreateChild("Input").AddComponent<TMP_InputField>();
            _presenter.difficultyDropdown = CreateChild("Dropdown").AddComponent<TMP_Dropdown>();
            _presenter.difficultyInput = CreateChild("NumericInput").AddComponent<InputField>();
            _presenter.difficultyDropdown.ClearOptions();
            _presenter.difficultyDropdown.AddOptions(new System.Collections.Generic.List<string> { "Easy", "Normal", "Hard", "Expert" });
            _presenter.legacySlider = CreateChild("LegacySlider").AddComponent<Slider>();
            _presenter.inheritedSlider = CreateChild("InheritedSlider").AddComponent<Slider>();

            _model.musicVolume.Value = 0.5f;
            _model.musicEnabled.Value = true;
            _model.serverUrl.Value = "http://localhost";
            _model.difficulty.Value = 1;

            _host.SetActive(true);
        }

        [TearDown]
        public void TearDown() {
            if (_host != null) {
                UnityEngine.Object.DestroyImmediate(_host);
            }
        }

        [Test]
        public void CommandAutoBind_SubclassedNumericInput_EmptyRemainsEditable() {
            _presenter.EnsureInitialized();
            _presenter.difficultyInput.text = "";
            Assert.AreEqual(0, _logic.DifficultyCallCount);
            Assert.AreEqual(1, _model.difficulty.Value);
            Assert.AreEqual("", _presenter.difficultyInput.text);
            _presenter.difficultyInput.text = "2";
            Assert.AreEqual(1, _logic.DifficultyCallCount);
            Assert.AreEqual(2, _model.difficulty.Value);
        }

        [Test]
        public void InitialBind_DoesNotInvokeCommands_SyncsCurrentModelValuesToControls() {
            _presenter.EnsureInitialized();

            Assert.AreEqual(0, _logic.VolumeCallCount);
            Assert.AreEqual(0, _logic.ToggleCallCount);
            Assert.AreEqual(0, _logic.UrlCallCount);
            Assert.AreEqual(0, _logic.DifficultyCallCount);

            Assert.AreEqual(0.5f, _presenter.volumeSlider.value);
            Assert.IsTrue(_presenter.enabledToggle.isOn);
            Assert.AreEqual("http://localhost", _presenter.urlInput.text);
            Assert.AreEqual(1, _presenter.difficultyDropdown.value);
            Assert.AreEqual(0.5f, _presenter.legacySlider.value);
        }

        [Test]
        public void CommandAutoBind_Slider_DispatchesToLogic_AndClampsReconciles() {
            _presenter.EnsureInitialized();

            // User moves slider to 1.8f (exceeds [0, 1] range)
            _presenter.volumeSlider.value = 1.8f;

            Assert.AreEqual(1, _logic.VolumeCallCount);
            Assert.AreEqual(1.0f, _model.musicVolume.Value);
            // Reconciled back to 1.0f on the originating control
            Assert.AreEqual(1.0f, _presenter.volumeSlider.value);
        }

        [Test]
        public void CommandAutoBind_Toggle_LogicRejectsInput_ReconcilesControlToOldValue() {
            _presenter.EnsureInitialized();
            _logic.RejectToggle = true;

            // User clicks toggle to false
            _presenter.enabledToggle.isOn = false;

            Assert.AreEqual(1, _logic.ToggleCallCount);
            // Model was not changed
            Assert.IsTrue(_model.musicEnabled.Value);
            // Control reconciled back to true
            Assert.IsTrue(_presenter.enabledToggle.isOn);
        }

        [Test]
        public void CommandAutoBind_StringInput_NormalizesWithoutNotification_ReconcilesControl() {
            _presenter.EnsureInitialized();

            // Set model to "http://api"
            _model.serverUrl.Value = "http://api";
            Assert.AreEqual("http://api", _presenter.urlInput.text);

            // User enters "  http://api  "
            _presenter.urlInput.text = "  http://api  ";

            Assert.AreEqual(1, _logic.UrlCallCount);
            Assert.AreEqual("http://api", _model.serverUrl.Value);
            // Control reconciled, whitespace stripped
            Assert.AreEqual("http://api", _presenter.urlInput.text);
        }

        [Test]
        public void CommandAutoBind_Dropdown_DispatchesToLogic() {
            _presenter.EnsureInitialized();

            _presenter.difficultyDropdown.value = 3;

            Assert.AreEqual(1, _logic.DifficultyCallCount);
            Assert.AreEqual(3, _model.difficulty.Value);
            Assert.AreEqual(3, _presenter.difficultyDropdown.value);
        }

        [Test]
        public void CommandAutoBind_DirectModelWrite_UpdatesUI_DoesNotInvokeCommand() {
            _presenter.EnsureInitialized();

            _model.musicVolume.Value = 0.8f;

            Assert.AreEqual(0, _logic.VolumeCallCount);
            Assert.AreEqual(0.8f, _presenter.volumeSlider.value);
        }

        [Test]
        public void CommandAutoBind_InheritedCommand_DispatchesCorrectly() {
            _presenter.EnsureInitialized();

            _presenter.inheritedSlider.value = 0.9f;

            Assert.AreEqual(1, _logic.InheritedCallCount);
        }

        [Test]
        public void CommandAutoBind_ExceptionInLogic_LogsOnce_AndReconcilesControl() {
            _presenter.EnsureInitialized();
            _logic.ThrowOnVolume = true;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Command 'SetMusicVolume' on 'SettingsLogic' threw an exception: Simulated volume failure"));

            _presenter.volumeSlider.value = 0.2f;

            // Control reconciled to accepted model value (0.5f)
            Assert.AreEqual(0.5f, _presenter.volumeSlider.value);
        }

        [Test]
        public void LegacyAutoBind_MutatesModelDirectly() {
            _presenter.EnsureInitialized();

            _presenter.legacySlider.value = 0.75f;

            // Direct model write, logic command was not called
            Assert.AreEqual(0, _logic.VolumeCallCount);
            Assert.AreEqual(0.75f, _model.musicVolume.Value);
        }

        [Test]
        public void CommandAutoBind_Rebind_DoesNotDuplicateCommandInvocation() {
            _presenter.EnsureInitialized();

            // Rebind via SetListeners
            Observable.SetListeners(_presenter, _model);

            _presenter.volumeSlider.value = 0.3f;

            Assert.AreEqual(1, _logic.VolumeCallCount);
        }

        [Test]
        public void CommandAutoBind_PresenterDestruction_CleansUpUIListeners() {
            _presenter.EnsureInitialized();

            var slider = _presenter.volumeSlider;
            UnityEngine.Object.DestroyImmediate(_presenter);

            // Changing slider value after presenter destruction should not call logic or throw
            slider.value = 0.1f;
            Assert.AreEqual(0, _logic.VolumeCallCount);
        }

        [Test]
        public void CommandAutoBind_InvalidDeclarations_LogErrorsAndDoNotWireInput() {
            var go = new GameObject("InvalidHost");
            var extraGos = new System.Collections.Generic.List<GameObject>();
            try {
                go.SetActive(false);
                go.AddComponent<SettingsModel>();
                go.AddComponent<InvalidLogic>();
                var p = go.GetComponent<InvalidDeclarationsPresenter>() ?? go.AddComponent<InvalidDeclarationsPresenter>();

                p.displayOnlyWithCommand = go.AddComponent<TextMeshProUGUI>();

                var c1 = new GameObject("C1"); extraGos.Add(c1);
                p.listWithCommand = c1.AddComponent<TMP_InputField>();

                var c2 = new GameObject("C2"); extraGos.Add(c2);
                p.missingMethodSlider = c2.AddComponent<Slider>();

                var c3 = new GameObject("C3"); extraGos.Add(c3);
                p.asyncMethodSlider = c3.AddComponent<Slider>();

                var c4 = new GameObject("C4"); extraGos.Add(c4);
                p.wrongParamSlider = c4.AddComponent<Slider>();

                go.SetActive(true);

                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("declares Command 'SetMusicVolume', but UI component type 'TextMeshProUGUI' does not support input events"));
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("declares Command 'SetTags' for 'tags', but command binding is not supported for ObservableList"));
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Invalid \\[AutoBind\\] Command 'NonExistentCommand'.*Method 'NonExistentCommand' not found"));
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Invalid \\[AutoBind\\] Command 'AsyncCommand'.*is async void"));
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Invalid \\[AutoBind\\] Command 'WrongParamType'.*does not match expected value type"));

                p.EnsureInitialized();
            } finally {
                foreach (var eg in extraGos) {
                    if (eg != null) UnityEngine.Object.DestroyImmediate(eg);
                }
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CommandAutoBind_PlainPresenterWithoutLogic_LogsError() {
            var go = new GameObject("PlainHost");
            try {
                go.SetActive(false);
                go.AddComponent<SettingsModel>();
                var p = go.AddComponent<PlainPresenterWithoutLogicTarget>();
                p.sliderWithoutTarget = go.AddComponent<Slider>();
                go.SetActive(true);

                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("declares Command 'SomeCommand', but presenter does not implement IAutoBindCommandTarget"));

                p.EnsureInitialized();
            } finally {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
