using UnityEngine;

namespace SavableObservable {

    public abstract class LoaderWithModelAndLogic<M, LO> : LoaderWithModel<M> 
    {
        public LO GetLogic() {
            return GetComponent<LO>();
        }
    }
}