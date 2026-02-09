using UnityEngine;

namespace SavableObservable {

    [DisallowMultipleComponent]
    public abstract class BasePresenter<M> : MonoBehaviour {
        protected virtual void Reset() {
            ComponentAutoRequire.EnsureComponent<M>(this);
        }

        protected virtual void OnValidate() {
            ComponentAutoRequire.EnsureComponent<M>(this);
        }

        public M GetModel()  {
            return GetComponent<M>();
        }
    }
}
