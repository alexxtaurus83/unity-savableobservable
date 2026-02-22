# MMVC Framework for Unity

## Introduction

The MMVC (Model-Model-View-Controller) framework is a custom-designed architectural pattern for Unity, tailored to facilitate a clean separation of concerns, enhance data binding, and streamline data persistence. It's a powerful approach that blends concepts from traditional MVC and MVVM patterns to offer a reactive, scalable, and maintainable structure for your Unity projects.

At its core, the framework is built on these key principles:

* **Reactive Data Binding**: The `ObservableVariable<T>` class allows UI elements and game logic to automatically react to data changes without tight coupling.
* **Separation of Concerns**: Each component has a distinct responsibility: data (`Model`), business logic (`Logic`), and presentation (`Presenter`).
* **Centralized Lifecycle Management**: The `Loader` component acts as a central point for creating, managing, and saving/loading the other components.
* **Convention over Configuration**: The framework uses reflection to automatically wire up dependencies, reducing boilerplate code and simplifying setup.
* **Attribute-Based Binding**: Use `[ObservableHandler]` and `[AutoBind]` attributes to declaratively connect observables to handlers and UI elements.
* **Automatic Component Requirements**: In the Unity Editor, when you add core MMVC classes, missing dependencies are auto-added on the same GameObject (no `[RequireComponent]` attributes required in your concrete classes).

## Core Components

The MMVC framework is comprised of four main components:

### 1. Model

The `Model` is the data layer of your application. It holds the state and is responsible for the application's data. In the MMVC framework, the Model is represented by a class that inherits from `BaseObservableDataModel`. 

**Key Features:**

* It uses `ObservableVariable<T>` fields to store data, which automatically notify any listeners when their value changes.
* The `EnsureFieldsInitialized()` method automatically initializes any null `ObservableVariable` fields and sets up parent references for cleanup.
* **Mixed Data Types**: You can include regular, non-observable fields (like `int`, `string`, `List<T>`) alongside `ObservableVariable` fields in your model. The save/load system will correctly handle both.

### ObservableList<T>

For collections, the framework provides `ObservableList<T>`, a fully reactive list implementation.

**Key Features:**

*   **Reactivity**: Similar to `ObservableVariable`, `ObservableList` notifies listeners whenever the list is modified (Add, Remove, Clear, etc.).
*   **Previous Value**: It maintains a snapshot of the list state before the last modification, accessible via `PreviousValue`.
*   **Unity Editor Support**: Fully integrated with the Unity Inspector, including undo/redo support and validation.

```csharp
[Serializable]
public class InventoryModel : BaseObservableDataModel {
    // A reactive list of items
    public ObservableList<string> items = new ObservableList<string>();
}
```

Usage in Logic:

```csharp
public void AddItem(string item) {
    GetModel().items.Add(item); // Triggers OnChanged
}
```

Usage in Presenter:

```csharp
protected override void Start() {
    base.Start();
    
    // Subscribe to list changes
    GetModel().items.OnChanged.Add(OnInventoryChanged, this);
}

private void OnInventoryChanged(ObservableList<string> list) {
    Debug.Log($"Inventory updated. Count: {list.Count}");
    
    // Access previous state if needed
    foreach (var item in list.PreviousValue) { ... }
}
```

**Example Model:**

```csharp
[Serializable]
public class PlayerDataModel : BaseObservableDataModel {
    public ObservableVariable<string> playerName;
    public ObservableVariable<int> health;
    public ObservableVariable<int> score;
    public ObservableVariable<bool> isAlive;
    
    // Regular fields also supported
    public List<string> achievements;
}
```

### 2. Logic

The `Logic` component acts as the "Controller" or "ViewModel." It contains the business logic that manipulates the `Model` and responds to events. It listens for changes in the `Model` and executes the appropriate actions.

**Key Features:**

* It inherits from `BaseLogic<M>`, where `M` is the type of the `Model`.
* Its primary role is to contain business logic that manipulates the `Model`.

```csharp
public class PlayerLogic : BaseLogic<PlayerDataModel> {

    public void TakeDamage(int damage) {
        var health = GetModel().health.Value - damage;
        GetModel().health.Value = Mathf.Max(0, health);
        
        if (GetModel().health.Value <= 0) {
            GetModel().isAlive.Value = false;
        }
    }
    
    public void AddScore(int points) {
        GetModel().score.Value += points;
    }
}
```

### 3. Presenter Hierarchy

The framework provides two base classes for presenters, allowing you to choose the right one for your needs.

#### Automatic Dependency Provisioning (Editor)

The framework auto-adds required components in the Unity Editor (during `Reset` / `OnValidate`) so concrete classes no longer need explicit `[RequireComponent]` attributes.

| Base Class | Auto-added dependency |
|------------|------------------------|
| `BasePresenter<M>` | `M` (Model) |
| `ObservablePresenterWithLogic<M, L>` | `M` (via base) + `L` (Logic) |
| `BaseLogic<M>` | `M` (Model) |
| `LoaderWithModel<M>` | `M` (Model) |
| `LoaderWithModelAndLogic<M, L>` | `M` (via base) + `L` (Logic) |

This keeps setup simple: add your main class and the framework fills missing MMVC components automatically on the same GameObject in Editor mode.

#### `BasePresenter<M>`

This is the simplest presenter. It should be used when you have a `Model` that does **not** contain any `ObservableVariable` fields. It provides a `GetModel()` method but does not have any built-in reactivity.

#### `BaseObservablePresenter<M>`

This is the reactive presenter, which inherits from `BasePresenter<M>`. It's designed to work with a `Model` that uses `ObservableVariable` fields.

**Key Features:**

*   It inherits from `BasePresenter<M>`.
*   It has references to UI elements (e.g., `Button`, `TextMeshProUGUI`).
*   It provides two powerful, declarative ways to handle model changes:
    * `[AutoBind]` attribute for simple UI bindings
    * `[ObservableHandler]` attribute for custom handler methods
*   **Automatic Setup Validation**: It includes a check that will log a warning if `Observable.SetListeners()` was not called for it.

#### `ObservablePresenterWithLogic<M, L>`

This presenter extends `BaseObservablePresenter<M>` and adds a `GetLogic()` method for convenient access to the Logic component.

```csharp
public class PlayerPresenter : ObservablePresenterWithLogic<PlayerDataModel, PlayerLogic> {
    
    [AutoBind("health")]
    [SerializeField] private TextMeshProUGUI healthText;
    
    [AutoBind("score")]
    [SerializeField] private TextMeshProUGUI scoreText;
    
    [ObservableHandler("isAlive")]
    private void OnIsAliveChanged(bool isAlive) {
        if (!isAlive) {
            ShowGameOverScreen();
        }
    }
}
```

### 4. Event Handling in Presenters

The framework offers two powerful, declarative approaches to handle `ObservableVariable` changes in your presenters. These can be used together on the same presenter.

#### Approach 1: AutoBind (Recommended for Simple UI Updates)

The `[AutoBind]` attribute provides zero-code UI binding. Simply mark a UI field with the attribute, and the framework automatically updates it when the observable value changes.

**Using `nameof()` for Type-Safety (Recommended)**

Instead of using plain string literals, use `nameof()` to get compile-time safety:

```csharp
public class BlockDetailsPresenter : BaseObservablePresenter<BlockDetailsModalDataModel> {
    
    // Type-safe binding using nameof() - compile error if field is renamed
    [AutoBind(nameof(BlockDetailsModalDataModel.processingBlockExecTime))]
    [SerializeField] private Text processingBlockExecTime;
    
    [AutoBind(nameof(BlockDetailsModalDataModel.processingBlockFixTime))]
    [SerializeField] private Text processingBlockFixTime;
}
```

**String Literal Binding**

You can also use plain strings if you prefer:

```csharp
public class GamePresenter : BaseObservablePresenter<GameDataModel> {
    
    // Auto-bind to model.playerScore - converts to string automatically
    [AutoBind("playerScore")]
    [SerializeField] private TextMeshProUGUI scoreText;
    
    // Auto-bind to model.isActive - sets Toggle.isOn
    [AutoBind("isActive")]
    [SerializeField] private Toggle activeToggle;
    
    // Auto-bind to model.buttonLabel - sets text on child TMP_Text
    [AutoBind("buttonLabel")]
    [SerializeField] private Button actionButton;
    
    // If field name matches observable name exactly, you can omit the parameter
    [AutoBind]
    [SerializeField] private TextMeshProUGUI playerName; // Binds to model.playerName
}
```

**Supported UI Types with Built-in Adapters:**

| UI Type | Behavior |
|---------|----------|
| `TextMeshProUGUI` / `TMP_Text` | Sets `text` to `value.ToString()` |
| `Text` (Unity UI) | Sets `text` to `value.ToString()` |
| `Toggle` | **Two-way binding:** Sets `isOn` from value, and updates value when `isOn` changes. |
| `TMP_InputField` | **Two-way binding:** Sets `text` from value, and updates value when `text` changes. Supports automatic conversion for basic types (int, float, bool, etc.). **Note:** Complex types like `Vector2` are not supported for auto-binding. |
| `InputField` (Unity UI) | **Two-way binding:** Sets `text` from value, and updates value when `text` changes. Supports automatic conversion for basic types (int, float, bool, etc.). **Note:** Complex types like `Vector2` are not supported for auto-binding. |
| `Button` | Sets text on child `TMP_Text` component |
| `Image` | Sets `sprite` to Sprite value |

**Custom Adapters:**

The framework provides two interfaces for UI adapters, following the Interface Segregation Principle:

| Interface | Purpose | Methods |
|-----------|---------|---------|
| [`IUIAdapter`](Runtime/UIAdapters.cs:13) | Display-only / Model→UI only | `CanHandle(Type)`, `Priority`, `SetValue(object, object, Type)` |
| [`IUIListenerAdapter`](Runtime/UIAdapters.cs:34) | Interactive / Two-way binding | Extends `IUIAdapter` + `AddListener(object, Action<object>, Type)`, `RemoveListener(object, object)` |

### How to Create Custom UI Adapters

#### Display-Only Adapters (Model→UI)

Use [`IUIAdapter`](Runtime/UIAdapters.cs:13) for components that only display data and don't need to propagate user input back to the model:

```csharp
using System;
using UnityEngine;
using SavableObservable;

// Display-only adapter for a progress bar
public class ProgressBarAdapter : IUIAdapter {
    public int Priority => 100;

    public bool CanHandle(Type uiComponentType) {
        return typeof(ProgressBar).IsAssignableFrom(uiComponentType);
    }

    public void SetValue(object uiComponent, object value, Type valueType) {
        if (uiComponent is ProgressBar progressBar) {
            if (value is float floatValue) {
                progressBar.fillAmount = floatValue;
            }
        }
    }
}
```

#### Interactive Adapters (Two-Way Binding)

Use [`IUIListenerAdapter`](Runtime/UIAdapters.cs:34) for components that need to propagate user input back to the model:

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;
using SavableObservable;

// Interactive adapter for Slider - supports two-way binding
public class SliderAdapter : IUIListenerAdapter {
    public int Priority => 100;

    public bool CanHandle(Type uiComponentType) {
        return typeof(Slider).IsAssignableFrom(uiComponentType);
    }

    public void SetValue(object uiComponent, object value, Type valueType) {
        if (uiComponent is Slider slider) {
            if (value is float floatValue)
                slider.value = floatValue;
            else if (value is int intValue)
                slider.value = intValue;
        }
    }

    // Returns an opaque token used for listener removal
    public object AddListener(object uiComponent, Action<object> onValueChanged, Type valueType) {
        if (uiComponent is Slider slider) {
            UnityAction<float> listener = val => onValueChanged(val);
            slider.onValueChanged.AddListener(listener);
            return listener; // Return the listener as the token
        }
        return null;
    }

    public void RemoveListener(object uiComponent, object token) {
        if (uiComponent is Slider slider && token is UnityAction<float> listener) {
            slider.onValueChanged.RemoveListener(listener);
        }
    }
}
```

#### Registering Custom Adapters

Register your adapters at runtime using [`UIAdapterRegistry.RegisterAdapter()`](Runtime/UIAdapters.cs:71):

```csharp
using SavableObservable;

public class AdapterSetup : MonoBehaviour {
    void Awake() {
        UIAdapterRegistry.RegisterAdapter(new SliderAdapter());
        UIAdapterRegistry.RegisterAdapter(new ProgressBarAdapter());
    }
}
```

#### Adapter Lookup

The registry provides two methods for adapter lookup:

- [`UIAdapterRegistry.GetAdapter(Type)`](Runtime/UIAdapters.cs:89) — Returns an [`IUIAdapter`](Runtime/UIAdapters.cs:13) for the given UI component type, or `null` if none found.
- [`UIAdapterRegistry.TryGetListenerAdapter(Type, out IUIListenerAdapter)`](Runtime/UIAdapters.cs:166) — Returns `true` and provides an [`IUIListenerAdapter`](Runtime/UIAdapters.cs:34) if the adapter supports two-way binding. Use this when you need to check if an adapter can propagate user input back to the model.

#### Migration Note

If you have existing custom adapters from a previous version:

1. **Display-only adapters**: Remove any no-op `AddListener`/`RemoveListener` implementations and implement only [`IUIAdapter`](Runtime/UIAdapters.cs:13).
2. **Interactive adapters**: Move listener methods into the [`IUIListenerAdapter`](Runtime/UIAdapters.cs:34) interface implementation. The `AddListener` method now returns an opaque token (instead of `void`) for proper listener cleanup.

#### Approach 2: ObservableHandler (For Custom Logic)

When you need more than simple UI updates, use the `[ObservableHandler]` attribute to create dedicated handler methods.

**Flexible Method Signatures:**

You can define the handler method with the arguments you need. The framework will automatically provide them.

```csharp
public class PlayerStatsPresenter : BaseObservablePresenter<PlayerStatsModel> {
    
    // Option A: No parameters - access model directly
    [ObservableHandler("level")]
    private void OnLevelChanged() {
        levelText.text = $"Level: {GetModel().level.Value}";
        PlayLevelUpAnimation();
    }

    // Option B: One parameter - receive the new value directly
    [ObservableHandler("health")]
    private void OnHealthChanged(int newHealth) {
        healthBar.fillAmount = newHealth / 100f;
    }

    // Option C: Two parameters - receive new and previous values
    [ObservableHandler("experience")]
    private void OnExperienceChanged(int current, int previous) {
        if (current > previous) {
            ShowExperienceGainPopup(current - previous);
        }
    }
}
```

#### Combining AutoBind and ObservableHandler

You can use both approaches in the same presenter. Use `[AutoBind]` for simple bindings and `[ObservableHandler]` for complex logic:

```csharp
public class GameManagerPresenter : ObservablePresenterWithLogic<GameDataModel, GameLogic> {
    
    // Simple bindings - use AutoBind
    [AutoBind("redScore")]
    [SerializeField] private TextMeshProUGUI redScoreText;
    
    [AutoBind("blueScore")]
    [SerializeField] private TextMeshProUGUI blueScoreText;
    
    [AutoBind("statusMessage")]
    [SerializeField] private TextMeshProUGUI statusText;
    
    // Complex logic - use ObservableHandler
    [ObservableHandler("isGameOver")]
    private void OnGameOverChanged(bool isGameOver) {
        if (isGameOver) {
            var winner = GetLogic().DetermineWinner();
            ShowGameOverScreen(winner);
            PlayEndGameSound();
        }
    }
    
    [ObservableHandler("currentPlayer")]
    private void OnCurrentPlayerChanged(PlayerType player) {
        if (player == PlayerType.AI) {
            GetModel().statusMessage.Value = "AI is thinking...";
            GetLogic().StartAITurn();
        } else {
            GetModel().statusMessage.Value = "Your turn";
        }
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
public interface ISaveable {
    object SaveState();
    void LoadState(object state);
}
```

**2. Your Concrete Loader**

You create a class that inherits from `LoaderWithModel` (or `LoaderWithModelAndLogic`) and also implements **your** `ISaveable` interface.

```csharp
public class MyPlayerLoader : LoaderWithModel<PlayerDataModel>, ISaveable {
    
    // Implement the SaveState method from your ISaveable interface
    public object SaveState() {
        // Call the helper method from the framework's base class
        return GetModelToSave();
    }

    // Implement the LoadState method from your ISaveable interface
    public void LoadState(object state) {
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
    * Add `ObservableVariable<T>` fields for each piece of data you want to track.

2.  **Create the Logic**:
    * Create a new C# script that inherits from `BaseLogic<YourModel>`. 
    * Implement methods to handle your game's business logic.
 
3.  **Create the Presenter**:
    * Create a new C# script that inherits from `BaseObservablePresenter<YourModel>` or `ObservablePresenterWithLogic<YourModel, YourLogic>`.
    * Add references to your UI elements in the script.
    * Use `[AutoBind]` attributes for simple UI bindings.
    * Use `[ObservableHandler]` attributes for methods that need custom logic.

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

The model defines the data. Note that you don't need to initialize the variables; the `EnsureFieldsInitialized()` method in the base class will handle this automatically.

```csharp
[Serializable]
public class ComponentDataModel : BaseObservableDataModel {
    public ObservableVariable<string> status;
    public ObservableVariable<bool> timerEnabled;
    public ObservableVariable<float> timerValue;
}
```

### 2. The Logic (`ComponentLogic.cs`)

The logic contains the methods that change the model's state.

```csharp
public class ComponentLogic : BaseLogic<ComponentDataModel> {

    public void StartTimer() {
        GetModel().status.Value = "Timer Running";
        GetModel().timerEnabled.Value = true;
    }
    
    public void UpdateTimer(float deltaTime) {
        if (GetModel().timerEnabled.Value) {
            GetModel().timerValue.Value += deltaTime;
        }
    }
}
```

### 3. The Presenter (`ComponentPresenter.cs`)

The presenter uses `[AutoBind]` for simple bindings and `[ObservableHandler]` for complex logic.

```csharp
public class ComponentPresenter : ObservablePresenterWithLogic<ComponentDataModel, ComponentLogic> {
    
    // Simple bindings - use AutoBind
    [AutoBind("status")]
    [SerializeField] private TextMeshProUGUI statusText;
    
    [AutoBind("timerValue")]
    [SerializeField] private TextMeshProUGUI timerText;
    
    // Complex logic - use ObservableHandler
    [ObservableHandler("timerEnabled")]
    private void OnTimerEnabledChanged(bool enabled) {
        // Control the visibility of a timer panel
        timerPanel.SetActive(enabled);
        
        if (enabled) {
            PlayTimerStartSound();
        }
    }
    
    [SerializeField] private GameObject timerPanel;
}
```

This example demonstrates the core principle of the framework: use declarative attributes to bind data to UI, keeping your presenter code clean and focused on presentation logic.

### Manual Event Subscription

In addition to the automatic subscription via attributes, you can manually subscribe to the `OnValueChanged` event of any `ObservableVariable` from any class. This is useful for cross-component communication. There are two ways to subscribe: using the **`+=` operator** or using the **`.Add()` method**. Each approach has different implications for memory management.

#### ⚠️ Important: Understanding Subscription Cleanup

The framework provides automatic cleanup of subscriptions **only when you use the `.Add(handler, subscriber)` method with a valid subscriber object**. Using `+=` creates an **untracked subscription** that you **MUST manually unsubscribe** to prevent memory leaks.

#### Approach 1: Using `+=` / `-=` Operators (Manual Cleanup Required)

The `+=` and `-=` operators provide a familiar C#-style syntax for subscribing and unsubscribing. However, this approach creates a **strong reference** that is **NOT automatically tracked** by the framework's cleanup system.

> **⚠️ WARNING:** When using `+=`, you **MUST** manually unsubscribe using `-=` (typically in `OnDestroy()`) to prevent memory leaks. Forgetting to unsubscribe will keep your object alive in memory even after it should have been destroyed.

```csharp
public class EnemyHealthMonitor : MonoBehaviour {
    [SerializeField] private PlayerDataModel playerDataModel;
    
    // Store the handler reference for later unsubscription
    private Action<ObservableVariable<int>> _healthHandler;

    private void Start() {
        // Create and store the handler
        _healthHandler = OnPlayerHealthChanged;
        
        // Subscribe using += (creates an UNTRACKED subscription)
        playerDataModel.health.OnValueChanged += _healthHandler;
    }

    private void OnDestroy() {
        // ⚠️ CRITICAL: You MUST unsubscribe manually when using +=
        // Failure to do so will cause a memory leak!
        playerDataModel.health.OnValueChanged -= _healthHandler;
    }

    private void OnPlayerHealthChanged(ObservableVariable<int> variable) {
        int newHealth = variable.Value;
        Debug.Log($"Player health is now: {newHealth}");
        
        if (newHealth < 30) {
            // Player is vulnerable - trigger aggressive behavior
            BecomeAggressive();
        }
    }
    
    private void BecomeAggressive() {
        // ... enemy behavior logic
    }
}
```

#### Approach 2: Using `.Add()` Method (Recommended - Auto-Cleanup Supported)

The `.Add(handler, subscriber)` method allows you to specify a subscriber object. When you provide a subscriber, the framework **automatically tracks** the subscription for cleanup when the parent data model is destroyed.

```csharp
public class SomeOtherClass : MonoBehaviour {
    [SerializeField] private PlayerDataModel playerDataModel;

    private void Start() {
        // Subscribe using .Add() with 'this' as the subscriber
        // The framework tracks this subscription for automatic cleanup
        playerDataModel.health.OnValueChanged.Add(OnHealthChanged, this);
    }

    private void OnDestroy() {
        // Optional: You can manually remove if needed, but if playerDataModel
        // is destroyed, cleanup happens automatically for tracked subscriptions
        playerDataModel.health.OnValueChanged.Remove(OnHealthChanged);
    }

    private void OnHealthChanged(ObservableVariable<int> variable) {
        int newHealth = variable.Value;
        int previousHealth = variable.PreviousValue;
        Debug.Log($"Health changed from {previousHealth} to {newHealth}");
    }
}
```

#### Comparison: `+=` vs `.Add()`

| Feature | `+=` Operator | `.Add(handler, subscriber)` |
|---------|---------------|----------------------------|
| **Syntax** | Familiar C# event syntax | Method call with subscriber |
| **Auto-cleanup** | ❌ No | ✅ Yes (when subscriber provided) |
| **Memory leak risk** | ⚠️ High if you forget `-=` | Low |
| **Manual unsubscribe required** | ✅ Always | Optional (recommended for safety) |
| **Best for** | Quick prototyping, short-lived objects | Production code, long-lived subscriptions |

#### Best Practices

1. **Prefer `.Add(handler, this)`** for production code to benefit from automatic cleanup.
2. **Always unsubscribe in `OnDestroy()`** when using `+=` to prevent memory leaks.
3. **Store handler references** if you need to unsubscribe the same handler later.
4. **Consider the object lifecycle**: If your subscriber might be destroyed before the data model, always use `.Add()` with the subscriber parameter or ensure manual cleanup.

## Performance & Best Practices

### Boxing and Value Types

The framework is designed to be efficient and avoid common performance pitfalls in Unity.

*   **No Boxing for Value Types**: Because `ObservableVariable<T>` is a generic class, when you use it with value types (`int`, `float`, `bool`, `Vector3`, etc.), no boxing occurs when the value is set or retrieved. This prevents unnecessary memory allocations and garbage collection.
*   **Efficient Event Handling**: The automatic subscription system uses compiled expressions or delegates, which are highly performant after the initial setup.

### Memory Management and Unsubscribing

*   **Automatic Subscriptions**: For events subscribed automatically via `[AutoBind]` or `[ObservableHandler]` attributes, the framework manages the lifetime of the subscription. You do not need to manually unsubscribe.
*   **Tracked Actions**: The `ObservableTrackedAction` system automatically tracks subscriptions and cleans them up when the data model is destroyed.
*   **Manual Subscriptions**: If you manually subscribe using `OnValueChanged.Add()`, the tracked action system will handle cleanup when the parent data model is destroyed.

# Integrating Singletons with the MMVC Framework

This section explains how to integrate globally accessible manager classes (using a Singleton/Service Locator pattern) with your existing MMVC architecture. This pattern is ideal for central systems like a game manager, sound manager, or data store that need to be accessed from many different parts of your application without creating complex dependencies.

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

This method, which should be called once when the application starts, is responsible for finding, registering, and validating all singleton services.

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
                Debug.Log($"You have more than 1 instance of {service.GetType()} with {Type.GetType(typeof(ISharedSingleton).FullName)} interface.");
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
 
First, build your component as you normally would using the MMVC pattern.
 
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
            componentLogic.StartTimer();
        }
    }
}
```

## Conclusion

By combining the `ISharedSingleton` interface with a `Services` locator and an initialization routine, you can seamlessly integrate global manager classes into your MMVC framework. This gives you the best of both worlds:

* **Decoupled Components**: Individual components don't need hard references to managers.
* **Centralized Access**: You have a single, reliable point of access (`Services.Get<T>()`) for all global systems.
* **Reactive Singletons**: Because `SetListeners` is called on registered services, your global managers can fully participate in the reactive data-binding of the MMVC framework.

---

## Migration from Legacy Framework

If you are migrating from an older version of this framework that used:
- Legacy typed observable classes (e.g., `Observable.ObservableInt32`, `Observable.ObservableString`)
- The `OnModelValueChanged()` universal handler pattern
- The `InitFields()` method

Please refer to the [Migration Guide](../plans/UniversalHandler-Migration-Guide.md) for detailed instructions on updating your code to the modern attribute-based system.
