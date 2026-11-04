using UnityEngine;

namespace SavableObservable {
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-150)]
    public abstract class BaseObservablePresenter<M> : BasePresenter<M>, IObservablePresenter where M : BaseObservableDataModel {
        private enum InitState {
            Uninitialized,
            Initializing,
            Initialized
        }

        private InitState _initState = InitState.Uninitialized;
        private bool _isBound;

        protected virtual void Awake() {
            EnsureInitialized();
        }

        /// <summary>
        /// Runs <see cref="InitializePresenter"/> once if not already completed.
        /// Guards against recursive reentrancy without permanently latching on unhandled exceptions.
        /// Called automatically by <see cref="EnsureInitialized"/> and <see cref="Observable.SetListeners(object, BaseObservableDataModel)"/>.
        /// </summary>
        public void EnsurePresenterInitialized() {
            if (_initState == InitState.Initialized || _initState == InitState.Initializing) return;

            _initState = InitState.Initializing;
            try {
                GetModel();
                InitializePresenter();
                _initState = InitState.Initialized;
            } catch {
                _initState = InitState.Uninitialized;
                throw;
            }
        }

        /// <summary>
        /// Initializes observable fields on the model, runs <see cref="InitializePresenter"/> once,
        /// and attaches <see cref="Observable.SetListeners(object, BaseObservableDataModel)"/>.
        /// Idempotent no-op when called repeatedly after successful binding, avoiding repeated destructive rebinds.
        /// Explicit calls to <see cref="Observable.SetListeners(object, BaseObservableDataModel)"/> (e.g. from loaders)
        /// retain their explicit rebind behavior.
        /// Safe when called early by <see cref="ServiceLocator"/> on active or inactive GameObjects.
        /// </summary>
        public void EnsureInitialized() {
            if (_initState == InitState.Initialized && _isBound) return;

            var model = GetModel();
            EnsurePresenterInitialized();
            if (model != null && !_isBound) {
                Observable.SetListeners(this, model);
                _isBound = true;
            }
        }

        /// <summary>
        /// Override to configure static UI controls (slider min/max, dropdown options, button listeners,
        /// initial CanvasGroup state). Guaranteed to run once BEFORE <see cref="Observable.SetListeners"/>
        /// and AFTER <see cref="ServiceLocator"/> has registered all scene <see cref="ISharedSingleton"/> services.
        /// </summary>
        protected virtual void InitializePresenter() { }

        public BaseObservableDataModel GetObservableModel() {
            return GetModel();
        }
    }
}
