using UnityEngine;
using TMPro;
using System;
using SavableObservable;

namespace SavableObservable.Examples 
{
    [Serializable]
    public class MemoryTestModel : BaseObservableDataModel 
    { 
        public ObservableVariable<string> status;
        public ObservableVariable<int> counter;    
    }

    public class MemoryTestPresenter : ObservablePresenterWithLogic<MemoryTestModel, MemoryTestLogic> 
    { 
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI counterText;

        // This will be automatically unsubscribed when the GameObject is destroyed
        public void OnModelValueChanged(IObservableVariable variable) 
        {
            switch (variable.Name) 
            {            
                case var value when value == GetModel().counter.Name:
                    if (counterText != null) counterText.text = GetModel().counter.Value.ToString();
                    break;                        
                case var value when value == GetModel().status.Name:
                    if (statusText != null) statusText.text = GetModel().status.Value;
                    break;
            }
        }

        private void Start()
        {
            // RECOMMENDED: Use the ObservableTrackedAction for manual subscriptions that will be automatically cleaned up
            GetModel().status.OnValueChanged.Add(ManualEventHandler, this);
            
            // NOT RECOMMENDED: Direct subscription bypasses automatic cleanup tracking
            // GetModel().counter.OnValueChanged += CounterChangedDirect;  // This would bypass our tracking!
            // If you must use direct subscription, you MUST manually unsubscribe in OnDestroy
        }

        private void ManualEventHandler(IObservableVariable variable)
        {
            Debug.Log($"Manual handler called for {variable.Name}: {((dynamic)variable).Value}");
        }

        private void OnDestroy()
        {
            // The automatic cleanup will happen in BaseObservableDataModel.OnDestroy
            // No need to manually unsubscribe for tracked subscriptions
        }
    }

    public class MemoryTestLogic : BaseLogic<MemoryTestModel> 
    {    
        public void UpdateValues() 
        {          
            GetModel().status.Value = "Updated";
            GetModel().counter.Value = GetModel().counter.Value + 1;            
        }
    }
}