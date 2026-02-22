using UnityEditor;
using UnityEngine;

namespace SavableObservable.Editor {
    [CustomPropertyDrawer(typeof(SavableObservable.ObservableList<>), true)]
    public class ObservableListDrawer : PropertyDrawer {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            EditorGUI.BeginProperty(position, label, property);

            var itemsProperty = property.FindPropertyRelative("_items");
            if (itemsProperty != null) {
                IObservableVariable targetObject = null;
                try {
                    targetObject = PropertyResolver.GetTargetObjectOfProperty(property) as IObservableVariable;
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
    }
}
