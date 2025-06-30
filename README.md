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
* It can implement `OnModelValueChanged` methods, which are automatically registered as listeners for the `Model`)'s `ObservableVariable` fields by the `SavableObservableExtensions` class.

### 3. Presenter

The `Presenter` is the "View" component of the framework. It's responsible for displaying the data from the `Model` to the user and handling user input. It observes the `Model` for changes and updates the UI accordingly.

**Key Features: **

* It inherits from `BaseObservablePresenter<M>` or `ObservablePresenterWithLogic<M, LO>`. 
* It has references to UI elements (e.g., `Button`, `TextMeshProUGUI`).
* It implements `OnModelValueChanged` methods to update the UI when the `Model` changes.

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
    * Create `OnModelValueChanged` methods to react to changes in the `Model`.

3.  **Create the Presenter**:
    * Create a new C# script that inherits from `BaseObservablePresenter<YourModel>`. 
    * Add references to your UI elements in the script.
    * Implement `OnModelValueChanged` methods to update the UI when the `Model` changes.

4.  **Create the Loader**:
    * Create a new C# script that inherits from `BaseLoader<YourModel>`. 
    * If your `Presenter` also has `Logic`, you can use `LoaderWithModelAndLogic<YourModel, YourLogic>`. 

5.  **Set Up the GameObject**:
    * In the Unity Editor, create a new `GameObject`.
    * Attach your `Model`, `Logic`, `Presenter`, and `Loader` scripts to the `GameObject`.
    * If you're using a save/load system, also attach a `SaveableEntity` component, as required by `BaseLoader`. 
    * In the Inspector, connect the UI element references in your `Presenter`.

	## Usage Examples

Here are some examples of how the MMVC framework is used in the provided code:

### Pipeline Example

The `Pipeline` set of scripts (`PipelineDataModel`, `PipelineLogic`, `PipelinePresenter`, `PipelineLoader`) demonstrates a complete implementation of the framework for managing a CI/CD-like pipeline in the game.

* **`PipelineDataModel.cs`**: Defines the data for a pipeline, such as its status, CPU-RAM usage, and logs.
* **`PipelineLogic.cs`**: Contains the logic for starting, stopping, and managing the pipeline's execution. It also handles the generation of logs and the calculation of resource usage.
* **`PipelinePresenter.cs`**: Updates the UI to reflect the pipeline's state, including the push button text, resource usage displays, and timer. 
* **`PipelineLoader.cs`**: Manages the saving and loading of the pipeline components. The `PostInstantiation` method is used to restore the pipeline's state after loading, including the progress of any running blocks and timers.

### Socket Example

The `Socket` scripts (`SocketDataModel`, `SocketLogic`, `SocketPresenter`) show how the framework can be used for smaller, reusable components.

* **`SocketDataModel.cs`**: Holds the data for a socket, such as the currently installed block and its status.
* **`SocketLogic.cs`**: Handles the logic for installing and removing blocks from the socket when buttons are clicked.
* **`SocketPresenter.cs`**: Updates the UI to show the installed block, its name, and status indicators.


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
                Extensions.SetListeners(service);
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

### Step 1: Create Your Manager with MMVC

First, build your manager class as you normally would using the MMVC pattern. For example, you might create an `AtaxxGameManager` with its own `AtaxxGameManagerDataModel`, `AtaxxGameManagerLogic`, and `AtaxxGameManagerPresenter`.

### Step 2: Mark it as a Singleton

In the primary `MonoBehaviour` class for your manager (e.g., `AtaxxGameManagerPresenter`), implement the `ISharedSingleton` interface.

```csharp
public class AtaxxGameManagerPresenter 
    : ObservablePresenterWithLogic<AtaxxGameManagerDataModel, AtaxxGameManagerLogic>, ISharedSingleton 
{
    // ... all your normal presenter code
}
```

### Step 3: Place it in Your Scene

Ernsure that the `GameObject` with your `Presenter` (and its related Model and Logic components) is present in your game's initial scene. The `InitSingletones` method will find it on startup.

### Step 4: Access the Service from Other Components

Now, any other component can easily access this global manager through the `Services` locator. This is demonstrated in the `Presenter` components.

**Example: `PlayerChipLogic.cs`**

The `PlayerChipLogic` needs to communicate with the central game manager to get game state and execute moves. Instead of requiring a manual link, it simply requests the manager from the `Services` registry in its `Awake` method.

```csharp
public class PlayerChipLogic : BaseLogic<PlayerChipDataModel> {

    public AtaxxGameManagerLogic managerLogic;

    private void Awake() {
        // Retrieve the globally registered game manager's logic.
        managerLogic = Services.Get<AtaxxGameManagerPresenter>().GetLogic();
        GetComponent<Button>()?.onClick.AddListener(OnClick);
    }

    private void OnClick() {
        // Now it can use the manager's logic to check game state.
        if (managerLogic.GetModel().isGameOver.Value) return;

        // ...
    }
}
```

**Example: `PlayerChipPresenter.cs`**

Similarly, the `PlayerChipPresenter` can access the service to get resources, like the correct sprites for the player colors.

```csharp
public class PlayerChipPresenter : ObservablePresenterWithLogic<PlayerChipDataModel, PlayerChipLogic> {    

    public void OnModelValueChanged(AtaxxAIEngine.PlayerColor previous, AtaxxAIEngine.PlayerColor current, string name) {
        if (name == GetModel().owner.Name) {
            // ...
            var managerLogic = Services.Get<AtaxxGameManagerPresenter>().GetLogic();
            // ...

            switch (current) {
                case AtaxxAIEngine.PlayerColor.Red:
                    SetImageAndColor(image, managerLogic.red, color);
                    break;
                //...
            }
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
