using UnityEngine;
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
using Unity.Scripting.LifecycleManagement;
#endif

namespace SavableObservable {

    /// <summary>
    /// Global switches for the binding engine.
    /// </summary>
    public static partial class ObservableSettings {

#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [AutoStaticsCleanup]
#endif
        private static bool _strictBinding;

        /// <summary>
        /// When <c>true</c>, <see cref="Observable.SetListeners(object, BaseObservableDataModel)"/> logs a warning for every
        /// observable field of the model that its subscriber handles neither with an <c>[ObservableHandler]</c> method nor an
        /// <c>[AutoBind]</c> field. Default: <c>false</c> (a presenter is free to ignore fields it does not display).
        /// A <c>[AutoBind]</c> that names a field the model does not have is always reported, regardless of this switch.
        /// Resets to <c>false</c> on every domain / subsystem start; enable it from a bootstrap or test.
        /// </summary>
        public static bool StrictBinding {
            get => _strictBinding;
            set => _strictBinding = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnSubsystemRegistration() {
            _strictBinding = false;
        }
    }
}
