using UnityEngine;

namespace SavableObservable {

    /// <summary>
    /// Base class for reactive presenters managing an observable data model.
    /// Generic parameter <typeparamref name="M"/> must derive from <see cref="BaseObservableDataModel"/>.
    /// Non-observable models and interfaces (e.g. IModel) are not supported.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class BaseObservablePresenter<M> : BasePresenter<M>, IObservablePresenter where M : BaseObservableDataModel {

        /// <summary>
        /// Gets the model as <see cref="BaseObservableDataModel"/>.
        /// </summary>
        public BaseObservableDataModel GetObservableModel() {
            return GetModel();
        }
    }
}