using UnityEngine;

namespace SavableObservable {

    [DisallowMultipleComponent]
    public abstract class BaseObservablePresenter<M> : MonoBehaviour {
        
        public M GetModel() {
            return GetComponent<M>();
        }

        /// <summary>
        /// This method will be automatically subscribed to all ObservableVariable changes in the model.
        /// It must be implemented by any concrete presenter class.
        /// </summary>
        public abstract void OnModelValueChanged(IObservableVariable variable);
    }
}