using UnityEngine;

namespace SavableObservable {

    /// <summary>
    /// Base loader component to load and save model state.
    /// Generic parameter <typeparamref name="M"/> must derive from <see cref="BaseObservableDataModel"/>.
    /// Non-observable models and interfaces (e.g. IModel) are not supported.
    /// </summary>
    public abstract class LoaderWithModel<M> : MonoBehaviour where M : BaseObservableDataModel
    {
        protected virtual void Reset() {
            ComponentAutoRequire.EnsureComponent<M>(this);
        }

        protected virtual void OnValidate() {
            ComponentAutoRequire.EnsureComponent<M>(this);
        }

        private M _model;

        public M GetModel() {
            if (_model == null) {
                _model = GetComponent<M>();
            }
            return _model;
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
            var model = GetModel();
            if (model == null) {
                Debug.LogError($"[LoaderWithModel] Model of type '{typeof(M).Name}' not found on '{gameObject.name}'.", this);
                return;
            }

            model.LoadDataFromModel(state);
            
            // Set up listeners AFTER model state is loaded to prevent notifications during load.
            var presenter = GetComponent<BaseObservablePresenter<M>>();
            if (presenter != null) {
                Observable.SetListeners(presenter, model);
            }
        }
    }
}