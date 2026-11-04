using UnityEngine;

namespace SavableObservable {

    /// <summary>
    /// Base class for reactive presenters that pair with a logic component.
    /// Generic parameter <typeparamref name="M"/> must derive from <see cref="BaseObservableDataModel"/>.
    /// Generic parameter <typeparamref name="LO"/> must derive from <see cref="BaseLogic"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class ObservablePresenterWithLogic<M, LO> : BaseObservablePresenter<M>
        where M : BaseObservableDataModel
        where LO : BaseLogic {

        private LO _logic;

        public LO GetLogic() {
            if (_logic == null) {
                _logic = GetComponent<LO>();
            }
            return _logic;
        }
    }
}
