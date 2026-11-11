using System.Collections.Generic;
using UnityEngine;
using Unity.Scripting.LifecycleManagement;

namespace SavableObservable {

    /// <summary>
    /// Internal registry that tracks live presenters to guarantee cleanup of their allocated
    /// ListenerSets and subscriptions even when derived classes shadow <c>OnDestroy()</c>.
    /// </summary>
    [AutoStaticsCleanup]
    internal static partial class PresenterLifetimeTracker {
        private static readonly List<IPresenterBindingState> _trackedPresenters = new List<IPresenterBindingState>();
        private static readonly object _lock = new object();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() {
            lock (_lock) {
                _trackedPresenters.Clear();
            }
        }

        public static void Track(IPresenterBindingState presenter) {
            if (presenter == null) return;
            lock (_lock) {
                if (!_trackedPresenters.Contains(presenter)) {
                    _trackedPresenters.Add(presenter);
                }
            }
        }

        public static void Untrack(IPresenterBindingState presenter) {
            if (presenter == null) return;
            lock (_lock) {
                _trackedPresenters.Remove(presenter);
            }
        }

        /// <summary>
        /// Opportunistically sweeps tracked presenters that were destroyed, cleaning their lifetime state.
        /// </summary>
        public static void SweepDeadPresenters() {
            lock (_lock) {
                for (int i = _trackedPresenters.Count - 1; i >= 0; i--) {
                    var p = _trackedPresenters[i];
                    if (p is UnityEngine.Object uo && uo == null) {
                        p.CleanupPresenterLifetime();
                        _trackedPresenters.RemoveAt(i);
                    }
                }
            }
        }
    }
}
