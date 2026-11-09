using System;
using UnityEngine;

namespace SavableObservable {

    /// <summary>
    /// Non-generic root class for all MMVC Logic components.
    /// Automatically implements <see cref="ISharedSingleton"/> with an optional serialized <see cref="singletonName"/>
    /// used when multiple instances of the same Logic class exist in a scene.
    /// </summary>
    [DisallowMultipleComponent]
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

        /// <summary>
        /// Controls whether this Logic component automatically registers with <see cref="ServiceLocator"/> on Awake.
        /// Derived multi-instance logic classes (such as pooled or child units) can override this to return false.
        /// </summary>
        protected virtual bool RegisterAsSharedSingleton => true;

        internal bool ShouldRegisterAsSharedSingleton => RegisterAsSharedSingleton;

        protected virtual void Awake() {
            if (RegisterAsSharedSingleton && ServiceLocator.instance != null) {
                ServiceLocator.instance.TryRegister(this);
            }
        }

        protected virtual void OnDestroy() {
            if (ServiceLocator.instance != null) {
                ServiceLocator.instance.Unregister(this);
            }
        }

        /// <summary>Exact model type of this Logic (used by <see cref="ServiceLocator.GetModel{M}"/> to filter before resolving).</summary>
        internal abstract Type ModelType { get; }

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
            ComponentAutoRequire.EnsureComponents(this, false, typeof(M), typeof(P));
        }

        protected virtual void OnValidate() {
            ComponentAutoRequire.EnsureComponents(this, true, typeof(M), typeof(P));
        }

        private M _model;
        private P _presenter;

        private M ResolveAndInitializeModel() => ComponentCache.GetModel(this, ref _model);

        /// <summary>
        /// Returns the model on this GameObject and makes sure the paired presenter is initialized and bound
        /// (idempotent, also for inactive GameObjects), so model writes always reach the view.
        /// </summary>
        public M GetModel() {
            var model = ResolveAndInitializeModel();
            if (model != null) {
                ComponentCache.Get(this, ref _presenter)?.EnsureInitialized();
            }
            return model;
        }

        internal override Type ModelType => typeof(M);

        /// <summary>
        /// Used by <see cref="ServiceLocator.GetModel{M}"/> to resolve the model
        /// without triggering Presenter initialization.
        /// </summary>
        internal override BaseObservableDataModel GetUntypedModel() => ResolveAndInitializeModel();
    }
}
