using UnityEngine;

namespace SavableObservable {

    /// <summary>
    /// Base class for business logic managing a data model.
    /// Generic parameter <typeparamref name="M"/> must derive from <see cref="BaseObservableDataModel"/>.
    /// Non-observable models and interfaces (e.g. IModel) are not supported.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class BaseLogic<M> : MonoBehaviour where M : BaseObservableDataModel {
        protected virtual void Reset() {
            ComponentAutoRequire.EnsureComponent<M>(this);
        }

        protected virtual void OnValidate() {
            ComponentAutoRequire.EnsureComponent<M>(this);
        }

        public M GetModel() {
            return GetComponent<M>();
        }
    }
}
