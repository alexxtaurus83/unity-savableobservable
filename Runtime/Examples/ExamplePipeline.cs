using UnityEngine;
using TMPro;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SavableObservable;
 
namespace SavableObservable.Examples {


 [Serializable]
public class ComponentDataModel : BaseObservableDataModel {               

    public ObservableVariable<string> status;
    public ObservableVariable<bool> newVersionTimerEnabled;    

}

 public class ComponentPresenter : ObservablePresenterWithLogic<ComponentDataModel,ComponentLogic> { 
    
    [SerializeField] private TextMeshProUGUI versionTextTMP;
    [SerializeField] private TextMeshProUGUI newVersionTimerTextTMP;     

    public void OnModelValueChanged(IObservableVariable variable) {
        switch (variable.Name) {            
            case var value when value == GetModel().newVersionTimerEnabled.Name:
                versionTextTMP.text = GetModel().newVersionTimerEnabled.Value.ToString();
                break;                        
            case var value when value == GetModel().status.Name:
                newVersionTimerTextTMP.enabled = GetModel().status.Value;
                break;
            default:
                break;
        }
    }  
 }
 public class ComponentLogic : BaseLogic<ComponentDataModel> {    

    public void TimerStarts() {          
        GetModel().status.Value = "InDevelopment";
        GetModel().newVersionTimerEnabled.Value = true;            
    }
  
}