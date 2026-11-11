using System;
using System.Collections.Generic;
using UnityEngine.Events;

namespace SavableObservable {

    /// <summary>
    /// Scoped container that manages UnityEvent listener registrations and guarantees safe, owned cleanup.
    /// Deduplicates identical listeners, removes only registrations owned by this set, and preserves persistent Inspector listeners.
    /// </summary>
    public sealed class ListenerSet : IDisposable {

        private abstract class Entry {
            public object Source { get; }
            public Delegate Handler { get; }

            protected Entry(object source, Delegate handler) {
                Source = source;
                Handler = handler;
            }

            public abstract void Unregister();
        }

        private sealed class ZeroArgEntry : Entry {
            public UnityEvent Event => (UnityEvent)Source;
            public UnityAction Action => (UnityAction)Handler;

            public ZeroArgEntry(UnityEvent evt, UnityAction action) : base(evt, action) { }

            public override void Unregister() {
                try {
                    Event?.RemoveListener(Action);
                } catch {
                    // Suppress exceptions from destroyed targets
                }
            }
        }

        private sealed class OneArgEntry<T> : Entry {
            public UnityEvent<T> Event => (UnityEvent<T>)Source;
            public UnityAction<T> Action => (UnityAction<T>)Handler;

            public OneArgEntry(UnityEvent<T> evt, UnityAction<T> action) : base(evt, action) { }

            public override void Unregister() {
                try {
                    Event?.RemoveListener(Action);
                } catch {
                    // Suppress exceptions from destroyed targets
                }
            }
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private bool _isDisposed;

        internal int Count => _entries.Count;

        /// <summary>
        /// Registers a listener for a parameterless UnityEvent.
        /// Deduplicates the same event and equal delegate within this set.
        /// Ignores null event or handler arguments.
        /// </summary>
        public void Listen(UnityEvent source, UnityAction handler) {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ListenerSet));
            if (source == null || handler == null) return;

            for (int i = 0; i < _entries.Count; i++) {
                var entry = _entries[i];
                if (ReferenceEquals(entry.Source, source) && Equals(entry.Handler, handler)) {
                    return;
                }
            }

            source.AddListener(handler);
            _entries.Add(new ZeroArgEntry(source, handler));
        }

        /// <summary>
        /// Registers a listener for a single-argument UnityEvent.
        /// Deduplicates the same event and equal delegate within this set.
        /// Ignores null event or handler arguments.
        /// </summary>
        public void Listen<T>(UnityEvent<T> source, UnityAction<T> handler) {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ListenerSet));
            if (source == null || handler == null) return;

            for (int i = 0; i < _entries.Count; i++) {
                var entry = _entries[i];
                if (ReferenceEquals(entry.Source, source) && Equals(entry.Handler, handler)) {
                    return;
                }
            }

            source.AddListener(handler);
            _entries.Add(new OneArgEntry<T>(source, handler));
        }

        /// <summary>
        /// Removes all listeners owned by this set while keeping the set open for subsequent registrations.
        /// Idempotent.
        /// </summary>
        public void Clear() {
            if (_isDisposed) return;
            for (int i = _entries.Count - 1; i >= 0; i--) {
                _entries[i].Unregister();
            }
            _entries.Clear();
        }

        /// <summary>
        /// Rolls back registrations added after the specified checkpoint index.
        /// </summary>
        internal void RollbackTo(int snapshotCount) {
            if (_isDisposed) return;
            if (snapshotCount < 0) snapshotCount = 0;
            for (int i = _entries.Count - 1; i >= snapshotCount; i--) {
                _entries[i].Unregister();
                _entries.RemoveAt(i);
            }
        }

        /// <summary>
        /// Removes all listeners and permanently closes this set.
        /// Subsequent calls to <see cref="Listen"/> will throw <see cref="ObjectDisposedException"/>.
        /// Idempotent.
        /// </summary>
        public void Dispose() {
            if (_isDisposed) return;
            Clear();
            _isDisposed = true;
        }
    }
}
