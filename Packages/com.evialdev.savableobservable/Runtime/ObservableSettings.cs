using UnityEngine;
using Unity.Scripting.LifecycleManagement;

namespace SavableObservable {

    /// <summary>
    /// Global switches for the binding engine.
    /// </summary>
    public static partial class ObservableSettings {

        [AutoStaticsCleanup]
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

        // Stored inverted so that the default value of the field (what [AutoStaticsCleanup] resets it to) is the default behavior: replay on.
        [AutoStaticsCleanup]
        private static bool _syncOnBindDisabled;

        /// <summary>
        /// When <c>true</c> (default), <see cref="Observable.SetListeners(object, BaseObservableDataModel)"/> replays the current model
        /// state right after binding, so a presenter does not paint initial values by hand:
        /// every <c>[AutoBind]</c> target receives the current value (two-way controls use their "without notify" path, so nothing is
        /// written back to the model), and every <c>[ObservableHandler(..., InvokeOnBind = true)]</c> method is called once with the
        /// current value. Handlers without <c>InvokeOnBind</c> are never replayed (sounds, tweens and other side effects stay safe).
        /// Set to <c>false</c> to get the pre-2.0 behavior (bind only; nothing is replayed, including <c>InvokeOnBind</c> handlers).
        /// Resets to <c>true</c> on every domain / subsystem start; change it from a bootstrap or test.
        /// </summary>
        public static bool SyncOnBind {
            get => !_syncOnBindDisabled;
            set => _syncOnBindDisabled = !value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnSubsystemRegistration() {
            _strictBinding = false;
            _syncOnBindDisabled = false;
        }
    }
}
