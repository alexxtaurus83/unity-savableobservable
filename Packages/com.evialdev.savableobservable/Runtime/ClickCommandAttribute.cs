using System;

namespace SavableObservable {

    /// <summary>
    /// Declaratively wires a <see cref="UnityEngine.UI.Button"/> field's onClick event to a zero-argument,
    /// synchronous method on the paired Logic component.
    /// The target method must return void, have zero parameters, and be public and instance.
    /// Async void, Task, ValueTask and coroutine methods are not supported.
    /// Note: To prevent method stripping under IL2CPP, mark the target logic method with <c>[UnityEngine.Scripting.Preserve]</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class ClickCommandAttribute : Attribute {
        /// <summary>
        /// Name of the zero-argument public method on the paired Logic component.
        /// </summary>
        public string MethodName { get; }

        public ClickCommandAttribute(string methodName) {
            MethodName = methodName;
        }
    }
}
