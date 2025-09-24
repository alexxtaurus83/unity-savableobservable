#if UNITY_EDITOR // This directive prevents the example script from being included in a build

using UnityEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SavableObservable;
 
namespace SavableObservable.Examples {
 
    [Serializable]
    public class PipelineDataModel : BaseObservableDataModel {
        [SerializeReference] public Observable.ObservableString pipelineName = new("pipelineName");
        [SerializeReference] public Observable.ObservableDouble progress = new("progress");
        [SerializeReference] public Observable.ObservableBoolean isActive = new("isActive");
        [SerializeReference] public Observable.ObservableInt32 tokenCount = new("tokenCount");
        
        public void AddLogEntry(string entry) {
            logEntries.Add(entry);
        }
    }
 
    // Logic class no longer needs to handle OnModelValueChanged,
    // as this responsibility is now enforced on the Presenter.
    public class PipelineLogic : BaseLogic<PipelineDataModel> {
        
        public void StartPipeline() {
            if (!GetModel().isActive.GetValue()) {
                GetModel().isActive.Value = true;
                Debug.Log("Pipeline started!");
            }
        }

        public void StopPipeline() {
            if (GetModel().isActive.GetValue()) {
                GetModel().isActive.Value = false;
                Debug.Log("Pipeline stopped!");
            }
        }
    }
 
    public class PipelinePresenter : BaseObservablePresenter<PipelineDataModel> {
        // The 'override' keyword is now required because the base class method is abstract.
        // This ensures at compile time that the Presenter will handle model changes.
        public override void OnModelValueChanged(IObservableVariable variable) {
            Debug.Log($"Presenter received change from '{variable.Name}'");

            if (variable.Name == nameof(PipelineDataModel.pipelineName)) {
                // Example: Update a Text component with the new name
                // myTextComponent.text = ((Observable.ObservableString)variable).GetValue();
                Debug.Log($"UI would be updated with new pipeline name: {variable.GetValueAsObject()}");
            }

            if (variable.Name == nameof(PipelineDataModel.isActive)) {
                Debug.Log($"UI would be updated with new active state: {variable.GetValueAsObject()}");
            }
        }
    }
 
    // This component would be attached to a GameObject in the scene,
    // along with PipelineDataModel, PipelineLogic, and PipelinePresenter.
    public class PipelineLoader : LoaderWithModelAndLogic<PipelineDataModel, PipelineLogic> {
 
        private void Start() {
            // The BaseLoader will automatically handle finding the components
            // and the SavableObservable system will call SetListeners on the Presenter.
            // We can initialize some default values here.
            GetModel().pipelineName.Value = "Initial Pipeline";
            GetModel().progress.Value = 0.0;
            GetModel().tokenCount.Value = 10;
        }
 
        private void Update() {
            // Example of updating the pipeline's data model
            if (Input.GetKeyDown(KeyCode.Space)) {
                GetModel().pipelineName.Value = "Updated Pipeline Name " + Time.frameCount;
            }
            if (Input.GetKeyDown(KeyCode.UpArrow)) {
                GetModel().progress.Value += 0.1;
            }
            if (Input.GetKeyDown(KeyCode.RightArrow)) {
                GetModel().tokenCount.Value++;
            }
            if (Input.GetKeyDown(KeyCode.S)) {
                // Example of calling logic
                GetLogic().StartPipeline();
            }
        }
    }
}

#endif // UNITY_EDITOR