# Changelog

## [2.1.0] - 2026-11-11 - Unity 6000.5+ baseline & engine bug-fixes

### Changed (engine & compatibility)
- **Minimum Unity Version Bumped to 6000.5:**
  - Updated `package.json` minimum Unity version to `6000.5`.
  - Removed `< 6000.5` preprocessor `#if` shims and deleted `Runtime/LifecycleManagementCompatibility.cs`.
  - Native `Unity.Scripting.LifecycleManagement` attributes (`[AutoStaticsCleanup]`, `[NoAutoStaticsCleanup]`) are unconditionally active across runtime and editor assemblies.
  - Removed `UNITY_HAS_LIFECYCLE_MANAGEMENT` from versionDefines in all `.asmdef` files.

### Fixed (runtime & editor bugs)
- **`ServiceLocator` Scene Discovery in `Awake()`:**
  - `InitializeSharedSingletons()` attempts `scene.GetRootGameObjects()` during `Awake()` whenever `scene.IsValid() && (Application.isPlaying || scene.isLoaded)` and filters any fallback scan so objects from other scenes or `DontDestroyOnLoad` are never pulled into this scene's `ServiceLocator`.
- **`[AutoBind]` Fake-Null Guards:**
  - Guarded UI component references in `Observable.SubscribeAutoBindHandler` with `IsTargetAlive(currentUiComponent)` to safely handle unassigned `[SerializeField]` fields and destroyed `UnityEngine.Object` components without logging errors.
- **Editor Validation & Multi-Object Editing:**
  - Added `BaseObservableDataModel.OnValidate()` to forward editor validation and undo/redo to observable fields.
  - Synchronized `_lastValidatedValue` and `_lastValidatedSnapshot` on runtime and editor mutations.
  - Cleared `_hasEditorGuiSnapshot` cleanly when change checks complete without modifications so `Layout`/`Repaint` passes do not clobber pre-undo baselines.
  - Added `PropertyResolver.GetTargetObjectsOfProperty` and updated drawers to support multi-object editing across all selected targets.
- **Execution Order Synchronizer Batching:**
  - Confirmed explicit `[DefaultExecutionOrder(-200)]` on `ServiceLocator`.
  - Batched `MonoImporter.SetExecutionOrder` calls in `MMVCExecutionOrderSynchronizer.SyncAll()` inside `AssetDatabase.StartAssetEditing()` and `AssetDatabase.StopAssetEditing()`.
- **Custom UIAdapter Deduplication in Fast Play Mode:**
  - `UIAdapterRegistry.RegisterAdapter` now replaces existing custom adapters of the same concrete `Type` rather than accumulating duplicate instances across Fast Play Mode sessions.
- **`BaseObservableDataModel.LoadDataFromModel` Fixes:**
  - `ObservableList<T>.Value` setter now suppresses notifications when assigned a list element-wise equal to the current one (preserving the silent-on-equal load contract).
  - Broadened `[NonSaved]` attribute to `AttributeTargets.Field | AttributeTargets.Property` and skipped `[NonSaved]` properties in `ModelDescriptor`.
  - Excluded compiler-generated backing fields from `LoadPlainFields`, ensuring read-only auto-properties are not overwritten during load.
  - Added cross-type / DTO support in `LoadDataFromModel(object model)` by resolving matching readable properties/fields on `model.GetType()` without throwing `TargetException`.
- **FIFO Re-Entrant Transitions in `ObservableVariable<T>`:**
  - When a subscriber mutates `Value` or calls `ForceNotify()` during `OnValueChanged.Invoke`, the transition is enqueued in a FIFO queue and processed pass-by-pass, guaranteeing every subscriber observes each transition in order without missing transitions or receiving duplicate callbacks.
- **Shadowed Base-Class Private Fields:**
  - `ModelDescriptor` includes all declared observable fields across the hierarchy in `ObservableFields` and `LoadObservableFields`, and preserves base-class private plain fields in `LoadPlainFields`.
- **Enum and `Nullable<T>` UI Adapter Support:**
  - Added `UIAdapterRegistry.TryConvertValue` supporting `enum`, `Nullable<T>`, primitives, and strings in `StringInputAdapterBase` and `SliderAdapter`.
  - `SliderAdapter.SetValue` converts enums to float values.
- **Per-Control UI Re-Entrancy Tracking:**
  - Replaced thread-static `_isSettingValue` with per-control `_activeSetValueTargets` in `UIAdapterRegistry`, preventing callbacks on one control from suppressing two-way binding on another control of the same type.
- **`PropertyResolver` Hierarchy & Enumerator Fixes:**
  - `PropertyResolver.GetValueByPathSegment` performs exact case-sensitive field lookup across the entire hierarchy before checking parameterless properties.
  - Disposes `IEnumerator` in a `try / finally` block when resolving indexed path segments.
- **`ObservableList<T>` Null-Items Deserialization Guard:**
  - Guarded `_items` with an internal lazy accessor (`Items => _items ??= new List<T>();`), ensuring operations succeed cleanly even if deserialized with a null items field.

## [2.0.0] - 2026-11-09 - MMVC simplification & opt-in same-value notification

Major release: several behaviors and public APIs changed. See **Migration** below.

### Changed (behavior)
- `ObservableVariable<T>.Value` no longer notifies when assigned a value equal to the current one (`EqualityComparer<T>.Default`); `Value` and `PreviousValue` are left untouched. `ObservableList<T>` is unchanged.
- `LoaderWithModel<M>.LoadDataFromModel` no longer calls `Observable.SetListeners` on the presenter. The presenter keeps its existing binding and observes loaded values as ordinary change notifications (equal values are silent).
- **`ServiceLocator` rewritten around one store `(type, key) -> instance`.** The eviction model is gone: a second live instance with the same key logs a `Debug.LogError` at registration time (type, key and both GameObjects named), is **not** registered, and the first instance keeps the key. `Get` / `GetModel` for that key return the first instance (they no longer return `null`). A destroyed holder's key is empty afterwards; a rejected duplicate never silently takes it over.
  - `RegistrationResult.EvictedDuplicate` renamed to `Duplicate`.
  - `Unregister(instance)` removes only the entry whose stored value is that instance (no-op for a never-registered instance).
  - `Get(type)` without a name: default key, else the sole registered instance of the type; several instances under different keys log an ambiguity error.
  - `GetModel<M>(name)` is derived from the registered `BaseLogic` entries; there is no separate model store.
  - `Services` reads `ServiceLocator.instance` directly (no cached copy, no `ReleaseLocator`); `Services.TestState` holds the single locator (`CaptureTestState` / `RestoreTestState` / `SetTestLocator` signatures unchanged).
- **Execution order simplified to two stages.** `[DefaultExecutionOrder]` was removed from `BaseObservableDataModel`, `LoaderWithModel<M>` and `BaseLogic`; `ServiceLocator` (`-200`) and `BaseObservablePresenter<M>` (`-150`) keep theirs. `MMVCExecutionOrderSynchronizer` now sets models, loaders and logic to `0`, which **resets** the earlier non-default orders it wrote into existing projects (`Assets/` scripts) the next time the Editor loads. Nothing depends on those orders any more because every reader initializes the model fields lazily through `GetModel()`. Projects that deliberately relied on a model's `Awake()` running before other scripts must set that order themselves.
- The unbound-field warning is opt-in: `ObservableSettings.StrictBinding` (default `false`). The "`[AutoBind]` names a missing field" warning is unchanged.
- `BaseLogic<M, P>.GetModel()` no longer keeps its own presenter-initialized flag; it calls the idempotent `EnsureInitialized()` on the paired presenter.

- **`Observable.SetListeners` replays the current model state after binding (`ObservableSettings.SyncOnBind`, default `true`).** Every `[AutoBind]` target receives the current value; two-way controls use their "without notify" path, so nothing is written back to the model and other subscribers are not notified. A handler runs once with the current value only when it opts in with `[ObservableHandler(..., InvokeOnBind = true)]`; handlers without it (sounds, tweens, one-shot commands) are never replayed. A replay is not a change: `PreviousValue` is untouched. Presenters no longer need to paint initial values by hand in `InitializePresenter()`. A control whose scene text differs from the model value is overwritten on bind, so put the intended default into the model.

### Removed (breaking)
- `BasePresenter<M>`: merged into `BaseObservablePresenter<M>`. Presenters derive from `BaseObservablePresenter<M>` (or `ObservablePresenterWithLogic<M, LO>`); a former `BasePresenter<M>` subclass now also binds `[AutoBind]` / `[ObservableHandler]` members, which is a no-op when none are declared.
- `IObservablePresenter.EnsurePresenterInitialized()`: `EnsureInitialized()` is the single entry point. The "`InitializePresenter` runs before handlers attach" guarantee is unchanged, including for explicit `Observable.SetListeners` calls.
- `Observable.SetAutoBindListeners` (both overloads) and `Observable.SetListeners(object)` (model resolved implicitly). `Observable.SetListeners(subscriber, model)` is the only bind API; it binds `[ObservableHandler]` and `[AutoBind]` together and is idempotent.
- `LoaderWithModel<M>.GetModelToSave()`: use `GetModel()`.
- `ObservableList<T>.OnValueChanged` alias: use `OnChanged`. (`ObservableVariable<T>.OnValueChanged` is unchanged.)
- `ServiceLocator.Add`, `ServiceLocator.Remove<T>` / `Remove(Type)`, `Services.Add`, `Services.Remove`: use `TryRegister` / `Unregister`.
- The no-op `BaseLogic<M, P>.Awake` override (`BaseLogic.Awake` still registers the logic).
- `IObservableVariable` gained `CreateHandler`, `CreateAutoBindHandler`, `AddHandler`, `RemoveHandler` and `InvokeHandler`. **Breaking for custom implementers:** a custom observable type must implement them (the built-in `ObservableVariable<T>` / `ObservableList<T>` do). The reflection fallback and the per-type binding bridge classes are gone.

### Added
- `[NotifyOnSameValue]` field attribute and public `ObservableVariable<T>.NotifyOnSameValue` property: every assignment notifies; an equal assignment yields `PreviousValue == Value`. The flag is not serialized, not saved and not copied by `LoadDataFromModel`. Applying the attribute to an `ObservableList<T>` or a non-observable field logs a one-time warning.
- `ObservableSettings.StrictBinding` (see above).
- `ObservableSettings.SyncOnBind` (default `true`; set `false` for the pre-2.0 "bind only" behavior) and `ObservableHandlerAttribute.InvokeOnBind` (default `false`). See "Changed (behavior)".
- Tests in `ObservableBindingTests`: `SetListeners_SyncOnBind_PushesCurrentValueToAutoBindLabel`, `SetListeners_SyncOnBind_TwoWaySlider_DoesNotWriteBackToModel`, `ObservableHandler_InvokeOnBind_CalledOnceWithCurrentValue`, `ObservableHandler_DefaultNoInvokeOnBind`, `Rebind_DoesNotDoubleInvoke`, `SyncOnBind_False_KeepsOldBehavior`.
- Contract test `Contract_EqualAssignment_IsSilent_UnlessNotifyOnSameValue` in `MmvcTriadContractTestsBase`, and the `ObservableAccess` test helper.
- Sample: `SamplePlayerModel.lastRoll` (`[NotifyOnSameValue]`) with a handler that uses `previous`; the sample presenter uses `nameof(...)` in its attributes.
- README section "Change notification semantics".

### Internal
- Subscriptions are tracked in one per-model registry (subscriber -> records of source, handler, managed flag) instead of three separate dictionaries; `ObservableTrackedAction<T>` and the registry take no locks (all access is main-thread, asserted as before), static metadata caches keep theirs.
- One cached `ModelDescriptor` replaces `ModelMetadata` and `ModelLoadDescriptor`; shared `TypeUtil`, `ComponentCache` and `ComponentAutoRequire.EnsureComponents`; the single-model / single-presenter check lives only in `Observable.SetListeners`.
- `TMPInputFieldAdapter` and `InputFieldAdapter` derive from `StringInputAdapterBase<TField>` (priorities and per-adapter re-entrancy guard unchanged). Custom adapters are unaffected.

### Fixed
- Nested `EnsureInitialized()` during `InitializePresenter()` (e.g. via `GetLogic().GetModel()`) no longer binds listeners before initialization completes.
- `Observable.RemoveListeners` now resets presenter binding state so `EnsureInitialized()` rebinds.

### Migration
- **Poke-style re-assignments** (`x.Value = x.Value` to refresh a view) are now silent: call `ForceNotify()`, or mark the field `[NotifyOnSameValue]` (or set `field.NotifyOnSameValue = true`).
- **Save/load code** that relied on the loader rebinding the presenter: the existing binding now receives the loaded values; equal values are silent.
- **Several unnamed Logic of one type** (e.g. pooled board cells) now produce a duplicate error per extra instance: override `RegisterAsSharedSingleton => false`, or give each instance a distinct `singletonName`.
- Rename `BasePresenter<M>` subclasses to derive from `BaseObservablePresenter<M>`; replace `GetModelToSave()` with `GetModel()`, `ObservableList.OnValueChanged` with `OnChanged`, `Services.Add` / `Remove` with `TryRegister` / `Unregister`.
- Custom `IObservableVariable` implementations must add the five handler members (`CreateHandler`, `CreateAutoBindHandler`, `AddHandler`, `RemoveHandler`, `InvokeHandler`).
- **Scene-authored `[AutoBind]` text is overwritten on bind** by the model value. Move the intended default into the model (Inspector value on the model component) instead of the scene label. To keep the old behavior for a whole project, set `ObservableSettings.SyncOnBind = false` from a bootstrap.
- **Manual first paint** in `InitializePresenter()` (`label.text = model.x.Value`, `slider.SetValueWithoutNotify(...)`) can be deleted for `[AutoBind]` fields; for a pure-render `[ObservableHandler]` add `InvokeOnBind = true` and delete the explicit call. Keep `InvokeOnBind` off for handlers with side effects.

## [1.0.21-preview] - 2026-11-08 - Live UI Contracts, Fast Testing Infrastructure & Universal Test Runner Enhancements
- **Live UI Assertions & Automated Audit (`MmvcLiveUiAssert`, `MmvcLiveUiAudit`):**
  - Added `MmvcLiveUiAssert` in `SavableObservable.Tests.Contracts` with user-simulation drivers (`Click`, `SetToggle`, `SetSlider`, `Type`), visual readers (`Text`, `SpriteName`, `Alpha`, `IsEnabled`, `Interactable`), and subscription tracking scopes.
  - Added `ModelToView` and `ViewToModel` round-trip assertions verifying single notification, value convergence, and absence of bounce-back.
  - Added `MmvcLiveUiAudit` for automated reflection-driven scene and prefab testing over `[AutoBind]` fields with typed sample generation, skip attributes (`[LiveUiAuditSkip]`), and `PrefsSandboxUtility` isolation.
- **Universal Unity Test Runner Enhancements:**
  - Added `-IncludeCategories` for targeted category filtering in both live Editor and batch mode.
  - Added `-ListOnly` mode to discover and display test counts by assembly and fixture without running tests.
  - Added report metrics: extraction of slowest 10 tests and per-category breakdown in Markdown and JSON summaries.
  - Added consumer project template wrapper in `Samples~/ConsumerWrapper/Invoke-UnityTests.ps1`.
  - Added dedicated documentation in `Documentation~/Testing.md` and updated `README.md`.

## [1.0.20] - 2026-11-04 - MMVC Framework Hardening & Unity Lifecycle Fixes
- **Dynamic & Additive `ISharedSingleton` / `BaseLogic` Lifecycle:**
  - Added `ServiceLocator.Unregister(ISharedSingleton service, string name = null)` and `Services.Unregister` to remove an instance from tracking and reconcile its key so surviving collided instances recover lookup cleanly.
  - Added `Awake()` and `OnDestroy()` lifecycle hooks on `BaseLogic`: dynamically instantiated (`Instantiate`/`AddComponent`) or additively loaded `BaseLogic` units auto-register in `Awake()` and unregister in `OnDestroy()`.
  - Added `IsRegistered<T>(string name = null)` generic convenience overloads on `ServiceLocator` and `Services`.
  - Fixed scene validity checks in `ServiceLocator.InitializeSharedSingletons()` to check `!scene.IsValid() || !scene.isLoaded` instead of `scene.name == null`, ensuring untitled and test scenes register properly.
  - Removed unused and redundant `_boundPresenters` from `ServiceLocator`, relying on `BaseObservablePresenter.EnsureInitialized()` idempotency.
- **Base-Class Private/Protected `[ObservableHandler]` & `[AutoBind]` Inheritance:**
  - `SubscriberMetadata` in `Observable.cs` now traverses the type hierarchy using `BindingFlags.DeclaredOnly` (stopping before `MonoBehaviour` / `BaseObservableDataModel`), discovering `private` `[ObservableHandler]` methods and `private` `[AutoBind]` fields declared on intermediate generic/abstract base presenters while deduplicating virtual overrides by base definition.
  - `ModelMetadata` in `Observable.cs` and `ModelLoadDescriptor` in `BaseObservableDataModel.cs` traverse the type hierarchy to discover `private [SerializeField]` observable fields declared on base model classes while strictly excluding engine internals.
- **Deep List Cloning in `BaseObservableDataModel.LoadDataFromModel`:**
  - Resolved shallow-copy list aliasing in `LoadDataFromModel`: cloning `ObservableList<T>` elements and `PlainFields` implementing `IList` so mutating a loaded model never mutates the source snapshot or template model.
- **Two-Way UI Adapter Bounce-Back Prevention & Fast Play Mode Custom Adapters:**
  - Replaced mutating property setters with `SetIsOnWithoutNotify`, `SetValueWithoutNotify`, and `SetTextWithoutNotify` plus re-entrancy suppression in `ToggleAdapter`, `SliderAdapter`, `TMPInputFieldAdapter`, and `InputFieldAdapter` to eliminate redundant `Model -> UI -> Model` notifications while preserving full `UI -> Model` user interactivity.
  - Handled numeric fallback parsing using `CultureInfo.InvariantCulture` and default value initialization when clearing input fields.
  - Preserved custom adapters across Fast Play Mode (`ResetRegistry`) via `_customAdapters` while exposing `UIAdapterRegistry.UnregisterAdapter` and `ClearCustomAdapters()` for clean test teardown.
- **Deterministic Main-Thread Capture in `ObservableTrackedAction`:**
  - Added non-generic companion `ObservableTrackedAction` with `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` and static constructor to latch `_mainThreadId` deterministically on the Unity main thread at startup.
- **Safe Component Provisioning & Minor Guards:**
  - Guarded against prefab disk assets and deferred `AddComponent` via `EditorApplication.delayCall` during `OnValidate` in `ComponentAutoRequire`, eliminating `SendMessage` warnings.
  - Added null guards for `_items` in `ObservableList.OnBeginGui()` and `OnValidate()`.
  - Removed `UnityEditor.TestRunner` reference from `Tests/Runtime/com.evialdev.savableobservable.RuntimeTests.asmdef`.
  - Corrected `README.md` `ObservableTrackedAction` callback parameter signature example.
- **Documentation & Architecture Rule Clarification:**
  - Clarified strict MMVC rule: Logic must never get, store, or call a Presenter (`GetComponent<XPresenter>()`, `[SerializeField]`, `Find*`). Logic writes Model observables; Presenter reacts via `[AutoBind]` / `[ObservableHandler]`.
  - Documented the canonical one-shot command pattern (Model event token + payload, e.g. `latestEvent` and `eventToken++`) as the standard alternative for transient view commands.
  - Removed outdated `ScorePresenter` direct access example from `ScoreLoader.LoadState`.

## [1.0.19] - 2026-11-04 - Built-in ServiceLocator & ISharedSingleton, Primary BaseLogic<M, P>, Collision-Eviction & Deterministic Execution-Order Pipeline
- **Built-in `ServiceLocator`, `Services`, `ISharedSingleton`, and `RegistrationResult`:**
  - Added `ISharedSingleton`, `ServiceLocator`, `Services`, and `RegistrationResult` (`Added`, `SameInstance`, `EvictedDuplicate`, `Rejected`) to the `SavableObservable` runtime assembly with zero external dependencies.
  - `ServiceLocator.Awake()` (`[DefaultExecutionOrder(-200)]`) automatically discovers and registers all scene `ISharedSingleton` instances (`FindObjectsInactive.Include`) in Pass 1, and initializes/binds all scene `IObservablePresenter` instances in Pass 2 so `Services.Get<T>()` is guaranteed ready inside `InitializePresenter()`.
  - Added `Services.GetModel<M>(string name = null)` to resolve a registered MMVC unit's `BaseObservableDataModel` directly by model type (and optional `Logic.Name`).
  - Strictly rejected `IObservablePresenter`, `BasePresenter<M>`, `BaseObservableDataModel`, and interface types from `ServiceLocator.TryRegister` and Editor `TypeCache` validation: only `BaseLogic` and custom non-MMVC `MonoBehaviour` services may be registered as shared singletons.
- **Primary `BaseLogic<M, P>` & Automatic `ISharedSingleton` Inheritance:**
  - Added non-generic root `BaseLogic : MonoBehaviour, ISharedSingleton` with built-in `[SerializeField] protected string singletonName` and `Name` property (returning `singletonName` when assigned or `GetType().Name` otherwise, never `gameObject.name`).
  - Replaced 1-parameter `BaseLogic<M>` with `BaseLogic<M, P> : BaseLogic where M : BaseObservableDataModel where P : BaseObservablePresenter<M>` as the primary MMVC component.
  - Adding `BaseLogic<M, P>` to a GameObject in the Unity Editor automatically provisions both `M` (Model) and `P` (Presenter) via `ComponentAutoRequire.EnsureComponent` in `Reset()` and `OnValidate()`.
  - Removed `ComponentAutoRequire` from `BasePresenter<M>` and `ObservablePresenterWithLogic<M, LO>`, eliminating circular dependency locks in the Unity Inspector.
  - `BaseLogic<M, P>` exposes `GetModel()` and intentionally omits `GetPresenter()` so logic never calls presenter methods directly.
  - `BaseLogic<M, P>.GetModel()` defensively initializes model fields and privately calls `EnsureInitialized()` on its sibling presenter `P` on first access, ensuring even prefabs instantiated at runtime in a disabled (`inactive`) state have their presenter initialized and bound before logic mutates the model.
- **Live-Instance Collision-Eviction Rule:**
  - `ServiceLocator` tracks live instances per `(Type, key)` (pruned via Unity object liveness checks).
  - When two or more live instances share the same `(Type, Name)` key (e.g., multi-instance prefabs like board cells or unnamed duplicate modals), `ServiceLocator` silently evicts the key from `_serviceBindings` and `_modelBindings` during registration while still initializing and binding each GameObject's local presenter.
  - Calling `Services.Get<T>()` or `Services.GetModel<M>()` for an evicted key (checked before any single-entry fallback) logs a descriptive `Debug.LogError` instructing the developer to assign distinct `singletonName` values in the Inspector.
- **Deterministic 5-Stage Execution-Order Pipeline & `InitializePresenter()`:**
  - Added `[DefaultExecutionOrder]` pipeline and `Editor/MMVCExecutionOrderSynchronizer.cs` (`[InitializeOnLoad]`) to automatically synchronize `MonoImporter` execution order for project scripts under `"Assets/"`: `BaseObservableDataModel` (`-300`) -> `LoaderWithModel<M>` (`-250`) -> `ServiceLocator` (`-200`) -> `BaseObservablePresenter<M>` (`-150`) -> `BaseLogic` (`0`).
  - Added `EnsureInitialized()`, `EnsurePresenterInitialized()`, and `protected virtual void InitializePresenter()` to `BaseObservablePresenter<M>`. `Observable.SetListenersCore` invokes `EnsurePresenterInitialized()` before wiring handlers, guaranteeing static UI control setup (slider ranges, dropdown options, button listeners, initial CanvasGroup state) always executes once before bindings are attached.
- **Added `[NonSaved]` Attribute for Transient Observable Fields:**
  - Added `[NonSavedAttribute]` (`Runtime/NonSavedAttribute.cs`) to mark runtime/UI-only `ObservableVariable<T>` and `ObservableList<T>` fields that must be skipped during `BaseObservableDataModel.LoadDataFromModel` and persistence serialization.

## [1.0.18] - 2026-10-13 - Minor files fix

## [1.0.17] - 2026-10-12 - UI listener unbinding unification, ASMDEF decoupling & ObservableList PreviousValue fix
- **Fixed Redundant Two-Way UI Listener Removal:**
  - Consolidated UI listener cleanup into `UnbindActions` closure tracking, eliminating the redundant `UiListenerTokens` list and ensuring `IUIListenerAdapter.RemoveListener` is invoked exactly once per token during subscriber detachment and data model destruction.
- **Decoupled Core Editor Assembly & Isolated Unity Pipeline Tools:**
  - Stripped `Unity.Pipeline`, `Unity.Pipeline.Attributes`, and `com.unity.pipeline` versionDefines from `Editor/com.evialdev.savableobservable.Editor.asmdef`.
  - Moved developer CLI pipeline tools (`MmvcSceneBuilder.cs`, `MmvcSelfTest.cs`) to `Tests/Editor/Pipeline/` backed by a dedicated test assembly (`com.evialdev.savableobservable.Tests.Pipeline.asmdef`) constrained by `UNITY_INCLUDE_TESTS` and `HAS_UNITY_PIPELINE`.
  - Consumer projects importing this package now compile cleanly on any Unity version without requiring `com.unity.pipeline` installed.
- **ObservableList Lazy Delta-Based `PreviousValue`:**
  - `PreviousValue` is always available, rebuilt on read from a recorded mutation delta. There is no per-mutation allocation, except `Clear` and `Value` replacement.
- **Added Regression Tests:**
  - Added tests validating `ObservableList.PreviousValue` population without event listeners, allocation-free mutations via `Is.Not.AllocatingGCMemory()`, and mutation delta reconstruction.
  - Added test verifying two-way UI listener cleanup detaches correctly on `Observable.RemoveListeners`.

## [1.0.16] - 2026-10-08 - Performance, AOT/IL2CPP hardening & architecture simplification
- **Unity 6.6 & Configurable Enter Play Mode Support (Domain Reload Disabled):**
  - Added `Unity.Scripting.LifecycleManagement` annotations (`[AutoStaticsCleanup]`, `[NoAutoStaticsCleanup]`) and marked containing types with `partial` modifier to comply with Unity 6.5/6.6+ Roslyn analyzers (UAL0011-UAL0014) and source generation.
  - Applied `[AutoStaticsCleanup]` and `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` to `Observable._instanceData`, ensuring deterministic teardown of per-instance model subscriptions and UI listener tokens across Play sessions without Domain Reload.
  - Applied `[NoAutoStaticsCleanup]` to immutable type-reflection caches and singleton binding bridges (`_subscriberMetadataCache`, `_modelMetadataCache`, `_bindingCache`, `_loadDescriptors`, `ObservableTrackedAction._mainThreadId`, `ObservableVariableBinding.Instance`, `ObservableListBinding.Instance`) to preserve precomputed reflection metadata across Play sessions.
  - Overhauled `UIAdapterRegistry` for domain-reload persistence: added `[AutoStaticsCleanup]`, `[NoAutoStaticsCleanup]`, and explicit `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` reset, preventing duplicate adapter registrations and stale component references across Play sessions.
  - Guarded all `using Unity.Scripting.LifecycleManagement;` imports and attributes with `#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT` and configured `versionDefines` in `com.evialdev.savableobservable.asmdef`, ensuring full backwards compatibility with Unity 2021.3 through 6.4 while enabling native engine lifecycle attributes in Unity 6.5+.
- **Allocation-Free Event Dispatch:**
  - Optimized `ObservableTrackedAction<T>.Invoke` to be 100% allocation-free by using copy-on-write snapshotting on `Add`/`Remove`, eliminating `Action<T>[]` heap allocations on every event dispatch.
  - Aligned thread assertions across `Add`, `Remove`, `Invoke`, and `HandlerCount` to enforce main-thread execution cleanly.
- **Overhauled `Observable.SetListeners` & AOT/IL2CPP Support:**
  - Replaced runtime dynamic expression compilation (`System.Linq.Expressions.Expression.Compile()`) with strongly-typed generic binding bridges (`Delegate.CreateDelegate` with safe invocation fallback).
  - Unified listener cleanup with direct unbind closures, replacing the $O(N \times M)$ reflection-based unsubscription loop and eliminating caught `ArgumentException` spikes during teardown.
  - Added type-level metadata caching for subscriber attributes (`[ObservableHandler]`, `[AutoBind]`) and model observable fields, eliminating redundant multi-pass reflection and dictionary allocations on repeated bindings.
  - Unified model-to-UI and UI-to-model subscription tracking into a single deterministic unbinding pipeline.
- **Optimized `ObservableList<T>` Mutations:**
  - Skipped array cloning in `CapturePrevious()` when there are no active event subscribers, drastically reducing GC pressure during initial population, factory spawns, and bulk updates.
  - Guarded `AddRange` against empty collection operations.
- **Cached Save/Load Descriptors:**
  - Added cached `ModelLoadDescriptor` in `BaseObservableDataModel.LoadDataFromModel`, caching property and observable-field reflection metadata per model type to accelerate save/load state restoration.
- **UI Adapters Modernization:**
  - Removed `ButtonTextAdapter` (which performed expensive recursive `GetComponentInChildren` searches on every value change); label text should now be targeted directly on `TextMeshProUGUI` or `Text` components.
  - Added built-in two-way `SliderAdapter` for `UnityEngine.UI.Slider`.
- **Component Access Caching:**
  - Cached `GetComponent` lookups in private backing fields for `BasePresenter<M>.GetModel()`, `BaseLogic<M>.GetModel()`, `ObservablePresenterWithLogic<M, LO>.GetLogic()`, `LoaderWithModel<M>.GetModel()`, and `LoaderWithModelAndLogic<M, LO>.GetLogic()` to minimize native engine interop overhead during frequent updates.
- **Loaders & Preprocessor Directives:**
  - Simplified `LoaderWithModel<M>.LoadDataFromModel(object state)` to call `model.LoadDataFromModel(state)` directly on the model instance instead of using reflection, leveraging the `where M : BaseObservableDataModel` constraint.
  - Replaced deprecated `DEVELOPMENT_BUILD` preprocessor directive in `ObservableTrackedAction.cs` with `#if UNITY_EDITOR || DEBUG || UNITY_ENABLE_CHECKS` and runtime check `Debug.isDebugBuild`.

## [1.0.15] - 2026-10-07 - Strict MMVC architecture guards & generic model constraints
- Added `where M : BaseObservableDataModel` compile-time generic constraint to `BasePresenter<M>`, `BaseObservablePresenter<M>`, `BaseLogic<M>`, and `LoaderWithModel<M>`. Non-observable models and interfaces are strictly prohibited and caught at compile time.
- Added `where LO : BaseLogic<M>` generic constraint to `ObservablePresenterWithLogic<M, LO>` and `LoaderWithModelAndLogic<M, LO>`, ensuring type safety between presenters/loaders and their corresponding logic.
- Simplified `BaseObservablePresenter<M>.GetObservableModel()` now that `M` is statically known to derive from `BaseObservableDataModel`.
- Enforced strict "One MMVC unit per GameObject" architecture:
  - Added `[DisallowMultipleComponent]` to `BaseObservableDataModel`.
  - Added runtime duplicate validation logging `LogError` when multiple models or presenters are found on the same GameObject during listener setup.
  - Added strict same-GameObject validation in `Observable.SetListeners(subscriber, model)` and `Observable.SetAutoBindListeners(subscriber, model)`: cross-GameObject binding is rejected with `LogError` to prevent subscription and lifecycle leaks.
- Factored out `Observable.ResolveModel`, `SetListenersCore`, and `SetAutoBindListenersCore` to eliminate duplicate model discovery code and prevent duplicate error logs.

## [1.0.14] - 2026-10-07 - Explicit model listener binding
- Added `IObservablePresenter` and overloads of `Observable.SetListeners` / `SetAutoBindListeners` that accept an explicit model, supporting presenters when a GameObject contains multiple data models.
- Updated loader and singleton integration to pass the presenter's associated model when wiring listeners.

## [2026-02-21] - Editor/runtime assembly separation
- Added a dedicated Editor assembly with property drawers for observable variables and lists, plus a shared serialized-property resolver.
- Split display-only UI adapters from listener-capable adapters and added explicit lookup for two-way binding support.
- Removed Odin Inspector-specific drawer integrations.

## [2026-02-18] - Binding and serialization hardening
- Added safer model-state loading with null, type, and reflection validation, and ensured reactive listeners are initialized after state restoration.
- Made listener setup idempotent and added deterministic cleanup for UI listeners and observable subscriptions.
- Added observable-field caching and re-entrant event dispatch safeguards.

## [2026-02-12] - Two-way UI binding and observable lists
- Added two-way model/UI binding for toggles and text input fields, including conversion of basic input values to their observable types.
- Added \ObservableList<T>\ with change notifications, previous-state snapshots, and Unity Inspector editing support.
- Improved binding validation and stability, and declared Textmesh Pro as a package dependency.

## [2026-02-09] - Declarative UI binding
- Added \[AutoBind]\ and UI adapter APIs for binding observable values to Textmesh Pro, Unity UI text, toggles, buttons, and images.
- Added adapter registration and registry lookup for custom UI components.

## [2026-01-16] - Subscription lifecycle management
- Added tracked observable subscriptions with automatic cleanup when subscribers or models are removed, plus cached observable-field reflection data.
- Added observable model ownership and listener initialization checks to improve lifecycle validation.

## [2026-01-12] - Observable serialization and editor support
- Added Unity Inspector property drawing and explicit notifications for observable values edited in the Inspector; avoided firing events during deserialization.
- Expanded \ObservableVariable<T>\ support to arbitrary generic value types and improved observable-field discovery and model-state restoration.

## [2025-10-11] - Presenter event handlers
- Added \[ObservableHandler]\ for per-variable presenter methods with handlers accepting no arguments, the new value, or current and previous values.
- Added validation for conflicting handler strategies and missing observable handlers.

## [2025-09-25] - Save/load integration refactor
- Reworked loaders around \LoaderWithModel<M>\ and \LoaderWithModelAndLogic<M, LO>\, exposing model access and model-state restoration without coupling the framework to one save system.
- Restored observable model initialization and state-copy support for observable and regular model fields.

## [2025-09-24] - Type-safe change notifications
- Changed observable updates to publish the changed variable through \IObservableVariable\, including current and previous values, instead of type-specific previous/current/name arguments.
- Made observable presenters require a model-change handler and updated the example flow to keep business actions in logic and UI reactions in presenters.
- Added a separate non-reactive \BasePresenter<M>\ base class.

## [2025-09-22] - Observable value support
- Added nullable boolean observables and refined observable value serialization behavior.

## [2025-08-19] - Package layout adjustment
- Moved runtime sources into a Unity \Assets/Plugins/SavableObservable\ layout, then restored the package's \Runtime\ layout and updated package contents/version metadata.

## [2025-07-23] - Observable listener setup
- Moved listener registration into the core \Observable\ API, allowing model changes to notify presenter handlers through reflection-based subscriptions.

## [2025-06-30] - Package assembly setup
- Added a Unity assembly definition for the framework and aligned runtime code under the \SavableObservable\ namespace.
- Removed hard dependencies on the sample project's save-system types from the loader base.

## [2025-06-29] - Initial framework foundation
- Added the initial Unity MMVC framework with observable variables, reflection-based model initialization and change subscriptions, presenter/logic base classes, and save/load loader abstractions.
- Added Unity package metadata and a pipeline example demonstrating reactive model, logic, presenter, and loader components.
