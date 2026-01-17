using System;
using System.Collections.Generic;

namespace SavableObservable
{
    /// <summary>
    /// A custom action implementation that automatically tracks subscriptions for memory management.
    /// </summary>
    public class ObservableTrackedAction<T> where T : class, IObservableVariable
    {
        private List<Action<T>> _handlers = new List<Action<T>>();
        private readonly object _lock = new object();
        
        /// <summary>
        /// The parent data model for cleanup purposes.
        /// </summary>
        internal BaseObservableDataModel ParentDataModel { get; set; }
        
        /// <summary>
        /// The subscriber object for this subscription.
        /// </summary>
        internal object Subscriber { get; set; }
        
        /// <summary>
        /// Adds a handler to the tracked action.
        /// </summary>
        public void Add(Action<T> handler, object subscriber)
        {
            lock (_lock)
            {
                if (!_handlers.Contains(handler))
                {
                    _handlers.Add(handler);
                    
                    // Register this subscription for automatic cleanup if possible
                    if (ParentDataModel != null && subscriber != null)
                    {
                        ParentDataModel.RegisterSubscription(subscriber, handler);
                    }
                }
            }
        }
        
        /// <summary>
        /// Removes a handler from the tracked action.
        /// </summary>
        public void Remove(Action<T> handler)
        {
            lock (_lock)
            {
                _handlers.Remove(handler);
            }
        }
        
        /// <summary>
        /// Invokes all registered handlers.
        /// </summary>
        public void Invoke(T variable)
        {
            Action<T>[] handlersCopy;
            lock (_lock)
            {
                handlersCopy = new Action<T>[_handlers.Count];
                _handlers.CopyTo(handlersCopy);
            }
            
            foreach (var handler in handlersCopy)
            {
                try
                {
                    handler?.Invoke(variable);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"Error invoking handler: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Gets the count of registered handlers.
        /// </summary>
        public int HandlerCount
        {
            get
            {
                lock (_lock)
                {
                    return _handlers.Count;
                }
            }
        }
    }
}