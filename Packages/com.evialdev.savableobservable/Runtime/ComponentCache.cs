using UnityEngine;

namespace SavableObservable {

    /// <summary>
    /// Shared lazy sibling-component lookup used by presenters, logic and loaders.
    /// </summary>
    internal static class ComponentCache {

        /// <summary>Returns the cached sibling component, resolving it from <paramref name="owner"/> on first use.</summary>
        internal static T Get<T>(Component owner, ref T cache) where T : Component {
            if (cache == null) {
                cache = owner.GetComponent<T>();
            }
            return cache;
        }

        /// <summary>
        /// Returns the cached sibling model, resolving it on first use, and ensures its observable fields are initialized.
        /// </summary>
        internal static M GetModel<M>(Component owner, ref M cache) where M : BaseObservableDataModel {
            var model = Get(owner, ref cache);
            if (model != null) {
                model.EnsureFieldsInitialized();
            }
            return model;
        }
    }
}
