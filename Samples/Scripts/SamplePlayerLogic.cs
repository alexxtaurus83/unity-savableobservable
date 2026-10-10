using System;
using UnityEngine;
using SavableObservable;

namespace SavableObservable.Samples {
    /// <summary>
    /// Sample business logic manipulating SamplePlayerModel state.
    /// Derives from BaseLogic&lt;SamplePlayerModel&gt;.
    /// </summary>
    [DisallowMultipleComponent]
    public class SamplePlayerLogic : BaseLogic<SamplePlayerModel> {
        public void SetPlayerName(string newName) {
            var model = GetModel();
            if (model != null) {
                model.playerName.Value = newName;
            }
        }

        public void TakeDamage(int amount) {
            var model = GetModel();
            if (model != null) {
                int currentHp = model.health.Value;
                int actualDamage = model.isShieldActive.Value ? amount / 2 : amount;
                model.health.Value = Mathf.Max(0, currentHp - actualDamage);
            }
        }

        public void Heal(int amount) {
            var model = GetModel();
            if (model != null) {
                int currentHp = model.health.Value;
                model.health.Value = Mathf.Min(100, currentHp + amount);
            }
        }

        public void SetEnergy(float value) {
            var model = GetModel();
            if (model != null) {
                model.energy.Value = Mathf.Clamp01(value);
            }
        }

        public void ToggleShield() {
            var model = GetModel();
            if (model != null) {
                model.isShieldActive.Value = !model.isShieldActive.Value;
            }
        }

        public void AddInventoryItem(string item) {
            var model = GetModel();
            if (model != null && !string.IsNullOrEmpty(item)) {
                model.inventory.Add(item);
            }
        }

        public void RemoveInventoryItem(string item) {
            var model = GetModel();
            if (model != null && !string.IsNullOrEmpty(item)) {
                model.inventory.Remove(item);
            }
        }

        public void ClearInventory() {
            var model = GetModel();
            if (model != null) {
                model.inventory.Clear();
            }
        }

        public void AddScore(int points) {
            var model = GetModel();
            if (model != null) {
                model.score += points;
            }
        }
    }
}
