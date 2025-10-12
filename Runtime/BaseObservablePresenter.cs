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
        }
    }
}