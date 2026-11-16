using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SavableObservable.Samples.CommandSettings;

namespace SavableObservable.Tests {

    [TestFixture]
    [Category("MmvcContract")]
    public class CommandSettingsContractTests {

        private GameObject _host;
        private CommandSettingsModel _model;
        private CommandSettingsLogic _logic;
        private CommandSettingsPresenter _presenter;

        [SetUp]
        public void SetUp() {
            _host = new GameObject("CommandSettingsHost");
            _host.SetActive(false);

            _model = _host.AddComponent<CommandSettingsModel>();
            _model.InitializeDefaults();

            _logic = _host.AddComponent<CommandSettingsLogic>();
            _presenter = _host.GetComponent<CommandSettingsPresenter>() ?? _host.AddComponent<CommandSettingsPresenter>();

            GameObject CreateChild(string name) {
                var c = new GameObject(name);
                c.transform.SetParent(_host.transform);
                return c;
            }

            _presenter.VolumeSlider = CreateChild("VolumeSlider").AddComponent<Slider>();
            _presenter.VolumeSlider.minValue = -1f;
            _presenter.VolumeSlider.maxValue = 2f;

            _presenter.SoundToggle = CreateChild("SoundToggle").AddComponent<Toggle>();
            _presenter.AddressInput = CreateChild("AddressInput").AddComponent<TMP_InputField>();

            _presenter.DifficultyDropdown = CreateChild("DifficultyDropdown").AddComponent<TMP_Dropdown>();
            _presenter.DifficultyDropdown.ClearOptions();
            _presenter.DifficultyDropdown.AddOptions(new List<string> { "Easy", "Normal", "Hard", "Expert" });

            _presenter.LegacyDifficultySlider = CreateChild("LegacyDifficultySlider").AddComponent<Slider>();
            _presenter.LegacyDifficultySlider.minValue = 0f;
            _presenter.LegacyDifficultySlider.maxValue = 3f;
            _presenter.ResetButton = CreateChild("ResetButton").AddComponent<Button>();
            _presenter.MutePresetButton = CreateChild("MutePresetButton").AddComponent<Button>();
            _presenter.VolumePercentLabel = CreateChild("VolumePercentLabel").AddComponent<TextMeshProUGUI>();

            _host.SetActive(true);
            _presenter.EnsureInitialized();
        }

        [TearDown]
        public void TearDown() {
            if (_host != null) {
                UnityEngine.Object.DestroyImmediate(_host);
            }
        }

        [Test]
        public void Sample_InitialBind_SyncsModelToControlsAndRunsHandler() {
            Assert.AreEqual(0.8f, _presenter.VolumeSlider.value);
            Assert.IsTrue(_presenter.SoundToggle.isOn);
            Assert.AreEqual("https://api.game.example.com", _presenter.AddressInput.text);
            Assert.AreEqual(1, _presenter.DifficultyDropdown.value);
            Assert.AreEqual("80%", _presenter.VolumePercentLabel.text);
            Assert.AreEqual(1, _presenter.VolumeHandlerCallCount);
        }

        [Test]
        public void Sample_CommandBoundSlider_ClampsInputAndReconciles() {
            _presenter.VolumeSlider.value = 1.5f;

            // Clamped by logic to 1.0f
            Assert.AreEqual(1.0f, _model.musicVolume.Value);
            Assert.AreEqual(1.0f, _presenter.VolumeSlider.value);
            Assert.AreEqual("100%", _presenter.VolumePercentLabel.text);
        }

        [Test]
        public void Sample_CommandBoundToggle_UpdatesModel() {
            _presenter.SoundToggle.isOn = false;

            Assert.IsFalse(_model.soundEnabled.Value);
            Assert.IsFalse(_presenter.SoundToggle.isOn);
        }

        [Test]
        public void Sample_CommandBoundInput_NormalizesWhitespaceAndReconciles() {
            _presenter.AddressInput.text = "   https://new.api.example.com   ";

            Assert.AreEqual("https://new.api.example.com", _model.serverAddress.Value);
            Assert.AreEqual("https://new.api.example.com", _presenter.AddressInput.text);
        }

        [Test]
        public void Sample_ClickCommand_ResetsAllFieldsToDefaults() {
            _model.musicVolume.Value = 0.2f;
            _model.soundEnabled.Value = false;
            _model.serverAddress.Value = "https://custom.api";
            _model.difficultyLevel.Value = 3;

            _presenter.ResetButton.onClick.Invoke();

            Assert.AreEqual(0.8f, _model.musicVolume.Value);
            Assert.IsTrue(_model.soundEnabled.Value);
            Assert.AreEqual("https://api.game.example.com", _model.serverAddress.Value);
            Assert.AreEqual(1, _model.difficultyLevel.Value);

            Assert.AreEqual(0.8f, _presenter.VolumeSlider.value);
            Assert.IsTrue(_presenter.SoundToggle.isOn);
            Assert.AreEqual("https://api.game.example.com", _presenter.AddressInput.text);
            Assert.AreEqual(1, _presenter.DifficultyDropdown.value);
        }

        [Test]
        public void Sample_ScopedListeners_ArgumentTakingButton_AppliesPreset() {
            _presenter.MutePresetButton.onClick.Invoke();

            Assert.AreEqual(0f, _model.musicVolume.Value);
            Assert.IsFalse(_model.soundEnabled.Value);
            Assert.AreEqual(0f, _presenter.VolumeSlider.value);
            Assert.IsFalse(_presenter.SoundToggle.isOn);
            Assert.AreEqual("0%", _presenter.VolumePercentLabel.text);
        }

        [Test]
        public void Sample_LegacyAutoBind_MutatesModelDirectly() {
            _presenter.LegacyDifficultySlider.value = 2;

            Assert.AreEqual(2, _model.difficultyLevel.Value);
            Assert.AreEqual(2, _presenter.DifficultyDropdown.value);
        }
    }
}
