using UnityEngine;

namespace SavableObservable {

    [DisallowMultipleComponent]
    public abstract class ObservablePresenterWithLogic<M, LO> : BaseObservablePresenter<M> {
        protected override void Reset() {
            base.Reset();
            ComponentAutoRequire.EnsureComponent<LO>(this);
        }

        protected override void OnValidate() {
            base.OnValidate();
            ComponentAutoRequire.EnsureComponent<LO>(this);
        }

        public LO GetLogic() {
            return GetComponent<LO>();
        }
    }
}
