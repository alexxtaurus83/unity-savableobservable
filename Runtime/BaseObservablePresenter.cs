using UnityEngine;

namespace SavableObservable {

    [DisallowMultipleComponent]
    public abstract class BaseObservablePresenter<M> : BasePresenter<M> {

        protected virtual void Start() {
            if (!Observable.AreListenersInitialized(this)) {
                Debug.LogWarning($"[SavableObservable] Listeners for {this.GetType().Name} on '{this.gameObject.name}' were not initialized. " +
                    $"If this is a singleton, ensure it's registered with the GameManager. " +
                    $"If it's dynamically instantiated, ensure its initialization logic calls Observable.SetListeners().",
                    this.gameObject);
            }
            
            // Validate that this presenter is not sharing the same model with another presenter
            ValidateUniqueModelAssociation();
        }
        
        private void ValidateUniqueModelAssociation()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var model = GetModel();
            if (model != null)
            {
                var presenters = FindObjectsOfType<BaseObservablePresenter<M>>();
                foreach(var presenter in presenters)
                {
                    if(presenter != this && presenter.GetModel() != null && presenter.GetModel() == model)
                    {
                        Debug.LogError($"[SavableObservable] Multiple presenters sharing the same model detected! This violates the one-presenter-per-model constraint.");
                        UnityEngine.Debug.LogError("Application will quit due to constraint violation.");
                        UnityEngine.Application.Quit();
#if UNITY_EDITOR
                        UnityEditor.EditorApplication.ExitPlaymode();
#endif
                        break;
                    }
                }
            }
#endif
        }
    }
}