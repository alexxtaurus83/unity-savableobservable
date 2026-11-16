using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SavableObservable;

namespace SavableObservable.Samples.CommandSettings {

    /// <summary>
    /// Reactive presenter binding UI controls to CommandSettingsModel and CommandSettingsLogic.
    /// Demonstrates command-aware AutoBind, [ClickCommand], [ObservableHandler], and scoped Listeners.
    /// </summary>
    public class CommandSettingsPresenter : ObservablePresenterWithLogic<CommandSettingsModel, CommandSettingsLogic> {

        [Header("Command-Aware AutoBind Controls")]
        [SerializeField]
        [AutoBind(nameof(CommandSettingsModel.musicVolume), Command = nameof(CommandSettingsLogic.SetMusicVolume))]
        private Slider volumeSlider;

        [SerializeField]
        [AutoBind(nameof(CommandSettingsModel.soundEnabled), Command = nameof(CommandSettingsLogic.SetSoundEnabled))]
        private Toggle soundToggle;

        [SerializeField]
        [AutoBind(nameof(CommandSettingsModel.serverAddress), Command = nameof(CommandSettingsLogic.SetServerAddress))]
        private TMP_InputField addressInput;

        [SerializeField]
        [AutoBind(nameof(CommandSettingsModel.difficultyLevel), Command = nameof(CommandSettingsLogic.SetDifficultyLevel))]
        private TMP_Dropdown difficultyDropdown;

        [Header("Legacy Direct AutoBind Control")]
        [SerializeField]
        [AutoBind(nameof(CommandSettingsModel.difficultyLevel))]
        private Slider legacyDifficultySlider;

        [Header("Action Buttons")]
        [SerializeField]
        [ClickCommand(nameof(CommandSettingsLogic.ResetToDefaults))]
        private Button resetButton;

        [SerializeField]
        private Button mutePresetButton;

        [Header("Custom Presentation")]
        [SerializeField]
        private TextMeshProUGUI volumePercentLabel;

        public int VolumeHandlerCallCount { get; private set; }

        [ObservableHandler(nameof(CommandSettingsModel.musicVolume), InvokeOnBind = true)]
        private void OnVolumeChanged(float volume) {
            VolumeHandlerCallCount++;
            if (volumePercentLabel != null) {
                volumePercentLabel.text = $"{Mathf.RoundToInt(volume * 100f)}%";
            }
        }

        protected override void InitializePresenter() {
            base.InitializePresenter();

            // Generic scoped UnityEvent listener for argument-taking actions
            if (mutePresetButton != null) {
                Listeners.Listen(
                    mutePresetButton.onClick,
                    () => GetLogic().ApplyPreset("MuteAll"));
            }
        }

        // Test inspection accessors
        public Slider VolumeSlider { get => volumeSlider; set => volumeSlider = value; }
        public Toggle SoundToggle { get => soundToggle; set => soundToggle = value; }
        public TMP_InputField AddressInput { get => addressInput; set => addressInput = value; }
        public TMP_Dropdown DifficultyDropdown { get => difficultyDropdown; set => difficultyDropdown = value; }
        public Slider LegacyDifficultySlider { get => legacyDifficultySlider; set => legacyDifficultySlider = value; }
        public Button ResetButton { get => resetButton; set => resetButton = value; }
        public Button MutePresetButton { get => mutePresetButton; set => mutePresetButton = value; }
        public TextMeshProUGUI VolumePercentLabel { get => volumePercentLabel; set => volumePercentLabel = value; }
    }
}
