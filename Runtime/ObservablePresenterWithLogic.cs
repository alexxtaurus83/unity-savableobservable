using UnityEngine;

namespace Assets.Scripts.Base.SavableObservable {

    [DisallowMultipleComponent]
    public class ObservablePresenterWithLogic<M, LO> : BaseObservablePresenter<M> {        

        public LO GetLogic() {
            return GetComponent<LO>();
        }             
    }        
}