using System;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.Text.RegularExpressions;
using System.Collections;
#endif

namespace SavableObservable {
    [Serializable]
    public class ObservableVariable<T> : IObservableVariable {
        /// <summary>
        /// The actual stored value. Set via the inspector or code.
        /// </summary>
        [SerializeField] private T _value;

        /// <summary>
        /// Optional name for debugging, data binding, or event filtering.
        /// </summary>
        [field: SerializeField] public string Name { get; set; }

        /// <summary>
        /// Custom tracked action that automatically manages subscriptions.
        /// </summary>
        /// <summary>
        /// Public access to the tracked action for subscription management.
        /// </summary>
        public ObservableTrackedAction<ObservableVariable<T>> OnValueChanged { get; } = new ObservableTrackedAction<ObservableVariable<T>>();

        /// <summary>
        /// Stores the previous value of the variable.
        /// </summary>
        public T PreviousValue { get; private set; }

        /// <summary>
        /// Constructor that sets the observable field name.
        /// </summary>
        public ObservableVariable(string name = null) {
            Name = name;
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

                if (OnValueChanged != null) {
                    OnValueChanged.Invoke(this);
                }
            }
        }

        /// <summary>
        /// Forces the OnValueChanged event to fire, useful for when values are changed directly in the editor.
        /// </summary>
        public void ForceNotify() {
            if (OnValueChanged != null) {
                OnValueChanged.Invoke(this);
            }
        }

        public override string ToString() => _value?.ToString();

        /// <summary>
        /// Sets the parent data model for cleanup purposes.
        /// </summary>
        /// <param name="dataModel">The parent data model</param>
        public void SetParentDataModel(BaseObservableDataModel dataModel) {
            if (OnValueChanged != null) {
                OnValueChanged.ParentDataModel = dataModel;
            }
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
                    // Draw the value field with the original label
                    EditorGUI.PropertyField(position, valueProperty, label, true);
                    
                    // Check if the value has changed in the editor
                    if (EditorGUI.EndChangeCheck())
                    {
                        // Apply the changes to ensure the serialized data is updated
                        property.serializedObject.ApplyModifiedProperties();
                        
                        // Find the target object to call ForceNotify
                        var targetObject = GetTargetObject(property);
                        if (targetObject != null && targetObject is IObservableVariable observableVar)
                        {
                            // Call ForceNotify to trigger the OnValueChanged event
                            // Use reflection to call ForceNotify to avoid generic type issues
                            var forceNotifyMethod = targetObject.GetType().GetMethod("ForceNotify",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            
                            if (forceNotifyMethod != null)
                            {
                                forceNotifyMethod.Invoke(targetObject, null);
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
#endif
}