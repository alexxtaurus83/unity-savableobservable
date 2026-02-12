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
    public class ObservableVariableDrawer : PropertyDrawer
    {
            public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
            {
                EditorGUI.BeginProperty(position, label, property);
    
                // Find the actual value field (which is _value)
                var valueProperty = property.FindPropertyRelative("_value");
                
                if (valueProperty != null)
                {
                    var targetObject = GetTargetObject(property);
                    InvokeMethod(targetObject, "OnBeginGui");

                    // Begin change check before drawing
                    EditorGUI.BeginChangeCheck();
                    // Draw the value field with the original label
                    EditorGUI.PropertyField(position, valueProperty, label, true);
                    
                    // Check if the value has changed in the editor
                    if (EditorGUI.EndChangeCheck())
                    {
                        // Apply the changes to ensure the serialized data is updated
                        property.serializedObject.ApplyModifiedProperties();
                        
                        if (targetObject != null && targetObject is IObservableVariable)
                        {
                            // Use OnValidate first so PreviousValue uses the editor snapshot.
                            if (!InvokeMethod(targetObject, "OnValidate"))
                            {
                                InvokeMethod(targetObject, "ForceNotify");
                            }
                        }
                    }
                }
                else
                {
                    // Fallback: draw the default property field if _value is not found
                    EditorGUI.PropertyField(position, property, label, true);
                }
    
                EditorGUI.EndProperty();
            }
    
            public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            {
                var valueProperty = property.FindPropertyRelative("_value");
                if (valueProperty != null)
                {
                    // Return the height required for drawing the value property
                    return EditorGUI.GetPropertyHeight(valueProperty, label, true);
                }
                // Default height if the property isn't found
                return EditorGUIUtility.singleLineHeight;
            }
    
            private object GetTargetObject(SerializedProperty property)
            {
                // Navigate to the target object using reflection
                string propertyPath = property.propertyPath;
                object obj = property.serializedObject.targetObject;
    
                // Split the path and traverse the object hierarchy
                var paths = propertyPath.Split('.');
                for (int i = 0; i < paths.Length; i++)
                {
                    string path = paths[i];
                    
                    // Handle array elements
                    if (path == "Array")
                    {
                        i++; // Move to the element part: data[index]
                        if (i < paths.Length)
                        {
                            var match = Regex.Match(paths[i], @"data\[([0-9]+)\]");
                            if (match.Success)
                            {
                                int index = int.Parse(match.Groups[1].Value);
                                if (obj is IList list)
                                {
                                    obj = list[index];
                                }
                            }
                        }
                        continue;
                    }
    
                    // Get the field info
                    FieldInfo fieldInfo = GetFieldInfo(obj.GetType(), path);
                    if (fieldInfo != null)
                    {
                        obj = fieldInfo.GetValue(obj);
                    }
                    else
                    {
                        // If we can't find the field, break
                        break;
                    }
                }
    
                return obj;
            }
    
            private bool InvokeMethod(object target, string methodName)
            {
                if (target == null) return false;

                var method = target.GetType().GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (method == null) return false;

                method.Invoke(target, null);
                return true;
            }

            private FieldInfo GetFieldInfo(System.Type type, string fieldName)
            {
                FieldInfo fieldInfo = null;
                System.Type currentType = type;
    
                // Look in the current type and all base types
                while (currentType != null && fieldInfo == null)
                {
                    fieldInfo = currentType.GetField(fieldName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    
                    if (fieldInfo != null)
                        break;
                        
                    currentType = currentType.BaseType;
                }
    
                return fieldInfo;
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