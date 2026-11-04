using UnityEngine;

namespace SavableObservable {

    /// <summary>
    /// Non-generic root class for all MMVC Logic components.
    /// Automatically implements <see cref="ISharedSingleton"/> with an optional serialized <see cref="singletonName"/>
    /// used when multiple instances of the same Logic class exist in a scene.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(0)]
    public abstract class BaseLogic : MonoBehaviour, ISharedSingleton {
        [Header("Shared Singleton Identity")]
        [SerializeField] protected string singletonName;

        /// <summary>
        /// Registration key used by <see cref="ServiceLocator"/>.
        /// Returns <see cref="singletonName"/> when assigned in the Inspector, or the concrete Logic class name otherwise.
        /// Never uses <c>gameObject.name</c>.
        /// </summary>
        public virtual string Name => !string.IsNullOrEmpty(singletonName) ? singletonName : GetType().Name;

        /// <summary>Sets the singleton registration name (used by tests or programmatic setup before registration).</summary>
        public void SetSingletonName(string name) {
            singletonName = name;
        }

        protected virtual void Awake() {
            if (ServiceLocator.instance != null) {
                ServiceLocator.instance.TryRegister(this);
            }
        }

        protected virtual void OnDestroy() {
            if (ServiceLocator.instance != null) {
                ServiceLocator.instance.Unregister(this);
            }
        }

        internal abstract BaseObservableDataModel GetUntypedModel();
    }

    /// <summary>
    /// Primary MMVC component managing business logic for model <typeparamref name="M"/> and paired presenter <typeparamref name="P"/>.
    /// Adding <see cref="BaseLogic{M, P}"/> to a GameObject in the Unity Editor automatically provisions both
    /// <typeparamref name="M"/> and <typeparamref name="P"/> on the same GameObject.
    /// Exposes <see cref="GetModel"/> and intentionally never exposes the presenter so logic never calls presenter methods directly.
    /// </summary>
    public abstract class BaseLogic<M, P> : BaseLogic
        where M : BaseObservableDataModel
        where P : BaseObservablePresenter<M> {

        protected virtual void Reset() {
            ComponentAutoRequire.EnsureComponent<M>(this, delayed: false);
            ComponentAutoRequire.EnsureComponent<P>(this, delayed: false);
        }

        protected virtual void OnValidate() {
            ComponentAutoRequire.EnsureComponent<M>(this, delayed: true);
            ComponentAutoRequire.EnsureComponent<P>(this, delayed: true);
        }

        protected override void Awake() {
            base.Awake();
        }

        private M _model;
        private bool _presenterInitialized;

        private M ResolveAndInitializeModel() {
            if (_model == null) {
                _model = GetComponent<M>();
            }
            _model?.EnsureFieldsInitialized();
            return _model;
        }

        public M GetModel() {
            var model = ResolveAndInitializeModel();
            if (!_presenterInitialized && model != null) {
                var presenter = GetComponent<P>();
                if (presenter != null) {
                    _presenterInitialized = true;
                    try {
                        presenter.EnsureInitialized();
                    } catch {
                        _presenterInitialized = false;
                        throw;
                    }
                }
            }
            return model;
        }

        /// <summary>
        /// Used by <see cref="ServiceLocator"/> during Pass 1 registration to index the model
        /// without prematurely triggering Presenter initialization before all services are registered.
        /// </summary>
        internal override BaseObservableDataModel GetUntypedModel() => ResolveAndInitializeModel();
    }
}
