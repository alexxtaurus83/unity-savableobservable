using UnityEngine;

namespace SavableObservable {

    public abstract class LoaderWithModel<M> : MonoBehaviour 
    {
        protected virtual void Reset() {
            ComponentAutoRequire.EnsureComponent<M>(this);
        }

        protected virtual void OnValidate() {
            ComponentAutoRequire.EnsureComponent<M>(this);
        }

        public M GetModel() {
            return GetComponent<M>();
        }

        /// <summary>
        /// Returns the model component to be used by a save system.
        /// </summary>
        public M GetModelToSave() {
            return GetModel();
        }

        /// <summary>
        /// Applies a loaded state to the model and sets up observable event listeners.
        /// </summary>
        public virtual void LoadDataFromModel(object state) {
            var presenter = GetComponent<BaseObservablePresenter<M>>();
            if (presenter != null) { Observable.SetListeners(presenter); }
            
            // Note: This uses reflection to call the LoadDataFromModel method on the specific model instance.
            // This method is defined in the BaseObservableDataModel class.
            typeof(M).GetMethod("LoadDataFromModel").Invoke(GetModel(), new object[] { state });
        }
    }
}