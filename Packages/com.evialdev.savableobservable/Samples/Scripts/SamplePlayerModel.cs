using System;
using UnityEngine;
using SavableObservable;

namespace SavableObservable.Samples {
    /// <summary>
    /// Sample data model demonstrating SavableObservable reactive primitives.
    /// Derives from BaseObservableDataModel.
    /// </summary>
    [Serializable]
    public class SamplePlayerModel : BaseObservableDataModel {
        [SerializeField] public ObservableVariable<string> playerName = new ObservableVariable<string>();
        [SerializeField] public ObservableVariable<int> health = new ObservableVariable<int>();
        [SerializeField] public ObservableVariable<float> energy = new ObservableVariable<float>();
        [SerializeField] public ObservableVariable<bool> isShieldActive = new ObservableVariable<bool>();
        [SerializeField] public ObservableList<string> inventory = new ObservableList<string>();
        [SerializeField] public int score;

        // Every assignment notifies, even when the value equals the current one (a repeated roll is still an event).
        // The handler can tell the cases apart through its `previous` argument.
        [NotifyOnSameValue] public ObservableVariable<int> lastRoll = new ObservableVariable<int>();

        public void InitializeDefaults() {
            playerName.Value = "Player";
            health.Value = 100;
            energy.Value = 1.0f;
            isShieldActive.Value = false;
            inventory.Clear();
            inventory.Add("Potion");
            score = 0;
        }
    }
}
