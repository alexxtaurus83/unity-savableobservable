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
            // Note: This uses reflection to call the LoadDataFromModel method on the specific model instance.
            // This method is defined in the BaseObservableDataModel class.
            var methodInfo = typeof(M).GetMethod("LoadDataFromModel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (methodInfo == null) {
                var modelType = typeof(M);
                var modelInstance = GetModel();
                var actualRuntimeType = modelInstance?.GetType();
                Debug.LogError($"[LoaderWithModel] Method 'LoadDataFromModel' not found on model type '{modelType.FullName}'. Expected signature: void LoadDataFromModel(object state). Actual runtime type: {(actualRuntimeType != null ? actualRuntimeType.FullName : "null")}");
                return;
            }
            methodInfo.Invoke(GetModel(), new object[] { state });
            
            // Set up listeners AFTER model state is loaded to prevent notifications during load.
            var presenter = GetComponent<BaseObservablePresenter<M>>();
            if (presenter != null) { Observable.SetListeners(presenter); }
        }
    }
}