# Framework Guide

## Architecture

Use MMVC (Model-Model-View-Controller) to separate state, business rules, and presentation while connecting them through observable notifications. Import `SavableObservable` for framework types. Treat models, logic, presenters, and loaders as Unity components, not plain objects to construct with `new`.

Follow the data flow `input -> logic -> model -> notification -> presenter/UI`. Keep state in the model, validation and game decisions in logic, and visual effects/input delegation in the presenter. Use a loader to bridge the project's persistence system, not as a mandatory component on every feature.

Use this guide and the companion examples as the primary documentation. If a design or API detail remains unclear, consult package source under `{projectRoot}\Library\PackageCache\com.evialdev.savableobservable@{hash}\Runtime`. Read only the relevant implementation, such as `Observable.cs` for binding or `UIAdapters.cs` for conversion. Source reading is a fallback for uncertainty, not a routine version-comparison requirement.

## Component Selection

Every GameObject represents a single MMVC unit: exactly one Model, at most one Logic, at most one Presenter, and an optional Loader. All base classes are marked `[DisallowMultipleComponent]` to prevent duplicate instances. All generic parameters `M` are strictly constrained with `where M : BaseObservableDataModel`, `P` with `where P : BaseObservablePresenter<M>`, and logic parameters with `where LO : BaseLogic`; non-observable models and interfaces are not supported. Other GameObjects and Logic on the same GameObject must never reference a Presenter directly; access foreign units through `Services.Get<TLogic>()` or `Services.GetModel<TModel>()`.

### Presenter Access Rule

> Logic never gets, stores, or calls a Presenter (no `GetComponent<XPresenter>()`, no `[SerializeField]` presenter field, no `Find*`). Logic writes Model observables; the Presenter reacts through `[AutoBind]` / `[ObservableHandler]`. To affect another MMVC unit, call that unit's Logic (`Services.Get<OtherLogic>()`) or work with its Model (`Services.GetModel<M>()`). This is a convention: C# cannot block `GetComponent`, so it is enforced by review only.

```csharp
// DON'T: Logic reaching into Presenter
public class PlayerLogic : BaseLogic<PlayerDataModel, PlayerPresenter> {
    public void FlashPlayer() {
        GetComponent<PlayerPresenter>()?.Flash(); // VIOLATION
    }
}

// DO: Logic writes Model, Presenter reacts
public class PlayerLogic : BaseLogic<PlayerDataModel, PlayerPresenter> {
    public void FlashPlayer() {
        GetModel().isFlashing.Value = true;
    }
}
```

#### One-Shot Command Pattern (Event Tokens)

When Logic needs to trigger a transient, one-shot visual command (animations, VFX, node repaints) rather than steady state:
1. Add an `[NonSaved] public ObservableVariable<int> eventToken;` and an event payload struct/field `[NonSaved] public MyEvent latestEvent;` to the Model.
2. In Logic, write the payload to `model.latestEvent` and increment `model.eventToken.Value++`.
3. In Presenter, decorate a handler with `[ObservableHandler(nameof(MyDataModel.eventToken))]` to switch on `model.latestEvent.Kind` and execute the view action.

```csharp
// Model
public class BoardGraphDataModel : BaseObservableDataModel {
    [NonSaved] public ObservableVariable<int> eventToken;
    [NonSaved] public BoardGraphEvent latestEvent;
}

// Logic: publishes the event
public void SetNodeVisual(int row, int col, PlayerColor owner, bool animate) {
    var model = GetModel();
    model.latestEvent = new BoardGraphEvent {
        Kind = BoardGraphEventKind.NodeVisual,
        Row = row,
        Col = col,
        Owner = owner,
        Animate = animate
    };
    model.eventToken.Value++;
}

// Presenter: reacts via [ObservableHandler]
[ObservableHandler(nameof(BoardGraphDataModel.eventToken))]
private void OnEventTokenChanged(int token) {
    if (token <= 0) return;
    var model = GetModel();
    if (model == null) return;
    switch (model.latestEvent.Kind) {
        case BoardGraphEventKind.NodeVisual:
            SetNodeVisual(model.latestEvent.Row, model.latestEvent.Col, model.latestEvent.Owner, model.latestEvent.Animate);
            break;
    }
}
```

### Justified Component Shapes (No Dummy Components)

Never create an empty model or presenter just to obtain a singleton or complete a trio:
- **Coordination, operations, or service only (no observable model or view needed):** A plain `MonoBehaviour` implementing `ISharedSingleton` when global access is needed. Do not derive from `BaseLogic<M, P>` with dummy types.
- **Observable/persisted state and logic without a view:** A meaningful model (`BaseObservableDataModel`) and a plain `MonoBehaviour, ISharedSingleton` service exposing `GetModel()`. Do not invent a model-only `BaseLogic` or empty presenter.
- **Real MMVC view unit:** Model (`BaseObservableDataModel`), Presenter (`BaseObservablePresenter<M>`), and primary Logic (`BaseLogic<M, P>`).

| Component | Responsibility |
| --- | --- |
| `BaseObservableDataModel` | MonoBehaviour holding observable and ordinary state fields (`[DisallowMultipleComponent]`, `[DefaultExecutionOrder(-300)]`) |
| `BaseLogic` | Non-generic root implementing `ISharedSingleton` with `singletonName` and `Name` property (`[DisallowMultipleComponent]`, `[DefaultExecutionOrder(0)]`) |
| `BaseLogic<M, P>` | Primary MMVC component for rules and state changes through `GetModel()` (`where M : BaseObservableDataModel`, `where P : BaseObservablePresenter<M>`, `[DisallowMultipleComponent]`) |
| `BasePresenter<M>` | Non-reactive presentation with same-object `GetModel()` (`where M : BaseObservableDataModel`, `[DisallowMultipleComponent]`) |
| `BaseObservablePresenter<M>` | Reactive presenter base with `InitializePresenter()` and `EnsureInitialized()` (`where M : BaseObservableDataModel`, `[DisallowMultipleComponent]`, `[DefaultExecutionOrder(-150)]`) |
| `ObservablePresenterWithLogic<M, LO>` | Reactive presenter with `GetLogic()` (`where M : BaseObservableDataModel`, `where LO : BaseLogic`, `[DisallowMultipleComponent]`) |
| `LoaderWithModel<M>` | Model access, state application, then presenter binding (`where M : BaseObservableDataModel`, `[DefaultExecutionOrder(-250)]`) |
| `LoaderWithModelAndLogic<M, LO>` | Loader with logic access (`where M : BaseObservableDataModel`, `where LO : BaseLogic`) |

Preserve the editor's same-object dependency provisioning. Adding `BaseLogic<M, P>` to a GameObject in the Unity Editor automatically provisions both `M` (Model) and `P` (Presenter). Presenters do not auto-provision Model or Logic components, avoiding circular Inspector locks.

| Added component | Editor-provisioned dependencies |
| --- | --- |
| `BaseLogic<M, P>` | Model `M` and Presenter `P` |
| `LoaderWithModel<M>` | Model `M` |
| `LoaderWithModelAndLogic<M, L>` | Model `M` and logic `L` |
| `BasePresenter<M>` / `BaseObservablePresenter<M>` | *None* (`BaseLogic<M, P>` is primary) |

Avoid redundant `[RequireComponent]` attributes on concrete Presenter classes. Use Inspector references for view objects and the supplied `GetModel()` / `GetLogic()` helpers for same-object dependencies.

## Model State

Declare reactive state as public `ObservableVariable<T>` fields. Access values through `.Value`; use `.PreviousValue` in notifications to compare changes. Initialize fields through `EnsureFieldsInitialized()` before accessing them, even when relying on automatic creation of null wrappers. `BaseObservableDataModel.Awake()` (`-300`) and `GetModel()` automatically call `EnsureFieldsInitialized()`.

```csharp
using System.Collections.Generic;
using SavableObservable;

public class PlayerDataModel : BaseObservableDataModel {
    public ObservableVariable<string> playerName;
    public ObservableVariable<int> health;
    public ObservableVariable<bool> isAlive;
    public ObservableList<string> inventory;

    // Transient session/UI state not persisted by loaders
    [NonSaved] public ObservableVariable<bool> isSessionActive;

    public List<string> achievements = new List<string>();
}
```

Choose ordinary fields for state that does not require change notifications. Do not expect mutations of an ordinary list or fields inside a referenced object to update bound UI automatically. Use observable fields at the granularity that consumers actually observe.

Use `ForceNotify()` deliberately when a current value must be redisplayed without a value change. Do not toggle a value to a dummy value just to provoke UI updates. Treat previous values as change context, not an undo history.

## Initialization and Binding

The framework uses a deterministic 5-stage initialization pipeline synchronized by `MMVCExecutionOrderSynchronizer`:
1. `BaseObservableDataModel` (`-300`): `Awake()` calls `EnsureFieldsInitialized()`.
2. `LoaderWithModel<M>` (`-250`): `Awake()` sets up persistence before singletons or logic run.
3. `ServiceLocator` (`-200`): Registers all scene `ISharedSingleton` instances in Pass 1, then calls `EnsureInitialized()` on all scene `IObservablePresenter` components (active or inactive) in Pass 2.
4. `BaseObservablePresenter<M>` (`-150`): `Awake()` calls `EnsureInitialized()`, which is an idempotent no-op once bound and binds dynamically instantiated prefabs automatically.
5. `BaseLogic<M, P>` & services (`0`): Business logic startup (`Start()`).

Because `ServiceLocator` registers all services in Pass 1 before initializing presenters in Pass 2, `Services.Get<T>()` is guaranteed non-null inside `InitializePresenter()`.

Calling `EnsureInitialized()` on `BaseObservablePresenter<M>` is strictly idempotent after binding: subsequent calls will not destructively re-bind or duplicate listeners. In contrast, explicit calls to `Observable.SetListeners(subscriber, model)` (e.g. from `LoaderWithModel.LoadDataFromModel`) retain their explicit rebind behavior, replacing existing subscriptions cleanly.

Override `protected virtual void InitializePresenter()` in `BaseObservablePresenter<M>` to configure static UI controls (slider min/max, dropdown options, button listeners, initial CanvasGroup state). It runs once before `Observable.SetListeners` attaches handlers.

Call `Observable.SetListeners(subscriber)` on a MonoBehaviour with the model on the same GameObject. If using `Observable.SetListeners(subscriber, model)` or `SetAutoBindListeners(subscriber, model)`, the model must be on the subscriber's own GameObject; cross-GameObject binding is strictly rejected with `Debug.LogError` to prevent lifecycle leaks.

## AutoBind Usage

Mark a serialized view field with `[AutoBind(nameof(PlayerDataModel.playerName))]` to bind it to the corresponding model field. Use plain strings only when necessary; `nameof` gives rename/compiler safety. Omit the attribute argument only when the view field name exactly matches the model field name.

```csharp
[AutoBind(nameof(PlayerDataModel.playerName))]
[UnityEngine.SerializeField] private TMPro.TextMeshProUGUI playerNameText;

[AutoBind]
[UnityEngine.SerializeField] private UnityEngine.UI.Toggle isAlive;
```

Assign the actual UI component in the Inspector before binding. Treat AutoBind as value propagation, not formatting, validation, button-command wiring, or list item creation. Use a handler for formatted text, progress calculations, visibility, animations, or multiple view updates. Route validated gameplay input through logic rather than allowing two-way binding to bypass rules.

## Handlers and Collections

Prefer `nameof(Model.field)` over literal names. Match `[ObservableHandler]` methods to these forms for `ObservableVariable<T>`:

```csharp
private void OnChanged() { }
private void OnChanged(T current) { }
private void OnChanged(T current, T previous) { }
```

Treat the sample `T` as the actual field value type, not a wrapper parameter. Use a wrapper argument only for manual `OnValueChanged.Add` callbacks. Use one attributed method per observable per subscriber: the lookup maps the field name to a single method. Combine AutoBind and a handler when both rendering and a custom reaction are needed.

Use `ObservableList<T>.Add`, `Remove`, `Clear`, indexer assignment, and similar methods to notify. Avoid `list.Value.Add(...)`, which mutates the underlying `List<T>` without the wrapper's notification. `Value` is `List<T>` and `PreviousValue` is `IReadOnlyList<T>`; list-handler signatures must match those types, not `T` and `T`. The `OnValueChanged` alias supports reflection-based handlers; manual subscriptions can use `OnChanged.Add(Action<ObservableList<T>>, subscriber)`.

## Inspector Editing and Undo/Redo

Use the framework's `ObservableList<T>` property drawer for Inspector editing. It draws the serialized backing list, captures a pre-edit snapshot through `OnBeginGui()`, applies serialized changes, and calls the observable's editor-only `OnValidate()`. Validation compares the list with its captured or last validated snapshot and updates `PreviousValue` before notifying when contents differ.

## UI Adapters

| UI type | Behavior |
| --- | --- |
| `TMPro.TMP_Text` / `TextMeshProUGUI` | Display value as text |
| `UnityEngine.UI.Text` | Display value as text |
| `UnityEngine.UI.Toggle` | Two-way boolean binding |
| `UnityEngine.UI.Slider` | Two-way numeric binding |
| `TMPro.TMP_InputField`, `UnityEngine.UI.InputField` | Two-way text with basic type conversion |
| `UnityEngine.UI.Image` | Set sprite |

Wire button clicks separately to logic entry points. To bind a button's label text, target its `TextMeshProUGUI` or `Text` child component directly with `[AutoBind]`. Implement `IUIAdapter` (`CanHandle`, `Priority`, `SetValue`) for display-only controls. Implement `IUIListenerAdapter` additionally for interactive controls, returning a listener token from `AddListener` and removing that exact listener in `RemoveListener`. Register with `UIAdapterRegistry.RegisterAdapter`.

## Lifetime and Cleanup

Use `Add(Action<T> handler, object subscriber)` and `Remove(Action<T> handler)` for explicit registration. The `+=` and `-=` operators provide alternate syntax, but `+=` supplies no subscriber identity.

Use `.Add(handler, subscriber)` after parent initialization. Tracking requires both a non-null subscriber and a parent model; subscriptions made before parent linkage are not retroactively tracked.

Preserve `BaseObservableDataModel.OnDestroy()`, which calls `Observable.CleanupSubscriptions(this)`.

Call `Observable.RemoveListeners(model, subscriber)` to remove tracked model callbacks and UI listener tokens for one subscriber.

## Persistence

Decorate transient `ObservableVariable<T>` or `ObservableList<T>` fields with `[NonSaved]`. `BaseObservableDataModel.LoadDataFromModel` automatically skips these fields when applying saved state.

Initialize target observable fields before `LoaderWithModel.LoadDataFromModel(state)`.

Binding setup (`Observable.SetListeners`) does not replay current model values. Initial refresh after loading data must be performed explicitly, either by invoking presentation refresh methods or by calling `ForceNotify()` on loaded model observables.

## Shared Services and Domain Reload (Fast Play Mode)

Use `SavableObservable.ServiceLocator`, `SavableObservable.Services`, and `SavableObservable.ISharedSingleton` for shared singletons.
- `BaseLogic` automatically implements `ISharedSingleton`.
- Custom non-MMVC services implement `MonoBehaviour, ISharedSingleton`.
- Presenters and Models must **never** implement `ISharedSingleton` or register in `ServiceLocator`.
- Access units via `Services.Get<TLogic>()` or `Services.GetModel<TModel>()`.
- Multiple instances of the same Logic class require distinct `singletonName` values on `BaseLogic`. Unnamed duplicates are evicted from singleton lookup while local presentation remains bound.
- **Domain Reload Disabled (Fast Play Mode):**
  - When Enter Play Mode options disable Domain Reload, C# statics persist across Play Mode sessions.
  - Framework registries provide `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` hooks to clear statics (`ServiceLocator.instance`, `Services.instance`, `Observable`, `UIAdapterRegistry`).
  - Types holding mutable static fields use `[AutoStaticsCleanup]` and must be declared `partial`.
  - Always validate service registration, subscriptions, and teardown across at least **two consecutive Play Mode runs without script recompilation**.

## Performance and Migration

Use generic observable values without assuming the entire notification pipeline is allocation-free. Reflection, compiled expression setup, delegate invocation snapshots, formatting, and UI adapters can allocate or box values. Initialize at lifecycle boundaries rather than every frame.
