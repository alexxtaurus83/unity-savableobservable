using UnityEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Assets.Scripts.Base.SavableObservable;

namespace Assets.Scripts.Base.SavableObservable.Examples {

    [Serializable]
    public class PipelineDataModel : BaseObservableDataModel {
        [SerializeReference] public Observable.ObservableString pipelineName;
        [SerializeReference] public Observable.ObservableDouble progress;
        [SerializeReference] public Observable.ObservableBoolean isActive;
        [SerializeReference] public Observable.ObservableInt32 tokenCount;
        
        [SerializeField] public Dictionary<string, int> blockStatuses = new();
        [SerializeField] public ObservableCollection<string> logEntries = new ObservableCollection<string>();
        
        public void AddLogEntry(string entry) {
            logEntries.Add(entry);
        }
    }

    public class PipelineLogic : BaseLogic<PipelineDataModel> {
        private PipelineDataModel _model;
        
        public override void Initialize(PipelineDataModel model) {
            _model = model;
            Extensions.SetListeners(this);
        }

        public void OnModelValueChanged(string previous, string current, string name) {
            if (name == nameof(PipelineDataModel.pipelineName)) {
                Debug.Log($"Pipeline name changed from {previous} to {current}");
            }
        }

        public void OnModelValueChanged(double previous, double current, string name) {
            if (name == nameof(PipelineDataModel.progress)) {
                Debug.Log($"Progress changed from {previous} to {current}");
            }
        }

        public void OnModelValueChanged(bool previous, bool current, string name) {
            if (name == nameof(PipelineDataModel.isActive)) {
                Debug.Log($"Active state changed from {previous} to {current}");
            }
        }

        public void OnModelValueChanged(int previous, int current, string name) {
            if (name == nameof(PipelineDataModel.tokenCount)) {
                Debug.Log($"Token count changed from {previous} to {current}");
            }
        }
    }

    public class PipelinePresenter : BaseObservablePresenter<PipelineDataModel> {
        private PipelineLogic _logic;
        
        public override void Initialize(PipelineDataModel model) {
            _logic = new PipelineLogic();
            _logic.Initialize(model);
        }

        public void UpdateUI() {
            // Update UI elements based on model state
            if (_model != null) {
                Debug.Log($"Updating UI for pipeline: {_model.pipelineName.Value}");
            }
        }
    }

    public class PipelineLoader : BaseLoader {
        public PipelineDataModel PipelineData { get; private set; }
        public PipelineLogic PipelineLogic { get; private set; }
        public PipelinePresenter PipelinePresenter { get; private set; }

        private void Awake() {
            // Create instances
            PipelineData = new PipelineDataModel();
            PipelineLogic = new PipelineLogic();
            PipelinePresenter = new PipelinePresenter();

            // Initialize pipeline
            PipelineData.InitFields();
            PipelineLogic.Initialize(PipelineData);
            PipelinePresenter.Initialize(PipelineData);
        }

        private void Update() {
            // Example of updating the pipeline
            if (Input.GetKeyDown(KeyCode.Space)) {
                PipelineData.pipelineName.Value = "New Pipeline Name";
                PipelineData.progress.Value += 0.1f;
                PipelineData.tokenCount.Value++;
            }
        }
    }
}
