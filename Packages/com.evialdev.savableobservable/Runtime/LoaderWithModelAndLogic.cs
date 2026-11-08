using UnityEngine;

namespace SavableObservable {

    /// <summary>
    /// Base loader component to load and save model state with logic access.
    /// Generic parameter <typeparamref name="M"/> must derive from <see cref="BaseObservableDataModel"/>.
    /// Generic parameter <typeparamref name="LO"/> must derive from <see cref="BaseLogic"/>.
    /// </summary>
    public abstract class LoaderWithModelAndLogic<M, LO> : LoaderWithModel<M> 
        where M : BaseObservableDataModel
        where LO : BaseLogic
    {
        protected override void Reset() {
            base.Reset();
            ComponentAutoRequire.EnsureComponent<LO>(this, delayed: false);
        }

        protected override void OnValidate() {
            base.OnValidate();
            ComponentAutoRequire.EnsureComponent<LO>(this, delayed: true);
        }

        private LO _logic;

        public LO GetLogic() {
            if (_logic == null) {
                _logic = GetComponent<LO>();
            }
            return _logic;
        }
    }
}
