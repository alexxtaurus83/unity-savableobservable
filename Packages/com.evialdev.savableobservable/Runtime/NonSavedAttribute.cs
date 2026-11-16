using System;

namespace SavableObservable {
    /// <summary>
    /// Marks an <see cref="ObservableVariable{T}"/> or <see cref="ObservableList{T}"/> field on a
    /// <see cref="BaseObservableDataModel"/> as transient runtime/UI state that must be skipped during
    /// <see cref="BaseObservableDataModel.LoadDataFromModel"/> and persistence serialization.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class NonSavedAttribute : Attribute {
    }
}
