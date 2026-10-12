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

        /// <summary>
        /// Compatibility alias used by existing reflection-based subscription code.
        /// </summary>
        public ObservableTrackedAction<ObservableList<T>> OnValueChanged {
            get => OnChanged;
            set => OnChanged = value;
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

        /// <summary>
        /// Compatibility value property used by existing load/save reflection logic.
        /// </summary>
        public List<T> Value {
            get => _items;
            set {
                var snapshot = _items == null || _items.Count == 0 ? Array.Empty<T>() : _items.ToArray();
                _items = value ?? new List<T>();
                RecordSnapshot(snapshot);
                NotifyChanged();
            }
        }

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public T this[int index] {
            get => _items[index];
            set {
                T oldItem = _items[index];
                _items[index] = value;
                RecordReplaced(index, oldItem);
                NotifyChanged();
            }
        }

        public void Add(T item) {
            int oldCount = _items.Count;
            _items.Add(item);
            RecordInserted(oldCount, 1);
            NotifyChanged();
        }

        public void AddRange(IEnumerable<T> items) {
            if (items == null) return;
            if (items is ICollection<T> col && col.Count == 0) return;
            int oldCount = _items.Count;
            _items.AddRange(items);
            int addedCount = _items.Count - oldCount;
            if (addedCount == 0) return;
            RecordInserted(oldCount, addedCount);
            NotifyChanged();
        }

        public void Clear() {
            if (_items.Count == 0) return;
            var snapshot = _items.ToArray();
            _items.Clear();
            RecordSnapshot(snapshot);
            NotifyChanged();
        }

        public bool Contains(T item) => _items.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        public int IndexOf(T item) => _items.IndexOf(item);

        public void Insert(int index, T item) {
            _items.Insert(index, item);
            RecordInserted(index, 1);
            NotifyChanged();
        }

        public bool Remove(T item) {
            var index = _items.IndexOf(item);
            if (index < 0) return false;

            T removedItem = _items[index];
            _items.RemoveAt(index);
            RecordRemoved(index, removedItem);
            NotifyChanged();
            return true;
        }

        public void RemoveAt(int index) {
            T removedItem = _items[index];
            _items.RemoveAt(index);
            RecordRemoved(index, removedItem);
            NotifyChanged();
        }

        public void ForceNotify() {
            RecordUnchanged();
            NotifyChanged();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Captures list snapshot before drawing editor controls.
        /// </summary>
        public void OnBeginGui() {
            _editorGuiSnapshot = _items.ToArray();
            _hasEditorGuiSnapshot = true;

            if (!_hasLastValidatedSnapshot) {
                _lastValidatedSnapshot = _items.ToArray();
                _hasLastValidatedSnapshot = true;
            }
        }

        /// <summary>
        /// Handles editor validation (including undo/redo) and emits change notifications with accurate previous snapshots.
        /// </summary>
        public void OnValidate() {
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

        public override string ToString() => $"Count = {_items.Count}";

        public void SetParentDataModel(BaseObservableDataModel dataModel) {
            OnChanged.ParentDataModel = dataModel;
        }

        private void RecordInserted(int index, int count) {
            _previousCache = null;
            _deltaKind = PrevDelta.Inserted;
            _deltaIndex = index;
            _deltaCount = count;
            _deltaSnapshot = null;
            _deltaItem = default;
            _itemsAtDelta = _items;
            _countAtDelta = _items.Count;
        }

        private void RecordRemoved(int index, T item) {
            _previousCache = null;
            _deltaKind = PrevDelta.Removed;
            _deltaIndex = index;
            _deltaCount = 1;
            _deltaItem = item;
            _deltaSnapshot = null;
            _itemsAtDelta = _items;
            _countAtDelta = _items.Count;
        }

        private void RecordReplaced(int index, T oldItem) {
            _previousCache = null;
            _deltaKind = PrevDelta.Replaced;
            _deltaIndex = index;
            _deltaCount = 1;
            _deltaItem = oldItem;
            _deltaSnapshot = null;
            _itemsAtDelta = _items;
            _countAtDelta = _items.Count;
        }

        private void RecordUnchanged() {
            _previousCache = null;
            _deltaKind = PrevDelta.Unchanged;
            _deltaSnapshot = null;
            _deltaItem = default;
            _itemsAtDelta = _items;
            _countAtDelta = _items.Count;
        }

        private void RecordSnapshot(T[] snapshot) {
            _previousCache = null;
            _deltaKind = PrevDelta.Snapshot;
            _deltaSnapshot = snapshot ?? Array.Empty<T>();
            _deltaItem = default;
            _itemsAtDelta = _items;
            _countAtDelta = _items.Count;
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
            OnChanged?.Invoke(this);
        }

        private static bool AreListsEqual(IReadOnlyList<T> previous, IReadOnlyList<T> current) {
            if (ReferenceEquals(previous, current)) return true;
            if (previous == null || current == null) return false;
            if (previous.Count != current.Count) return false;

            var comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < previous.Count; i++) {
                if (!comparer.Equals(previous[i], current[i])) {
                    return false;
                }
            }

            return true;
        }
    }

}
