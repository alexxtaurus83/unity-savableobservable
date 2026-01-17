# NOTE: This project is under development.

# MMVC Framework for Unity

## Introduction

The MMVC (Model-Model-View-Controller) framework is a custom-designed architectural pattern for Unity, tailored to facilitate a clean separation of concerns, enhance data binding, and streamline data persistence. It's a powerful approach that blends concepts from traditional MVC and MVVM patterns to offer a reactive, scalable, and maintainable structure for your Unity projects.

At its core, the framework is built on these key principles:

* *Reactive Data Binding*: The `ObservableVariable` class allows UI elements and game logic to automatically react to data changes without tight coupling.
* **Separation of Concerns**: Each component has a distinct responsibility: data (`Model`), business logic (`Logic`), and presentation (`Presenter`).
* **Centralized Lifecycle Management**: The `Loader` component acts as a central point for creating, managing, and saving/loading the other components.
* **Convention over Configuration*): The framework uses reflection to automatically wire up dependencies, reducing boilerplate code and simplifying setup.

## Core Components

The MMVC framework is comprised of four main components:

### 1. Model

The `Model` is the data layer of your application. It holds the state and is responsible for the application's data. In the MMVC framework, the Model is represented by a class that inherits from `BaseObservableDataModel`. 

**Key Features:**

* It uses `ObservableVariable` fields to store data, which automatically notify any listeners when their value changes.
* The `InitFields` method uses reflection to initialize all `ObservableVariable` fields, so you don't have to do it manually.
* **Mixed Data Types**: You can include regular, non-observable fields (like `int`, `string`, `List<T>`) alongside `ObservableVariable` fields in your model. The save/load system will correctly handle both.


### 2. Logic

The `Logic` component acts as the "Controller" or "ViewModel." It contains the business logic that manipulates the `Model` and responds to events. It listens for changes in the `Model` and executes the appropriate actions.

**Key Features:**

* It inherits from `BaseLogic<M>`, where `M` is the type of the `Model`.
* Its primary role is to contain business logic that manipulates the `Model`.

### 3. Presenter Hierarchy

The framework provides two base classes for presenters, allowing you to choose the right one for your needs.

#### `BasePresenter<M>`

This is the simplest presenter. It should be used when you have a `Model` that does **not** contain any `ObservableVariable` fields. It provides a `GetModel()` method but does not have any built-in reactivity.

#### `BaseObservablePresenter<M>`

This is the reactive presenter, which inherits from `BasePresenter<M>`. It's designed to work with a `Model` that uses `ObservableVariable` fields.

**Key Features:**

*   It inherits from `BasePresenter<M>`.
*   It has references to UI elements (e.g., `Button`, `TextMeshProUGUI`).
*   It provides two ways to handle model changes: a single universal handler or individual methods marked with an `[ObservableHandler]` attribute. See the "Event Handling in Presenters" section below for details.
*   **Automatic Setup Validation**: It includes a check in `Start()` that will log an error if `Observable.SetListeners()` was not called for it. This helps prevent configuration errors where the UI does not update because event subscriptions are missing.

##### **Important Note on `Start()`**

Because `BaseObservablePresenter` uses `Start()` for its internal validation, if you need to use the `Start()` method in your own presenter, you **must** declare it as `protected override` and call `base.Start()`.

```csharp
public class MyPresenter : BaseObservablePresenter<MyModel>
{
    protected override void Start()
    {
        // 1. This call is crucial for the framework's validation to run.
        base.Start();

        // 2. Add your own Start() logic here.
        Debug.Log("My custom Start logic.");
    }

    // ... other methods
}
```

### 4. Event Handling in Presenters

The framework offers two powerful, mutually exclusive ways to handle `ObservableVariable` changes in your presenters.

#### Approach 1: The Universal Handler (High Priority)

You can override the `OnModelValueChanged` method in your presenter. If this method is overridden, it will receive **all** change events from the model. This is useful for simple components or for debugging.

**If this method is overridden, the framework will ignore any `[ObservableHandler]` attributes in the class.**

```csharp
public class MyPresenter : BaseObservablePresenter<MyModel>
{
    // Override the single handler
    protected override void OnModelValueChanged(IObservableVariable variable)
    {
        Debug.Log($"'{variable.Name}' changed!");
        // Use a switch to handle different variables
        switch (variable.Name) {
            // ...
        }
    }
}
```

#### Approach 2: Individual Handlers (Recommended)

If you do **not** override `OnModelValueChanged`, the framework will instead look for individual methods marked with the `[ObservableHandler("VariableName")]` attribute. This is the recommended approach as it leads to cleaner, more organized code.

*   The framework will scan your presenter for these attributes on startup.
*   If an `ObservableVariable` exists in the model but no corresponding `[ObservableHandler]` is found in the presenter, a warning will be logged in the console.

**Flexible Method Signatures:**

You can define the handler method with the arguments you need. The framework will automatically provide them.

```csharp
public class PlayerStatsPresenter : BaseObservablePresenter<PlayerStatsModel>
{
    // 1. Get only the new value (most common)
    [ObservableHandler(nameof(PlayerStatsModel.Level))]
    private void OnLevelChanged(int newLevel)
    {
        levelText.text = $"Level: {newLevel}";
    }

    // 2. Get the new and old values
    [ObservableHandler(nameof(PlayerStatsModel.Health))]
    private void OnHealthChanged(int newHealth, int oldHealth)
    {
        Debug.Log($"Health changed from {oldHealth} to {newHealth}");
        healthBar.fillAmount = newHealth / 100f;
    }

    // 3. Get the raw variable object for more complex scenarios
    [ObservableHandler(nameof(PlayerStatsModel.PlayerName))]
    private void OnNameChanged(IObservableVariable variable)
    {
        var nameVar = (ObservableVariable<string>)variable;
        nameplateText.text = nameVar.Value;
    }
}
```

### 5. Loader (Save/Load Integration)

The framework is designed to be **agnostic of any specific save/load system**. Instead of providing a concrete implementation, it offers base classes that you can inherit from to easily integrate with your own save/load solution.

#### Base Classes

*   **`LoaderWithModel<M>`**: The base class for a loader that is associated with a `Model`.
*   **`LoaderWithModelAndLogic<M, LO>`**: Inherits from `LoaderWithModel<M>` and adds a `GetLogic()` method for convenience.

These classes provide two key methods for integration:

*   **`public M GetModelToSave()`**: Call this from your save system to get the strongly-typed model component whose data you want to serialize and save.
*   **`public virtual void LoadDataFromModel(object state)`**: Call this from your save system after loading and deserializing data. This method applies the state to the model and crucially, sets up the reactive event listeners for the `Presenter`.

#### Example Implementation

Here is how you would create your own loader that integrates with your own save system interface (e.g., `ISaveable`).

**1. Your Save System Interface (Example)**

This is an interface from **your** project, not from the framework.

```csharp
public interface ISaveable
{
    object SaveState();
    void LoadState(object state);
}
```

**2. Your Concrete Loader**

You create a class that inherits from `LoaderWithModel` (or `LoaderWithModelAndLogic`) and also implements **your** `ISaveable` interface.

```csharp
public class MyPlayerLoader : LoaderWithModel<PlayerDataModel>, ISaveable
{
    // Implement the SaveState method from your ISaveable interface
    public object SaveState()
    {
        // Call the helper method from the framework's base class
        return GetModelToSave();
    }

    // Implement the LoadState method from your ISaveable interface
    public void LoadState(object state)
    {
        // Call the helper method from the framework's base class
        LoadDataFromModel(state);
    }
}
```

This approach gives you full control over how and when you save/load data, while the framework's base classes handle the complex work of connecting the `Model` and `Presenter`.

## How to Use the Framework

Here's a step-by-step guide to implementing the MMVC framework in your Unity project:

1.  **Create the Model**:
    * Create a new C# script that inherits from `BaseObservableDataModel`. 
    * Add `ObservableVariable` fields for each piece of data you want to track.

2.  **Create the Logic**:
    * Create a new C' script that inherits from `BaseLogic<YourModel>`. 
    * Implement methods to handle your game's business logic.
 
3.  **Create the Presenter**:
    * Create a new C# script that inherits from `BaseObservablePresenter<YourModel>`.
    * Add references to your UI elements in the script.
    * Choose your event handling strategy: either override the single `OnModelValueChanged` method, or create individual methods marked with the `[ObservableHandler]` attribute.

4.  **Create Your Loader**:
    * Create a new C# script that inherits from `LoaderWithModel<YourModel>` or `LoaderWithModelAndLogic<YourModel, YourLogic>`.
    * Implement the interface of **your** save/load system (e.g., `ISaveable`).
    * In your implementation, call the `GetModelToSave()` and `LoadDataFromModel(state)` methods from the base class.
 
5.  **Set Up the GameObject**:
    * In the Unity Editor, create a new `GameObject`.
    * Attach your `Model`, `Logic`, `Presenter`, and your new `Loader` script to the `GameObject`.
    * In the Inspector, connect the UI element references in your `Presenter`.

	## Usage Example
	
Here is a complete example of a simple component that displays a status and a timer.
	
### 1. The Model (`ComponentDataModel.cs`)
	
The model defines the data. Note that you don't need to initialize the variables; the `InitFields()` method in the base class will handle this automatically using reflection.
	
```csharp
	[Serializable]
	public class ComponentDataModel : BaseObservableDataModel {
	    [SerializeReference] public ObservableVariable<string> status;
	    [SerializeReference] public ObservableVariable<bool> newVersionTimerEnabled;
	}
```
	
### 2. The Logic (`ComponentLogic.cs`)
	
The logic contains the methods that change the model's state.
	
```csharp
	public class ComponentLogic : BaseLogic<ComponentDataModel> {
	
	    public void TimerStarts() {
	        GetModel().status.Value = "InDevelopment";
	        GetModel().newVersionTimerEnabled.Value = true;
	    }
	}
```
	
### 3. The Presenter (`ComponentPresenter.cs`)
	
The presenter listens for changes in the model and updates the UI. Note that `OnModelValueChanged` is now required due to the base class being abstract.
	
```csharp
	public class ComponentPresenter : ObservablePresenterWithLogic<ComponentDataModel,ComponentLogic> {
	    
	    [SerializeField] private TextMeshProUGUI versionTextTMP;
	    [SerializeField] private TextMeshProUGUI newVersionTimerTextTMP;
	
	    public override void OnModelValueChanged(IObservableVariable variable) {
	        switch (variable.Name) {
	            case var name when name == GetModel().newVersionTimerEnabled.Name:
	                // Use the bool value to format the text
	                versionTextTMP.text = GetModel().newVersionTimerEnabled.Value ? "Timer: ON" : "Timer: OFF";
	                break;
	            case var name when name == GetModel().status.Name:
	                // Use the string value to control a boolean property
	                newVersionTimerTextTMP.enabled = GetModel().status.Value == "InDevelopment";
	                break;
	            default:
	                break;
	        }
	    }
	 }
```
	
This example demonstrates the core principle of the framework: the `Presenter` listens for notifications via `OnModelValueChanged` and then reads the strongly-typed data directly from the `Model` to update the view, completely avoiding casting.

### Manual Event Subscription

In addition to the automatic subscription in `BaseObservablePresenter`, you can manually subscribe to the `OnValueChanged` event of any `ObservableVariable` from any class. This is useful for cross-component communication.

The event handler method must accept a single argument of type `IObservableVariable`. Inside the method, you can use pattern matching (`is`) to safely cast the variable to its concrete type and access its `Value`.

```csharp
public class SomeOtherClass : MonoBehaviour
{
	   [SerializeField] private PlayerDataModel playerDataModel; // Reference to a model

	   private void Start()
	   {
	       // Subscribe to the event
	       playerDataModel.level.OnValueChanged += LevelChanged;
	   }

	   private void OnDestroy()
	   {
	       // Don't forget to unsubscribe!
	       playerDataModel.level.OnValueChanged -= LevelChanged;
	   }

	   public void LevelChanged(IObservableVariable variable)
	   {
	       // Use pattern matching to safely get the concrete type and value
	       if (variable is ObservableVariable<int> levelVariable)
	       {
	           int newLevel = levelVariable.Value;
	           Debug.Log($"Player level changed to: {newLevel}");
	       }
	   }
}
```

## Performance & Best Practices

### Boxing and Value Types

The framework is designed to be efficient and avoid common performance pitfalls in Unity.

*   **No Boxing for Value Types**: Because `ObservableVariable<T>` is a generic class, when you use it with value types (`int`, `float`, `bool`, `Vector3`, etc.), no boxing occurs when the value is set or retrieved. This prevents unnecessary memory allocations and garbage collection.
*   **Efficient Event Handling**: The automatic subscription system uses compiled expressions or delegates, which are highly performant after the initial setup.

### Memory Management and Unsubscribing

*   **Automatic Subscriptions**: For events subscribed automatically via `BaseObservablePresenter` (either through `OnModelValueChanged` or `[ObservableHandler]` attributes), the framework manages the lifetime of the subscription. You do not need to manually unsubscribe.
*   **Manual Subscriptions**: If you manually subscribe to an `OnValueChanged` event from another class (as shown in the "Manual Event Subscription" section), it is **crucial** that you unsubscribe from the event when your listening object is destroyed, typically in the `OnDestroy()` method. Failure to do so can lead to memory leaks, as the `ObservableVariable` will hold a reference to your destroyed object, preventing it from being garbage collected.

```csharp
private void OnDestroy()
{
    // Always match a subscription with an unsubscription!
    playerDataModel.level.OnValueChanged -= LevelChanged;
}
```

# Integrating Singletons with the MMVC Framework

This document explains how to integrate globally accessible manager classes (using a Singleton/Service Locator pattern) with your existing MMVC architecture. This pattern is ideal for central systems like a game manager, sound manager, or data store that need to be accessed from many different parts of your application without creating complex dependencies.

The approach uses a central static `Services` class (a Service Locator) to register and retrieve "singleton" instances that are identified by an `ISharedSingleton` interface.

## The Core Concept: `ISharedSingleton` and the `Services` Locator

The system relies on three key parts: a marker interface, a central registry, and an initialization process.

### 1. The `ISharedSingleton` Interface

This is a simple "marker" interface. It has no methods or properties. Its sole purpose is to mark a MonoBehaviour as a global service that should be registered with the central Services registry.

```csharp
namespace Assets.Scripts.Base {
    public interface ISharedSingleton {              
    }
}
```

### 2. The Initialization Process (`InitSingletones`)

This method, which should be called once when the application starts, is responsible for finding, registering, and validating al singleton services.

Here is a breakdown of how it works:

```csharp
private void InitSingletones() {
    // 1. Find all MonoBehaviours in the project, including inactive ones.
    var sharedServicesList = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                .AsValueEnumerable()
                                .OfType<ISharedSingleton>(); // 2. Filter for only those that implement ISharedSingleton.
    
    foreach (var service in sharedServicesList) {
        var mb = (MonoBehaviour)service;
        if (mb.gameObject.scene.name != null) { // 3. Ensure it's a scene object, not a prefab asset.
            
            // 4. Try to add the service to the central registry.
            if (Services.Add(service) != null) { 
                
                // 5. If successful, integrate with the MMVC framework by setting up listeners.
                SavableObservable.Observable.SetListeners(service);
            } else {
                // 6. If a service of this type is already registered, enforce the Singleton pattern by quitting.
                Debug.Log($"You have more than 1 instannce of {service.GetType()} with {Type.GetType(typeof(ISharedSingleton).FullName)} interface.");
                UnityEngine.Application.Quit();
#if UNITY_EDITOR
                EditorApplication.ExitPlaymode();
#endif
            }
        }
    }
}
```
This process creates a robust, auto-registering system for your global managers.

### Step 1: Create Your Component with MMVC
 
First, build your component as you normally would using the MMVC pattern. We will use the `Component` example from the previous section.
 
### Step 2: Mark the Presenter as a Singleton
 
In the `ComponentPresenter`, implement the `ISharedSingleton` interface. This marks it for automatic registration on startup.
 
```csharp
public class ComponentPresenter
    : ObservablePresenterWithLogic<ComponentDataModel, ComponentLogic>, ISharedSingleton
{
    // ... all your normal presenter code from the example above
}
```
 
### Step 3: Place it in Your Scene
 
Ensure that the `GameObject` with your `ComponentPresenter` (and its related `ComponentDataModel` and `ComponentLogic` scripts) is present in your game's initial scene. The `InitSingletones` method will find and register it on startup.
 
### Step 4: Access the Service from Other Components
 
Now, any other component in your project can easily access this global `Component` and interact with its `Logic`.
 
**Example: `AnotherComponent.cs`**
 
Imagine you have another `MonoBehaviour` in your scene. It can retrieve the `ComponentLogic` from the `Services` registry and call its methods.
 
```csharp
using UnityEngine;
 
public class AnotherComponent : MonoBehaviour {
 
    void Start() {
        // 1. Retrieve the globally registered ComponentPresenter.
        var componentPresenter = Services.Get<ComponentPresenter>();
 
        if (componentPresenter != null) {
            // 2. Get its Logic component.
            var componentLogic = componentPresenter.GetLogic();
 
            // 3. Call a method on the Logic to change the model's state.
            // The ComponentPresenter will automatically react to this change and update its UI.
            componentLogic.TimerStarts();
        }
    }
}
```
Conclusion
By combining the `ISharedSingleton` interface with a `Services` locator and an initialization routine, you can seamlessly integrate global manager classes into your MMVC framework. This gives you the best of both worlds:

Decoupled Components: Individual components like PlayerChip don't need hard references to managers.

Centralized Access: You have a single, reliable point of access (`Services.Get<T>()`) for all global systems.

Reactive Singletons: Because `SetListeners` is called on registered services, your global managers can fully participate in the reactive data-binding of the MMVC framework.


