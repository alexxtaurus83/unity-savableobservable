using UnityEngine;

namespace SavableObservable {

    [DisallowMultipleComponent]
    public class BaseObservablePresenter<M> : MonoBehaviour {
        
        public M GetModel() {
            return GetComponent<M>();
        }            
    }        
}