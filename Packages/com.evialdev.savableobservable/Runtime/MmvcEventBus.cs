using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SavableObservable {

    /// <summary>
    /// Typed event bus owned by the scene's <see cref="ServiceLocator"/> (an instance, never a static: a new locator starts empty).
    /// Use it through <c>Services.Publish</c>, <c>Services.Subscribe</c>, <c>Services.Unsubscribe</c> and <see cref="EventHandlerAttribute"/>.
    /// <list type="bullet">
    /// <item>Publishing does not allocate: one channel per event type holds an array snapshot of its handlers.</item>
    /// <item>Handlers run in subscription order, which is not a contract: events carry order-independent notifications only.</item>
    /// <item>A handler that throws is logged; the others still run. Handlers of destroyed Unity subscribers are pruned.</item>
    /// <item>A publish from inside a handler is queued and delivered after the current dispatch (at most 32 per outer publish; more are dropped with an error).</item>
    /// </list>
    /// Rules: Logic and services may publish and subscribe; presenters may only subscribe; models do neither.
    /// </summary>
    public sealed class MmvcEventBus {
        private const int MaxQueuedPublishes = 32;

        private readonly Dictionary<Type, IEventChannel> _channels = new Dictionary<Type, IEventChannel>();
        private readonly Queue<IEventChannel> _pending = new Queue<IEventChannel>();
        private bool _dispatching;
        private int _queuedInDispatch;

        /// <summary>Delivers <paramref name="evt"/> to every handler of <typeparamref name="TEvent"/>.</summary>
        public void Publish<TEvent>(in TEvent evt) where TEvent : struct, IMmvcEvent {
            // No subscriber ever bound to this type: nothing to deliver, no channel created.
            if (!_channels.TryGetValue(typeof(TEvent), out var untyped)) return;
            var channel = (EventChannel<TEvent>)untyped;

            if (_dispatching) {
                if (_queuedInDispatch >= MaxQueuedPublishes) {
                    Debug.LogError($"[MmvcEventBus] More than {MaxQueuedPublishes} events were published from event handlers during one dispatch; '{typeof(TEvent).Name}' was dropped. Check for handlers that publish in a loop.");
                    return;
                }
                _queuedInDispatch++;
                channel.Enqueue(evt);
                _pending.Enqueue(channel);
                return;
            }

            _dispatching = true;
            _queuedInDispatch = 0;
            try {
                channel.Dispatch(in evt);
                while (_pending.Count > 0) {
                    _pending.Dequeue().DispatchNext();
                }
            } finally {
                while (_pending.Count > 0) _pending.Dequeue().DropPending();
                _dispatching = false;
            }
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> on behalf of <paramref name="subscriber"/> and returns a handle whose <c>Dispose</c>
        /// removes it (idempotent). The handler is also removed when <paramref name="subscriber"/> is destroyed or passed to
        /// <see cref="Unsubscribe(object)"/>. A model subscriber is rejected with an error (no-op handle).
        /// </summary>
        public IDisposable Subscribe<TEvent>(object subscriber, Action<TEvent> handler) where TEvent : struct, IMmvcEvent {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (subscriber is BaseObservableDataModel model) {
                Debug.LogError($"[MmvcEventBus] Model '{model.GetType().Name}' must not subscribe to '{typeof(TEvent).Name}'. Models neither publish nor subscribe; subscribe from the unit's Logic or Presenter.", model);
                return new Subscription(null);
            }
            var channel = GetChannel<TEvent>();
            var entry = channel.Add(subscriber, handler, attributed: false);
            return new Subscription(() => channel.Remove(entry));
        }

        /// <summary>Removes every subscription of <paramref name="handler"/> to <typeparamref name="TEvent"/>.</summary>
        public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : struct, IMmvcEvent {
            if (handler == null) return;
            if (_channels.TryGetValue(typeof(TEvent), out var channel)) {
                ((EventChannel<TEvent>)channel).RemoveHandler(handler);
            }
        }

        /// <summary>Removes every subscription of <paramref name="subscriber"/> (explicit and <see cref="EventHandlerAttribute"/> ones).</summary>
        public void Unsubscribe(object subscriber) => RemoveSubscriber(subscriber, attributedOnly: false);

        /// <summary>Number of live handlers for <typeparamref name="TEvent"/> (diagnostics and tests).</summary>
        public int GetHandlerCount<TEvent>() where TEvent : struct, IMmvcEvent =>
            _channels.TryGetValue(typeof(TEvent), out var channel) ? channel.Count : 0;

        /// <summary>Binds the <see cref="EventHandlerAttribute"/> methods of <paramref name="subscriber"/>, replacing its previous attributed bindings.</summary>
        internal void BindAttributed(object subscriber, List<(MethodInfo method, Type eventType)> handlers) {
            RemoveSubscriber(subscriber, attributedOnly: true);
            if (handlers == null || handlers.Count == 0) return;
            if (subscriber is BaseObservableDataModel) return; // reported once per model type by ModelDescriptor
            for (int i = 0; i < handlers.Count; i++) {
                var (method, eventType) = handlers[i];
                try {
                    GetChannel(eventType).AddReflected(subscriber, method);
                } catch (Exception ex) {
                    Debug.LogError($"[MmvcEventBus] Failed to bind [EventHandler] '{subscriber.GetType().Name}.{method.Name}' to '{eventType.Name}': {ex.Message}", subscriber as UnityEngine.Object);
                }
            }
        }

        internal void RemoveSubscriber(object subscriber, bool attributedOnly) {
            if (subscriber == null || _channels.Count == 0) return;
            foreach (var channel in _channels.Values) {
                channel.RemoveSubscriber(subscriber, attributedOnly);
            }
        }

        /// <summary>Drops every channel and subscription (called when the owning locator is destroyed).</summary>
        internal void Clear() {
            foreach (var channel in _channels.Values) channel.Clear();
            _channels.Clear();
            _pending.Clear();
        }

        // Typed path used by Publish/Subscribe: this static instantiation of EventChannel<TEvent> is also what makes
        // the reflected path below available under IL2CPP.
        private EventChannel<TEvent> GetChannel<TEvent>() where TEvent : struct, IMmvcEvent {
            if (!_channels.TryGetValue(typeof(TEvent), out var channel)) {
                channel = new EventChannel<TEvent>();
                _channels.Add(typeof(TEvent), channel);
            }
            return (EventChannel<TEvent>)channel;
        }

        // Reflected path for [EventHandler] discovery: MakeGenericType on the channel type only (no MakeGenericMethod).
        private IEventChannel GetChannel(Type eventType) {
            if (!_channels.TryGetValue(eventType, out var channel)) {
                channel = (IEventChannel)Activator.CreateInstance(typeof(EventChannel<>).MakeGenericType(eventType));
                _channels.Add(eventType, channel);
            }
            return channel;
        }
    }

    /// <summary>Non-generic view of an <see cref="EventChannel{TEvent}"/> (reflected binding, queue flushing, cleanup).</summary>
    internal interface IEventChannel {
        int Count { get; }
        void AddReflected(object subscriber, MethodInfo method);
        void RemoveSubscriber(object subscriber, bool attributedOnly);
        void DispatchNext();
        void DropPending();
        void Clear();
    }

    internal sealed class EventChannel<TEvent> : IEventChannel where TEvent : struct, IMmvcEvent {
        internal sealed class Entry {
            public object Subscriber;
            public Action<TEvent> Handler;
            public bool Attributed;
            public bool Removed;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private Entry[] _snapshot = Array.Empty<Entry>();
        private Queue<TEvent> _pending;

        public int Count {
            get {
                int count = 0;
                for (int i = 0; i < _entries.Count; i++) {
                    if (!IsDead(_entries[i].Subscriber)) count++;
                }
                return count;
            }
        }

        public Entry Add(object subscriber, Action<TEvent> handler, bool attributed) {
            var entry = new Entry { Subscriber = subscriber, Handler = handler, Attributed = attributed };
            _entries.Add(entry);
            _snapshot = _entries.ToArray();
            return entry;
        }

        private delegate void InHandler(in TEvent evt);

        public void AddReflected(object subscriber, MethodInfo method) {
            Action<TEvent> handler;
            if (method.GetParameters()[0].ParameterType.IsByRef) {
                // 'in TEvent': typed delegate, no boxing per publish.
                var inHandler = (InHandler)Delegate.CreateDelegate(typeof(InHandler), subscriber, method);
                handler = e => inHandler(in e);
            } else {
                handler = (Action<TEvent>)Delegate.CreateDelegate(typeof(Action<TEvent>), subscriber, method);
            }
            Add(subscriber, handler, attributed: true);
        }

        public void Remove(Entry entry) {
            if (entry == null || entry.Removed) return;
            entry.Removed = true;
            _entries.Remove(entry);
            RebuildSnapshot();
        }

        public void RemoveHandler(Action<TEvent> handler) {
            bool changed = false;
            for (int i = _entries.Count - 1; i >= 0; i--) {
                if (_entries[i].Handler.Equals(handler)) changed |= RemoveAt(i);
            }
            if (changed) RebuildSnapshot();
        }

        public void RemoveSubscriber(object subscriber, bool attributedOnly) {
            bool changed = false;
            for (int i = _entries.Count - 1; i >= 0; i--) {
                var entry = _entries[i];
                if (ReferenceEquals(entry.Subscriber, subscriber) && (!attributedOnly || entry.Attributed)) changed |= RemoveAt(i);
            }
            if (changed) RebuildSnapshot();
        }

        /// <summary>Marks every entry removed (in-flight snapshots skip them) and empties the channel.</summary>
        public void Clear() {
            for (int i = 0; i < _entries.Count; i++) _entries[i].Removed = true;
            _entries.Clear();
            _snapshot = Array.Empty<Entry>();
            _pending?.Clear();
        }

        private bool RemoveAt(int index) {
            _entries[index].Removed = true;
            _entries.RemoveAt(index);
            return true;
        }

        private void RebuildSnapshot() {
            _snapshot = _entries.Count > 0 ? _entries.ToArray() : Array.Empty<Entry>();
        }

        public void Dispatch(in TEvent evt) {
            var snapshot = _snapshot;
            bool pruned = false;
            for (int i = 0; i < snapshot.Length; i++) {
                var entry = snapshot[i];
                if (entry.Removed) continue;
                if (IsDead(entry.Subscriber)) {
                    entry.Removed = true;
                    _entries.Remove(entry);
                    pruned = true;
                    continue;
                }
                try {
                    entry.Handler(evt);
                } catch (Exception ex) {
                    Debug.LogException(ex, entry.Subscriber as UnityEngine.Object);
                }
            }
            if (pruned) RebuildSnapshot();
        }

        public void Enqueue(in TEvent evt) {
            (_pending ??= new Queue<TEvent>()).Enqueue(evt);
        }

        public void DispatchNext() {
            if (_pending == null || _pending.Count == 0) return;
            var evt = _pending.Dequeue();
            Dispatch(in evt);
        }

        public void DropPending() {
            _pending?.Clear();
        }

        private static bool IsDead(object subscriber) => subscriber is UnityEngine.Object unityObject && unityObject == null;
    }
}
