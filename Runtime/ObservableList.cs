using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.Text.RegularExpressions;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
#endif
#endif

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

        /// <summary>
        /// Snapshot of list state before the latest mutation.
        /// </summary>
        public IReadOnlyList<T> PreviousValue { get; private set; } = Array.Empty<T>();

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
                CapturePrevious();
                _items = value ?? new List<T>();
                NotifyChanged();
            }
        }

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public T this[int index] {
            get => _items[index];
            set {
                CapturePrevious();
                _items[index] = value;
                NotifyChanged();
            }
        }

        public void Add(T item) {
            CapturePrevious();
            _items.Add(item);
            NotifyChanged();
        }

        public void AddRange(IEnumerable<T> items) {
            if (items == null) return;
            CapturePrevious();
            _items.AddRange(items);
            NotifyChanged();
        }

        public void Clear() {
            if (_items.Count == 0) return;
            CapturePrevious();
            _items.Clear();
            NotifyChanged();
        }

        public bool Contains(T item) => _items.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        public int IndexOf(T item) => _items.IndexOf(item);

        public void Insert(int index, T item) {
            CapturePrevious();
            _items.Insert(index, item);
            NotifyChanged();
        }

        public bool Remove(T item) {
            var index = _items.IndexOf(item);
            if (index < 0) return false;

            CapturePrevious();
            _items.RemoveAt(index);
            NotifyChanged();
            return true;
        }

        public void RemoveAt(int index) {
            CapturePrevious();
            _items.RemoveAt(index);
            NotifyChanged();
        }

        public void ForceNotify() {
            CapturePrevious();
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
                PreviousValue = previous;
                NotifyChanged();
            }

            _lastValidatedSnapshot = CopyToArray(_items);
            _hasLastValidatedSnapshot = true;
        }
#endif

        public override string ToString() => $"Count = {_items.Count}";

        public void SetParentDataModel(BaseObservableDataModel dataModel) {
            OnChanged.ParentDataModel = dataModel;
        }

        private void CapturePrevious() {
            PreviousValue = _items.ToArray();
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

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(ObservableList<>), true)]
    public class ObservableListDrawer : PropertyDrawer {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            EditorGUI.BeginProperty(position, label, property);

            var itemsProperty = property.FindPropertyRelative("_items");
            if (itemsProperty != null) {
                IObservableVariable targetObject = null;
                try {
                    targetObject = GetTargetObjectOfProperty(property) as IObservableVariable;
                } catch {
                    // Ignore resolution errors
                }

                if (targetObject != null) {
                    targetObject.OnBeginGui();
                }

                EditorGUI.BeginChangeCheck();
                EditorGUI.PropertyField(position, itemsProperty, label, true);

                if (EditorGUI.EndChangeCheck()) {
                    property.serializedObject.ApplyModifiedProperties();

                    if (targetObject != null) {
                        targetObject.OnValidate();
                    }
                }
            } else {
                EditorGUI.PropertyField(position, property, label, true);
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            var itemsProperty = property.FindPropertyRelative("_items");
            if (itemsProperty != null) {
                return EditorGUI.GetPropertyHeight(itemsProperty, label, true);
            }

            return EditorGUIUtility.singleLineHeight;
        }

        /// <summary>
        /// Gets the object instance that the SerializedProperty points to.
        /// Improved to avoid regex and be safer.
        /// </summary>
        private object GetTargetObjectOfProperty(SerializedProperty property) {
            if (property == null) return null;

            var path = property.propertyPath.Replace(".Array.data[", "[");
            object obj = property.serializedObject.targetObject;
            var elements = path.Split('.');

            foreach (var element in elements) {
                if (element.Contains("[")) {
                    var elementName = element.Substring(0, element.IndexOf("["));
                    var index = Convert.ToInt32(element.Substring(element.IndexOf("[")).Replace("[", "").Replace("]", ""));
                    obj = GetValue_Imp(obj, elementName, index);
                } else {
                    obj = GetValue_Imp(obj, element);
                }
            }

            return obj;
        }

        private object GetValue_Imp(object source, string name) {
            if (source == null) return null;
            var type = source.GetType();

            while (type != null) {
                var f = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (f != null) return f.GetValue(source);

                var p = type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p != null) return p.GetValue(source, null);

                type = type.BaseType;
            }
            return null;
        }

        private object GetValue_Imp(object source, string name, int index) {
            var enumerable = GetValue_Imp(source, name) as IEnumerable;
            if (enumerable == null) return null;

            var enm = enumerable.GetEnumerator();
            for (int i = 0; i <= index; i++) {
                if (!enm.MoveNext()) return null;
            }
            return enm.Current;
        }
    }

#if ODIN_INSPECTOR
    public class ObservableListOdinDrawer<T> : OdinValueDrawer<ObservableList<T>> {
        protected override void DrawPropertyLayout(GUIContent label) {
            var observable = ValueEntry.SmartValue;
            observable?.OnBeginGui();

            EditorGUI.BeginChangeCheck();
            CallNextDrawer(label);

            if (EditorGUI.EndChangeCheck()) {
                observable?.OnValidate();
                ValueEntry.SmartValue = observable;
            }
        }
    }
#endif
#endif
}
