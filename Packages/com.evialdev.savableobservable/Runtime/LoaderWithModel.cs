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
            ComponentAutoRequire.EnsureComponents(this, false, typeof(M));
        }

        protected virtual void OnValidate() {
            ComponentAutoRequire.EnsureComponents(this, true, typeof(M));
        }

        private M _model;

        /// <summary>
        /// Returns the model component on this GameObject (observable fields initialized).
        /// Save systems read the state to persist from it.
        /// </summary>
        public M GetModel() => ComponentCache.GetModel(this, ref _model);

        /// <summary>
        /// Applies a loaded state to the model. Existing presenter bindings are kept: the presenter
        /// observes loaded values through normal change notifications (equal values are silent unless
        /// the field opts in with <see cref="NotifyOnSameValueAttribute"/>).
        /// </summary>
        public virtual void LoadDataFromModel(object state) {
            var model = GetModel();
            if (model == null) {
                Debug.LogError($"[LoaderWithModel] Model of type '{typeof(M).Name}' not found on '{gameObject.name}'.", this);
                return;
            }

            model.LoadDataFromModel(state);
        }
    }
}
