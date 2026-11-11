---
name: mmvc-unity
description: Implements and debugs Unity MMVC components using the SavableObservable framework (com.evialdev.savableobservable). This skill should be used for observable data models, Logic and Presenter classes, AutoBind and ObservableHandler bindings, dynamic component initialization, subscription cleanup, and custom save/load integration. It does not cover unrelated MVC or MVVM frameworks.
---

# MMVC Unity

Build and maintain the custom Model-Model-View-Controller architecture using `SavableObservable`. Preserve separation between model state, logic rules, and presenter rendering. Use this skill and its references as standalone framework documentation; no project README or example repository is required.

## Discover the Project

1. Read project instructions, nearby feature components, and `Packages/manifest.json`. Confirm `com.evialdev.savableobservable` before generating framework-dependent code.
2. If unsure how an MMVC API is designed, consult source at `{projectRoot}\Library\PackageCache\com.evialdev.savableobservable@{hash}\Runtime`; equivalently, under `\{projectName}\Library\PackageCache\com.evialdev.savableobservable@{hash}` relative to the project's parent. Resolve the actual hash with a file search when source is needed. Do not require a routine package-version audit.
3. Check embedded/local package locations and package resolution when that cache is absent. Report missing dependencies rather than inventing APIs or installing packages without authorization. Never edit cached package source.
4. Inspect the bootstrap, factory, or loader that owns initialization. `ServiceLocator`, `Services`, `ISharedSingleton`, `RegistrationResult`, and `[NonSaved]` are built-in `SavableObservable` APIs. Confirm a `ServiceLocator` component exists in the scene when using `Services.Get<TLogic>()` or `Services.GetModel<TModel>()`.
5. Read [framework-guide.md](references/framework-guide.md) for architecture, model fields, component provisioning, lifecycle, AutoBind, handlers, collections, adapters, cleanup, persistence, and migration. Read [examples.md](references/examples.md) for self-contained score, timer, inventory, prefab factory, manual subscription, loader, service registry, and custom adapter examples.

## Implement a Feature

*(Optional Kilo integration: projects using Kilo can install the companion guard plugin `mmvc-guard` from `mmvc-kilo-plugin/` to enforce mandatory skill loading and lint new framework violations before C# mutations.)*

1. Select the smallest justified component shape (do not create empty models or presenters just to complete a trio):
   - **Service / Coordination only (no view, no model needed):** Plain `MonoBehaviour` implementing `ISharedSingleton` when global access is needed. Do not derive from `BaseLogic<M, P>` with dummy types.
   - **Observable/persisted state and logic without a view:** Meaningful model (`BaseObservableDataModel`) and plain `MonoBehaviour, ISharedSingleton` service exposing `GetModel()`. Do not invent a model-only `BaseLogic` or empty presenter.
   - **Real MMVC view unit:** Model (`BaseObservableDataModel`), Presenter (`BaseObservablePresenter<M>`), and primary Logic (`BaseLogic<M, P>` where `M : BaseObservableDataModel` and `P : BaseObservablePresenter<M>`).
2. Strictly enforce **one MMVC unit per GameObject** and **Logic-only service access**:
   - Exactly one Model (`BaseObservableDataModel`), at most one Logic (`BaseLogic<M, P>`), and at most one Presenter (`BaseObservablePresenter<M>`), plus an optional Loader (`LoaderWithModel<M>`).
   - `BaseLogic<M, P>` is the primary component added to a GameObject in the Editor (auto-provisioning `M` and `P` in `Reset`/`OnValidate`) and automatically implements `ISharedSingleton` with a serialized `singletonName` (`Name`).
   - Custom non-MMVC services implement `MonoBehaviour, ISharedSingleton`.
    - **Never implement `ISharedSingleton` on a Presenter or Model, and never call methods on a Presenter, from outside or from Logic.** Follow the data flow: `Logic -> Model -> notification -> Presenter`. Access foreign units via `Services.Get<ForeignLogic>()` or `Services.GetModel<ForeignModel>()`!
3. Follow the **two-stage execution order** synchronized by `MMVCExecutionOrderSynchronizer`:
   - `ServiceLocator` (`-200`, registers all `ISharedSingleton`s in Pass 1, then calls `EnsureInitialized()` on all scene Presenters in Pass 2) -> `BaseObservablePresenter<M>` (`-150`, handles dynamically spawned prefabs). Models, loaders and `BaseLogic<M, P>` run at the default order: `GetModel()` initializes the model fields lazily, so nothing depends on their `Awake()` order.
   - `GetModel()` on `BaseLogic<M, P>`, `BaseObservablePresenter<M>`, and `LoaderWithModel<M>` automatically calls `_model.EnsureFieldsInitialized()`, and `BaseLogic<M, P>.GetModel()` privately calls `presenter.EnsureInitialized()` so even disabled (`inactive`) runtime-instantiated prefabs are initialized and bound before Logic mutates the Model.
   - Repeated calls to `EnsureInitialized()` on an already-bound presenter are idempotent no-ops, preventing destructive rebinds of UI handlers. Loaders do not rebind after a load; the presenter observes loaded values through its existing binding. An explicit `Observable.SetListeners(presenter, model)` replaces subscriptions cleanly.
4. Configure static UI controls (slider min/max, dropdown options, button listeners, initial `CanvasGroup` state) by overriding `protected override void InitializePresenter()` on `BaseObservablePresenter<M>` (always calling `base.InitializePresenter()` first), never in `Start()`. `InitializePresenter()` is guaranteed to run once before `Observable.SetListeners` (whether triggered by `ServiceLocator`, `Awake()`, `Logic.GetModel()`, or direct `Observable.SetListeners`). Do not paint initial values there: binding replays the current state (step 5).
5. Route input to logic, logic to model, and notifications to presentation. Use `[AutoBind(nameof(Model.field))]` for supported display/input controls and `[ObservableHandler(nameof(Model.field))]` for custom presentation. Keep game rules in logic. **Binding replays the current state** (`ObservableSettings.SyncOnBind`, default on): `[AutoBind]` targets get the current value (two-way controls without writing back), and a handler marked `[ObservableHandler(..., InvokeOnBind = true)]` is called once with the current value. Mark pure-render handlers `InvokeOnBind = true`; leave it off for handlers with side effects (sounds, tweens, one-shot commands). Binding overwrites scene-authored text, so keep the intended default in the model.
6. Preserve base lifecycle methods when overriding them, especially model `OnDestroy`. Do not call `base.Start()` without verifying such a method exists.
7. Use tracked manual subscriptions with an initialized parent model (`model.field.OnValueChanged.Add(handler, this)`). Remove subscriptions explicitly when a subscriber is disabled, pooled, or destroyed before its model; reattach deliberately when needed.
8. Mark transient runtime/UI-only fields or properties (`ObservableVariable<T>`, `ObservableList<T>`, or plain values) with `[NonSaved]` so `LoadDataFromModel` and persistence loaders skip them. `BaseObservableDataModel.OnValidate()` automatically forwards Inspector edits and Undo/Redo to observable fields (subclasses overriding `OnValidate()` should call `base.OnValidate()`). Add loaders only when persistence integration is requested.
9. **Domain Reload Disabled (Fast Play Mode) rules:**
   - Framework static registries (`ServiceLocator`, `Services`, `Observable`, `UIAdapterRegistry`) reset via `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` hooks and `[AutoStaticsCleanup]`.
   - Verify all static state, singleton registrations, and subscriptions across at least **two consecutive Play Mode runs without script recompilation** to prevent stale cached references and registration leaks.

## Debug a Feature

- For compile errors on generic type parameters (`CS0311`), verify `BaseLogic<M, P>` requires `M : BaseObservableDataModel` and `P : BaseObservablePresenter<M>`; `ObservablePresenterWithLogic<M, LO>` requires `LO : BaseLogic`.
- For null observable fields, trace access before initialization, including logic in `Awake` and factory code after `Instantiate`.
- For duplicate component errors (`[SavableObservable] GameObject '...' has multiple ...`), ensure only one Model and one Presenter exist on the GameObject.
- For cross-GameObject binding errors (`[SavableObservable] Cross-GameObject binding rejected in SetListeners ...`), access the foreign unit via `Services.Get<ForeignLogic>().GetModel()` or `Services.GetModel<ForeignModel>()` (never via Presenter, Logic included).
- If Logic needs to make the view do something, add an observable field or event token to the Model and a handler in the Presenter; never call Presenter methods directly. Use the `BoardGraphLogic` event-token pattern (`eventToken.Value++` with payload on `latestEvent`) as the reference for one-shot view commands.
- For `[ServiceLocator] Duplicate registration of ... key '...'`: a second live instance of the same Logic class has the same key; it was NOT registered and the first instance keeps the key. Assign distinct `singletonName` values in the Inspector when every instance must be resolved via `Services`, or override `RegisterAsSharedSingleton => false` for multi-instance (pooled / repeated) units.
- For stale UI, check initialization ownership, initial refresh, field names, serialized UI references, handler signatures, adapter availability, and whether the value actually changed.
- For duplicate or missing callbacks, inspect repeated setup and manual subscriptions. Setup replaces this subscriber's tracked registrations; it does not preserve manually registered callbacks automatically.
- For collections, mutate through observable list methods rather than the raw `Value` list. Check current/previous handler parameter types.
- For callbacks after hiding or destroying UI, inspect subscriber lifetime and both model-to-UI and UI-to-model teardown.
- For reload issues, distinguish initialization, state application, rebinding, and rendering. Existing listeners may run during state application.
- For player-only problems, verify reflection/expression compilation and stripping behavior on the actual scripting backend. Do not infer IL2CPP/AOT compatibility from Editor success or promise allocation-free bindings.

## Coordinate Unity Work

Load `unity-cli` for Editor operations, scene/prefab changes, builds, and tests. Follow its live-Editor preflight and command discovery instead of guessing commands or editing live scene YAML. Load the applicable UI skill before constructing UI; use `ui-ugui` for Canvas/RectTransform/TMP work. Do not assume uGUI adapters apply to UI Toolkit. Use `unity-package-management` for authorized UPM changes.

## Verify the Result

Check compilation and, when runtime behavior changes, exercise initial display, normal updates, repeated initialization, two-way input, reload, and subscriber/model destruction order. Include disable/re-enable cycles for pooled UI. Confirm one user action produces the intended number of callbacks. Report tests actually run and blockers; distinguish source review from successful Unity execution.
