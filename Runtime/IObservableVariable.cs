using System;

namespace SavableObservable 
{
    public interface IObservableVariable 
    {
        /// <summary>
        /// The name of the variable (e.g., "pipelineName").
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Sets the parent data model for cleanup purposes.
        /// </summary>
        /// <param name="dataModel">The parent data model</param>
        void SetParentDataModel(BaseObservableDataModel dataModel);
    }
}