using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
using Unity.Scripting.LifecycleManagement;
#endif

namespace SavableObservable {
    /// <summary>
    /// Non-generic companion for <see cref="ObservableTrackedAction{T}"/> providing deterministic
    /// main-thread capture at subsystem registration and domain load.
    /// </summary>
    public static class ObservableTrackedAction {
#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT
        [NoAutoStaticsCleanup]
#endif
        private static int _mainThreadId = -1;

        static ObservableTrackedAction() {
            CaptureMainThread();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void CaptureMainThread() {
            _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        internal static int MainThreadId {
            get {
                if (_mainThreadId == -1) {
                    CaptureMainThread();
                }
                return _mainThreadId;
            }
        }

        internal static bool IsMainThread => System.Threading.Thread.CurrentThread.ManagedThreadId == MainThreadId;
    }

    /// <summary>
    /// A custom action implementation that automatically tracks subscriptions for memory management.
    /// <para>
    /// Thread-safety contract: All operations (Add, Remove, Invoke) must be called from the Unity main thread.
    /// This class uses a lightweight runtime assertion to enforce this contract. It does not lock;
    /// violations will log an error in player builds and throw in development builds.
    /// </para>
    /// </summary>
    public partial class ObservableTrackedAction<T> where T : class, IObservableVariable {
        private readonly List<Action<T>> _handlers = new List<Action<T>>();
        private readonly Dictionary<Action<T>, object> _handlerSubscribers = new Dictionary<Action<T>, object>();
        private Action<T>[] _handlersArray = Array.Empty<Action<T>>();
        private bool _isInvoking;
        private bool _pending;

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
        /// The observable that owns this action (set together with <see cref="ParentDataModel"/>).
        /// Lets the model's subscription registry remove a handler again without reflection.
        /// </summary>
        internal IObservableVariable Owner { get; set; }

        /// <summary>
        /// Adds a handler to the tracked action. When the action belongs to a model and a
        /// <paramref name="subscriber"/> is given, the subscription is recorded so it is removed with
        /// <c>Observable.RemoveListeners</c> and when the model is destroyed.
        /// </summary>
        public void Add(Action<T> handler, object subscriber) {
            Add(handler, subscriber, managed: false);
        }

        /// <summary>
        /// Adds a handler; <paramref name="managed"/> marks framework-created bindings
        /// (<c>[ObservableHandler]</c> / <c>[AutoBind]</c>) that <c>Observable.SetListeners</c> replaces on rebind,
        /// while manual subscriptions (<c>managed == false</c>) survive a rebind.
        /// </summary>
        internal void Add(Action<T> handler, object subscriber, bool managed) {
            AssertMainThread();
            if (_handlers.Contains(handler)) return;

            _handlers.Add(handler);
            _handlerSubscribers[handler] = subscriber;
            _handlersArray = _handlers.ToArray();

            if (ParentDataModel != null && Owner != null && subscriber != null) {
                Observable.RegisterSubscription(ParentDataModel, subscriber, Owner, handler, managed);
            }
        }

        /// <summary>
        /// Removes a handler from the tracked action.
        /// </summary>
        public void Remove(Action<T> handler) {
            AssertMainThread();
            if (!_handlers.Remove(handler)) return;

            _handlerSubscribers.TryGetValue(handler, out var subscriber);
            _handlerSubscribers.Remove(handler);
            _handlersArray = _handlers.Count > 0 ? _handlers.ToArray() : Array.Empty<Action<T>>();

            if (ParentDataModel != null && subscriber != null) {
                Observable.UnregisterSubscription(ParentDataModel, subscriber, handler);
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
                    var handlers = _handlersArray;
                    for (int i = 0; i < handlers.Length; i++) {
                        var handler = handlers[i];
                        if (handler == null) continue;

                        // Dead subscriber check:
                        // 1) Explicit tracked subscriber key check (handles closures/lambdas where subscriber was passed as a MonoBehaviour/Object)
                        // 2) Direct handler.Target check (for instance methods on UnityEngine.Object)
                        bool isDeadSubscriber = false;
                        if (_handlerSubscribers.TryGetValue(handler, out var sub) && sub is UnityEngine.Object unitySub && unitySub == null) {
                            isDeadSubscriber = true;
                        } else if (handler.Target is UnityEngine.Object unityTarget && unityTarget == null) {
                            isDeadSubscriber = true;
                        }

                        if (isDeadSubscriber) {
                            Remove(handler);
                            continue;
                        }

                        try {
                            handler.Invoke(variable);
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
                return _handlers.Count;
            }
        }

        /// <summary>
        /// Asserts that the current thread is the Unity main thread.
        /// Captures the main thread ID on startup / first use on the main thread.
        /// Logs an error in player builds and throws in development builds when violated.
        /// </summary>
        private static void AssertMainThread() {
            int currentThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            int mainThreadId = ObservableTrackedAction.MainThreadId;

            // Check if we're on the main thread
            if (currentThreadId != mainThreadId) {
                string message = $"[SavableObservable] ObservableTrackedAction<{typeof(T).Name}> accessed from non-main thread (thread {currentThreadId}). All ObservableTrackedAction operations must be called from the Unity main thread.";
                
#if UNITY_EDITOR || DEBUG || UNITY_ENABLE_CHECKS
                throw new InvalidOperationException(message);
#else
                if (Debug.isDebugBuild) {
                    throw new InvalidOperationException(message);
                }
                Debug.LogError(message);
#endif
            }
        }
    }
}
