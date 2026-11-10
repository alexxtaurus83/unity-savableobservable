using System;

namespace SavableObservable
{
    /// <summary>
    /// Marks a method as a handler for an ObservableVariable change.
    /// The method signature can be flexible.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class ObservableHandlerAttribute : Attribute
    {
        public string VariableName { get; }

        /// <summary>
        /// When <c>true</c>, <see cref="Observable.SetListeners(object, BaseObservableDataModel)"/> calls the handler once with the
        /// current value right after binding (a two-parameter handler receives <c>Value</c> and the unchanged <c>PreviousValue</c>;
        /// a replay is not a change), so a pure render handler also paints the initial state. Default: <c>false</c>.
        /// Keep it <c>false</c> for handlers with side effects
        /// (sounds, tweens, one-shot commands). Ignored when <see cref="ObservableSettings.SyncOnBind"/> is <c>false</c>.
        /// </summary>
        public bool InvokeOnBind { get; set; }

        /// <summary>
        /// Marks a method to handle a specific ObservableVariable change.
        /// </summary>
        /// <param name="variableName">The name of the field in the Model to listen to. Use nameof() for type safety.</param>
        public ObservableHandlerAttribute(string variableName)
        {
            VariableName = variableName;
        }
    }
}
