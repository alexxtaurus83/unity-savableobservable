using System;
using System.Collections.Generic;
using UnityEngine;

namespace SavableObservable {
    /// <summary>
    /// A custom action implementation that automatically tracks subscriptions for memory management.
    /// <para>
    /// Thread-safety contract: All operations (Add, Remove, Invoke) must be called from the Unity main thread.
    /// This class uses a lightweight runtime assertion to enforce this contract. No locking is provided for
    /// cross-thread access; violations will log an error in player builds and throw in development builds.
    /// </para>
    /// </summary>
    public class ObservableTrackedAction<T> where T : class, IObservableVariable {
        private List<Action<T>> _handlers = new List<Action<T>>();
        private readonly object _lock = new object();
        private bool _isInvoking;
        private bool _pending;

        // Main thread assertion support
        private static int _mainThreadId = -1;
        private static readonly object _mainThreadInitLock = new object();

        public static ObservableTrackedAction<T> operator +(ObservableTrackedAction<T> trackedAction, Action<T> handler) {
            if (trackedAction == null) {
                trackedAction = new ObservableTrackedAction<T>();
            }
            trackedAction.Add(handler, null);
            return trackedAction;
        }

        public static ObservableTrackedAction<T> operator -(ObservableTrackedAction<T> trackedAction, Action<T> handler) {
            trackedAction?.Remove(handler);
            return trackedAction;
        }

        /// <summary>
        /// The parent data model for cleanup purposes.
        /// </summary>
        internal BaseObservableDataModel ParentDataModel { get; set; }

        /// <summary>
        /// Adds a handler to the tracked action.
        /// </summary>
        public void Add(Action<T> handler, object subscriber) {
            lock (_lock) {
                if (!_handlers.Contains(handler)) {
                    _handlers.Add(handler);

                    // Register this subscription for automatic cleanup if possible
                    if (ParentDataModel != null && subscriber != null) {
                        Observable.RegisterSubscription(ParentDataModel, subscriber, handler);
                    }
                }
            }
        }

        /// <summary>
        /// Removes a handler from the tracked action.
        /// </summary>
        public void Remove(Action<T> handler) {
            lock (_lock) {
                _handlers.Remove(handler);
                // Notify Observable to unregister the subscription for cleanup
                if (ParentDataModel != null) {
                    Observable.UnregisterSubscription(ParentDataModel, null, handler);
                }
            }
        }

        /// <summary>
        /// Invokes all registered handlers.
        /// Must be called from the Unity main thread.
        /// </summary>
        public void Invoke(T variable) {
            AssertMainThread();

            // Re-entrant call: mark pending and return; outer loop will re-invoke
            if (_isInvoking) {
                _pending = true;
                return;
            }

            const int maxIterations = 32;
            int iterations = 0;

            // Ensure at least one invocation pass runs; repeat if re-entrant calls set _pending
            do {
                // Clear pending before starting this pass
                _pending = false;

                _isInvoking = true;
                try {
                    Action<T>[] handlersCopy;
                    lock (_lock) {
                        handlersCopy = new Action<T>[_handlers.Count];
                        _handlers.CopyTo(handlersCopy);
                    }

                    foreach (var handler in handlersCopy) {
                        try {
                            handler?.Invoke(variable);
                        } catch (Exception ex) {
                            UnityEngine.Debug.LogException(ex);
                        }
                    }
                } finally {
                    _isInvoking = false;
                }

                iterations++;
                if (iterations >= maxIterations) {
                    UnityEngine.Debug.LogError($"ObservableTrackedAction.Invoke exceeded maxIterations ({maxIterations}); stopping to prevent infinite loop.");
                    break;
                }
            } while (_pending);
        }

        /// <summary>
        /// Gets the count of registered handlers.
        /// Must be called from the Unity main thread.
        /// </summary>
        public int HandlerCount {
            get {
                AssertMainThread();
                lock (_lock) {
                    return _handlers.Count;
                }
            }
        }

        /// <summary>
        /// Asserts that the current thread is the Unity main thread.
        /// Captures the main thread ID on first use.
        /// Logs an error in player builds and throws in development builds when violated.
        /// </summary>
        private static void AssertMainThread() {
            int currentThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

            // Capture main thread ID on first use
            if (_mainThreadId == -1) {
                lock (_mainThreadInitLock) {
                    if (_mainThreadId == -1) {
                        _mainThreadId = currentThreadId;
                        return; // First call is always considered valid
                    }
                }
            }

            // Check if we're on the main thread
            if (currentThreadId != _mainThreadId) {
                string message = $"[SavableObservable] ObservableTrackedAction<{typeof(T).Name}> accessed from non-main thread (thread {currentThreadId}). All ObservableTrackedAction operations must be called from the Unity main thread.";
                
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                throw new InvalidOperationException(message);
#else
                Debug.LogError(message);
#endif
            }
        }
    }
}
