using UnityEngine;

namespace SavableObservable {

    [DisallowMultipleComponent]
    public class ObservablePresenterWithLogic<M, LO> : BaseObservablePresenter<M> {        

        public LO GetLogic() {
            return GetComponent<LO>();
        }             
    }        
}