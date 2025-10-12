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
        
        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableInt32 : ObservableVariable<int> { public ObservableInt32(string name) : base(name) { } }

        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableString : ObservableVariable<string> { public ObservableString(string name) : base(name) { } }

        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableBoolean : ObservableVariable<bool> { public ObservableBoolean(string name) : base(name) { } }

        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableNullableBoolean : ObservableVariable<bool?> { public ObservableNullableBoolean(string name) : base(name) { } }

        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableSingle : ObservableVariable<float> { public ObservableSingle(string name) : base(name) { } }

        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableDouble : ObservableVariable<double> { public ObservableDouble(string name) : base(name) { } }
        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableInt64 : ObservableVariable<long> { public ObservableInt64(string name) : base(name) { } }
        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableByte : ObservableVariable<byte> { public ObservableByte(string name) : base(name) { } }
        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableInt16 : ObservableVariable<short> { public ObservableInt16(string name) : base(name) { } }
        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableVector2 : ObservableVariable<Vector2> { public ObservableVector2(string name) : base(name) { } }
        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableVector3 : ObservableVariable<Vector3> { public ObservableVector3(string name) : base(name) { } }
        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableVector4 : ObservableVariable<Vector4> { public ObservableVector4(string name) : base(name) { } }
        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableQuaternion : ObservableVariable<Quaternion> { public ObservableQuaternion(string name) : base(name) { } }        
        [Serializable] public class ObservableShort : ObservableVariable<ushort> { public ObservableShort(string name) : base(name) { } }
        [Serializable] public class ObservableByte : ObservableVariable<byte> { public ObservableByte(string name) : base(name) { } }
 
        /// <summary>Determines whether field type is <see cref="ObservableVariable" /> field</summary>
        /// <param name="field">The <see cref="ObservableVariable" /> field of the <see cref="BaseObservableDataModel" /> model.</param>
        /// <returns>
        ///   <c>true</c> if filed of type <see cref="ObservableVariable" /> otherwise, <c>false</c>.</returns>
        public static bool IsSupportedFieldType(FieldInfo field) {
            return ObservableTypes.types.Keys.Contains(field.FieldType);
        }      

        public static class ObservableTypes {
            public static Dictionary<Type, Type> types = new() {
                { typeof(ObservableInt32), typeof(int) },
                { typeof(ObservableDouble), typeof(double) },
                { typeof(ObservableBoolean), typeof(bool) },
                { typeof(ObservableNullableBoolean), typeof(bool?) },
                { typeof(ObservableSingle), typeof(float) },
                { typeof(ObservableString), typeof(string) },
                { typeof(ObservableInt64), typeof(long) },
                { typeof(ObservableByte), typeof(byte) },
                { typeof(ObservableInt16), typeof(short) },
                { typeof(ObservableVector2), typeof(Vector2) },
                { typeof(ObservableVector3), typeof(Vector3) },
                { typeof(ObservableVector4), typeof(Vector4) },
                { typeof(ObservableShort), typeof(ushort) },
                { typeof(ObservableByte), typeof(byte) },
                { typeof(ObservableQuaternion), typeof(Quaternion) }
            };
        }

        public static void SetListeners(object obj) {
            _initializedListeners.Add(obj);
            var dataModel = ((MonoBehaviour)obj).GetComponent<BaseObservableDataModel>();
            if (dataModel == null) return;

            var initMethod = dataModel.GetType().GetMethod("InitFields");
            initMethod?.Invoke(dataModel, null);

            // --- New Logic Starts Here ---

            // 1. Check for a single, overridden universal handler
            var universalHandler = obj.GetType().GetMethod("OnModelValueChanged", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (universalHandler != null && universalHandler.DeclaringType == obj.GetType())
            {
                // Universal handler is overridden, subscribe all variables to it.
                foreach (var field in GetObservableFields(dataModel)) {
                    SubscribeUniversalHandler(obj, universalHandler, field, dataModel);
                }
                return; // Stop processing
            }

            // 2. If no universal handler, look for individual handlers with attributes
            var individualHandlers = obj.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Select(m => new { Method = m, Attribute = m.GetCustomAttribute<ObservableHandlerAttribute>() })
                .Where(x => x.Attribute != null)
                .ToDictionary(x => x.Attribute.VariableName, x => x.Method);

            foreach (var field in GetObservableFields(dataModel))
            {
                if (individualHandlers.TryGetValue(field.Name, out var handlerMethod))
                {
                    SubscribeIndividualHandler(obj, handlerMethod, field, dataModel);
                }
                else
                {
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
            var concreteObservableType = typeof(ObservableVariable<>).MakeGenericType(ObservableTypes.types[field.FieldType]);

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
    public abstract class ObservableVariable<T> : IObservableVariable  { //: ISerializationCallbackReceiver
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
                //if (!EqualityComparer<T>.Default.Equals(_value, value)) { TODO enable after test with pipleine class as it has current block is running
                    PreviousValue = _value;
                    _value = value;
                    OnValueChanged?.Invoke(this);
                //}
            }
        }
 
        public override string ToString() => _value?.ToString();
    }
}