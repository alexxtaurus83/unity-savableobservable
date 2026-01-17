# ObservableVariable Memory Management

## Overview

This document explains the automatic memory management system for ObservableVariables in the Unity MMVC framework. The system prevents memory leaks by automatically unsubscribing event handlers when GameObjects are destroyed.

## Key Components

### 1. BaseObservableDataModel
- Now includes a `_subscriptions` dictionary to track all subscriptions per subscriber
- Implements `OnDestroy()` to automatically unsubscribe all registered event handlers
- Provides methods to register and unregister subscriptions

### 2. Observable Class
- Updated subscription methods to automatically register subscriptions with the data model
- Added `RegisterManualSubscription()` for manual subscriptions outside of `SetListeners()`
- Enhanced cleanup logic to properly remove event handlers

### 3. ObservableTracker
- Lightweight MonoBehaviour component that ensures proper lifecycle management
- Automatically attached when `Observable.SetListeners()` is called
- Works in conjunction with BaseObservableDataModel's OnDestroy to clean up subscriptions

## How It Works

### Automatic Cleanup (Recommended)
When using `Observable.SetListeners(presenter)`, the system automatically:
1. Creates subscriptions between ObservableVariables and presenter methods
2. Registers these subscriptions in the data model's tracking system
3. Automatically unsubscribes when the GameObject containing the BaseObservableDataModel is destroyed

```csharp
// This will be automatically cleaned up
Observable.SetListeners(this); // Called in LoaderWithModel.LoadDataFromModel
```

### Manual Subscriptions
For manual subscriptions to `OnValueChanged` events, you have two options:

**Option 1: Recommended - Use ObservableTrackedAction Add method**
```csharp
// Manual subscription that will be automatically cleaned up
GetModel().myVariable.OnValueChanged.Add(MyHandlerMethod, this);
```

**Option 2: Alternative - Use RegisterManualSubscription**
```csharp
// Manual subscription that will be automatically cleaned up
GetModel().myVariable.OnValueChanged.Add(MyHandlerMethod, this);
// Note: With the new ObservableTrackedAction, the subscription is automatically registered for cleanup
```

**Important Warning: Direct Subscription Bypasses Tracking**
```csharp
// NOT RECOMMENDED: This bypasses our tracking system and may cause memory leaks
GetModel().myVariable.OnValueChanged += MyHandlerMethod;  // No automatic cleanup!
```

If you directly subscribe to the `OnValueChanged` event without using the ObservableTrackedAction, the subscription will not be automatically cleaned up when the GameObject is destroyed, potentially causing memory leaks.

## Benefits

1. **Prevents Memory Leaks**: Event handlers are automatically removed when GameObjects are destroyed
2. **Zero Performance Impact**: No reflection used during runtime after initial subscription
3. **Backward Compatible**: Existing code continues to work without changes
4. **Handles Complex Scenarios**: Works with DontDestroyOnLoad objects and complex hierarchies

## Best Practices

1. Always use `Observable.SetListeners()` for automatic subscription management
2. When manually subscribing, register the subscription using `Observable.RegisterManualSubscription()`
3. The system handles both universal handlers (`OnModelValueChanged`) and individual handlers (`[ObservableHandler]`)
4. No need to manually unsubscribe in `OnDestroy()` - the system handles it automatically

## Example Usage

```csharp
public class MyPresenter : ObservablePresenterWithLogic<MyDataModel, MyLogic> 
{
    public void OnModelValueChanged(IObservableVariable variable) 
    {
        // This subscription will be automatically cleaned up
        switch (variable.Name) 
        {
            case nameof(GetModel().myVariable):
                UpdateUI(GetModel().myVariable.Value);
                break;
        }
    }

    private void Start()
    {
        // Manual subscription that will also be cleaned up automatically
        GetModel().anotherVariable.OnValueChanged += AnotherHandler;
        Observable.RegisterManualSubscription(this, (Action<IObservableVariable>)AnotherHandler);
    }

    private void AnotherHandler(IObservableVariable variable)
    {
        // Handle the event
    }
}