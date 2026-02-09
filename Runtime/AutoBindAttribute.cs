using System;

namespace SavableObservable
{
    /// <summary>
    /// Marks a UI field for automatic binding to an ObservableVariable.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class AutoBindAttribute : Attribute
    {
        /// <summary>
        /// The name of the ObservableVariable to bind to.
        /// If null, uses the field name.
        /// </summary>
        public string VariableName { get; }

        /// <summary>
        /// Auto-bind using the field name as the variable name.
        /// </summary>
        public AutoBindAttribute()
        {
            VariableName = null;
        }

        /// <summary>
        /// Auto-bind to a specific ObservableVariable by name.
        /// </summary>
        /// <param name="variableName">The name of the ObservableVariable field in the model</param>
        public AutoBindAttribute(string variableName)
        {
            VariableName = variableName;
        }
    }
}
