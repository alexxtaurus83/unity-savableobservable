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
    public class ObservableVariable<T> : IObservableVariable {
        /// <summary>
        /// The actual stored value. Set via the inspector or code.
        /// </summary>
        [SerializeField] private T _value;

        /// <summary>
        /// Custom tracked action that automatically manages subscriptions.
        /// </summary>
        /// <summary>
        /// Public access to the tracked action for subscription management.
        /// </summary>
        public ObservableTrackedAction<ObservableVariable<T>> OnValueChanged {
            get {
                if (_onValueChanged == null) {
                    _onValueChanged = new ObservableTrackedAction<ObservableVariable<T>>();
                }
                return _onValueChanged;
            }
            set {
                _onValueChanged = value ?? new ObservableTrackedAction<ObservableVariable<T>>();
            }
        }
        private ObservableTrackedAction<ObservableVariable<T>> _onValueChanged;

        /// <summary>
        /// Stores the previous value of the variable.
        /// </summary>
        public T PreviousValue { get; private set; }

#if UNITY_EDITOR
        [NonSerialized] private T _editorGuiSnapshot;
        [NonSerialized] private bool _hasEditorGuiSnapshot;
        [NonSerialized] private T _lastValidatedValue;
        [NonSerialized] private bool _hasLastValidatedValue;
#endif

        /// <summary>
        /// Default constructor.
        /// </summary>
        public ObservableVariable() {
        }

        /// <summary>
        /// Main access point for getting and setting the variable.
        /// Triggers change events when value is updated via code.
        /// </summary>
        public T Value {
            get => _value;
            set {
                PreviousValue = _value;
                _value = value;
                OnValueChanged?.Invoke(this);
            }
        }

        /// <summary>
        /// Forces the OnValueChanged event to fire, useful for when values are changed directly in the editor.
        /// </summary>
        public void ForceNotify() {
            PreviousValue = _value;
            OnValueChanged?.Invoke(this);
        }

        public override string ToString() => _value?.ToString();

#if UNITY_EDITOR
        /// <summary>
        /// Captures the value snapshot before drawing editor controls.
        /// </summary>
        public void OnBeginGui() {
            _editorGuiSnapshot = _value;
            _hasEditorGuiSnapshot = true;

            if (!_hasLastValidatedValue) {
                _lastValidatedValue = _value;
                _hasLastValidatedValue = true;
            }
        }

        /// <summary>
        /// Handles editor validation (including undo/redo) and emits change notifications with proper previous-value snapshots.
        /// </summary>
        public void OnValidate() {
            T previousValue;

            if (_hasEditorGuiSnapshot) {
                previousValue = _editorGuiSnapshot;
            }
            else if (_hasLastValidatedValue) {
                previousValue = _lastValidatedValue;
            }
            else {
                previousValue = _value;
            }

            _hasEditorGuiSnapshot = false;

            if (!EqualityComparer<T>.Default.Equals(previousValue, _value)) {
                PreviousValue = previousValue;
                OnValueChanged?.Invoke(this);
            }

            _lastValidatedValue = _value;
            _hasLastValidatedValue = true;
        }
#endif

        /// <summary>
        /// Sets the parent data model for cleanup purposes.
        /// </summary>
        /// <param name="dataModel">The parent data model</param>
        public void SetParentDataModel(BaseObservableDataModel dataModel) {
            OnValueChanged.ParentDataModel = dataModel;
        }
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(ObservableVariable<>), true)]
    public class ObservableVariableDrawer : PropertyDrawer {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            EditorGUI.BeginProperty(position, label, property);

            // Find the actual value field (which is _value)
            var valueProperty = property.FindPropertyRelative("_value");

            if (valueProperty != null) {
                // Use fieldInfo.GetValue(targetObject) where possible, but for PropertyDrawers
                // navigating to the actual object instance can be tricky/expensive.
                // However, we only need to invoke methods if we are actually about to change something or capture state.

                IObservableVariable targetObject = null;
                // Only resolve target object if we are in a context where we might need it (optimization)
                // or simply do it once. Resolving every frame in OnGUI can be heavy if deep in hierarchy.
                // We'll resolve it since we need to call OnBeginGui().
                try {
                    targetObject = GetTargetObjectOfProperty(property) as IObservableVariable;
                } catch {
                    // Ignore resolution errors to prevent UI freeze
                }

                if (targetObject != null) {
                    targetObject.OnBeginGui();
                }

                // Begin change check before drawing
                EditorGUI.BeginChangeCheck();
                // Draw the value field with the original label
                EditorGUI.PropertyField(position, valueProperty, label, true);

                // Check if the value has changed in the editor
                if (EditorGUI.EndChangeCheck()) {
                    // Apply the changes to ensure the serialized data is updated
                    property.serializedObject.ApplyModifiedProperties();

                    if (targetObject != null) {
                        targetObject.OnValidate();
                    }
                }
            } else {
                // Fallback: draw the default property field if _value is not found
                EditorGUI.PropertyField(position, property, label, true);
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            var valueProperty = property.FindPropertyRelative("_value");
            if (valueProperty != null) {
                // Return the height required for drawing the value property
                return EditorGUI.GetPropertyHeight(valueProperty, label, true);
            }
            // Default height if the property isn't found
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
    public class ObservableVariableOdinDrawer<T> : OdinValueDrawer<ObservableVariable<T>> {
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