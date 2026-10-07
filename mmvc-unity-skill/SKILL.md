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
4. Inspect the bootstrap, factory, or loader that owns initialization. Locate project-specific service registries before using them; `Services` and `ISharedSingleton` are not framework APIs.
5. Read [framework-guide.md](references/framework-guide.md) for architecture, model fields, component provisioning, lifecycle, AutoBind, handlers, collections, adapters, cleanup, persistence, and migration. Read [examples.md](references/examples.md) for self-contained score, timer, inventory, prefab factory, manual subscription, loader, service registry, and custom adapter examples.

## Implement a Feature

1. Select `BaseObservableDataModel` for state, `BaseLogic<M>` for rules, and `BaseObservablePresenter<M>` or `ObservablePresenterWithLogic<M, L>` for reactive presentation. Use `BasePresenter<M>` when no reactive binding is needed. All generic parameters `M` strictly require `where M : BaseObservableDataModel` (interfaces or non-observable models are not supported). `ObservablePresenterWithLogic<M, L>` strictly requires `where L : BaseLogic<M>`.
2. Strictly enforce **one MMVC unit per GameObject**: exactly one Model (`BaseObservableDataModel`), at most one Logic (`BaseLogic<M>`), and at most one Presenter (`BaseObservablePresenter<M>`), plus an optional Loader (`LoaderWithModel<M>`). Base classes are guarded with `[DisallowMultipleComponent]`. Other components must never take a direct Model reference: access foreign units via their Presenter (`presenter.GetModel()` / `GetLogic()`). Treat editor dependency provisioning through `Reset`/`OnValidate` as editor-only; verify prefabs/runtime assembly explicitly.
3. Initialize observable fields with `EnsureFieldsInitialized()` before early logic access, manual subscription, or loading. Do not assume an `Awake` in the framework initializes them.
4. Route input to logic, logic to model, and notifications to presentation. Use `[AutoBind(nameof(Model.field))]` for supported display/input controls and `[ObservableHandler(nameof(Model.field))]` for custom presentation. Keep game rules in logic, even if older examples mix responsibilities.
5. Assign one binding owner to call `Observable.SetListeners(presenter)`. If using explicit-model overloads `Observable.SetListeners(subscriber, model)` or `SetAutoBindListeners(subscriber, model)`, the model **must** reside on the subscriber's own GameObject; cross-GameObject binding is strictly rejected with a `Debug.LogError` to prevent subscription leaks. Account for initial rendering separately: setup subscribes but does not dispatch current values. Use deliberate refresh methods or `ForceNotify()` only after reviewing all affected subscribers for side effects.
6. Preserve base lifecycle methods when overriding them, especially model `OnDestroy`, `Reset`, and `OnValidate`. Do not call `base.Start()` without verifying such a method exists.
7. Use tracked manual subscriptions with an initialized parent model. Remove subscriptions explicitly when a subscriber is disabled, pooled, or destroyed before its model; reattach deliberately when needed.
8. Add loaders only when persistence integration is requested. Follow the project's save contract rather than inventing a generic serializer or service locator.

## Debug a Feature

- For compile errors on generic type parameters (`CS0311`), verify the model inherits from `BaseObservableDataModel` (interfaces such as `IModel` are not supported) and logic inherits from `BaseLogic<M>`.
- For null observable fields, trace access before initialization, including logic in `Awake` and factory code after `Instantiate`.
- For duplicate component errors (`[SavableObservable] GameObject '...' has multiple ...`), ensure only one Model and one Presenter exist on the GameObject.
- For cross-GameObject binding errors (`[SavableObservable] Cross-GameObject binding rejected in SetListeners ...`), access the foreign unit via its Presenter (`presenter.GetModel()`) and subscribe manually rather than calling `SetListeners` across GameObjects.
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
