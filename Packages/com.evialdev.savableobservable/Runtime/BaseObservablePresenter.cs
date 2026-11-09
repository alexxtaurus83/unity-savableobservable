using UnityEngine;

namespace SavableObservable {

    /// <summary>
    /// Reactive presenter for a unit's model. Binds <c>[AutoBind]</c> / <c>[ObservableHandler]</c> members
    /// to the model on the same GameObject and exposes a strongly-typed <see cref="GetModel"/>.
    /// Generic parameter <typeparamref name="M"/> must derive from <see cref="BaseObservableDataModel"/>;
    /// non-observable models and interfaces (e.g. IModel) are not supported.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-150)]
    public abstract class BaseObservablePresenter<M> : MonoBehaviour, IObservablePresenter, IPresenterBindingState where M : BaseObservableDataModel {
        private enum InitState {
            Uninitialized,
            Initializing,
            Initialized
        }

        private M _model;
        private InitState _state = InitState.Uninitialized;
        private bool _isBound;

        /// <summary>
        /// Returns the model component on this GameObject (observable fields initialized).
        /// </summary>
        public M GetModel() => ComponentCache.GetModel(this, ref _model);

        protected virtual void Awake() {
            EnsureInitialized();
        }

        /// <summary>
        /// Initializes observable fields on the model, runs <see cref="InitializePresenter"/> once,
        /// and attaches <see cref="Observable.SetListeners(object, BaseObservableDataModel)"/>.
        /// Idempotent no-op once bound, so repeated calls never rebind or duplicate handlers.
        /// Safe when called early by <see cref="ServiceLocator"/> on active or inactive GameObjects.
        /// </summary>
        public void EnsureInitialized() {
            if (_state == InitState.Initialized && _isBound) return;

            // Nested call during InitializePresenter() (e.g. via logic.GetModel()): the outer frame binds
            // after initialization completes, so listeners are never attached before InitializePresenter() finishes.
            if (_state == InitState.Initializing) return;

            var model = GetModel();
            RunInitializePresenter();
            if (model != null && !_isBound) {
                Observable.SetListeners(this, model);
                _isBound = true;
            }
        }

        /// <summary>
        /// Override to configure static UI controls (slider min/max, dropdown options, button listeners,
        /// initial CanvasGroup state). Guaranteed to run once BEFORE <see cref="Observable.SetListeners"/>
        /// and AFTER <see cref="ServiceLocator"/> has registered all scene <see cref="ISharedSingleton"/> services.
        /// An exception thrown here does not latch: the next <see cref="EnsureInitialized"/> retries.
        /// </summary>
        protected virtual void InitializePresenter() { }

        // Runs InitializePresenter once. Guards against recursive reentrancy without permanently latching on unhandled exceptions.
        private void RunInitializePresenter() {
            if (_state == InitState.Initialized || _state == InitState.Initializing) return;

            _state = InitState.Initializing;
            try {
                GetModel();
                InitializePresenter();
                _state = InitState.Initialized;
            } catch {
                _state = InitState.Uninitialized;
                throw;
            }
        }

        void IPresenterBindingState.EnsureInitializedForBinding() {
            RunInitializePresenter();
        }

        void IPresenterBindingState.MarkBound() {
            _isBound = true;
        }

        void IPresenterBindingState.MarkUnbound() {
            _isBound = false;
        }

        public BaseObservableDataModel GetObservableModel() {
            return GetModel();
        }
    }
}
