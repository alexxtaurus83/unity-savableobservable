using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System.Linq;
using System.Linq.Expressions;

namespace SavableObservable {

    public class Observable {

        private static readonly HashSet<object> _initializedListeners = new HashSet<object>();

        /// <summary>
        /// Checks if SetListeners has been called for a specific object (typically a Presenter or Logic).
        /// </summary>
        public static bool AreListenersInitialized(object obj) => _initializedListeners.Contains(obj);
        
        /// <summary>Determines whether field type is <see cref="ObservableVariable" /> field</summary>
        /// <param name="field">The <see cref="ObservableVariable" /> field of the <see cref="BaseObservableDataModel" /> model.</param>
        /// <returns>
        ///   <c>true</c> if filed of type <see cref="ObservableVariable" /> otherwise, <c>false</c>.</returns>
        public static bool IsSupportedFieldType(FieldInfo field) {
            return field.FieldType.IsGenericType &&
                   field.FieldType.GetGenericTypeDefinition() == typeof(ObservableVariable<>);
        }

        public static void SetListeners(object obj) {
            _initializedListeners.Add(obj);
            var dataModel = ((MonoBehaviour)obj).GetComponent<BaseObservableDataModel>();
            if (dataModel == null) return;

            var initMethod = dataModel.GetType().GetMethod("InitFields");
            initMethod?.Invoke(dataModel, null);
            
            var universalHandler = obj.GetType().GetMethod("OnModelValueChanged", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            bool isUniversalHandlerOverridden = universalHandler != null && universalHandler.DeclaringType == obj.GetType();

            var individualHandlers = obj.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => m.GetCustomAttribute<ObservableHandlerAttribute>() != null)
                .ToList();

            // Warn if a universal handler and attribute-based handlers are used together.
            if (isUniversalHandlerOverridden && individualHandlers.Any()) {
                Debug.LogError($"[SavableObservable] Presenter '{obj.GetType().Name}' uses both a universal 'OnModelValueChanged' handler and specific [ObservableHandler] attributes. This is not supported. The universal handler will be used, and attribute-based handlers will be ignored.", (MonoBehaviour)obj);
            }

            // If a universal handler is overridden, subscribe all variables to it and stop processing.
            if (isUniversalHandlerOverridden) {
                foreach (var field in GetObservableFields(dataModel)) {
                    SubscribeUniversalHandler(obj, universalHandler, field, dataModel);
                }
                return;
            }

            // If no universal handler, look for individual handlers with attributes.
            var individualHandlerMap = individualHandlers.ToDictionary(x => x.GetCustomAttribute<ObservableHandlerAttribute>().VariableName, x => x);

            foreach (var field in GetObservableFields(dataModel)) {
                if (individualHandlerMap.TryGetValue(field.Name, out var handlerMethod)) {
                    SubscribeIndividualHandler(obj, handlerMethod, field, dataModel);
                } else {
                    // Warn if an observable variable has no corresponding handler.
                    Debug.LogWarning($"[SavableObservable] ObservableVariable '{field.Name}' in {dataModel.GetType().Name} has no corresponding [ObservableHandler] method in {obj.GetType().Name}.", (MonoBehaviour)obj);
                }
            }
        }

        private static IEnumerable<FieldInfo> GetObservableFields(BaseObservableDataModel dataModel)
        {
            return dataModel.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(IsSupportedFieldType);
        }

        private static void SubscribeUniversalHandler(object obj, MethodInfo universalHandler, FieldInfo field, BaseObservableDataModel dataModel) {
            var eventInfo = field.FieldType.GetEvent("OnValueChanged");
            var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, obj, universalHandler);
            eventInfo.AddEventHandler(field.GetValue(dataModel), handler);
        }

        private static void SubscribeIndividualHandler(object obj, MethodInfo handlerMethod, FieldInfo field, BaseObservableDataModel dataModel)
        {
            var eventInfo = field.FieldType.GetEvent("OnValueChanged");
            var observableVar = field.GetValue(dataModel) as IObservableVariable;
            if (observableVar == null) return;

            var handlerParams = handlerMethod.GetParameters();
            var eventParams = new[] { Expression.Parameter(typeof(IObservableVariable), "variable") };

            Expression body;
            var concreteObservableType = typeof(ObservableVariable<>).MakeGenericType(field.FieldType.GetGenericArguments()[0]);

            if (handlerParams.Length == 0)
            {
                body = Expression.Call(Expression.Constant(obj), handlerMethod);
            }
            else if (handlerParams.Length == 1)
            {
                var arg1 = Expression.Property(Expression.Convert(eventParams[0], concreteObservableType), "Value");
                body = Expression.Call(Expression.Constant(obj), handlerMethod, arg1);
            }
            else if (handlerParams.Length == 2)
            {
                var arg1 = Expression.Property(Expression.Convert(eventParams[0], concreteObservableType), "Value");
                var arg2 = Expression.Property(Expression.Convert(eventParams[0], concreteObservableType), "PreviousValue");
                body = Expression.Call(Expression.Constant(obj), handlerMethod, arg1, arg2);
            }
            else
            {
                Debug.LogError($"[SavableObservable] Method '{handlerMethod.Name}' has an invalid number of parameters for [ObservableHandler].", (MonoBehaviour)obj);
                return;
            }

            var lambda = Expression.Lambda(eventInfo.EventHandlerType, body, eventParams);
            eventInfo.AddEventHandler(observableVar, lambda.Compile());
        }
    }

    public interface IObservableVariable {
        /// <summary>
        /// The name of the variable (e.g., "pipelineName").
        /// </summary>
        string Name { get; }
    }
 
    [Serializable]
    public abstract class ObservableVariable<T> : IObservableVariable, ISerializationCallbackReceiver {
        /// <summary>
        /// The actual stored value. Set via the inspector or code.
        /// </summary>
        [SerializeField] private T _value;
 
        /// <summary>
        /// Optional name for debugging, data binding, or event filtering.
        /// </summary>
        [field: SerializeField] public string Name { get; set; }
 
        /// <summary>
        /// Fired whenever the Value is changed via property setter or detected from Inspector.
        /// </summary>
        public event Action<IObservableVariable> OnValueChanged;
 
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
                OnValueChanged?.Invoke(this);               
            }
        }
      
        /// <summary>
        /// Forces the OnValueChanged event to fire, useful for when values are changed directly in the editor.
        /// </summary>
        public void ForceNotify() {
            OnValueChanged?.Invoke(this);
        }
      
        void ISerializationCallbackReceiver.OnBeforeSerialize() {
            // Store current value in serialized field
        }
      
        void ISerializationCallbackReceiver.OnAfterDeserialize() {
            // Ensure the property value is set after deserialization
            // This ensures that the internal state is synchronized after loading
            if (_value != null || typeof(T) == typeof(string) || !EqualityComparer<T>.Default.Equals(_value, default(T))) {
                // Only trigger if there's a meaningful value
                OnValueChanged?.Invoke(this);
            }
        }
      
        public override string ToString() => _value?.ToString();
    }
    
    #if UNITY_EDITOR
    using UnityEditor;
    using System.Text.RegularExpressions;
    using System.Collections;
    
    namespace SavableObservable
    {
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
    }
    #endif
}