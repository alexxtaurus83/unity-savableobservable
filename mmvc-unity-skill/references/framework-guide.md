# Framework Guide

## Architecture

Use MMVC (Model-Model-View-Controller) to separate state, business rules, and presentation while connecting them through observable notifications. Import `SavableObservable` for framework types. Treat models, logic, presenters, and loaders as Unity components, not plain objects to construct with `new`.

Follow the data flow `input -> logic -> model -> notification -> presenter/UI`. Keep state in the model, validation and game decisions in logic, and visual effects/input delegation in the presenter. Use a loader to bridge the project's persistence system, not as a mandatory component on every feature.

Use this guide and the companion examples as the primary documentation. If a design or API detail remains unclear, consult package source under `{projectRoot}\Library\PackageCache\com.evialdev.savableobservable@{hash}\Runtime`. Read only the relevant implementation, such as `Observable.cs` for binding or `UIAdapters.cs` for conversion. Source reading is a fallback for uncertainty, not a routine version-comparison requirement.

## Component Selection

Every GameObject represents a single MMVC unit: exactly one Model, at most one Logic, at most one Presenter, and an optional Loader. All base classes are marked `[DisallowMultipleComponent]` to prevent duplicate instances. All generic parameters `M` are strictly constrained with `where M : BaseObservableDataModel` and logic parameters with `where LO : BaseLogic<M>`; non-observable models and interfaces are not supported. Other GameObjects must never reference a Model directly; access foreign units through their Presenter (`presenter.GetModel()`).

| Component | Responsibility |
| --- | --- |
| `BaseObservableDataModel` | MonoBehaviour holding observable and ordinary state fields (`[DisallowMultipleComponent]`) |
| `BaseLogic<M>` | Rules and state changes through `GetModel()` (`where M : BaseObservableDataModel`, `[DisallowMultipleComponent]`) |
| `BasePresenter<M>` | Non-reactive presentation with same-object `GetModel()` (`where M : BaseObservableDataModel`, `[DisallowMultipleComponent]`) |
| `BaseObservablePresenter<M>` | Reactive presenter base; setup still requires an explicit caller (`where M : BaseObservableDataModel`, `[DisallowMultipleComponent]`) |
| `ObservablePresenterWithLogic<M, LO>` | Reactive presenter with `GetLogic()` (`where M : BaseObservableDataModel`, `where LO : BaseLogic<M>`, `[DisallowMultipleComponent]`) |
| `LoaderWithModel<M>` | Model access, state application, then presenter binding (`where M : BaseObservableDataModel`) |
| `LoaderWithModelAndLogic<M, LO>` | Loader with logic access (`where M : BaseObservableDataModel`, `where LO : BaseLogic<M>`) |

Preserve the editor's same-object dependency provisioning. Verify runtime prefabs contain all required components; do not rely on `OnValidate` to create dependencies in a player.

| Added component | Editor-provisioned dependencies |
| --- | --- |
| `BasePresenter<M>` / `BaseObservablePresenter<M>` | Model `M` |
| `ObservablePresenterWithLogic<M, L>` | Model `M` and logic `L` |
| `BaseLogic<M>` | Model `M` |
| `LoaderWithModel<M>` | Model `M` |
| `LoaderWithModelAndLogic<M, L>` | Model `M` and logic `L` |

Avoid redundant `[RequireComponent]` attributes solely to reproduce these editor dependencies. Preserve base `Reset` and `OnValidate` calls when overriding them. Use Inspector references for view objects and the supplied `GetModel()` / `GetLogic()` helpers for same-object dependencies.

## Model State

Declare reactive state as public `ObservableVariable<T>` fields. Access values through `.Value`; use `.PreviousValue` in notifications to compare changes. Initialize fields through `EnsureFieldsInitialized()` before accessing them, even when relying on automatic creation of null wrappers.

```csharp
using System.Collections.Generic;
using SavableObservable;

public class PlayerDataModel : BaseObservableDataModel {
    public ObservableVariable<string> playerName;
    public ObservableVariable<int> health;
    public ObservableVariable<bool> isAlive;
    public ObservableList<string> inventory;
    public List<string> achievements = new List<string>();
}
```

Choose ordinary fields for state that does not require change notifications. Do not expect mutations of an ordinary list or fields inside a referenced object to update bound UI automatically. Use observable fields at the granularity that consumers actually observe.

Use `ForceNotify()` deliberately when a current value must be redisplayed without a value change. Do not toggle a value to a dummy value just to provoke UI updates. Treat previous values as change context, not an undo history.

## Initialization and Binding

Call `model.EnsureFieldsInitialized()` before early accesses. It creates null supported observable fields and assigns their parent model for subscription tracking. The model base has no automatic `Awake` initializer. Explicit field construction alone does not establish parent tracking.

Call `Observable.SetListeners(subscriber)` on a MonoBehaviour with the model on the same GameObject. If using `Observable.SetListeners(subscriber, model)` or `SetAutoBindListeners(subscriber, model)`, the model must be on the subscriber's own GameObject; cross-GameObject binding is strictly rejected with `Debug.LogError` to prevent lifecycle leaks. Multiple models or presenters on one GameObject violate the one-unit rule, log a `Debug.LogError`, and bind to the resolved model. The implementation removes existing tracked subscriptions for that subscriber, initializes fields, wires attributed handlers, and wires UI adapters.

Treat repeated setup as replacement, not an additive call: it removes tracked manual subscriptions associated with that subscriber as well. Add manual callbacks after setup when both mechanisms are needed. Do not use `SetAutoBindListeners` directly as an idempotent substitute.

Refresh the initial display explicitly. Neither handler wiring nor AutoBind wiring dispatches current model values. `ObservableVariable<T>.ForceNotify()` can refresh bindings, but invokes every attached handler for that observable, including effects or gameplay callbacks. Prefer a presentation-only refresh when those effects must not replay.

Do not depend on a startup warning for uninitialized bindings or call a nonexistent `base.Start()`. Make initialization ownership explicit.

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

Do not assume a list has a built-in visual adapter or deep snapshots. Previous collection contents are shallow snapshots. Assigning `Value` replaces the underlying list reference and notifies; it does not clone the incoming list.

## Inspector Editing and Undo/Redo

Use the framework's `ObservableList<T>` property drawer for Inspector editing. It draws the serialized backing list, captures a pre-edit snapshot through `OnBeginGui()`, applies serialized changes, and calls the observable's editor-only `OnValidate()`. Validation compares the list with its captured or last validated snapshot and updates `PreviousValue` before notifying when contents differ.

Distinguish Unity's serialized undo/redo from observable notification delivery. SerializedProperty editing participates in Unity's undo system, but the list wrapper is not a MonoBehaviour and does not receive Unity lifecycle callbacks itself. Its validation method can process restored data when invoked; the drawer does not register an `Undo.undoRedoPerformed` callback. Do not promise every undo/redo automatically emits an observable event. When custom editor tooling requires that behavior, explicitly arrange validation after restoration and test undo and redo as well as ordinary Inspector edits. Keep editor-only hooks out of player code.

Treat Inspector changes, runtime wrapper mutations, and raw backing-list changes as separate paths when diagnosing missing notifications. Do not use snapshot support as a substitute for a gameplay undo system.

## UI Adapters

| UI type | Behavior |
| --- | --- |
| `TMPro.TMP_Text` / `TextMeshProUGUI` | Display value as text |
| `UnityEngine.UI.Text` | Display value as text |
| `UnityEngine.UI.Toggle` | Two-way boolean binding |
| `UnityEngine.UI.Slider` | Two-way numeric binding |
| `TMPro.TMP_InputField`, `UnityEngine.UI.InputField` | Two-way text with basic type conversion |
| `UnityEngine.UI.Image` | Set sprite |

Wire button clicks separately to logic entry points. To bind a button's label text, target its `TextMeshProUGUI` or `Text` child component directly with `[AutoBind]`. Validate input conversion for the requested type; do not assume complex types such as Vector2 are supported. Register custom adapters before binding. Implement `IUIAdapter` (`CanHandle`, `Priority`, `SetValue`) for display-only controls. Implement `IUIListenerAdapter` additionally for interactive controls, returning a listener token from `AddListener` and removing that exact listener in `RemoveListener`. Register with `UIAdapterRegistry.RegisterAdapter`.

Use no-notify UI setters where appropriate to avoid feedback. Built-in adapters support TMP text, UI text, Toggle, Slider, InputField, and Image. Consult the complete adapter examples in `examples.md` for signatures, registration, and token-based cleanup. Register adapters before the first binding setup, and avoid changing adapter selection while live bindings depend on it.

## Lifetime and Cleanup

Recognize `ObservableTrackedAction<T>` as the framework's event container, not a standard C# `event` or UnityEvent. Its type parameter is an observable wrapper implementing `IObservableVariable`: `ObservableVariable<int>.OnValueChanged` uses `ObservableTrackedAction<ObservableVariable<int>>`, while `ObservableList<string>.OnChanged` uses `ObservableTrackedAction<ObservableList<string>>`. Manual callbacks therefore receive the wrapper; attributed handlers receive its extracted values.

Use `Add(Action<T> handler, object subscriber)` and `Remove(Action<T> handler)` for explicit registration. The `+=` and `-=` operators provide alternate syntax, but `+=` supplies no subscriber identity. Let observable setters/mutation methods dispatch through `Invoke`; use the observable's `ForceNotify()` when an explicit notification is needed rather than directly invoking its event container. Use `HandlerCount` for targeted diagnostics, not as a substitute for correct lifetime ownership.

Use `.Add(handler, subscriber)` after parent initialization. Tracking requires both a non-null subscriber and a parent model; subscriptions made before parent linkage are not retroactively tracked. `+=` calls Add with no subscriber and requires explicit `-=` cleanup.

Preserve `BaseObservableDataModel.OnDestroy()`, which calls `Observable.CleanupSubscriptions(this)`. Do not infer subscriber-lifetime safety from the ConditionalWeakTable: subscriber keys and delegates can remain strongly referenced while the model lives.

Call `Observable.RemoveListeners(model, subscriber)` to remove tracked model callbacks and UI listener tokens for one subscriber. Remove untracked/manual delegates explicitly with their corresponding event API. Neither subscriber destruction nor `OnDisable` automatically tears down these bindings in the presenter base. For pooling, detach on disable and establish an intentional rebind/refresh on enable. Preserve other lifecycle ownership when introducing those hooks.

| Subscription style | Tracking | Teardown responsibility |
| --- | --- | --- |
| `[AutoBind]` / `[ObservableHandler]` after setup | Model-associated | Model destruction; explicit detach for shorter-lived subscribers |
| `.Add(handler, this)` after model initialization | Model-associated | Same lifetime rule; `.Remove(handler)` for one callback |
| `+= handler` | Untracked | Always use `-= handler` |

Store named methods or delegate instances for removal; do not create a different lambda while attempting to unsubscribe. Pair manual attach/detach at consistent lifecycle boundaries. Preserve `base.OnDestroy()` in derived models.

Perform observable operations on the Unity main thread; internal locks do not make Unity UI updates safe from worker threads.

## Persistence

Treat `GetModelToSave()` as returning the live model component, not a detached DTO or serialized snapshot. Integrate with an actual save system and establish its serialization contract.

Initialize target observable fields before `LoaderWithModel.LoadDataFromModel(state)`. The base loader does not initialize before loading: it reflects into the model loader first and binds one matching presenter afterward.

Pass a source compatible with the destination model's reflected members. The loader reads destination `FieldInfo`/`PropertyInfo` directly from the supplied object; an arbitrary similarly named DTO or dictionary is not supported automatically. Override the loader's virtual method to map a custom DTO explicitly when appropriate.

Expect observable fields to copy their `Value`, ordinary fields/properties to be assigned directly, and Unity-defined properties to be skipped. Null source model logs an error; null source observable wrappers are skipped. A null value inside a compatible wrapper is distinct from a null wrapper. Missing target wrappers are skipped with an error. Lists and ordinary reference fields may share references after copying; do not promise deep cloning.

Do not promise notification suppression on reload. Existing listeners can fire when `.Value` is assigned before rebinding. Separate teardown, data application, binding, and initial rendering if the feature requires a silent restore. Do not remove unrelated subscribers merely to silence a presenter.

## Shared Services

Use an existing service registry when a manager must be globally accessible. A marker interface such as `ISharedSingleton` can identify participants; register all services before initializing their consumers. Bind registered presenters and refresh their initial display after registration. Reject duplicate registrations explicitly rather than silently selecting an arbitrary manager.

Treat the marker, registry, scene discovery, and startup order as application code, not SavableObservable APIs. Reuse the project's equivalents when available. See the self-contained singleton example in `examples.md` when a concrete pattern is needed.

## Performance and Migration

Use generic observable values without assuming the entire notification pipeline is allocation-free. Reflection, compiled expression setup, delegate invocation snapshots, formatting, and UI adapters can allocate or box values. Initialize at lifecycle boundaries rather than every frame. Profile high-frequency model changes and actual target builds before optimizing.

When migrating legacy observable wrappers, replace them with `ObservableVariable<T>` and preserve serialized data through the project's migration strategy. Replace universal `OnModelValueChanged()` branching with field-specific handlers or AutoBind. Replace legacy initialization calls with `EnsureFieldsInitialized()` at the actual owner boundary. Do not mechanically rename methods or change persisted field names without checking consumers and saves.

| Legacy identifier to search for | Replacement direction |
| --- | --- |
| `Observable.ObservableInt32` | `ObservableVariable<int>` |
| `Observable.ObservableString` | `ObservableVariable<string>` |
| `OnModelValueChanged()` | Field-specific `[ObservableHandler]` methods and/or `[AutoBind]` |
| `InitFields()` | `EnsureFieldsInitialized()` before first access, plus owner-controlled `Observable.SetListeners` for binding |

Treat these as migration cues, not drop-in textual replacements. Preserve serialized field compatibility, translate callback signatures, and test save/load and initial display after migrating.

When upgrading custom adapters, keep display-only behavior in `IUIAdapter`; move interactive listeners into `IUIListenerAdapter`. Return and retain the actual delegate token so cleanup removes exactly the registered listener.
