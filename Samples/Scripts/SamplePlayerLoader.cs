using System;
using System.Collections.Generic;
using UnityEngine;
using SavableObservable;

namespace SavableObservable.Samples {
    /// <summary>
    /// Serializable DTO for saving/loading SamplePlayer state.
    /// </summary>
    [Serializable]
    public class SampleSaveDto {
        public string playerName;
        public int health;
        public float energy;
        public bool isShieldActive;
        public List<string> inventory = new List<string>();
        public int score;
    }

    /// <summary>
    /// Sample loader demonstrating state persistence and roundtrip restoration.
    /// Derives from LoaderWithModelAndLogic&lt;SamplePlayerModel, SamplePlayerLogic&gt;.
    /// </summary>
    [DisallowMultipleComponent]
    public class SamplePlayerLoader : LoaderWithModelAndLogic<SamplePlayerModel, SamplePlayerLogic> {
        public SampleSaveDto SaveCurrentState() {
            var model = GetModel();
            if (model == null) return null;

            var dto = new SampleSaveDto {
                playerName = model.playerName?.Value,
                health = model.health != null ? model.health.Value : 0,
                energy = model.energy != null ? model.energy.Value : 0f,
                isShieldActive = model.isShieldActive != null && model.isShieldActive.Value,
                inventory = model.inventory != null ? new List<string>(model.inventory.Value) : new List<string>(),
                score = model.score
            };
            return dto;
        }

        public void LoadSampleState(SampleSaveDto dto) {
            var model = GetModel();
            if (model == null || dto == null) return;

            if (model.playerName != null) model.playerName.Value = dto.playerName;
            if (model.health != null) model.health.Value = dto.health;
            if (model.energy != null) model.energy.Value = dto.energy;
            if (model.isShieldActive != null) model.isShieldActive.Value = dto.isShieldActive;
            if (model.inventory != null && dto.inventory != null) {
                model.inventory.Clear();
                model.inventory.AddRange(dto.inventory);
            }
            model.score = dto.score;

            var presenter = GetComponent<BaseObservablePresenter<SamplePlayerModel>>();
            if (presenter != null) {
                Observable.SetListeners(presenter, model);
            }
        }
    }
}
