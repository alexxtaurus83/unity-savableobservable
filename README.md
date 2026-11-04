# MMVC Framework for Unity

## Introduction

The MMVC (Model-Model-View-Controller) framework is a custom-designed architectural pattern for Unity, tailored to facilitate a clean separation of concerns, enhance data binding, and streamline data persistence. It's a powerful approach that blends concepts from traditional MVC and MVVM patterns to offer a reactive, scalable, and maintainable structure for your Unity projects.

At its core, the framework is built on these key principles:

* **Reactive Data Binding**: The `ObservableVariable<T>` class allows UI elements and game logic to automatically react to data changes without tight coupling.
* **Separation of Concerns**: Each component has a distinct responsibility: data (`Model`), business logic (`Logic`), and presentation (`Presenter`).
* **Centralized Lifecycle Management**: The `Loader` component acts as a central point for creating, managing, and saving/loading the other components.
* **Convention over Configuration**: The framework uses reflection to automatically wire up dependencies, reducing boilerplate code and simplifying setup.
* **Attribute-Based Binding**: Use `[ObservableHandler]` and `[AutoBind]` attributes to declaratively connect observables to handlers and UI elements.
* **Primary Component & Automatic Component Requirements**: In the Unity Editor, adding `BaseLogic<M, P>` to a GameObject automatically provisions its paired `Model` and `Presenter` components on the same GameObject (no `[RequireComponent]` attributes required on concrete classes).
* **Built-in Service Locator & Logic Singletons**: `ServiceLocator`, `Services.Get<TLogic>()`, and `Services.GetModel<TModel>()` provide automatic scene discovery, live-instance collision-eviction, and zero-boilerplate access to MMVC Logic and Model instances.
* **Deterministic 5-Stage Execution-Order Pipeline**: `BaseObservableDataModel` (`-300`) -> `LoaderWithModel<M>` (`-250`) -> `ServiceLocator` (`-200`) -> `BaseObservablePresenter<M>` (`-150`) -> `BaseLogic<M, P>` (`0`), automatically synchronized in the Unity Editor by `MMVCExecutionOrderSynchronizer`.

## Core Components

The MMVC framework is comprised of four main components:

### 1. Model

The `Model` is the data layer of your application. It holds the state and is responsible for the application's data. In the MMVC framework, the Model is represented by a class that inherits from `BaseObservableDataModel`. 

**Key Features:**

* It uses `ObservableVariable<T>` fields to store data, which automatically notify any listeners when their value changes.
* `BaseObservableDataModel.Awake()` (`-300`) and `GetModel()` automatically call `EnsureFieldsInitialized()` to initialize any null `ObservableVariable` fields and set up parent references for cleanup.
* **Transient Fields (`[NonSaved]`)**: Decorate transient session or runtime UI tokens (`ObservableVariable<T>` or `ObservableList<T>`) with `[NonSaved]`. These fields are automatically skipped by `BaseObservableDataModel.LoadDataFromModel` and persistence loaders.
* **Mixed Data Types**: You can include regular, non-observable fields (like `int`, `string`, `List<T>`) alongside `ObservableVariable` fields in your model. The save/load system will correctly handle both.

```csharp
[Serializable]
public class PlayerDataModel : BaseObservableDataModel {
    public ObservableVariable<string> playerName;
    public ObservableVariable<int> health;
    public ObservableVariable<int> score;
    public ObservableVariable<bool> isAlive;

    // Transient UI / session token skipped during save/load
    [NonSaved] public ObservableVariable<bool> isSessionActive;

    // Regular fields also supported
    public List<string> achievements;
}
```

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
protected override void InitializePresenter() {
    base.InitializePresenter();
    
    // Subscribe to list changes
    GetModel().items.OnChanged.Add(OnInventoryChanged, this);
}

private void OnInventoryChanged(ObservableList<string> list) {
    Debug.Log($"Inventory updated. Count: {list.Count}");
    
    // Access previous state if needed
    foreach (var item in list.PreviousValue) { ... }
}
```

### 2. Logic

The `Logic` component acts as the "Controller" or "ViewModel." It contains the business logic that manipulates the `Model` and responds to events. It listens for changes in the `Model` and executes the appropriate actions.

**Key Features:**

* It inherits from `BaseLogic<M, P>`, where `M` is the type of the `Model` and `P` is the type of the `Presenter`.
* `BaseLogic<M, P>` is the **primary MMVC component** added to a GameObject:
  * Automatically provisions `M` (Model) and `P` (Presenter) in the Editor (`Reset`/`OnValidate`).
  * Automatically implements `ISharedSingleton` (via non-generic root `BaseLogic`) with an optional serialized `singletonName` field (`Name` property) for differentiating multiple instances of the same Logic class.
  * Exposes `GetModel()` and intentionally does **not** expose `GetPresenter()`—external callers and Logic must **never** call methods on Presenters directly.

```csharp
public class PlayerLogic : BaseLogic<PlayerDataModel, PlayerPresenter> {
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

The framework provides base classes for presenters, allowing you to choose the right one for your needs.

#### Architecture Rule: One GameObject = One MMVC Unit

Each GameObject represents a single MMVC unit containing:
- Exactly **one Model** (`BaseObservableDataModel`)
- At most **one Logic** (`BaseLogic<M, P>`)
- At most **one Presenter** (`BaseObservablePresenter<M>`)
- Optional **one Loader** (`LoaderWithModel<M>`)

**Guards and Validation:**
- **Editor Guard:** All MMVC base classes (`BaseObservableDataModel`, `BasePresenter<M>`, `BaseObservablePresenter<M>`, `BaseLogic`) are decorated with `[DisallowMultipleComponent]`. Unity blocks adding duplicate instances of these components to the same GameObject in the Inspector.
- **Runtime Guard (Multiple Components):** If duplicate model or presenter components exist on a GameObject (e.g. from legacy prefabs or runtime script additions), `Observable.SetListeners()` logs a `Debug.LogError` describing the violation and binds to the resolved model.
- **Runtime Guard (Cross-GameObject Binding):** Explicit-model overloads `Observable.SetListeners(subscriber, model)` and `Observable.SetAutoBindListeners(subscriber, model)` strictly require `model.gameObject == subscriber.gameObject`. Attempting cross-GameObject binding logs a `Debug.LogError` and rejects the binding to prevent dangling subscriptions and lifecycle leaks.

> **Strict Generic Constraints:** All generic base classes (`BasePresenter<M>`, `BaseObservablePresenter<M>`, `BaseLogic<M, P>`, `LoaderWithModel<M>`) strictly enforce `where M : BaseObservableDataModel`. In addition, `BaseLogic<M, P>` strictly requires `where P : BaseObservablePresenter<M>`, and `ObservablePresenterWithLogic<M, LO>` and `LoaderWithModelAndLogic<M, LO>` require `where LO : BaseLogic`. Non-observable models and interfaces (such as `IModel`) are **not supported** and produce compile-time errors.

#### Cross-Component Access

Components on other GameObjects must **never** hold a direct serialized reference to another GameObject's Presenter, nor call methods on Presenters from outside.
Instead, access another MMVC unit via `Services.Get<ScoreLogic>()` (or `[SerializeField] private ScoreLogic scoreLogic;`) or `Services.GetModel<ScoreDataModel>()`, and subscribe to its model's `ObservableVariable<T>.OnValueChanged.Add(handler, this)`:

```csharp
public class ScoreObserver : MonoBehaviour {
    private void Start() {
        var scoreModel = Services.GetModel<ScoreDataModel>();
        if (scoreModel != null) {
            // Subscribe manually to the foreign model's tracked action, passing this as target
            scoreModel.score.OnValueChanged.Add(OnScoreChanged, this);
        }
    }

    private void OnScoreChanged(ObservableVariable<int> score) {
        Debug.Log($"Score updated to: {score.Value}");
    }
}
```

#### Automatic Dependency Provisioning (Editor)

The framework auto-adds required components in the Unity Editor (during `Reset` / `OnValidate`) when you attach your primary components.

| Component Added | Auto-added dependencies |
|-----------------|-------------------------|
| `BaseLogic<M, P>` | `M` (Model) + `P` (Presenter) |
| `LoaderWithModel<M>` | `M` (Model) |
| `LoaderWithModelAndLogic<M, L>` | `M` (Model) + `L` (Logic) |
| `BasePresenter<M>` / `BaseObservablePresenter<M>` | *None* (prevents circular Inspector locks; `BaseLogic<M, P>` is primary) |

#### `BasePresenter<M>`

This is the non-reactive presenter base class. It should be used when the presenter manages a `BaseObservableDataModel` but does not need declarative `[AutoBind]` or `[ObservableHandler]` reactivity. It provides a strongly-typed `GetModel()` method. All models must inherit from `BaseObservableDataModel`.

#### `BaseObservablePresenter<M>`

This is the reactive presenter, which inherits from `BasePresenter<M>`. It's designed to work with a `Model` that uses `ObservableVariable` fields.

**Key Features:**

*   It inherits from `BasePresenter<M>`.
*   It has references to UI elements (e.g., `Button`, `TextMeshProUGUI`, `Slider`).
*   It provides two powerful, declarative ways to handle model changes:
    * `[AutoBind]` attribute for simple UI bindings
    * `[ObservableHandler]` attribute for custom handler methods
*   **`InitializePresenter()` Hook**: Override `protected virtual void InitializePresenter()` to configure UI controls (slider min/max, dropdown options, button click listeners, initial CanvasGroup state). It is guaranteed to run once **after** `ServiceLocator` (`-200`) has registered all scene services and **before** `Observable.SetListeners` attaches handlers (even on inactive scene GameObjects or disabled runtime prefabs).
*   **Idempotent Initialization & Explicit Loader Rebind**: Calling `EnsureInitialized()` repeatedly after initial binding is an idempotent no-op that avoids repeated destructive rebinds. In contrast, explicit calls to `Observable.SetListeners(subscriber, model)` (such as from `LoaderWithModel.LoadDataFromModel`) retain their explicit rebind semantics.
*   **Setup Validation**: Logs descriptive errors if duplicate MMVC components or cross-GameObject bindings are detected.

#### `ObservablePresenterWithLogic<M, L>`

This presenter extends `BaseObservablePresenter<M>` and adds a `GetLogic()` method for convenient access to the Logic component on the same GameObject.

```csharp
public class PlayerPresenter : ObservablePresenterWithLogic<PlayerDataModel, PlayerLogic> {
    
    [AutoBind("health")]
    [SerializeField] private TextMeshProUGUI healthText;
    
    [AutoBind("score")]
    [SerializeField] private TextMeshProUGUI scoreText;
    
    [SerializeField] private Button damageButton;

    protected override void InitializePresenter() {
        base.InitializePresenter();
        if (damageButton != null) {
            damageButton.onClick.AddListener(() => GetLogic().TakeDamage(10));
        }
    }
}
```

### 4. Loader

The `Loader` is responsible for integrating the MMVC components with a save/load system. It acts as the bridge between your persistent data and the runtime components. The framework provides two base classes: `LoaderWithModel<M>` and `LoaderWithModelAndLogic<M, L>`.

**Key Features:**

* Inherits from `LoaderWithModel<M>` (`[DefaultExecutionOrder(-250)]`), where `M` is the type of the `Model`.
* Provides a `GetModelToSave()` method that returns the `Model` instance.
* Provides a `LoadDataFromModel(object state)` method that loads saved data into the `Model` and sets up the listeners.
* Does **not** implement any specific save system interface. This is intentional to keep the framework decoupled from any particular save/load implementation.

## How to Use the Framework

Here's a step-by-step guide to implementing the MMVC framework in your Unity project:

1.  **Create the Model**:
    * Create a new C# script that inherits from `BaseObservableDataModel`. 
    * Add `ObservableVariable<T>` fields for each piece of data you want to track.

2.  **Create the Logic (Primary Component)**:
    * Create a new C# script that inherits from `BaseLogic<YourModel, YourPresenter>`.
    * Implement methods to handle your game's business logic.
 
3.  **Create the Presenter**:
    * Create a new C# script that inherits from `BaseObservablePresenter<YourModel>` or `ObservablePresenterWithLogic<YourModel, YourLogic>`.
    * Add references to your UI elements in the script.
    * Use `[AutoBind]` attributes for simple UI bindings.
    * Use `[ObservableHandler]` attributes for methods that need custom logic.
    * Override `InitializePresenter()` to wire button listeners or setup static UI elements.

4.  **Create Your Loader**:
    * Create a new C# script that inherits from `LoaderWithModel<YourModel>` or `LoaderWithModelAndLogic<YourModel, YourLogic>`.
    * Implement the interface of **your** save/load system (e.g., `ISaveable`).
    * In your implementation, call the `GetModelToSave()` and `LoadDataFromModel(state)` methods from the base class.
 
5.  **Set Up the GameObject**:
    * In the Unity Editor, create a new `GameObject`.
    * Attach your `YourLogic` component to the `GameObject`. Notice that `YourModel` and `YourPresenter` are automatically added by the Editor!
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
public class ComponentLogic : BaseLogic<ComponentDataModel, ComponentPresenter> {

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
    
    [SerializeField] private GameObject timerPanel;

    // Complex logic - use ObservableHandler
    [ObservableHandler("timerEnabled")]
    private void OnTimerEnabledChanged(bool enabled) {
        if (timerPanel != null) {
            timerPanel.SetActive(enabled);
        }
    }
}
```

# Integrating Singletons with the MMVC Framework

The framework includes built-in service locator and singleton support via `SavableObservable.ServiceLocator`, `SavableObservable.Services`, and `SavableObservable.ISharedSingleton`.

## The Core Concept: `ISharedSingleton` and `Services`

### 1. The `ISharedSingleton` Interface

`ISharedSingleton` marks scene-resident shared singletons registered with `ServiceLocator`.
- Automatically implemented by `BaseLogic` for all MMVC units.
- May be implemented by custom non-MMVC `MonoBehaviour` services (such as `AudioService`).
- **Never implement `ISharedSingleton` on Presenters or Models.** Presenters and Models are strictly rejected by `ServiceLocator`.

### 2. 5-Stage Execution-Order Pipeline

`MMVCExecutionOrderSynchronizer` automatically synchronizes script execution order in the Unity Editor:
1. `BaseObservableDataModel` (`-300`): Initializes fields.
2. `LoaderWithModel<M>` (`-250`): Wires persistence callbacks.
3. `ServiceLocator` (`-200`): Discovers and registers all scene `ISharedSingleton` instances in Pass 1, then initializes and binds all scene `IObservablePresenter` components in Pass 2.
4. `BaseObservablePresenter<M>` (`-150`): Initializes dynamically instantiated prefabs.
5. `BaseLogic<M, P>` & services (`0`): Business logic startup (`Start()`).

Because Pass 1 finishes before Pass 2 starts, `Services.Get<T>()` is guaranteed populated inside every `InitializePresenter()`!

### 3. Live-Instance Collision-Eviction Rule

- **Single instances** of a class resolve directly:
  ```csharp
  var logic = Services.Get<ComponentLogic>();
  var model = Services.GetModel<ComponentDataModel>();
  ```
- **Multiple instances of the same class** must assign distinct `singletonName` values on `BaseLogic` in the Inspector:
  ```csharp
  var blueAI = Services.Get<AISettingsLogic>("BlueAI");
  var blueModel = Services.GetModel<AISettingsDataModel>("BlueAI");
  ```
- **Unnamed duplicates** (e.g. 49 board cell prefabs sharing default `Name = "PlayerChipLogic"`) are automatically evicted from `Services` lookup without throwing during registration, while their local Presenter and Model remain fully bound on their own GameObject. Attempting to resolve an evicted key logs a clear `Debug.LogError` instructing you to assign distinct `singletonName` values.

## Conclusion

The MMVC framework provides a clean, reactive, and decoupled architecture for Unity:
* **Decoupled Components**: Models hold state, Logic holds rules, Presenters render and delegate input.
* **Logic-First Services**: Access foreign units via `Services.Get<Logic>()` or `Services.GetModel<Model>()`, never through Presenters.
* **Deterministic Lifecycle**: Guaranteed initialization order from Model to Presenter to Logic.
