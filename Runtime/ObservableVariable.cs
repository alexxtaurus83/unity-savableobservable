using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System.Linq;

namespace SavableObservable {

    public class Observable {
        
        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableInt32 : ObservableVariable<int> { public ObservableInt32(string name) : base(name) { } }

        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableString : ObservableVariable<string> { public ObservableString(string name) : base(name) { } }

        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableBoolean : ObservableVariable<bool> { public ObservableBoolean(string name) : base(name) { } }

        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableSingle : ObservableVariable<float> { public ObservableSingle(string name) : base(name) { } }

        /// <summary>Predefined observable type (without generic) required for Unity field serialization</summary>
        [Serializable] public class ObservableDouble : ObservableVariable<double> { public ObservableDouble(string name) : base(name) { } }

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
                { typeof(ObservableSingle), typeof(float) },
                { typeof(ObservableString), typeof(string) }
            };
        }

        public static void SetListeners(object obj) {
            var dataModel = ((MonoBehaviour)obj).GetComponent<BaseObservableDataModel>();
            if (dataModel == null) return;

            var initMethod = dataModel.GetType().GetMethod("InitFields");
            initMethod?.Invoke(dataModel, null);

            foreach (MemberInfo memberInfo in dataModel.GetType().GetMembers()) {
                if (memberInfo.MemberType == MemberTypes.Field) {
                    FieldInfo field = (FieldInfo)memberInfo;

                    if (!Observable.IsSupportedFieldType(field)) continue;

                    // Get the value type only once per supported field
                    if (Observable.ObservableTypes.types.TryGetValue(field.FieldType, out Type valueType)) {
                        SetOnValueChangedHandler(obj, valueType, field, dataModel);
                    }
                }
            }
        }

        private static void SetOnValueChangedHandler(object obj, Type valueType, FieldInfo field, BaseObservableDataModel dataModel) {
            // Look for the correct OnModelValueChanged(T previous, T current, string name)
            var method = obj.GetType().GetMethod("OnModelValueChanged", new Type[] { valueType, valueType, typeof(string) });
            if (method == null) {
                throw new Exception($"Can't find 'OnModelValueChanged' method for observable variable with {field.FieldType.Name} type for field {field.Name}.");
            }

            var eventInfo = field.FieldType.GetEvent("OnValueChanged");
            var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, obj, method);
            eventInfo.AddEventHandler(field.GetValue(dataModel), handler);
        }
        
    }

    public interface IObservableValue {
        string Name { get; }
        Type ValueType { get; }
        object GetValue();
        void SetValue(object value);
        string GetValueAsString();
        void NotifyObservers();
        event Action<object> OnValueChangedRaw;
    }

    [Serializable]
    public abstract class ObservableVariable<T> : ISerializationCallbackReceiver {
        /// <summary>
        /// The actual stored value. Set via the inspector or code.
        /// </summary>
        [SerializeField] private T _value;

        /// <summary>
        /// Optional name for debugging, data binding, or event filtering.
        /// </summary>
        [SerializeField] public string Name;

        /// <summary>
        /// If true, enables runtime detection of Inspector changes.
        /// Useful only when editing values at runtime via the Inspector.
        /// </summary>
        [SerializeField] private bool detectInspectorChanges = false;

        /// <summary>
        /// Fired whenever the Value is changed via property setter or detected from Inspector.
        /// </summary>
        public event Action<T, T, string> OnValueChanged;

        /// <summary>
        /// Keeps the last known value to detect changes caused by Unity Inspector.
        /// </summary>
        private T _previousValue;

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
                    T old = _value;
                    _value = value;
                    OnValueChanged?.Invoke(old, _value, Name);
                    _previousValue = _value; // Update cached value to prevent duplicate triggering
                //}
            }
        }

        public override string ToString() => _value?.ToString();

        // Called by Unity AFTER this object is deserialized (e.g., after inspector change)
        public void OnAfterDeserialize() {
            // Only react when explicitly allowed
            if (!detectInspectorChanges) return; //!Application.isPlaying ||

            // Compare deserialized value with previous runtime value
            if (!EqualityComparer<T>.Default.Equals(_value, _previousValue)) {
                T old = _previousValue;
                _previousValue = _value;
                OnValueChanged?.Invoke(old, _value, Name);
            }
        }

        // Called by Unity BEFORE this object is serialized (usually not needed here)
        public void OnBeforeSerialize() {
            // Could be used for pre-save cleanup, but not needed in this case
        }
    }



}