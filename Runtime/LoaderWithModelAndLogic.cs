using System.Linq;
using UnityEditor;
using UnityEngine;


namespace SavableObservable {

    public abstract class LoaderWithModelAndLogic<M, LO> : BaseLoader<M> {

        public LO GetLogic() {
            return GetComponent<LO>();
        }

        public M GetModel() {
            return GetComponent<M>();
        }
    }
}