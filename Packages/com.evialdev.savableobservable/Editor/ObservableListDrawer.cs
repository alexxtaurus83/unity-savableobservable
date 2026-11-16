using UnityEditor;
using UnityEngine;

namespace SavableObservable.Editor {
    [CustomPropertyDrawer(typeof(SavableObservable.ObservableList<>), true)]
    public class ObservableListDrawer : PropertyDrawer {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            EditorGUI.BeginProperty(position, label, property);

            var itemsProperty = property.FindPropertyRelative("_items");
            if (itemsProperty != null) {
                object[] targetObjects = null;
                try {
                    targetObjects = PropertyResolver.GetTargetObjectsOfProperty(property);
                } catch {
                    // Ignore resolution errors
                }

                if (targetObjects != null) {
                    for (int i = 0; i < targetObjects.Length; i++) {
                        (targetObjects[i] as IObservableVariable)?.OnBeginGui();
                    }
                }

                EditorGUI.BeginChangeCheck();
                EditorGUI.PropertyField(position, itemsProperty, label, true);

                if (EditorGUI.EndChangeCheck()) {
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
