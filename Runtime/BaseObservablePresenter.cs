using UnityEngine;

namespace Assets.Scripts.Base.SavableObservable {

    [DisallowMultipleComponent]
    public class BaseObservablePresenter<M> : MonoBehaviour {
        
        public M GetModel() {
            return GetComponent<M>();
        }            
    }        
}