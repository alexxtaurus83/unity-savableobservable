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
