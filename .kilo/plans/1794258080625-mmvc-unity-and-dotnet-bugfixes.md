# MMVC Framework Unity & .NET Bug-Fix Plan

## Goal
Update `com.evialdev.savableobservable` to require **Unity 6000.5+**, remove legacy `< 6000.5` conditional compilation shims, and fix all 12 verified Unity-specific and .NET bugs across Runtime and Editor assemblies. Every task includes explicit **Code Changes**, **Test Updates / New Regression Tests**, and **Documentation & Skill Updates** (including syncing `mmvc-unity-skill/` to the user profile at `C:\Users\alexx\.skills\mmvc-unity\`).

### Execution Sequence With Companion Feature Plan
1. **Phase 1 (This Plan — `.kilo/plans/1794258080625-mmvc-unity-and-dotnet-bugfixes.md`):** Execute **FIRST** in `unity-savableobservable`. Establishes the `Unity 6000.5` baseline, removes `< 6000.5` `#if` shims, and fixes the core runtime/editor bugs (including `Observable.IsTargetAlive` fake-null guards, `UIAdapters.TryConvertValue` for `enum`/`Nullable<T>`, `UIAdapterRegistry` per-control `_activeSetValueTargets` re-entrancy guards, and FIFO `ObservableVariable<T>` re-entrant transition queuing).
2. **Phase 2 (`.kilo/plans/1794369600000-command-aware-autobind-and-listener-set.md`):** Execute **SECOND** in `unity-savableobservable`, building directly on Phase 1's `Observable.cs` and `UIAdapters.cs` foundations (including `TMP_DropdownAdapter` / `DropdownAdapter`, `[ClickCommand]` for zero-argument sync Button actions, and the framework `ListenerSet` that supersedes AttaxUnity's `UiListeners.cs`).
3. **Phase 3 (`.kilo/plans/1794370000000-attax-command-autobind-and-listeners-adoption.md`):** Execute **THIRD** in `F:\Unity_projects\AttaxUnity` to adopt Command-Aware `AutoBind` (`PlayerSettings`, `BaseAISettings`, `PrivateRoom`, `OnlineLobby`), display `[AutoBind]` (`ByteCodePresenter`), `[ClickCommand]` on 20 sync action buttons, and framework `BaseObservablePresenter<M>.Listeners` across all modals and presenters, retiring `Assets/Scripts/Base/UiListeners.cs`.

---

## Consumer Impact & Contract Compatibility Summary
* **Public API / Signatures:** **0 breaking changes.** All existing classes, method signatures, generic constraints, and attributes remain backward-compatible (`[NonSaved]` is broadened from `Field` to `Field | Property`, which is strictly additive).
* **Existing Consumer Code (`AttaxUnity` or other projects):** **No consumer code changes required.**
* **Contract Test Suites (`MmvcTriadContractTestsBase`, `MmvcPersistenceContractTestsBase`, `MmvcLiveUiAudit`):** No contract breaks. All existing contract tests remain valid.
* **Subtle Behavioral Bug Fixes to Be Aware Of:**
  1. **Minimum Unity Editor version:** Now `6000.5` in `package.json`.
  2. **`LoadDataFromModel` on `ObservableList<T>` (`#7A`):** Previously, `LoadDataFromModel` unconditionally fired `OnChanged` for every `ObservableList<T>` even when the loaded list had identical items (violating `LoaderWithModel`'s documented "equal values are silent" contract). Now, `ObservableList<T>.Value` setter suppresses `OnChanged` when the new list is element-wise equal (`AreListsEqual`) to the current list; in-place list mutators (`Add`, `Remove`, `Clear`, `Insert`, `this[i]`, `ForceNotify`) still notify on every mutation. If a consumer relied on `LoadDataFromModel` firing `OnChanged` for an unchanged list, they should call `list.ForceNotify()`.
  3. **Read-only auto-properties in `LoadDataFromModel` (`#7B`):** Previously, `public int Id { get; }` had its compiler-generated `<Id>k__BackingField` overwritten during `LoadDataFromModel`. Now compiler-generated backing fields are excluded from `LoadPlainFields`, so read-only properties are respected and writable auto-properties are only set once through their property setter.
  4. **Re-entrant `ObservableVariable<T>.Value` changes (`#8`):** Previously, setting `Value` inside an `OnValueChanged` handler immediately mutated `_value`/`PreviousValue` mid-loop (causing later handlers in the first pass to miss the first transition and receive the second transition twice). Now, re-entrant `Value` transitions are queued FIFO so every handler sees each transition in order exactly once.

---

## Ordered Implementation Tasks

### Task 1 — Bump Minimum Unity Version to `6000.5` & Remove `#if` Shims
* **Code Files:**
  * `Packages/com.evialdev.savableobservable/package.json`
  * `Packages/com.evialdev.savableobservable/Runtime/LifecycleManagementCompatibility.cs` (+ `.meta`)
  * `Packages/com.evialdev.savableobservable/Runtime/com.evialdev.savableobservable.asmdef`
  * `Packages/com.evialdev.savableobservable/Editor/com.evialdev.savableobservable.Editor.asmdef`
  * `Packages/com.evialdev.savableobservable/Tests/Editor/com.evialdev.savableobservable.EditorTests.asmdef`
  * `Packages/com.evialdev.savableobservable/Runtime/ModelDescriptor.cs`
  * `Packages/com.evialdev.savableobservable/Runtime/Observable.cs`
  * `Packages/com.evialdev.savableobservable/Runtime/ObservableSettings.cs`
  * `Packages/com.evialdev.savableobservable/Runtime/ObservableTrackedAction.cs`
  * `Packages/com.evialdev.savableobservable/Runtime/ServiceLocator.cs`
  * `Packages/com.evialdev.savableobservable/Runtime/UIAdapters.cs`
* **Code Changes:**
  1. Set `"unity": "6000.5"` in `package.json`.
  2. Delete `Runtime/LifecycleManagementCompatibility.cs` and `Runtime/LifecycleManagementCompatibility.cs.meta`.
  3. Remove the `UNITY_HAS_LIFECYCLE_MANAGEMENT` entries from `"versionDefines"` in the three `.asmdef` files.
  4. Remove all `#if UNITY_6000_5_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT` preprocessor guards so `using Unity.Scripting.LifecycleManagement;`, `[AutoStaticsCleanup]`, and `[NoAutoStaticsCleanup]` are unconditional.
* **Test Updates:**
  * `Packages/com.evialdev.savableobservable/Tests/Editor/DomainReloadLifecycleTests.cs`: Remove `#if UNITY_6000_0_OR_NEWER || UNITY_6_OR_NEWER || UNITY_HAS_LIFECYCLE_MANAGEMENT` and `#endif` so domain-reload tests are unconditionally compiled.
  * `Packages/com.evialdev.savableobservable/Tests/Shared/MmvcLiveUiAudit.cs:265`: Replace deprecated `FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)` with `FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include)`.
* **Documentation & Skill Updates:**
  * `Packages/com.evialdev.savableobservable/Documentation~/UnityLifecycle.md`: Update section 8 (`Domain Reload Disabled`) to state that Unity 6000.5+ is the minimum supported version and `[AutoStaticsCleanup]` / `[NoAutoStaticsCleanup]` are unconditionally active.
  * `Packages/com.evialdev.savableobservable/CHANGELOG.md`: Record the minimum Unity version update to `6000.5` and removal of `< 6000.5` compatibility shims.

---

### Task 2 — Fix `ServiceLocator.InitializeSharedSingletons()` Scene Scanning During `Awake()`
* **Code File:** `Packages/com.evialdev.savableobservable/Runtime/ServiceLocator.cs`
* **Problem:** During `MonoBehaviour.Awake()` when loading a scene, `gameObject.scene.isLoaded` is still `false`, causing `ServiceLocator` to skip `scene.GetRootGameObjects()` and fall back to global `FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include)` across all loaded scenes.
* **Code Changes:**
  1. In `InitializeSharedSingletons()`, attempt `scene.GetRootGameObjects()` whenever `scene.IsValid() && (Application.isPlaying || scene.isLoaded)` inside a `try/catch (ArgumentException)` block.
  2. Only fall back to `FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include)` if `GetRootGameObjects()` could not be used, and filter `if (item != null && (!scene.IsValid() || item.gameObject.scene == scene))` so objects from other scenes or `DontDestroyOnLoad` are never pulled into this scene's `ServiceLocator`.
* **Test Updates:**
  * `Packages/com.evialdev.savableobservable/Tests/Editor/MMVCRestrictionsTests.cs` (or `Packages/com.evialdev.savableobservable/Tests/Runtime/MmvcPlayModeTests.cs`): Add a regression test verifying that `ServiceLocator.InitializeSharedSingletons()` only discovers singletons/presenters in its own `gameObject.scene` and ignores `MonoBehaviour`s in other sceneseven when `scene.isLoaded` is `false`.
* **Documentation & Skill Updates:**
  * `Packages/com.evialdev.savableobservable/Documentation~/UnityLifecycle.md`: Clarify in section 2 that `ServiceLocator` scopes discovery strictly to `gameObject.scene` via `GetRootGameObjects()` during `Awake()` (even before `scene.isLoaded` flips to `true`).

---

### Task 3 — Fix `UnityEngine.Object` Fake-Null Check in `Observable.SubscribeAutoBindHandler`
* **Code File:** `Packages/com.evialdev.savableobservable/Runtime/Observable.cs`
* **Problem:** `var currentUiComponent = uiField.GetValue(obj); if (currentUiComponent != null)` performs a CLR reference equality check on `object`, which evaluates to `true` for unassigned Inspector `[SerializeField]` fields (Unity fake-null) and destroyed `UnityEngine.Object` components.
* **Code Changes:**
  1. Add helper `private static bool IsTargetAlive(object obj) => obj != null && !(obj is UnityEngine.Object uo && uo == null);`.
  2. Replace `if (currentUiComponent != null)` in `modelToUiHandler` (`line 337`) and two-way binding setup (`line 356`) with `if (IsTargetAlive(currentUiComponent))`.
* **Test Updates:**
  * `Packages/com.evialdev.savableobservable/Tests/Editor/ObservableBindingTests.cs`: Add a test where a presenter has an `[AutoBind]` UI component (`TextMeshProUGUI` and `Slider`) that is destroyed via `Object.DestroyImmediate` (producing a non-null managed wrapper that is `== null` in Unity) before `SetListeners` and before a model value mutation, asserting zero `Debug.LogError` / `Debug.LogWarning` emissions.
* **Documentation & Skill Updates:**
  * `Packages/com.evialdev.savableobservable/CHANGELOG.md`: Document fake-null `UnityEngine.Object` guard on `[AutoBind]` fields.

---

### Task 4 — Fix Editor `OnValidate`, Undo/Redo, and Multi-Object Editing in `ObservableVariable`, `ObservableList`, `BaseObservableDataModel`, and Drawers
* **Code Files:**
  * `Packages/com.evialdev.savableobservable/Runtime/BaseObservableDataModel.cs`
  * `Packages/com.evialdev.savableobservable/Runtime/ObservableVariable.cs`
  * `Packages/com.evialdev.savableobservable/Runtime/ObservableList.cs`
  * `Packages/com.evialdev.savableobservable/Editor/PropertyResolver.cs`
  * `Packages/com.evialdev.savableobservable/Editor/ObservableVariableDrawer.cs`
  * `Packages/com.evialdev.savableobservable/Editor/ObservableListDrawer.cs`
* **Code Changes:**
  1. **`BaseObservableDataModel.cs`:** Add `#if UNITY_EDITOR protected virtual void OnValidate()` that calls `EnsureFieldsInitialized()` and iterates `Observable.GetCachedObservableFields(this)` to invoke `(field.GetValue(this) as IObservableVariable)?.OnValidate()`.
  2. **`ObservableVariable.cs` & `ObservableList.cs`:**
     * Keep `_lastValidatedValue` / `_lastValidatedSnapshot` synchronized whenever `Value` or list mutation methods run in `#if UNITY_EDITOR`.
     * Ensure `OnValidate()` uses `_lastValidatedValue` / `_lastValidatedSnapshot` when triggered outside an active drawer `EndChangeCheck()` (e.g. from `MonoBehaviour.OnValidate()` after `Undo.PerformUndo()` or Inspector Reset), and clear `_hasEditorGuiSnapshot` cleanly so `Layout`/`Repaint` events do not clobber the pre-undo baseline.
  3. **`PropertyResolver.cs` & Drawers:**
     * Add `PropertyResolver.GetTargetObjectsOfProperty(SerializedProperty property)` that resolves the property path across all `property.serializedObject.targetObjects`.
     * Update `ObservableVariableDrawer` and `ObservableListDrawer` to call `OnBeginGui()` and `OnValidate()` on all resolved target objects so multi-object editing (`[CustomPropertyDrawer(..., true)]`) works across every selected instance.
* **Test Updates:**
  * `Packages/com.evialdev.savableobservable/Tests/Editor/ObservablePrimitivesTests.cs`: Add tests verifying:
    1. `BaseObservableDataModel` `OnValidate()` forwards validation to its `ObservableVariable<T>` and `ObservableList<T>` fields when `SerializedObject` changes are applied (simulating Undo/Redo without `PropertyDrawer.OnGUI`).
    2. `PropertyResolver.GetTargetObjectsOfProperty` resolves targets across a multi-object `SerializedObject(new Object[] { modelA, modelB })`.
* **Documentation & Skill Updates:**
  * `Packages/com.evialdev.savableobservable/README.md`, `mmvc-unity-skill/SKILL.md`, and `mmvc-unity-skill/references/framework-guide.md`: Note that `BaseObservableDataModel.OnValidate()` (in `UNITY_EDITOR`) forwards editor validation to observable fields, so custom models overriding `OnValidate()` should call `base.OnValidate()`.

---

### Task 5 — Add `[DefaultExecutionOrder(-200)]` to `ServiceLocator` & Batch `MMVCExecutionOrderSynchronizer`
* **Code Files:**
  * `Packages/com.evialdev.savableobservable/Runtime/ServiceLocator.cs`
  * `Packages/com.evialdev.savableobservable/Editor/MMVCExecutionOrderSynchronizer.cs`
* **Code Changes:**
  1. Add `[DefaultExecutionOrder(-200)]` to `ServiceLocator` so execution order `-200` is guaranteed by attribute even before or without `.meta` sync.
  2. In `MMVCExecutionOrderSynchronizer.SyncAll()`, collect all `(MonoScript script, int targetOrder)` pairs where `MonoImporter.GetExecutionOrder(script) != targetOrder` first; if the list is non-empty, wrap the `MonoImporter.SetExecutionOrder` loop in `AssetDatabase.StartAssetEditing()` / `try { ... } finally { AssetDatabase.StopAssetEditing(); }`.
* **Test Updates:**
  * `Packages/com.evialdev.savableobservable/Tests/Editor/MMVCRestrictionsTests.cs`: Add an assertion verifying `typeof(ServiceLocator).GetCustomAttribute<DefaultExecutionOrder>()?.order == -200` and `typeof(BaseObservablePresenter<>).GetCustomAttribute<DefaultExecutionOrder>()?.order == -150`.
* **Documentation & Skill Updates:**
  * `Packages/com.evialdev.savableobservable/Documentation~/UnityLifecycle.md` and `mmvc-unity-skill/references/framework-guide.md`: Update the Execution Order table to note that `ServiceLocator` also carries `[DefaultExecutionOrder(-200)]` in addition to `MMVCExecutionOrderSynchronizer`.

---

### Task 6 — Prevent Duplicate Custom UIAdapter Accumulation Across Fast Play Mode Runs
* **Code File:** `Packages/com.evialdev.savableobservable/Runtime/UIAdapters.cs`
* **Code Changes:**
  1. In `UIAdapterRegistry.RegisterAdapter(IUIAdapter adapter)` and `RegisterListenerAdapter(IUIListenerAdapter listenerAdapter)`, remove any existing custom adapter of the same concrete `Type` (`adapter.GetType()`) from `_customAdapters` and `_adapters` (and `_customListenerAdapters` / `_listenerAdapters`) before inserting the new instance.
  2. Keep `_customAdapters` surviving `ResetRegistry()` while ensuring repeated `RegisterAdapter(new MyCustomAdapter())` calls in runtime bootstrap code replace rather than accumulate duplicate instances.
* **Test Updates:**
  * `Packages/com.evialdev.savableobservable/Tests/Editor/DomainReloadLifecycleTests.cs`: Extend `CustomAdapterRegistration_SurvivesSubsystemRegistrationReset_And_CanBeClearedExplicitly` (or add a companion test) verifying that calling `UIAdapterRegistry.RegisterAdapter(new DummyCustomAdapter())` across multiple simulated Play Mode runs replaces the previous `DummyCustomAdapter` instance instead of growing `_customAdapters`.
* **Documentation & Skill Updates:**
  * `Packages/com.evialdev.savableobservable/Documentation~/UnityLifecycle.md` and `Packages/com.evialdev.savableobservable/CHANGELOG.md`: Document that `UIAdapterRegistry.RegisterAdapter` deduplicates custom adapters by concrete adapter type across Fast Play Mode sessions.

---

### Task 7 — Fix `BaseObservableDataModel.LoadDataFromModel` (3 Sub-Bugs)
* **Code Files:**
  * `Packages/com.evialdev.savableobservable/Runtime/ObservableList.cs`
  * `Packages/com.evialdev.savableobservable/Runtime/ModelDescriptor.cs`
  * `Packages/com.evialdev.savableobservable/Runtime/BaseObservableDataModel.cs`
* **Code Changes:**
  1. **7A (`ObservableList<T>.Value` equality check):**
     * In `ObservableList<T>.Value` setter (`ObservableList.cs:67-72`), normalize `var next = value ?? new List<T>();`. If `AreListsEqual(_items, next)` is `true`, assign `_items = next;` (updating `_itemsAtDelta = _items; _countAtDelta = _items.Count;`) and return without recording a new snapshot delta or calling `NotifyChanged()`.
     * Note: `[NotifyOnSameValue]` remains unsupported on `ObservableList<T>` (preserving the existing warning in `ModelDescriptor` and test `NotifyOnSameValueAttribute_OnListOrPlainField_LogsNoEffectWarning`).
  2. **7B (Exclude compiler-generated backing fields & support `[NonSaved]` on properties):**
     * Update `[NonSavedAttribute]` (`BaseObservableDataModel.cs:11`) to `[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]`.
     * In `ModelDescriptor.cs`:
       * Skip properties with `prop.IsDefined(typeof(NonSavedAttribute), true)` when populating `LoadProperties`.
       * Skip fields with `field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)` when populating `LoadPlainFields`.
  3. **7C (Support DTO / cross-type `model` argument in `LoadDataFromModel(object model)`):**
     * When `model.GetType() != this.GetType()` and `!GetType().IsAssignableFrom(model.GetType())`, resolve matching readable properties/fields on `model.GetType()` by name and compatible type instead of invoking `this`'s `PropertyInfo.GetValue(model)` / `FieldInfo.GetValue(model)` directly (which throws `TargetException`).
* **Test Updates:**
  * `Packages/com.evialdev.savableobservable/Tests/Editor/ModelPersistenceTests.cs`:
    1. In `LoaderWithModel_LoadDataFromModel_EqualValues_AreSilent`: also assert `Assert.AreEqual(inventoryEventsBefore, presenter.InventoryChangeEventsCount, "Loading equal ObservableList values must not notify.");`.
    2. In `DescriptorCoverageModel` / `LoadDataFromModel_Descriptor_CoversPropertiesObservablePlainNonSavedAndCloning`: add a read-only auto-property `public int AutoReadOnly { get; } = 42;` and a `[NonSaved] public string TransientProperty { get; set; }`, and assert neither is overwritten during `LoadDataFromModel`.
    3. Add a test `LoadDataFromModel_FromPlainDtoObject_CopiesMatchingFieldsAndObservablesWithoutTargetException` passing a plain DTO (`SampleSaveDto` or custom DTO) directly to `model.LoadDataFromModel(dto)`.
* **Documentation & Skill Updates:**
  * `Packages/com.evialdev.savableobservable/README.md`, `mmvc-unity-skill/SKILL.md`, and `mmvc-unity-skill/references/framework-guide.md`: Update `[NonSaved]` documentation to mention it can decorate fields and properties, and note that `ObservableList<T>.Value` assignment (including `LoadDataFromModel`) is silent when the new list is element-wise equal to the current list.

---

### Task 8 — Queue Re-Entrant Transitions FIFO in `ObservableVariable<T>` / `ObservableTrackedAction<T>`
* **Code Files:**
  * `Packages/com.evialdev.savableobservable/Runtime/ObservableTrackedAction.cs`
  * `Packages/com.evialdev.savableobservable/Runtime/ObservableVariable.cs`
* **Problem:** When handler `H1` sets `Value = 2` during `OnValueChanged.Invoke` for `Value = 1`, `ObservableVariable<T>` immediately overwrites `_value = 2` and `PreviousValue = 1` before `H2` runs in Pass 1 (so `H2` misses `1` and sees `2` in Pass 1, then gets called a second time with `2` in Pass 2).
* **Code Changes:**
  1. In `ObservableVariable<T>.Value` setter and `ForceNotify()`:
     * If `_onValueChanged != null && _onValueChanged.IsInvoking`, enqueue the transition into a FIFO queue on `ObservableVariable<T>` (or via `ObservableTrackedAction<T>.EnqueueTransition(Action applyState)`) instead of overwriting `_value` and `PreviousValue` mid-pass.
     * When the current pass of `OnValueChanged.Invoke(this)` finishes notifying all handlers `[H1, H2, ...]` of the active transition, dequeue the next queued value assignment, evaluate equality against the newly current `_value` (respecting `NotifyOnSameValue`), update `PreviousValue` and `_value`, and run the next pass of `Invoke` (up to `maxIterations = 32`, logging `ObservableTrackedAction.Invoke exceeded maxIterations (32)` if exceeded).
  2. Keep `ObservableTrackedAction<T>.Invoke(T variable)`'s direct re-entrancy guard (`_isInvoking` / `_pending` / `maxIterations = 32`) working for both queued `ObservableVariable<T>` transitions and direct `action.Invoke()` calls.
* **Test Updates:**
  * `Packages/com.evialdev.savableobservable/Tests/Editor/TrackedActionAndForeignSubTests.cs`:
    * Verify existing tests `ReentrantClamping_TwoWayUIAndHandlers_ConvergeToClampedValue_AndDependentMutationPropagates` and `InfiniteLoop_ExceedingMaxIterations_StopsAndLogsError` pass without modification.
    * Add a new test `ReentrantValueChange_MultipleSubscribers_DeliversEveryTransitionInFifoOrderWithoutDuplicateCalls`: register `H1` (which clamps/mutates `v.Value = 2` when `v.Value == 1`) and `H2` (which records `(v.Value, v.PreviousValue)` pairs). Set `v.Value = 1` from `0` and assert `H2` receives `[(1, 0), (2, 1)]` (exact 2 calls, never `[(2, 1), (2, 1)]`).
* **Documentation & Skill Updates:**
  * `Packages/com.evialdev.savableobservable/Documentation~/UnityLifecycle.md` and `Packages/com.evialdev.savableobservable/CHANGELOG.md`: Document deterministic FIFO transition delivery when a handler mutates the same `ObservableVariable<T>` re-entrantly.

---

### Task 9 — Preserve Shadowed Base-Class `private` Fields in `ModelDescriptor`
* **Code File:** `Packages/com.evialdev.savableobservable/Runtime/ModelDescriptor.cs`
* **Code Changes:**
  1. In `ModelDescriptor`:
     * Include **every** declared observable field across `TypeUtil.GetUserHierarchy(modelType)` in `ObservableFields` and `LoadObservableFields` (even if a derived class declared a `private` observable field with the same name), so `EnsureFieldsInitialized()` initializes and binds `ParentDataModel` on all base-class observable fields and `LoadDataFromModel` copies them.
     * Keep `byName` (`ObservableFieldsByName`) using most-derived-wins (`if (!byName.ContainsKey(field.Name)) byName[field.Name] = field;`) for string-based attribute binding.
     * In `LoadPlainFields`, do not skip base-class `private` fields when a derived class has a `private` field of the same name (`BindingFlags.DeclaredOnly` already guarantees each `FieldInfo` is a distinct storage slot).
* **Test Updates:**
  * `Packages/com.evialdev.savableobservable/Tests/Editor/ModelPersistenceTests.cs`: Add a test with a `BaseShadowModel` declaring `[SerializeField] private ObservableVariable<int> _secret` and `[SerializeField] private int _plain` and a `DerivedShadowModel : BaseShadowModel` also declaring `[SerializeField] private ObservableVariable<int> _secret` and `[SerializeField] private int _plain`. Verify `EnsureFieldsInitialized()` initializes both `_secret` fields and `LoadDataFromModel` copies both base and derived fields independently.
* **Documentation & Skill Updates:**
  * `Packages/com.evialdev.savableobservable/CHANGELOG.md`: Document fix for base-class private field preservation when shadowed by derived-class private fields.

---

### Task 10 — Support `enum` and `Nullable<T>` in `StringInputAdapterBase` and `SliderAdapter`
* **Code File:** `Packages/com.evialdev.savableobservable/Runtime/UIAdapters.cs`
* **Code Changes:**
  1. Add `internal static bool TryConvertValue(object rawValue, Type targetType, out object converted)`:
     * Unwrap `Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;`.
     * Handle `null` or empty/whitespace string: if `Nullable.GetUnderlyingType(targetType) != null`, output `null`; if `underlying.IsValueType`, output `Activator.CreateInstance(underlying)`.
     * Handle `underlying.IsEnum`:
       * If `rawValue is string s`: use `Enum.TryParse(underlying, s, ignoreCase: true, out var parsed)` and return `parsed`.
       * Otherwise convert numeric `rawValue` to `Enum.GetUnderlyingType(underlying)` via `Convert.ChangeType(..., CultureInfo.InvariantCulture)` and call `Enum.ToObject(underlying, numericVal)`.
     * Otherwise call `Convert.ChangeType(rawValue, underlying, CultureInfo.InvariantCulture)`.
  2. Use `TryConvertValue` in both `StringInputAdapterBase.AddListener` and `SliderAdapter.AddListener`, and handle `Enum` conversion in `SliderAdapter.SetValue` (`Convert.ToSingle(value, CultureInfo.InvariantCulture)`).
* **Test Updates:**
  * `Packages/com.evialdev.savableobservable/Tests/Editor/UIAdapterTests.cs`: Add unit tests for two-way `Slider` and `TMP_InputField` / `InputField` binding to an `ObservableVariable<SampleEnum>` and `ObservableVariable<int?>`.
* **Documentation & Skill Updates:**
  * `mmvc-unity-skill/references/framework-guide.md` (`UI Adapters` table): Note that `Slider` and input field adapters support `enum` and `Nullable<T>` value types in addition to primitives.

---

### Task 11 — Replace Static `_isSettingValue` with Per-Control Re-Entrancy Tracking in `UIAdapters.cs`
* **Code File:** `Packages/com.evialdev.savableobservable/Runtime/UIAdapters.cs`
* **Code Changes:**
  1. Replace `[ThreadStatic] private static bool _isSettingValue;` on `StringInputAdapterBase<TControl>`, `ToggleAdapter`, and `SliderAdapter` with a per-instance guard on `UIAdapterRegistry`:
     * `[AutoStaticsCleanup] private static readonly HashSet<object> _activeSetValueTargets = new HashSet<object>();` (also cleared in `ResetRegistry()`).
     * Expose `internal static bool EnterSetValue(object control)` / `internal static void ExitSetValue(object control)` / `internal static bool IsSettingValue(object control)`.
     * Wrap `SetValue` in `EnterSetValue(uiComponent)` / `try ... finally { ExitSetValue(uiComponent); }` and check `if (UIAdapterRegistry.IsSettingValue(uiComponent)) return;` in `AddListener`.
* **Test Updates:**
  * `Packages/com.evialdev.savableobservable/Tests/Editor/UIAdapterTests.cs`: Add a regression test verifying that if `Control A`'s `SetValue` triggers a callback that mutates `Control B` of the same control type, `Control B`'s two-way listener is not suppressed.
* **Documentation & Skill Updates:**
  * `Packages/com.evialdev.savableobservable/CHANGELOG.md`: Document per-control re-entrancy guard in `UIAdapters`.

---

### Task 12 — Fix `PropertyResolver` `IEnumerator` Disposal & Field-First Hierarchy Lookup
* **Code File:** `Packages/com.evialdev.savableobservable/Editor/PropertyResolver.cs`
* **Code Changes:**
  1. In `GetValueByPathSegment(object source, string name)`:
     * First walk the entire `type` hierarchy (`while (type != null)`) looking for an exact case-sensitive `FieldInfo` (`BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly`).
     * Only if no field is found in the hierarchy, walk the hierarchy looking for a parameterless `PropertyInfo` (`p.GetIndexParameters().Length == 0`).
  2. In `GetValueByPathSegment(object source, string name, int index)`:
     * Dispose `IEnumerator` in a `try / finally` block: `(enm as IDisposable)?.Dispose();`.
* **Test Updates:**
  * `Packages/com.evialdev.savableobservable/Tests/Editor/ObservablePrimitivesTests.cs`: Add a test where a derived model defines a property with the same case-insensitive name as a base model's `private [SerializeField] ObservableVariable<int>` field, verifying `PropertyResolver.GetTargetObjectOfProperty` returns the `ObservableVariable<int>` field rather than the property.
* **Documentation & Skill Updates:**
  * `Packages/com.evialdev.savableobservable/CHANGELOG.md`: Document `PropertyResolver` field-first hierarchy lookup and `IEnumerator` disposal.

---

### Task 13 — Guard `_items` Against Null Deserialization in `ObservableList<T>`
* **Code File:** `Packages/com.evialdev.savableobservable/Runtime/ObservableList.cs`
* **Code Changes:**
  1. Add `private List<T> Items => _items ??= new List<T>();`.
  2. Route `Count`, `this[int index]`, `Add`, `AddRange`, `Clear`, `Contains`, `CopyTo`, `GetEnumerator`, `IndexOf`, `Insert`, `Remove`, `RemoveAt`, and `ToString()` through `Items`.
* **Test Updates:**
  * `Packages/com.evialdev.savableobservable/Tests/Editor/ObservablePrimitivesTests.cs`: Add a test that sets `_items` to `null` via reflection on an `ObservableList<int>` and verifies `Count`, `ToString()`, `Add()`, `Clear()`, and enumeration succeed without `NullReferenceException`.
* **Documentation & Skill Updates:**
  * `Packages/com.evialdev.savableobservable/CHANGELOG.md`: Document null-deserialization guard on `ObservableList<T>._items`.

---

### Task 14 — Synchronize All Markdown Docs & Sync `mmvc-unity` Skill to User Profile
* **Files:**
  * `Packages/com.evialdev.savableobservable/CHANGELOG.md`
  * `Packages/com.evialdev.savableobservable/README.md`
  * `Packages/com.evialdev.savableobservable/Documentation~/UnityLifecycle.md`
  * `Packages/com.evialdev.savableobservable/Documentation~/RuntimeObjects.md`
  * `Packages/com.evialdev.savableobservable/Documentation~/Testing.md`
  * `mmvc-unity-skill/SKILL.md`
  * `mmvc-unity-skill/references/framework-guide.md`
  * `mmvc-unity-skill/references/examples.md`
  * User profile skill installation: `C:\Users\alexx\.skills\mmvc-unity\`
* **Changes:**
  1. Add a new version entry in `Packages/com.evialdev.savableobservable/CHANGELOG.md` summarizing the Unity 6000.5 baseline and all 12 bug fixes.
  2. Review and update `README.md`, `Documentation~/UnityLifecycle.md`, `Documentation~/RuntimeObjects.md`, `Documentation~/Testing.md`, `mmvc-unity-skill/SKILL.md`, `mmvc-unity-skill/references/framework-guide.md`, and `mmvc-unity-skill/references/examples.md` so all behavioral details (`6000.5` baseline, `[DefaultExecutionOrder(-200)]` on `ServiceLocator`, `[NonSaved]` on properties, `BaseObservableDataModel.OnValidate()`, `ObservableList<T>.Value` equal-assignment silence on load, `enum`/`Nullable<T>` adapter support, FIFO re-entrant transitions) are consistent across every Markdown file.
  3. Copy the updated `mmvc-unity-skill/SKILL.md`, `mmvc-unity-skill/references/framework-guide.md`, and `mmvc-unity-skill/references/examples.md` to `C:\Users\alexx\.skills\mmvc-unity\` and verify byte-for-byte SHA256 hash equality between the repository skill files and the user-profile skill files.

---

## Validation Plan
1. Run all EditMode and PlayMode tests via `pwsh -File Tools/Invoke-UnityTests.ps1 -Suite All` (or Unity CLI test runner) and confirm 100% pass rate and 0 compiler errors/warnings.
2. Verify `ObservableList_Mutations_DoNotAllocateGCMemory` in `ObservablePrimitivesTests.cs` continues to pass with zero GC allocations on `Add`, `Insert`, `this[i]`, and `RemoveAt`.
3. Verify SHA256 hashes match between `mmvc-unity-skill/**` and `C:\Users\alexx\.skills\mmvc-unity\**`.
4. **Client / Consumer Project Verification (Post-Implementation Check):**
   * Check whether any `BaseObservableDataModel` subclass in consumer projects (e.g. `F:\Unity_projects\AttaxUnity` if present on disk) declares `void OnValidate()` without `override` + `base.OnValidate()`, or relies on `ObservableList<T>.Value` firing on equal list assignment during `LoadDataFromModel`, and report any findings clearly.
   * Verify `mmvc-kilo-plugin/mmvc-guard.ts` and user-level skill `C:\Users\alexx\.skills\mmvc-unity\` are completely in sync.
