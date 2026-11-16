using System;
using UnityEngine;
using SavableObservable;

namespace SavableObservable.Samples.CommandSettings {

    /// <summary>
    /// Observable model holding audio and network configuration.
    /// In production, fields can be serialized or loaded through a LoaderWithModel component.
    /// </summary>
    [Serializable]
    public class CommandSettingsModel : BaseObservableDataModel {
        [SerializeField] public ObservableVariable<float> musicVolume = new ObservableVariable<float>();
        [SerializeField] public ObservableVariable<bool> soundEnabled = new ObservableVariable<bool>();
        [SerializeField] public ObservableVariable<string> serverAddress = new ObservableVariable<string>();
        [SerializeField] public ObservableVariable<int> difficultyLevel = new ObservableVariable<int>();

        public void InitializeDefaults() {
            musicVolume.Value = 0.8f;
            soundEnabled.Value = true;
            serverAddress.Value = "https://api.game.example.com";
            difficultyLevel.Value = 1;
        }
    }
}
