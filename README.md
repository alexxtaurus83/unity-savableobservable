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
*   It **must** implement the `public override void OnModelValueChanged(IObservableVariable variable)` method. This is enforced at compile time.
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

### 4. Loader

The `Loader` is a unique and powerful component that acts as the composition root. It's responsible for wiring together the `Model`, `Logic`, and `Presenter`. It also handles the saving and loading of the `Model`)'s state.

**Key Features:**

* It inherits from `BaseLoader<M>`. 
* It implements the `ISaveable` to integrate with a save/load system. 
* The `PostInstantiation` method is used to restore references and state after loading a saved game. 

**Example: `PipelineLoader.cs`**

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
    * Implement the required `public override void OnModelValueChanged(IObservableVariable variable)` method to update the UI. Inside this method, use a `switch` on `variable.Name` to handle changes for different fields.

4.  **Create the Loader**:
    * Create a new C# script that inherits from `BaseLoader<YourModel>`. 
    * If your `Presenter` also has `Logic`, you can use `LoaderWithModelAndLogic<YourModel, YourLogic>`. 

5.  **Set Up the GameObject**:
    * In the Unity Editor, create a new `GameObject`.
    * Attach your `Model`, `Logic`, `Presenter`, and `Loader` scripts to the `GameObject`.
    * If you're using a save/load system, also attach a `SaveableEntity` component, as required by `BaseLoader`. 
    * In the Inspector, connect the UI element references in your `Presenter`.

	## Usage Example
	
Here is a complete example of a simple component that displays a status and a timer.
	
### 1. The Model (`ComponentDataModel.cs`)
	
The model defines the data. Note that you don't need to initialize the variables; the `InitFields()` method in the base class will handle this automatically using reflection.
	
	```csharp
	[Serializable]
	public class ComponentDataModel : BaseObservableDataModel {
	    [SerializeReference] public Observable.ObservableString status;
	    [SerializeReference] public Observable.ObservableBoolean newVersionTimerEnabled;
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


Here the example how you can add new types:
```csharp
    public class ExtObservable {
        internal static class ObservableTypeRegistrar {
            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
            private static void RegisterCustomObservableTypes() {                   
                Observable.ObservableTypes.types.Add(typeof(ObservableAtaxxPlayerColorEnum), typeof(AtaxxAIEngine.PlayerColor));
                
            }
        }

        [Serializable] public class ObservableAtaxxPlayerColorEnum : ObservableVariable<AtaxxAIEngine.PlayerColor> { public ObservableAtaxxPlayerColorEnum(string name) : base(name) { } }
    }
}
```
