using UnityEngine;

namespace SavableObservable {

    [DisallowMultipleComponent]
    public abstract class BaseObservablePresenter<M> : BasePresenter<M> {

        protected virtual void Start() {
            // This check ensures that a Loader or some other initialization script has called SetListeners.
            // If this error appears, it means the Presenter is in the scene but its event subscriptions
            // have not been set up, and it will not react to Model changes.
            if (!Observable.AreListenersInitialized(this)) {
                Debug.Log($"[SavableObservable] Listeners for {this.GetType().Name} on GameObject '{this.gameObject.name}' were not initialized. Ensure a Loader component is correctly configured to call Observable.SetListeners().", this.gameObject);
            }
        }        
    }
}