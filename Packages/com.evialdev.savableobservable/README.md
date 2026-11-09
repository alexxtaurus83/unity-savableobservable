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
* **Built-in Service Locator & Logic Singletons**: `ServiceLocator`, `Services.Get<TLogic>()`, and `Services.GetModel<TModel>()` provide automatic scene discovery, a clear duplicate-key rule (duplicate = error, first registration wins), and zero-boilerplate access to MMVC Logic and Model instances.
* **Two-Stage Execution Order**: `ServiceLocator` (`-200`) -> `BaseObservablePresenter<M>` (`-150`); models, loaders and logic run at the default order because they initialize lazily through `GetModel()`. Synchronized in the Unity Editor by `MMVCExecutionOrderSynchronizer`.

## Core Components

The MMVC framework is comprised of four main components:

### 1. Model

The `Model` is the data layer of your application. It holds the state and is responsible for the application's data. In the MMVC framework, the Model is represented by a class that inherits from `BaseObservableDataModel`. 

**Key Features:**

* It uses `ObservableVariable<T>` fields to store data, which automatically notify any listeners when their value changes.
* `BaseObservableDataModel.Awake()` and `GetModel()` automatically call `EnsureFieldsInitialized()` to initialize any null `ObservableVariable` fields and set up parent references for cleanup. Because `GetModel()` initializes lazily, nothing depends on the model's `Awake()` order.
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

### Change notification semantics

* **Default:** assigning a value equal to the current one (`EqualityComparer<T>.Default`) does **not** notify; `Value` and `PreviousValue` stay unchanged.
* **Opt-in:** mark the field with `[NotifyOnSameValue]`, or set `model.field.NotifyOnSameValue = true` at runtime. Every assignment then notifies, and an equal assignment gives `PreviousValue == Value`.

```csharp
[NotifyOnSameValue] public ObservableVariable<int> lastRoll;

[ObservableHandler(nameof(MyModel.lastRoll))]
void OnRoll(int value, int previous) {
    if (value == previous) HandleSameRoll(); else HandleNewRoll(value);
}
```

* `ForceNotify()` always fires. `ObservableList<T>` notifies on every mutation; `[NotifyOnSameValue]` on a list (or on a non-observable field) logs a one-time warning and has no effect.
* **Gotchas:**
  * Reference types are compared by reference: mutating an object and re-assigning the same reference is silent. Call `ForceNotify()`.
  * `NaN == NaN` is treated as equal, so it is suppressed.
  * The flag is not serialized, not saved, and not copied by `LoadDataFromModel`.
  * Inspector edits fire only on a real change.
  * Loading values equal to the current ones is silent.

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

* It inherits from `BaseLogic<M, P>`, where `M` is the type of the `Model` and `P` is the type of the `Presenter`. Note that the `P` parameter on `BaseLogic<M, P>` is only for auto-provisioning and presenter initialization, not an access handle.
* `BaseLogic<M, P>` is the **primary MMVC component** added to a GameObject:
  * Automatically provisions `M` (Model) and `P` (Presenter) in the Editor (`Reset`/`OnValidate`).
  * Automatically implements `ISharedSingleton` (via non-generic root `BaseLogic`) with an optional serialized `singletonName` field (`Name` property) for differentiating multiple instances of the same Logic class.
  * Exposes `GetModel()` and intentionally does **not** expose `GetPresenter()`.

> Logic never gets, stores, or calls a Presenter (no `GetComponent<XPresenter>()`, no `[SerializeField]` presenter field, no `Find*`). Logic writes Model observables; the Presenter reacts through `[AutoBind]` / `[ObservableHandler]`. To affect another MMVC unit, call that unit's Logic (`Services.Get<OtherLogic>()`) or work with its Model (`Services.GetModel<M>()`). This is a convention: C# cannot block `GetComponent`, so it is enforced by review only.

```csharp
// DON'T: Logic reaching into Presenter
GetComponent<PlayerPresenter>().Flash();

// DO: Logic writes Model, Presenter handles the reaction
GetModel().isHit.Value = true; // PlayerPresenter reacts via [ObservableHandler(nameof(PlayerDataModel.isHit))]
```

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
- **Editor Guard:** All MMVC base classes (`BaseObservableDataModel`, `BaseObservablePresenter<M>`, `BaseLogic`) are decorated with `[DisallowMultipleComponent]`. Unity blocks adding duplicate instances of these components to the same GameObject in the Inspector.
- **Runtime Guard (Multiple Components):** If duplicate model or presenter components exist on a GameObject (e.g. from legacy prefabs or runtime script additions), `Observable.SetListeners()` logs a `Debug.LogError` describing the violation and binds to the resolved model.
- **Runtime Guard (Cross-GameObject Binding):** `Observable.SetListeners(subscriber, model)` is the only bind API and strictly requires `model.gameObject == subscriber.gameObject`. Attempting cross-GameObject binding logs a `Debug.LogError` and rejects the binding to prevent dangling subscriptions and lifecycle leaks. Calling it again replaces the framework-created bindings of that subscriber and keeps its manual subscriptions; `Observable.RemoveListeners(model, subscriber)` removes everything of that subscriber.
- **Unbound fields (opt-in warning):** a presenter may ignore model fields it does not display. Set `ObservableSettings.StrictBinding = true` (for example in a bootstrap or a test) to get a warning for every observable field that has neither an `[ObservableHandler]` method nor an `[AutoBind]` field. An `[AutoBind]` naming a field the model does not have is always reported.

> **Strict Generic Constraints:** All generic base classes (`BaseObservablePresenter<M>`, `BaseLogic<M, P>`, `LoaderWithModel<M>`) strictly enforce `where M : BaseObservableDataModel`. In addition, `BaseLogic<M, P>` strictly requires `where P : BaseObservablePresenter<M>`, and `ObservablePresenterWithLogic<M, LO>` and `LoaderWithModelAndLogic<M, LO>` require `where LO : BaseLogic`. Non-observable models and interfaces (such as `IModel`) are **not supported** and produce compile-time errors.

#### Cross-Component Access

Components on other GameObjects must **never** hold a direct serialized reference to another GameObject's Presenter, nor call methods on Presenters, including Logic on the same GameObject.
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
| `BaseObservablePresenter<M>` | *None* (prevents circular Inspector locks; `BaseLogic<M, P>` is primary) |

#### `BaseObservablePresenter<M>`

This is the presenter base class. It binds the unit's `ObservableVariable` / `ObservableList` fields to the view and provides a strongly-typed `GetModel()`. All models must inherit from `BaseObservableDataModel`. Presenter hierarchy: `BaseObservablePresenter<M>` -> `ObservablePresenterWithLogic<M, LO>`.

**Key Features:**

*   It has references to UI elements (e.g., `Button`, `TextMeshProUGUI`, `Slider`).
*   It provides two powerful, declarative ways to handle model changes:
    * `[AutoBind]` attribute for simple UI bindings
    * `[ObservableHandler]` attribute for custom handler methods
*   **`InitializePresenter()` Hook**: Override `protected virtual void InitializePresenter()` to configure UI controls (slider min/max, dropdown options, button click listeners, initial CanvasGroup state). It is guaranteed to run once **after** `ServiceLocator` (`-200`) has registered all scene services and **before** `Observable.SetListeners` attaches handlers (even on inactive scene GameObjects or disabled runtime prefabs).
*   **Idempotent Initialization**: Calling `EnsureInitialized()` repeatedly after initial binding is an idempotent no-op that avoids repeated destructive rebinds. Loaders do not rebind: loading data into the model reaches the presenter through its existing binding as ordinary change notifications.
*   **Setup Validation**: Logs descriptive errors if duplicate MMVC components or cross-GameObject bindings are detected.

#### `ObservablePresenterWithLogic<M, L>`

This presenter extends `BaseObservablePresenter<M>` and adds a `GetLogic()` method for convenient access to the Logic component on the same GameObject.

```csharp
public class PlayerPresenter : ObservablePresenterWithLogic<PlayerDataModel, PlayerLogic> {
    
    [AutoBind(nameof(PlayerDataModel.health))]
    [SerializeField] private TextMeshProUGUI healthText;
    
    [AutoBind(nameof(PlayerDataModel.score))]
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

* Inherits from `LoaderWithModel<M>`, where `M` is the type of the `Model`. It has no execution-order requirement: `GetModel()` initializes the model's fields lazily, so loading works even before the model's `Awake()`.
* Provides a `GetModel()` method that returns the `Model` instance (use it to read the state to save).
* Provides a `LoadDataFromModel(object state)` method that loads saved data into the `Model`. It does **not** rebind the presenter: the presenter observes loaded values through its existing binding, and values equal to the current ones are silent (see [Change notification semantics](#change-notification-semantics)).
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
    * In your implementation, call the `GetModel()` and `LoadDataFromModel(state)` methods from the base class.
 
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
    [AutoBind(nameof(ComponentDataModel.status))]
    [SerializeField] private TextMeshProUGUI statusText;
    
    [AutoBind(nameof(ComponentDataModel.timerValue))]
    [SerializeField] private TextMeshProUGUI timerText;
    
    [SerializeField] private GameObject timerPanel;

    // Complex logic - use ObservableHandler
    [ObservableHandler(nameof(ComponentDataModel.timerEnabled))]
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

### 2. Two-Stage Execution Order

`MMVCExecutionOrderSynchronizer` automatically synchronizes script execution order in the Unity Editor:
1. `ServiceLocator` (`-200`): Discovers and registers all scene `ISharedSingleton` instances in Pass 1, then initializes and binds all scene `IObservablePresenter` components in Pass 2.
2. `BaseObservablePresenter<M>` (`-150`): Initializes dynamically instantiated prefabs.

Models, loaders, `BaseLogic<M, P>` and services run at the default order (`0`): they have no ordering requirement because every reader initializes the model's fields lazily through `GetModel()`.

Because Pass 1 finishes before Pass 2 starts, `Services.Get<T>()` resolves every scene-resident, eligible (not opted out) and uniquely keyed service inside `InitializePresenter()` of scene presenters. Dynamically instantiated prefabs and components initialized before `ServiceLocator.Awake()` are not covered by Pass 1 (see the Unity Lifecycle guide).

### 3. Duplicate Registration Rule (duplicate = error, first wins)

A `(type, key)` pair is held by one live instance. The key is the Logic's `singletonName`, or the class name when it is empty.

- **Single instances** of a class resolve directly:
  ```csharp
  var logic = Services.Get<ComponentLogic>();
  var model = Services.GetModel<ComponentDataModel>();
  ```
- **Multiple instances of the same class that must all be resolvable** assign distinct `singletonName` values on `BaseLogic` in the Inspector:
  ```csharp
  var blueAI = Services.Get<AISettingsLogic>("BlueAI");
  var blueModel = Services.GetModel<AISettingsDataModel>("BlueAI");
  ```
- **Multi-instance units that are not global services** (pooled prefabs, e.g. 49 board cells) override `RegisterAsSharedSingleton => false`. They are skipped silently, and their local Presenter and Model stay fully bound on their own GameObject:
  ```csharp
  public class PlayerChipLogic : BaseLogic<PlayerChipDataModel, PlayerChipPresenter> {
      protected override bool RegisterAsSharedSingleton => false;
  }
  ```
- **Unnamed duplicates that still register** (the opt-out was forgotten) log a `Debug.LogError` at registration time that names the type, the key and both GameObjects. The second instance is **not** registered; the first keeps the key, so `Services.Get<T>()` returns the first instance. Local binding of the second unit is unaffected.
- `Services.GetModel<M>()` is derived from the registered Logic units: with several Logic units owning the same model type, pass the Logic name; without one it logs an ambiguity error and returns `null`.

## Architectural Guides & Technical Documentation

For in-depth explanations, execution-order diagrams, and runtime patterns, see:
* [Unity Lifecycle & Automatic SetListeners](Documentation~/UnityLifecycle.md): Execution order, ServiceLocator dual-pass discovery, initialization hooks, and reactive binding mechanics.
* [Runtime Objects, Prefabs & Pooling](Documentation~/RuntimeObjects.md): Best practices for active/inactive prefab instantiation, object pooling, configure-first patterns, and programmatic GameObject composition.
* [Testing Guide & Universal Test Runner](Documentation~/Testing.md): Unattended test runner setup, live editor vs batch mode workflows, and consumer project wrappers.

## Unattended Tests & Test Runner

SavableObservable includes a universal modular test runner and contract testing tools (`MmvcLiveUiAssert`, `MmvcLiveUiAudit`).
For full runner documentation, live vs batch workflows, category filtering, and consumer project wrapper setup, see [Testing Guide](Documentation~/Testing.md).

From this repository, `pwsh -File Tools/Invoke-UnityTests.ps1 -Suite All` runs all framework tests. Consumer projects can copy the thin wrapper from `Samples~/ConsumerWrapper/Invoke-UnityTests.ps1` into their `Tools/` directory.

## Conclusion

The MMVC framework provides a clean, reactive, and decoupled architecture for Unity:
* **Decoupled Components**: Models hold state, Logic holds rules, Presenters render and delegate input.
* **Logic-First Services**: Access foreign units via `Services.Get<Logic>()` or `Services.GetModel<Model>()`, never through Presenters.
* **Predictable Lifecycle**: `ServiceLocator` registers services, then presenters initialize and bind; models initialize lazily through `GetModel()`, so nothing depends on their `Awake()` order.
