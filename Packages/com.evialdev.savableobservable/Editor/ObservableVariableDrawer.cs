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
                IObservableVariable targetObject = null;
                // Only resolve target object if we are in a context where we might need it (optimization)
                // or simply do it once. Resolving every frame in OnGUI can be heavy if deep in hierarchy.
                // We'll resolve it since we need to call OnBeginGui().
                try {
                    targetObject = PropertyResolver.GetTargetObjectOfProperty(property) as IObservableVariable;
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
    }
}
