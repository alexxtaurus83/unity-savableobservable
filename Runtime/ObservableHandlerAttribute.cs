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
        /// Marks a method to handle a specific ObservableVariable change.
        /// </summary>
        /// <param name="variableName">The name of the field in the Model to listen to. Use nameof() for type safety.</param>
        public ObservableHandlerAttribute(string variableName)
        {
            VariableName = variableName;
        }
    }
}