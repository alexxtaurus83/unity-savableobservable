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

#### One-Shot Notifications (`ObservableEvent<T>`)

When Logic needs to trigger a transient, one-shot visual command on its own Presenter (animations, VFX, cell flashes) rather than steady state, use `ObservableEvent<T>` on the Model instead of `[NonSaved][NotifyOnSameValue]` counter tokens:
1. Add a `public ObservableEvent<T>` field to the Model (automatically skipped by `LoadDataFromModel`, `GetSavableObservableFields()`, and serialization).
2. In Logic, call `GetModel().myEvent.Fire(payload)`. Every `Fire` notifies (equal payloads included).
3. In Presenter, handle it with `[ObservableHandler(nameof(MyDataModel.myEvent))]` using 0 or 1 parameter of type `T`. Never set `InvokeOnBind = true` (logs an error and is ignored). For unit -> unit notifications across GameObjects, use the typed event bus (`Services.Publish` / `[EventHandler]`), not an `ObservableEvent<T>` on a foreign model.

```csharp
// Model
public class BoardDataModel : BaseObservableDataModel {
    public ObservableEvent<int> cellFlashed = new ObservableEvent<int>();
}

// Logic
GetModel().cellFlashed.Fire(index);

// Presenter: 0 or 1 parameter of type T; never InvokeOnBind = true
[ObservableHandler(nameof(BoardDataModel.cellFlashed))]
private void OnCellFlashed(int index) { ... }
```

### Justified Component Shapes (No Dummy Components)

Never create an empty model or presenter just to obtain a singleton or complete a trio:
- **Coordination, operations, or service only (no observable model or view needed):** A plain `MonoBehaviour` implementing `ISharedSingleton`, named `*Service` (e.g., `AudioService`, `ResourceStoreService`). Do not derive from `BaseLogic<M, P>` with dummy types.
- **Observable/persisted state and logic without a view:** A meaningful model (`BaseObservableDataModel`) and a plain `MonoBehaviour, ISharedSingleton` service named `*Service` exposing `GetModel()`. Do not invent a model-only `BaseLogic` or empty presenter.
- **Real MMVC view unit:** Model (`*DataModel` : `BaseObservableDataModel`), Presenter (`*Presenter` : `BaseObservablePresenter<M>`), and primary Logic (`*Logic` : `BaseLogic<M, P>`).

**Naming Convention Rule (`*Logic` vs `*Service`):**
- Reserve the `*Logic` suffix exclusively for classes deriving from `BaseLogic<M, P>` (paired with `*DataModel` and `*Presenter`).
- Always suffix standalone `MonoBehaviour, ISharedSingleton` services (both class and `.cs` file) with `*Service` so `Services.Get<*Service>()` vs `Services.Get<*Logic>()` immediately communicates whether the target is a standalone service or an MMVC view unit.

| Component | Responsibility |
| --- | --- |
| `BaseObservableDataModel` | MonoBehaviour holding observable and ordinary state fields (`[DisallowMultipleComponent]`) |
| `BaseLogic` | Non-generic root implementing `ISharedSingleton` with `singletonName` and `Name` property (`[DisallowMultipleComponent]`) |
| `BaseLogic<M, P>` | Primary MMVC component for rules and state changes through `GetModel()` (`where M : BaseObservableDataModel`, `where P : BaseObservablePresenter<M>`, `[DisallowMultipleComponent]`) |
| `BaseObservablePresenter<M>` | Reactive presenter base with `InitializePresenter()` and `EnsureInitialized()` (`where M : BaseObservableDataModel`, `[DisallowMultipleComponent]`, `[DefaultExecutionOrder(-150)]`) |
| `ObservablePresenterWithLogic<M, LO>` | Reactive presenter with `GetLogic()` (`where M : BaseObservableDataModel`, `where LO : BaseLogic`, `[DisallowMultipleComponent]`) |
| `LoaderWithModel<M>` | Model access, state application, then presenter binding (`where M : BaseObservableDataModel`) |
| `LoaderWithModelAndLogic<M, LO>` | Loader with logic access (`where M : BaseObservableDataModel`, `where LO : BaseLogic`) |

Preserve the editor's same-object dependency provisioning. Adding `BaseLogic<M, P>` to a GameObject in the Unity Editor automatically provisions both `M` (Model) and `P` (Presenter). Presenters do not auto-provision Model or Logic components, avoiding circular Inspector locks.

| Added component | Editor-provisioned dependencies |
| --- | --- |
| `BaseLogic<M, P>` | Model `M` and Presenter `P` |
| `LoaderWithModel<M>` | Model `M` |
| `LoaderWithModelAndLogic<M, L>` | Model `M` and logic `L` |
| `BaseObservablePresenter<M>` | *None* (`BaseLogic<M, P>` is primary) |

Avoid redundant `[RequireComponent]` attributes on concrete Presenter classes. Use Inspector references for view objects and the supplied `GetModel()` / `GetLogic()` helpers for same-object dependencies.

## Model State

Declare reactive state as public `ObservableVariable<T>` fields. Access values through `.Value`; use `.PreviousValue` in notifications to compare changes. Initialize fields through `EnsureFieldsInitialized()` before accessing them, even when relying on automatic creation of null wrappers. `BaseObservableDataModel.Awake()` and `GetModel()` automatically call `EnsureFieldsInitialized()`.

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

Use `model.field.Set(value)` when you want to assign and know whether the value changed: it assigns like `Value = value` and returns `true` when it notified (`false` when unchanged), collapsing compare-then-assign to a single call (`if (!GetModel().volume.Set(v)) return;`). Use `ForceNotify()` deliberately when a current value must be redisplayed without a value change. Do not toggle a value to a dummy value just to provoke UI updates. Treat previous values as change context, not an undo history.

## Initialization and Binding

The framework uses a two-stage execution order synchronized by `MMVCExecutionOrderSynchronizer`:
1. `ServiceLocator` (`-200`): In `Awake()`, Pass 1 registers all eligible scene `ISharedSingleton` instances, then runs every scene `IBootstrapLoad.BootstrapLoad()` hook synchronously (sorted by `BootstrapOrder`, once per locator) to fill bind-time data, then Pass 2 calls `EnsureInitialized()` on all scene `IObservablePresenter` components (active or inactive). In `Start()`, it runs every scene `IBootstrapStart.BootstrapStart()` hook (sorted by `BootstrapOrder`, once per locator) for long or async work.
2. `BaseObservablePresenter<M>` (`-150`): `Awake()` calls `EnsureInitialized()`, which is an idempotent no-op once bound and binds dynamically instantiated prefabs automatically.

Models, loaders and `BaseLogic<M, P>` have no ordering requirement: `GetModel()` initializes the model's fields lazily, so the model's own `Awake()` is not relied upon. The synchronizer preserves explicit execution orders declared by user classes.

Because `ServiceLocator` finishes Pass 1 registration and all `IBootstrapLoad` hooks before initializing presenters in Pass 2, `Services.Get<T>()` is guaranteed non-null for unique scene services inside `InitializePresenter()`, and data loaded in `BootstrapLoad` is visible on `InvokeOnBind` replay. Each hook runs once per locator (a new Play session has a new locator), and `BootstrapLoad` exceptions are not caught (a failing hook stops startup before presenters bind).

Calling `EnsureInitialized()` on `BaseObservablePresenter<M>` is strictly idempotent after binding: subsequent calls will not destructively re-bind or duplicate listeners. Loaders do not rebind: `LoaderWithModel.LoadDataFromModel` fills the model and the presenter observes the loaded values through its existing binding (equal values are silent unless the field uses `[NotifyOnSameValue]`). An explicit `Observable.SetListeners(subscriber, model)` replaces existing framework bindings cleanly.

Override `protected virtual void InitializePresenter()` in `BaseObservablePresenter<M>` to configure static UI controls (slider min/max, dropdown options, button listeners, initial CanvasGroup state). It runs once before `Observable.SetListeners` attaches handlers. Do not paint initial values there; binding replays the current state (next section).

### Initial state on bind (`SyncOnBind`, `InvokeOnBind`)

After it attaches handlers, `Observable.SetListeners` replays the current model state once (after the presenter is marked bound), when `ObservableSettings.SyncOnBind` is `true` (the default):

- every `[AutoBind]` target receives the current value; two-way controls (`Slider`, `Toggle`, input fields) use their "without notify" setter, so the model is not written and other subscribers are not notified;
- every `[ObservableHandler(nameof(M.x), InvokeOnBind = true)]` method is called once with the current value (a two-parameter handler receives `Value` and the unchanged `PreviousValue`).

Handlers without `InvokeOnBind` are never replayed. A replay is not a change (no `PreviousValue` update, no other subscriber notified). A rebind replays again and never duplicates subscriptions. `ObservableSettings.SyncOnBind = false` restores the old bind-only behavior and makes `InvokeOnBind` inert; it resets to `true` on every domain / subsystem start.

Rules: mark a handler `InvokeOnBind = true` only when it is a pure render (set text, toggle visibility, refresh a dependent control). Never on handlers that play sounds, start tweens or send one-shot commands. Binding overwrites scene-authored text of `[AutoBind]` controls, so put the intended default into the model (Inspector value). Custom `IObservableVariable` implementations must implement `InvokeHandler`.

`Observable.SetListeners(subscriber, model)` is the only bind API. The model must be on the subscriber's own GameObject; cross-GameObject binding is strictly rejected with `Debug.LogError` to prevent lifecycle leaks. Presenters normally never call it: `EnsureInitialized()` binds once and is idempotent.

## AutoBind Usage

Mark a serialized view field with `[AutoBind(nameof(PlayerDataModel.playerName))]` to bind it to the corresponding model field. Use plain strings only when necessary; `nameof` gives rename/compiler safety. Omit the attribute argument only when the view field name exactly matches the model field name.

```csharp
[AutoBind(nameof(PlayerDataModel.playerName))]
[UnityEngine.SerializeField] private TMPro.TextMeshProUGUI playerNameText;

[AutoBind]
[UnityEngine.SerializeField] private UnityEngine.UI.Toggle isAlive;

// Command-aware AutoBind: dispatches input to Logic instead of mutating model directly
[AutoBind(nameof(SettingsModel.musicVolume), Command = nameof(SettingsLogic.SetMusicVolume))]
[UnityEngine.SerializeField] private UnityEngine.UI.Slider volumeSlider;
```

Assign the actual UI component in the Inspector before binding.

### Command-Aware AutoBind
Setting `Command = nameof(Logic.Method)` on `[AutoBind]` routes control input to a public instance synchronous method on the paired `BaseLogic` (`public void Method(T value)`), where `T` matches the observable value type.
- **Synchronous Only:** Returns `void`; accepts exactly one value parameter. `async void`, `Task`, `ValueTask`, and coroutine methods are rejected.
- **Automatic Reconciliation:** In `finally`, the framework repaints the accepted model value onto the control without triggering input events or notifications. If Logic clamps, normalizes, rejects, or throws, the control reconciles to the accepted model state.
- **Malformed Input:** Temporarily invalid numeric text (such as `-` or an empty non-nullable numeric input) invokes no command and stays editable until valid. Undefined numeric values are rejected for non-flags enums.
- **Legacy Fallback:** Omitting `Command` preserves direct model mutation.

### ClickCommand For Buttons
For zero-argument button actions triggering Logic commands:

```csharp
[SerializeField, ClickCommand(nameof(GameOverLogic.Hide))]
private Button okButton;
```
- **Zero-Argument, Synchronous:** Targets a `public void Method()` on paired `BaseLogic`. Async methods are rejected (use `Listeners.Listen`).
- **IL2CPP Stripping:** Mark the target logic method with `[UnityEngine.Scripting.Preserve]`.
- **Framework Lifecycle:** Wired after `InitializePresenter()` and refreshed on `Observable.SetListeners` rebind through an internal `ListenerSet`; survives consumer `Listeners.Clear()`.
- **Observable Handlers:** Use `[ObservableHandler(nameof(Model.field))]` for custom view reactions (presenters may call `GetLogic()` from handlers when needed; leave `InvokeOnBind = false` for handlers with side effects).

### Scoped ListenerSet (`Listeners`)
Use the lazy `Listeners` property on `BaseObservablePresenter<M>` for explicit UnityEvent subscriptions:

```csharp
protected override void InitializePresenter() {
    base.InitializePresenter();

    Listeners.Listen(saveButton.onClick, GetLogic().Save);
    Listeners.Listen(filterInput.onEndEdit, GetLogic().ApplyFilter);
    Listeners.Listen(presetButton.onClick, () => GetLogic().ApplyPreset("Custom"));
}
```
- Automatically disposed on presenter destruction (`OnDestroy`).
- Idempotent deduplication for equal delegates.
- Preserves Inspector-persistent listeners (does not call `RemoveAllListeners`).
- Preserved across `Observable.SetListeners` rebinding. Calling `Listeners.Clear()` in pooled views resets dynamic listeners.

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

Use the framework's `ObservableList<T>` and `ObservableVariable<T>` property drawers for Inspector editing. They draw the serialized backing properties, capture a pre-edit snapshot through `OnBeginGui()`, apply serialized changes, and call the observable's editor-only `OnValidate()`. Validation compares the values/list with captured or last validated snapshots and updates `PreviousValue` before notifying when contents differ. `BaseObservableDataModel.OnValidate()` in `UNITY_EDITOR` automatically forwards editor validation to all observable fields on the model, so custom models overriding `OnValidate()` must call `base.OnValidate()`.

## UI Adapters

| UI type | Behavior |
| --- | --- |
| `TMPro.TMP_Text` / `TextMeshProUGUI` | Display value as text |
| `UnityEngine.UI.Text` | Display value as text |
| `UnityEngine.UI.Toggle` | Two-way boolean binding |
| `UnityEngine.UI.Slider` | Two-way numeric binding (also supports `enum` and `Nullable<T>`) |
| `TMPro.TMP_InputField`, `UnityEngine.UI.InputField` | Two-way text with type conversion (supports primitives, `enum`, and `Nullable<T>`) |
| `TMPro.TMP_Dropdown`, `UnityEngine.UI.Dropdown` | Two-way integer index and enum binding |
| `UnityEngine.UI.Image` | Set sprite |

Wire button clicks separately to logic entry points. To bind a button's label text, target its `TextMeshProUGUI` or `Text` child component directly with `[AutoBind]`. Implement `IUIAdapter` (`CanHandle`, `Priority`, `SetValue`) for display-only controls. Implement `IUIListenerAdapter` additionally for interactive controls, returning a listener token from `AddListener` and removing that exact listener in `RemoveListener`. Register with `UIAdapterRegistry.RegisterAdapter` (custom adapters deduplicate by concrete adapter type across Fast Play Mode runs). Two-way adapters use per-control re-entrancy tracking so modifying one control in a callback does not suppress callbacks on another control of the same type.

## Lifetime and Cleanup

Use `Add(Action<T> handler, object subscriber)` and `Remove(Action<T> handler)` for explicit registration. The `+=` and `-=` operators provide alternate syntax, but `+=` supplies no subscriber identity (`null` subscriber), meaning manual subscriptions added via `+=` are not recorded in the model's subscription registry and will not be cleaned up by `Observable.RemoveListeners` or model destruction (though instance methods on `UnityEngine.Object` targets are pruned on next invocation if destroyed). Always prefer `Add(handler, this)` for managed lifecycles. Re-entrant assignments to `ObservableVariable<T>.Value` during handlers are queued FIFO so each transition is delivered in order to every subscriber.

Use `.Add(handler, subscriber)` after parent initialization. Tracking requires both a non-null subscriber and a parent model; subscriptions made before parent linkage are not retroactively tracked.

Do not hide the virtual `Awake()` or `OnDestroy()` methods on models, presenters, or logic. Override them and call the base implementation; in particular, `BaseObservableDataModel.OnDestroy()` calls `Observable.CleanupSubscriptions(this)`.

Call `Observable.RemoveListeners(model, subscriber)` to remove tracked model callbacks and UI listener tokens for one subscriber.

Alternatively, use `field.Subscribe(handler, replay)` (`ObservableVariable<T>`, `ObservableList<T>`, `ObservableEvent<T>`) or `Services.Subscribe<TEvent>(this, handler)`, which return an `IDisposable` (`Dispose` unsubscribes idempotently). Because these handles are not owned by a model subscriber, collect them in an instance `SubscriptionSet` field (never static) and call `Clear()` in `OnDisable()` (or when the subscriber goes away).

## Persistence

Decorate transient `ObservableVariable<T>`, `ObservableList<T>`, or plain fields and properties with `[NonSaved]`. `BaseObservableDataModel.LoadDataFromModel` automatically skips these members when applying saved state, respects read-only properties, and supports plain DTO objects as source. Setting `ObservableList<T>.Value` (such as during `LoadDataFromModel`) is silent when the new list is element-wise equal to the current list.

Initialize target observable fields before `LoaderWithModel.LoadDataFromModel(state)`.

Binding setup (`Observable.SetListeners`) replays the current model state once (`[AutoBind]` targets and `InvokeOnBind` handlers; see "Initial state on bind"). A load applied after binding reaches the presenter as ordinary change notifications (equal values are silent), so a presenter that must redisplay an unchanged value after a load uses `ForceNotify()` on that observable or a rebind.

## Shared Services and Domain Reload (Fast Play Mode)

Use `SavableObservable.ServiceLocator`, `SavableObservable.Services`, and `SavableObservable.ISharedSingleton` for shared singletons.
- `BaseLogic` automatically implements `ISharedSingleton`.
- Custom non-MMVC services implement `MonoBehaviour, ISharedSingleton`.
- Presenters and Models must **never** implement `ISharedSingleton` or register in `ServiceLocator`.
- Access units via `Services.Get<TLogic>()` or `Services.GetModel<TModel>()`.
- Multiple instances of the same Logic class require distinct `singletonName` values on `BaseLogic`. A duplicate key logs an error and is not registered (the first instance keeps the key) while local presentation remains bound; multi-instance units that are not global services override `RegisterAsSharedSingleton => false`.
- **Typed Event Bus (`IMmvcEvent`, `Services.Publish`, `[EventHandler]`, `Services.Subscribe`):**
  - Units notify each other without references using `readonly struct` events implementing `IMmvcEvent`.
  - **Roles:** Logic and services publish (`Services.Publish(new MyEvent(...))`) and subscribe (`[EventHandler] void M(TEvent e)` or `Services.Subscribe<TEvent>(this, handler)`); presenters only subscribe; models never publish or subscribe (enforced by Roslyn `MMVC016`; a model subscription logs an error and is ignored).
  - **Delivery & Lifetime:** Handler order is subscription order and not a contract (keep ordered steps as direct calls). Throwing handlers are logged while others still run; destroyed subscribers are pruned; re-entrant publishes are queued (at most 32 per outer publish). `ServiceLocator.EventBus` is per `ServiceLocator` instance (no statics) and cleared on destroy, so each Play session starts empty.
- **Optional References (`[AllowNullGuard]`):**
  - Required references (`GetModel()`, `Services.Get<T>()`, `Services.GetModel<M>()`, `[SerializeField]` fields) are never null-checked: missing references should throw (enforced by Roslyn `MMVC017`; `OnDisable`/`OnDestroy`/`OnApplicationQuit` are exempt). Mark genuinely optional service classes, fields, or methods with `[AllowNullGuard("reason")]`.
- **Domain Reload Disabled (Fast Play Mode):**
  - When Enter Play Mode options disable Domain Reload, C# statics persist across Play Mode sessions.
  - Framework registries provide `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` hooks to clear statics (`ServiceLocator.instance`, `Services.instance`, `Observable`, `UIAdapterRegistry`).
  - Types holding mutable static fields use `[AutoStaticsCleanup]` and must be declared `partial`.
  - Always validate service registration, subscriptions, and teardown across at least **two consecutive Play Mode runs without script recompilation**.

## Performance and Migration

Use generic observable values without assuming the entire notification pipeline is allocation-free. Reflection, compiled expression setup, delegate invocation snapshots, formatting, and UI adapters can allocate or box values. Initialize at lifecycle boundaries rather than every frame.
