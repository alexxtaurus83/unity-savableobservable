using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SavableObservable {
    [Serializable]
    public class ObservableList<T> : IObservableVariable, IList<T> {
        [SerializeField] private List<T> _items = new List<T>();

        private ObservableTrackedAction<ObservableList<T>> _onChanged;

        /// <summary>
        /// Main tracked event for list changes.
        /// </summary>
        public ObservableTrackedAction<ObservableList<T>> OnChanged {
            get {
                if (_onChanged == null) {
                    _onChanged = new ObservableTrackedAction<ObservableList<T>>();
                }
                return _onChanged;
            }
            set {
                _onChanged = value ?? new ObservableTrackedAction<ObservableList<T>>();
            }
        }

        private enum PrevDelta {
            Empty,
            Inserted,
            Removed,
            Replaced,
            Unchanged,
            Snapshot
        }

        [NonSerialized] private PrevDelta _deltaKind = PrevDelta.Empty;
        [NonSerialized] private int _deltaIndex;
        [NonSerialized] private int _deltaCount;
        [NonSerialized] private T _deltaItem;
        [NonSerialized] private T[] _deltaSnapshot;
        [NonSerialized] private IReadOnlyList<T> _previousCache;
        [NonSerialized] private List<T> _itemsAtDelta;
        [NonSerialized] private int _countAtDelta;
        [NonSerialized] private bool _isNotifying;
        [NonSerialized] private Queue<(List<T> items, T[] previous)> _pendingTransitions;
        [NonSerialized] private List<T> _pendingItems;

        private enum Mutation { Assign, Replace, Add, AddRange, Clear, Insert, Remove, RemoveAt, Force }

        private bool QueueMutation(Mutation mutation, T item = default, int index = 0, IEnumerable<T> values = null) {
            var previous = _pendingItems ?? Items;
            var next = new List<T>(previous);
            bool force = true;
            switch (mutation) {
                case Mutation.Assign: next.Clear(); next.AddRange(values); force = false; break;
                case Mutation.Replace: next[index] = item; break;
                case Mutation.Add: next.Add(item); break;
                case Mutation.AddRange: next.AddRange(values); force = next.Count != previous.Count; break;
                case Mutation.Clear: next.Clear(); force = previous.Count > 0; break;
                case Mutation.Insert: next.Insert(index, item); break;
                case Mutation.Remove: if (!next.Remove(item)) return false; break;
                case Mutation.RemoveAt: next.RemoveAt(index); break;
            }
            if (force || !AreListsEqual(previous, next)) {
                _pendingTransitions ??= new Queue<(List<T>, T[])>();
                _pendingTransitions.Enqueue((next, previous.ToArray()));
                _pendingItems = next;
            }
            return true;
        }

        /// <summary>
        /// Snapshot of list state before the latest mutation.
        /// Rebuilt on read from a recorded mutation delta.
        /// Note: in-place mutations made directly through <see cref="Value"/> (e.g. Value[i] = x)
        /// bypass delta tracking.
        /// </summary>
        public IReadOnlyList<T> PreviousValue => _previousCache ??= MaterializePrevious();

#if UNITY_EDITOR
        [NonSerialized] private T[] _editorGuiSnapshot = Array.Empty<T>();
        [NonSerialized] private bool _hasEditorGuiSnapshot;
        [NonSerialized] private T[] _lastValidatedSnapshot = Array.Empty<T>();
        [NonSerialized] private bool _hasLastValidatedSnapshot;
#endif

        private List<T> Items => _items ??= new List<T>();

        /// <summary>
        /// Compatibility value property used by existing load/save reflection logic.
        /// </summary>
        public List<T> Value {
            get => Items;
            set {
                var next = value ?? new List<T>();
                if (_isNotifying) {
                    QueueMutation(Mutation.Assign, values: next);
                    return;
                }
                if (AreListsEqual(Items, next)) {
                    _items = next;
                    _itemsAtDelta = _items;
                    _countAtDelta = _items.Count;
#if UNITY_EDITOR
                    _lastValidatedSnapshot = _items.ToArray();
                    _hasLastValidatedSnapshot = true;
                    _hasEditorGuiSnapshot = false;
#endif
                    return;
                }

                var snapshot = Items.Count == 0 ? Array.Empty<T>() : Items.ToArray();
                _items = next;
                RecordSnapshot(snapshot);
                NotifyChanged();
            }
        }

        public int Count => Items.Count;

        public bool IsReadOnly => false;

        public T this[int index] {
            get => Items[index];
            set {
                if (_isNotifying) {
                    QueueMutation(Mutation.Replace, value, index);
                    return;
                }
                T oldItem = Items[index];
                Items[index] = value;
                RecordReplaced(index, oldItem);
                NotifyChanged();
            }
        }

        public void Add(T item) {
            if (_isNotifying) { QueueMutation(Mutation.Add, item); return; }
            int oldCount = Items.Count;
            Items.Add(item);
            RecordInserted(oldCount, 1);
            NotifyChanged();
        }

        public void AddRange(IEnumerable<T> items) {
            if (items == null) return;
            if (_isNotifying) { QueueMutation(Mutation.AddRange, values: items); return; }
            if (items is ICollection<T> col && col.Count == 0) return;
            int oldCount = Items.Count;
            Items.AddRange(items);
            int addedCount = Items.Count - oldCount;
            if (addedCount == 0) return;
            RecordInserted(oldCount, addedCount);
            NotifyChanged();
        }

        public void Clear() {
            if (_isNotifying) { QueueMutation(Mutation.Clear); return; }
            if (Items.Count == 0) return;
            var snapshot = Items.ToArray();
            Items.Clear();
            RecordSnapshot(snapshot);
            NotifyChanged();
        }

        public bool Contains(T item) => Items.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => Items.CopyTo(array, arrayIndex);

        public IEnumerator<T> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Items.GetEnumerator();

        public int IndexOf(T item) => Items.IndexOf(item);

        public void Insert(int index, T item) {
            if (_isNotifying) { QueueMutation(Mutation.Insert, item, index); return; }
            Items.Insert(index, item);
            RecordInserted(index, 1);
            NotifyChanged();
        }

        public bool Remove(T item) {
            if (_isNotifying) {
                return QueueMutation(Mutation.Remove, item);
            }
            var index = Items.IndexOf(item);
            if (index < 0) return false;

            T removedItem = Items[index];
            Items.RemoveAt(index);
            RecordRemoved(index, removedItem);
            NotifyChanged();
            return true;
        }

        public void RemoveAt(int index) {
            if (_isNotifying) { QueueMutation(Mutation.RemoveAt, index: index); return; }
            T removedItem = Items[index];
            Items.RemoveAt(index);
            RecordRemoved(index, removedItem);
            NotifyChanged();
        }

        public void ForceNotify() {
            if (_isNotifying) { QueueMutation(Mutation.Force); return; }
            RecordUnchanged();
            NotifyChanged();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Captures list snapshot before drawing editor controls.
        /// </summary>
        public void OnBeginGui() {
            if (_items == null) _items = new List<T>();
            _editorGuiSnapshot = _items.ToArray();
            _hasEditorGuiSnapshot = true;

            if (!_hasLastValidatedSnapshot) {
                _lastValidatedSnapshot = _items.ToArray();
                _hasLastValidatedSnapshot = true;
            }
        }

        /// <summary>
        /// Clears the active GUI snapshot when a change check completes without modifications.
        /// </summary>
        public void ClearEditorGuiSnapshot() {
            _hasEditorGuiSnapshot = false;
        }

        /// <summary>
        /// Handles editor validation (including undo/redo) and emits change notifications with accurate previous snapshots.
        /// </summary>
        public void OnValidate() {
            if (_items == null) _items = new List<T>();
            var previous = _hasEditorGuiSnapshot
                ? _editorGuiSnapshot
                : (_hasLastValidatedSnapshot ? _lastValidatedSnapshot : _items.ToArray());

            _hasEditorGuiSnapshot = false;

            if (!AreListsEqual(previous, _items)) {
                RecordSnapshot(previous);
                NotifyChanged();
            }

            _lastValidatedSnapshot = _items.ToArray();
            _hasLastValidatedSnapshot = true;
        }
#endif

        public override string ToString() => $"Count = {Items.Count}";

        public void SetParentDataModel(BaseObservableDataModel dataModel) {
            OnChanged.ParentDataModel = dataModel;
            OnChanged.Owner = this;
        }

        Delegate IObservableVariable.CreateHandler(object target, MethodInfo method, int paramCount) {
            switch (paramCount) {
                case 0: {
                    var act = (Action)Delegate.CreateDelegate(typeof(Action), target, method);
                    Action<ObservableList<T>> handler = _ => act();
                    return handler;
                }
                case 1: {
                    var paramType = method.GetParameters()[0].ParameterType;
                    if (paramType.IsAssignableFrom(typeof(ObservableList<T>))) {
                        try {
                            var act = (Action<ObservableList<T>>)Delegate.CreateDelegate(typeof(Action<ObservableList<T>>), target, method);
                            Action<ObservableList<T>> handler = v => act(v);
                            return handler;
                        } catch {
                            Action<ObservableList<T>> handler = v => method.Invoke(target, new object[] { v });
                            return handler;
                        }
                    }

                    try {
                        var act = (Action<List<T>>)Delegate.CreateDelegate(typeof(Action<List<T>>), target, method);
                        Action<ObservableList<T>> handler = v => act(v.Value);
                        return handler;
                    } catch {
                        Action<ObservableList<T>> handler = v => method.Invoke(target, new object[] { v.Value });
                        return handler;
                    }
                }
                case 2: {
                    try {
                        var act = (Action<List<T>, IReadOnlyList<T>>)Delegate.CreateDelegate(typeof(Action<List<T>, IReadOnlyList<T>>), target, method);
                        Action<ObservableList<T>> handler = v => act(v.Value, v.PreviousValue);
                        return handler;
                    } catch {
                        Action<ObservableList<T>> handler = v => method.Invoke(target, new object[] { v.Value, v.PreviousValue });
                        return handler;
                    }
                }
                default:
                    return null;
            }
        }

        Delegate IObservableVariable.CreateAutoBindHandler(Action<object> modelToUiHandler) {
            Action<ObservableList<T>> handler = v => modelToUiHandler(v.Value);
            return handler;
        }

        void IObservableVariable.AddHandler(Delegate handler, object subscriber) {
            OnChanged.Add((Action<ObservableList<T>>)handler, subscriber, managed: true);
        }

        void IObservableVariable.RemoveHandler(Delegate handler) {
            OnChanged.Remove((Action<ObservableList<T>>)handler);
        }

        void IObservableVariable.InvokeHandler(Delegate handler) {
            ((Action<ObservableList<T>>)handler)(this);
        }

        private void RecordInserted(int index, int count) {
            _previousCache = null;
            _deltaKind = PrevDelta.Inserted;
            _deltaIndex = index;
            _deltaCount = count;
            _deltaSnapshot = null;
            _deltaItem = default;
            _itemsAtDelta = Items;
            _countAtDelta = Items.Count;
        }

        private void RecordRemoved(int index, T item) {
            _previousCache = null;
            _deltaKind = PrevDelta.Removed;
            _deltaIndex = index;
            _deltaCount = 1;
            _deltaItem = item;
            _deltaSnapshot = null;
            _itemsAtDelta = Items;
            _countAtDelta = Items.Count;
        }

        private void RecordReplaced(int index, T oldItem) {
            _previousCache = null;
            _deltaKind = PrevDelta.Replaced;
            _deltaIndex = index;
            _deltaCount = 1;
            _deltaItem = oldItem;
            _deltaSnapshot = null;
            _itemsAtDelta = Items;
            _countAtDelta = Items.Count;
        }

        private void RecordUnchanged() {
            _previousCache = null;
            _deltaKind = PrevDelta.Unchanged;
            _deltaSnapshot = null;
            _deltaItem = default;
            _itemsAtDelta = Items;
            _countAtDelta = Items.Count;
        }

        private void RecordSnapshot(T[] snapshot) {
            _previousCache = null;
            _deltaKind = PrevDelta.Snapshot;
            _deltaSnapshot = snapshot ?? Array.Empty<T>();
            _deltaItem = default;
            _itemsAtDelta = Items;
            _countAtDelta = Items.Count;
        }

        private IReadOnlyList<T> MaterializePrevious() {
            switch (_deltaKind) {
                case PrevDelta.Empty:
                    return Array.Empty<T>();

                case PrevDelta.Snapshot:
                    return _deltaSnapshot ?? Array.Empty<T>();

                case PrevDelta.Unchanged:
                    if (HasOutsideModification()) {
                        return LogOutsideModificationAndReturnCurrent();
                    }
                    return _items == null || _items.Count == 0 ? Array.Empty<T>() : _items.ToArray();

                case PrevDelta.Inserted: {
                    if (HasOutsideModification()) {
                        return LogOutsideModificationAndReturnCurrent();
                    }
                    int prevCount = _items.Count - _deltaCount;
                    if (prevCount <= 0) {
                        return Array.Empty<T>();
                    }
                    var prev = new T[prevCount];
                    if (_deltaIndex > 0) {
                        _items.CopyTo(0, prev, 0, _deltaIndex);
                    }
                    int remaining = _items.Count - (_deltaIndex + _deltaCount);
                    if (remaining > 0) {
                        _items.CopyTo(_deltaIndex + _deltaCount, prev, _deltaIndex, remaining);
                    }
                    return prev;
                }

                case PrevDelta.Removed: {
                    if (HasOutsideModification()) {
                        return LogOutsideModificationAndReturnCurrent();
                    }
                    var prev = new T[_items.Count + 1];
                    if (_deltaIndex > 0) {
                        _items.CopyTo(0, prev, 0, _deltaIndex);
                    }
                    prev[_deltaIndex] = _deltaItem;
                    int remaining = _items.Count - _deltaIndex;
                    if (remaining > 0) {
                        _items.CopyTo(_deltaIndex, prev, _deltaIndex + 1, remaining);
                    }
                    _deltaItem = default;
                    return prev;
                }

                case PrevDelta.Replaced: {
                    if (HasOutsideModification()) {
                        return LogOutsideModificationAndReturnCurrent();
                    }
                    if (_items.Count == 0) {
                        _deltaItem = default;
                        return Array.Empty<T>();
                    }
                    var prev = _items.ToArray();
                    if (_deltaIndex >= 0 && _deltaIndex < prev.Length) {
                        prev[_deltaIndex] = _deltaItem;
                    }
                    _deltaItem = default;
                    return prev;
                }

                default:
                    return Array.Empty<T>();
            }
        }

        private bool HasOutsideModification() {
            return !ReferenceEquals(_items, _itemsAtDelta) || (_items != null && _items.Count != _countAtDelta);
        }

        private IReadOnlyList<T> LogOutsideModificationAndReturnCurrent() {
            _deltaItem = default;
            Debug.LogWarning("ObservableList: External modification of underlying items detected. Delta cannot be applied; returning current list as PreviousValue.");
            return _items == null || _items.Count == 0 ? Array.Empty<T>() : _items.ToArray();
        }

        private void NotifyChanged() {
            _isNotifying = true;
            try {
                int iterations = 0;
                while (true) {
#if UNITY_EDITOR
                    // No editor snapshot is needed until an inspector/validation path uses it.
                    if (_hasLastValidatedSnapshot) _lastValidatedSnapshot = Items.ToArray();
                    _hasEditorGuiSnapshot = false;
#endif
                    OnChanged?.Invoke(this);
                    if (_pendingTransitions == null || _pendingTransitions.Count == 0) break;
                    if (++iterations >= 32) {
                        Debug.LogError("ObservableList notification exceeded maxIterations (32); stopping to prevent infinite loop.");
                        break;
                    }
                    var transition = _pendingTransitions.Dequeue();
                    _items = transition.items;
                    RecordSnapshot(transition.previous);
                    if (_pendingTransitions.Count == 0) _pendingItems = null;
                }
            } finally {
                _isNotifying = false;
                _pendingTransitions?.Clear();
                _pendingItems = null;
            }
        }

        private static bool AreListsEqual(IReadOnlyList<T> previous, IReadOnlyList<T> current) {
            if (ReferenceEquals(previous, current)) return true;
            if (previous == null || current == null) return false;
            if (previous.Count != current.Count) return false;

            for (int i = 0; i < previous.Count; i++) {
                if (!ObservableVariable<T>.AreEqual(previous[i], current[i])) {
                    return false;
                }
            }

            return true;
        }
    }

}
