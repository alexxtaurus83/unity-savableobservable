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
            ComponentAutoRequire.EnsureComponents(this, false, typeof(M), typeof(LO));
        }

        protected override void OnValidate() {
            ComponentAutoRequire.EnsureComponents(this, true, typeof(M), typeof(LO));
        }

        private LO _logic;

        public LO GetLogic() => ComponentCache.Get(this, ref _logic);
    }
}
