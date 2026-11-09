using System;
using System.Reflection;

namespace SavableObservable
{
    /// <summary>
    /// Contract of an observable value holder (<see cref="ObservableVariable{T}"/>, <see cref="ObservableList{T}"/>).
    /// The handler members are used by <see cref="Observable.SetListeners(object, BaseObservableDataModel)"/> to bind
    /// <c>[ObservableHandler]</c> methods and <c>[AutoBind]</c> fields without reflection over generic types.
    /// Custom implementers must implement every member.
    /// </summary>
    public interface IObservableVariable
    {
        /// <summary>
        /// Sets the parent data model for cleanup purposes.
        /// </summary>
        /// <param name="dataModel">The parent data model</param>
        void SetParentDataModel(BaseObservableDataModel dataModel);

        /// <summary>
        /// Forces the OnValueChanged event to fire. Useful for triggering updates
        /// when values are modified directly in the editor or when explicit notification is needed.
        /// </summary>
        void ForceNotify();

        /// <summary>
        /// Creates a change handler that invokes <paramref name="method"/> on <paramref name="target"/>.
        /// <paramref name="paramCount"/> is 0 (no arguments), 1 (value) or 2 (value, previous value).
        /// Returns <c>null</c> for any other count.
        /// </summary>
        Delegate CreateHandler(object target, MethodInfo method, int paramCount);

        /// <summary>
        /// Creates a change handler that forwards the current value to <paramref name="modelToUiHandler"/>.
        /// </summary>
        Delegate CreateAutoBindHandler(Action<object> modelToUiHandler);

        /// <summary>
        /// Subscribes a handler created by <see cref="CreateHandler"/> / <see cref="CreateAutoBindHandler"/> on behalf of
        /// <paramref name="subscriber"/>. The subscription is framework-managed: it is replaced by
        /// <c>Observable.SetListeners</c> and removed by <c>Observable.RemoveListeners</c>.
        /// </summary>
        void AddHandler(Delegate handler, object subscriber);

        /// <summary>Unsubscribes a handler previously added with <see cref="AddHandler"/>.</summary>
        void RemoveHandler(Delegate handler);

#if UNITY_EDITOR
        void OnBeginGui();
        void OnValidate();
#endif
    }
}
