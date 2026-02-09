using UnityEngine;

namespace SavableObservable {

    public abstract class LoaderWithModelAndLogic<M, LO> : LoaderWithModel<M> 
    {
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
