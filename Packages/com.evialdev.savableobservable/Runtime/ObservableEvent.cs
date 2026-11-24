using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SavableObservable {

    /// <summary>Non-generic marker for <see cref="ObservableEvent{T}"/> (binding and persistence checks without generic reflection).</summary>
    internal interface IObservableEvent { }

    /// <summary>
    /// One-shot notification on a model: Logic calls <see cref="Fire"/>, the paired Presenter reacts in an
    /// <c>[ObservableHandler]</c> (0 parameters, or 1 parameter of type <typeparamref name="T"/>).
    /// Replaces counter/request tokens (<c>[NonSaved][NotifyOnSameValue] ObservableVariable&lt;int&gt;</c> + <c>Value++</c>).
    /// <list type="bullet">
    /// <item>Never replayed on bind: an <c>InvokeOnBind = true</c> handler is a bind-time error, and <c>SyncOnBind</c> skips it.</item>
    /// <item>Never saved: excluded from <c>LoadDataFromModel</c> and <see cref="BaseObservableDataModel.GetSavableObservableFields"/>; not serialized.</item>
    /// <item>Handlers run on a snapshot of the listener list; a throwing handler is logged and the others still run.</item>
    /// <item>A <see cref="Fire"/> from inside a handler is queued and delivered after the current one (at most 32 in a row).</item>
    /// </list>
    /// Use it for a Logic -> own Presenter notification on one GameObject; use the event bus (<c>Services.Publish</c>) between units.
    /// </summary>
    public sealed class ObservableEvent<T> : IObservableVariable, IObservableEvent {
        private const int MaxQueuedFires = 32;

        private ObservableTrackedAction<ObservableEvent<T>> _onFired;
        private Queue<T> _pending;
        private bool _isFiring;
        private T _current;

        /// <summary>Tracked listener list (same ownership and cleanup rules as <see cref="ObservableVariable{T}.OnValueChanged"/>).</summary>
        public ObservableTrackedAction<ObservableEvent<T>> OnFired => _onFired ??= new ObservableTrackedAction<ObservableEvent<T>>();

        /// <summary>Notifies every listener once with <paramref name="value"/>. Nothing is stored after the call.</summary>
        public void Fire(T value) {
            if (_isFiring) {
                (_pending ??= new Queue<T>()).Enqueue(value);
                return;
            }

            _isFiring = true;
            try {
                _current = value;
                _onFired?.Invoke(this);

                int fired = 0;
                while (_pending != null && _pending.Count > 0) {
                    if (++fired > MaxQueuedFires) {
                        _pending.Clear();
                        Debug.LogError($"[SavableObservable] ObservableEvent<{typeof(T).Name}> exceeded {MaxQueuedFires} nested Fire calls; the remaining ones were dropped.");
                        break;
                    }
                    _current = _pending.Dequeue();
                    _onFired.Invoke(this);
                }
            } finally {
                _isFiring = false;
                _pending?.Clear();
                _current = default;
            }
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> and returns a handle whose <c>Dispose</c> removes it (idempotent).
        /// Never called on subscribe: an event has no current value.
        /// </summary>
        public IDisposable Subscribe(Action<T> handler) {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            Action<ObservableEvent<T>> wrapper = e => handler(e._current);
            var action = OnFired;
            action.Add(wrapper, null);
            return new Subscription(() => action.Remove(wrapper));
        }

        /// <summary>Fires with <c>default(T)</c>.</summary>
        public void ForceNotify() => Fire(default);

        public void SetParentDataModel(BaseObservableDataModel dataModel) {
            OnFired.ParentDataModel = dataModel;
            OnFired.Owner = this;
        }

        public override string ToString() => $"ObservableEvent<{typeof(T).Name}>";

        Delegate IObservableVariable.CreateHandler(object target, MethodInfo method, int paramCount) {
            switch (paramCount) {
                case 0: {
                    var act = (Action)Delegate.CreateDelegate(typeof(Action), target, method);
                    Action<ObservableEvent<T>> handler = _ => act();
                    return handler;
                }
                case 1: {
                    try {
                        var act = (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), target, method);
                        Action<ObservableEvent<T>> handler = e => act(e._current);
                        return handler;
                    } catch {
                        Action<ObservableEvent<T>> handler = e => method.Invoke(target, new object[] { e._current });
                        return handler;
                    }
                }
                default:
                    return null;
            }
        }

        Delegate IObservableVariable.CreateAutoBindHandler(Action<object> modelToUiHandler) {
            Action<ObservableEvent<T>> handler = e => modelToUiHandler(e._current);
            return handler;
        }

        void IObservableVariable.AddHandler(Delegate handler, object subscriber) {
            OnFired.Add((Action<ObservableEvent<T>>)handler, subscriber, managed: true);
        }

        void IObservableVariable.RemoveHandler(Delegate handler) {
            OnFired.Remove((Action<ObservableEvent<T>>)handler);
        }

        // Never replayed: an event has no current state to push on bind.
        void IObservableVariable.InvokeHandler(Delegate handler) { }

#if UNITY_EDITOR
        void IObservableVariable.OnBeginGui() { }
        void IObservableVariable.OnValidate() { }
        void IObservableVariable.ClearEditorGuiSnapshot() { }
#endif
    }
}
