using System;

namespace SavableObservable {

    /// <summary>
    /// Marks an <see cref="ObservableVariable{T}"/> field of a <see cref="BaseObservableDataModel"/> so that
    /// every assignment to <c>Value</c> notifies subscribers, even when the new value equals the current one.
    /// </summary>
    /// <remarks>
    /// By default an <see cref="ObservableVariable{T}"/> does not notify on an equal assignment
    /// (compared with <see cref="System.Collections.Generic.EqualityComparer{T}.Default"/>); <c>Value</c> and
    /// <c>PreviousValue</c> stay untouched. With this attribute (or the runtime
    /// <see cref="ObservableVariable{T}.NotifyOnSameValue"/> property) an equal assignment notifies and
    /// <c>PreviousValue</c> equals <c>Value</c>.
    /// The attribute has no effect on <see cref="ObservableList{T}"/> or on non-observable fields (a warning is logged once).
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class NotifyOnSameValueAttribute : Attribute {
    }
}
