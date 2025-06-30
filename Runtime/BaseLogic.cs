using UnityEngine;

namespace Assets.Scripts.Base.SavableObservable {

    [DisallowMultipleComponent]
    public abstract class BaseLogic<M> : MonoBehaviour  {

        public M GetModel() {
            return GetComponent<M>();
        }
    }
}