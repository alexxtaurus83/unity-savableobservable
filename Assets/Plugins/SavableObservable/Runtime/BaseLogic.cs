using UnityEngine;

namespace SavableObservable {

    [DisallowMultipleComponent]
    public abstract class BaseLogic<M> : MonoBehaviour  {

        public M GetModel() {
            return GetComponent<M>();
        }
    }
}