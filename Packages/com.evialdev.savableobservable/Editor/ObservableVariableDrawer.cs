using UnityEditor;
using UnityEngine;

namespace SavableObservable.Editor {
    [CustomPropertyDrawer(typeof(SavableObservable.ObservableVariable<>), true)]
    public class ObservableVariableDrawer : PropertyDrawer {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            EditorGUI.BeginProperty(position, label, property);

            // Find the actual value field (which is _value)
            var valueProperty = property.FindPropertyRelative("_value");

            if (valueProperty != null) {
                object[] targetObjects = null;
                try {
                    targetObjects = PropertyResolver.GetTargetObjectsOfProperty(property);
                } catch {
                    // Ignore resolution errors to prevent UI freeze
                }

                if (targetObjects != null) {
                    for (int i = 0; i < targetObjects.Length; i++) {
                        (targetObjects[i] as IObservableVariable)?.OnBeginGui();
                    }
                }

                // Begin change check before drawing
                EditorGUI.BeginChangeCheck();
                // Draw the value field with the original label
                EditorGUI.PropertyField(position, valueProperty, label, true);

                // Check if the value has changed in the editor
                if (EditorGUI.EndChangeCheck()) {
                    // Apply the changes to ensure the serialized data is updated
                    property.serializedObject.ApplyModifiedProperties();

                    if (targetObjects != null) {
                        for (int i = 0; i < targetObjects.Length; i++) {
                            (targetObjects[i] as IObservableVariable)?.OnValidate();
                        }
                    }
                } else {
                    if (targetObjects != null) {
                        for (int i = 0; i < targetObjects.Length; i++) {
                            (targetObjects[i] as IObservableVariable)?.ClearEditorGuiSnapshot();
                        }
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
    }
}
