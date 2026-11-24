using System;
using System.Collections.Generic;
using UnityEngine;

namespace SavableObservable {

    /// <summary>
    /// Owns a group of subscription handles (<see cref="ObservableVariable{T}.Subscribe"/>, <see cref="ObservableList{T}.Subscribe"/>,
    /// <see cref="ObservableEvent{T}.Subscribe"/>, <c>Services.Subscribe</c>) and disposes them together.
    /// Typical use: subscribe in <c>OnEnable</c>, <see cref="Clear"/> in <c>OnDisable</c>. Hold it in an instance field, never a static.
    /// </summary>
    public sealed class SubscriptionSet : IDisposable {
        private readonly List<IDisposable> _items = new List<IDisposable>();
        private bool _isDisposed;

        /// <summary>Number of handles currently owned.</summary>
        public int Count => _items.Count;

        /// <summary>Takes ownership of <paramref name="subscription"/>. Ignores <c>null</c>; throws after <see cref="Dispose"/>.</summary>
        public void Add(IDisposable subscription) {
            if (_isDisposed) throw new ObjectDisposedException(nameof(SubscriptionSet));
            if (subscription == null) return;
            _items.Add(subscription);
        }

        /// <summary>Disposes every owned handle (newest first) and keeps the set open for new ones. Idempotent.</summary>
        public void Clear() {
            if (_isDisposed) return;
            for (int i = _items.Count - 1; i >= 0; i--) {
                var item = _items[i];
                try {
                    item.Dispose();
                } catch (Exception ex) {
                    Debug.LogException(ex);
                }
            }
            _items.Clear();
        }

        /// <summary>Disposes every owned handle and closes the set; a later <see cref="Add"/> throws. Idempotent.</summary>
        public void Dispose() {
            if (_isDisposed) return;
            Clear();
            _isDisposed = true;
        }
    }

    /// <summary>Single-use disposable handle: runs its unsubscribe action on the first <see cref="Dispose"/> only.</summary>
    internal sealed class Subscription : IDisposable {
        private Action _unsubscribe;

        public Subscription(Action unsubscribe) {
            _unsubscribe = unsubscribe;
        }

        public void Dispose() {
            var unsubscribe = _unsubscribe;
            _unsubscribe = null;
            unsubscribe?.Invoke();
        }
    }
}
