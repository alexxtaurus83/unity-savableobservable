using System;

namespace SavableObservable
{
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

#if UNITY_EDITOR
        void OnBeginGui();
        void OnValidate();
#endif
    }
}