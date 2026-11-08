using UnityEngine;

namespace SavableObservable {

    /// <summary>
    /// Base class for presenters managing a data model.
    /// Generic parameter <typeparamref name="M"/> must derive from <see cref="BaseObservableDataModel"/>.
    /// Non-observable models and interfaces (e.g. IModel) are not supported.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class BasePresenter<M> : MonoBehaviour where M : BaseObservableDataModel {
        private M _model;

        public M GetModel() {
            if (_model == null) {
                _model = GetComponent<M>();
            }
            _model?.EnsureFieldsInitialized();
            return _model;
        }
    }
}
