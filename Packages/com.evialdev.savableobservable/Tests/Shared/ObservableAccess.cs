using System;
using System.Reflection;

namespace SavableObservable.Tests.Contracts {

    /// <summary>
    /// Test helper that resolves the tracked change event of an observable field by its type:
    /// <see cref="ObservableVariable{T}"/> exposes <c>OnValueChanged</c>, <see cref="ObservableList{T}"/> exposes <c>OnChanged</c>.
    /// </summary>
    public static class ObservableAccess {

        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>Returns the name of the tracked-action property for the given observable field type, or null if unsupported.</summary>
        public static string GetChangeEventName(Type observableType) {
            for (var t = observableType; t != null; t = t.BaseType) {
                if (!t.IsGenericType) continue;
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(ObservableVariable<>)) return nameof(ObservableVariable<int>.OnValueChanged);
                if (def == typeof(ObservableList<>)) return nameof(ObservableList<int>.OnChanged);
            }
            return null;
        }

        /// <summary>Returns the tracked-action property of the observable instance, or null if the type is not supported.</summary>
        public static PropertyInfo GetChangeEventProperty(object observable) {
            if (observable == null) return null;
            var name = GetChangeEventName(observable.GetType());
            return name == null ? null : observable.GetType().GetProperty(name, Flags);
        }

        /// <summary>Returns the tracked-action instance of the observable, or null if the type is not supported.</summary>
        public static object GetChangeEvent(object observable) {
            return GetChangeEventProperty(observable)?.GetValue(observable);
        }
    }
}
