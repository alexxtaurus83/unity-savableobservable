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
    }
}