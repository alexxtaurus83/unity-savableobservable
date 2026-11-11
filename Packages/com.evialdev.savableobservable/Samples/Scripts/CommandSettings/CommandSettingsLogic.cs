using System;
using UnityEngine;
using UnityEngine.Scripting;
using SavableObservable;

namespace SavableObservable.Samples.CommandSettings {

    /// <summary>
    /// Business logic governing settings validation, clamping, and command handling.
    /// Note: Production logic would save to PlayerPrefs, disk, or a remote profile after modifying model state.
    /// Mark reflection-invoked command methods with [Preserve] to prevent IL2CPP bytecode stripping.
    /// </summary>
    public class CommandSettingsLogic : BaseLogic<CommandSettingsModel, CommandSettingsPresenter> {

        [Preserve]
        public void SetMusicVolume(float rawValue) {
            // Validation & clamping: volume must be strictly between 0.0 and 1.0
            var clamped = Mathf.Clamp01(rawValue);
            GetModel().musicVolume.Value = clamped;
            // Production: SaveSettingsToDisk();
        }

        [Preserve]
        public void SetSoundEnabled(bool enabled) {
            GetModel().soundEnabled.Value = enabled;
            // Production: ApplyAudioMixerMute(enabled);
        }

        [Preserve]
        public void SetServerAddress(string rawAddress) {
            // Normalization: strip whitespace
            var normalized = rawAddress != null ? rawAddress.Trim() : string.Empty;
            GetModel().serverAddress.Value = normalized;
        }

        [Preserve]
        public void SetDifficultyLevel(int level) {
            GetModel().difficultyLevel.Value = Mathf.Clamp(level, 0, 3);
        }

        [Preserve]
        public void ResetToDefaults() {
            GetModel().InitializeDefaults();
        }

        public void ApplyPreset(string presetName) {
            // Example of argument-taking action invoked via Listeners.Listen
            if (presetName == "MuteAll") {
                GetModel().musicVolume.Value = 0f;
                GetModel().soundEnabled.Value = false;
            }
        }
    }
}
