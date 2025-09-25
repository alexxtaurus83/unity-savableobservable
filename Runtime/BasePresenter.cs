using UnityEngine;

namespace SavableObservable {

    [DisallowMultipleComponent]
    public abstract class BasePresenter<M> : MonoBehaviour {
        public M GetModel()  {
            return GetComponent<M>();
        }        
    }
}     