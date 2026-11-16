# MMVC Framework Hardening & Unity Lifecycle Fixes (`v1.0.20`)

## Context & Decisions
Following a full review of the `SavableObservable` framework (`v1.0.19`) and its usage in `AttaxUnity`:
1. **Dynamic & Additive `ISharedSingleton` / `BaseLogic` Lifecycle:** Dynamically instantiated (`Instantiate`) or additively loaded `BaseLogic` / `ISharedSingleton` instances must automatically register with `ServiceLocator` in `Awake()` and unregister in `OnDestroy()`.
2. **Base-Class Private/Protected `[ObservableHandler]` & `[AutoBind]` Inheritance:** Confirmed in `AttaxUnity` (`BaseConfirmModalPresenter<M, L>` declares 7 `private [ObservableHandler]` methods inherited by `BaseConfirmModalPresenter`). Reflection in `SubscriberMetadata`, `ModelMetadata`, and `ModelLoadDescriptor` must walk the inheritance chain (`BaseType` with `BindingFlags.DeclaredOnly`) up to the framework base types so private/protected members on intermediate base classes are discovered, while stopping before `BaseObservableDataModel` / `MonoBehaviour` so Unity internal fields are never touched.
3. **Two-Way UI Binding (`UI -> Model` and `Model -> UI`):** Two-way binding (`UI -> Model`) is kept and works when the user edits a UI control. However, when the **Model** updates the **UI** (`Model -> UI`), the UI adapters (`ToggleAdapter`, `SliderAdapter`, `TMPInputFieldAdapter`, `InputFieldAdapter`) currently assign `.isOn`, `.value`, and `.text`, which causes Unity UI to fire `onValueChanged` back into the Model a second time. Switching `SetValue` in those adapters to `SetIsOnWithoutNotify`, `SetValueWithoutNotify`, and `SetTextWithoutNotify` preserves full two-way `UI -> Model` binding while eliminating the redundant `Model -> UI -> Model` bounce-back notification.
4. **Execution Order Synchronizer:** Keep `MMVCExecutionOrderSynchronizer` enforcing the 5-stage execution order without allowing per-subclass overrides.

---

## Ordered Implementation Tasks

### 1. Auto-Register & Unregister `BaseLogic` with `ServiceLocator` (`Runtime/BaseLogic.cs`, `Runtime/ServiceLocator.cs`, `Runtime/Services.cs`)
- **In `Runtime/ServiceLocator.cs`:**
  - Add `public void Unregister(ISharedSingleton instance)` (and `Services.Unregister(ISharedSingleton instance)` in `Runtime/Services.cs`) that removes the specific instance from `_liveInstances[(type, key)]` and Reconciles `(type, key)` (so if 2 colliding instances existed and 1 is destroyed, the remaining 1 recovers cleanly, or if the last instance is destroyed, its binding is removed).
  - Remove the unused/mismatched `_boundPresenters` `HashSet<object>` from `ServiceLocator` (and `_boundPresenters.Remove(item)` in `Remove`), as `BaseObservablePresenter.EnsureInitialized()` is already idempotent on the presenter instance.
  - Fix the scene validity check in `ServiceLocator.InitializeSharedSingletons()` (`:606, :616`) from `mb.gameObject.scene.name == null` to `!mb.gameObject.scene.IsValid() || !mb.gameObject.scene.isLoaded` so unsaved/untitled scenes in tests or editor work reliably.
- **In `Runtime/BaseLogic.cs`:**
  - Add `protected virtual void Awake()` on `BaseLogic`: if `ServiceLocator.instance != null`, call `ServiceLocator.instance.TryRegister(this)`. (If `ServiceLocator.Awake()` already registered it during Pass 1, `TryRegister` returns `RegistrationResult.SameInstance` as a fast no-op; if instantiated dynamically after `ServiceLocator.Awake()`, it registers immediately and triggers collision-eviction if needed).
  - Add `protected virtual void OnDestroy()` on `BaseLogic`: if `ServiceLocator.instance != null`, call `ServiceLocator.instance.Unregister(this)`.
  - Ensure `BaseLogic<M, P>.EnsureInitialized()` / `Awake()` chains `base.Awake()`.

### 2. Fix Reflection Hierarchy Traversal for Private Base Members (`Runtime/Observable.cs`, `Runtime/BaseObservableDataModel.cs`)
- **In `Runtime/Observable.cs` (`SubscriberMetadata` & `ModelMetadata`):**
  - Add a helper to enumerate fields and methods by walking `for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(BaseObservableDataModel) && t != typeof(object); t = t.BaseType)` using `BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly`.
  - Deduplicate overridden virtual methods by signature/base definition so overridden `[ObservableHandler]` methods are only registered once, while `private` `[ObservableHandler]` methods and `private` `[AutoBind]` fields declared in base Presenter classes (such as `BaseConfirmModalPresenter<M, L>`) are always discovered on derived classes.
- **In `Runtime/BaseObservableDataModel.cs` (`GetCachedObservableFields` & `ModelLoadDescriptor`):**
  - Walk the type hierarchy from `type` up to (but excluding) `typeof(BaseObservableDataModel)` with `BindingFlags.DeclaredOnly` so private `[SerializeField]` fields on intermediate base models are included, while internal fields on `BaseObservableDataModel`, `MonoBehaviour`, `Component`, and `UnityEngine.Object` are strictly excluded.

### 3. Fix Shallow-Copy List Aliasing in `BaseObservableDataModel.LoadDataFromModel` (`Runtime/BaseObservableDataModel.cs`)
- **In `Runtime/BaseObservableDataModel.cs` (`:150-174`):**
  - When copying an `ObservableList<T>` in `LoadDataFromModel`, do not assign the `sourceModel`'s internal `List<T>` reference directly to `thisValue`. Instead, copy the elements into a new `List<T>` (or `Clear()` + `AddRange()`) so mutating the target model after loading never mutates the source model.
  - When copying `PlainFields` that implement `IList` (such as `List<string>`), clone the list instance if non-null rather than sharing the same list reference across models.

### 4. Fix Two-Way UI Adapter Bounce-Back & Type Conversion (`Runtime/UIAdapters.cs`)
- **In `Runtime/UIAdapters.cs`:**
  - **`ToggleAdapter.SetValue`:** Use `toggle.SetIsOnWithoutNotify(boolValue)` instead of `toggle.isOn = boolValue`.
  - **`SliderAdapter.SetValue`:** Use `slider.SetValueWithoutNotify(floatValue)` instead of `slider.value = floatValue`.
  - **`SliderAdapter.AddListener`:** Fix the `catch` block (`:465`) so it does not pass a raw `float` to `onValueChanged` when `valueType` is not `float` (which throws `ArgumentException` in `PropertyInfo.SetValue`).
  - **`TMPInputFieldAdapter.SetValue` & `InputFieldAdapter.SetValue`:** Use `inputField.SetTextWithoutNotify(stringValue)` instead of `inputField.text = stringValue`.
  - **`TMPInputFieldAdapter.AddListener` & `InputFieldAdapter.AddListener`:** Use `CultureInfo.InvariantCulture` fallback for numeric parsing, and when `string.IsNullOrEmpty(val)` for numeric value types, assign default (`0`) instead of leaving the stale previous digit in the model.
- **In `Runtime/UIAdapters.cs` (`UIAdapterRegistry`):**
  - Keep a separate `private static readonly List<IUIAdapter> _customAdapters = new List<IUIAdapter>()` (marked `[NoAutoStaticsCleanup]`) for adapters registered via `UIAdapterRegistry.RegisterAdapter`, or re-apply them unless explicitly cleared, and add `UIAdapterRegistry.UnregisterAdapter` / `ClearCustomAdapters()` so custom adapters registered in `[InitializeOnLoadMethod]` survive Fast Play Mode (`ResetRegistry`) while still allowing tests to reset custom adapters cleanly.

### 5. Deterministic Main-Thread Capture in `ObservableTrackedAction` (`Runtime/ObservableTrackedAction.cs`)
- **In `Runtime/ObservableTrackedAction.cs`:**
  - Add a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` static method (and static constructor initialization) that sets `_mainThreadId = Thread.CurrentThread.ManagedThreadId` on the actual Unity main thread at startup, preventing a background thread from accidentally latching its own thread ID if it touches an observable first.

### 6. Safe Component Provisioning & Minor Guards (`Runtime/ComponentAutoRequire.cs`, `Runtime/ObservableList.cs`, `Tests/Runtime/com.evialdev.savableobservable.RuntimeTests.asmdef`, `README.md`)
- **In `Runtime/ComponentAutoRequire.cs`:**
  - Guard against prefab assets on disk (`UnityEditor.PrefabUtility.IsPartOfPrefabAsset(owner)`) and guard/defer `AddComponent` when called during `OnValidate` via `EditorApplication.delayCall` so Unity does not log `SendMessage cannot be called during Awake, CheckConsistency, or OnValidate` warnings.
- **In `Runtime/ObservableList.cs` (`:166-193`):**
  - Add null checks for `_items` in `OnBeginGui()` and `OnValidate()`.
- **In `Tests/Runtime/com.evialdev.savableobservable.RuntimeTests.asmdef`:**
  - Remove `"UnityEditor.TestRunner"` from `references` so PlayMode tests can compile for non-Editor player builds.
- **In `README.md` (`:155`):**
  - Fix the `ScoreObserver.OnScoreChanged` example signature from `private void OnScoreChanged(int newScore)` to `private void OnScoreChanged(ObservableVariable<int> score)` to match `ObservableTrackedAction<ObservableVariable<int>>`.

---

## Validation Plan
1. **Unit & EditMode Tests (`Tests/Editor/`):**
   - Add test in `ObservableBindingTests.cs`: Derived presenter inheriting `private [ObservableHandler]` methods and `private [AutoBind]` fields from an abstract generic base presenter (mirroring `BaseConfirmModalPresenter<M, L>`).
   - Add test in `ModelPersistenceTests.cs`: `LoadDataFromModel` with `ObservableList<T>` and `List<T>` verifying that mutating the target model after load does **not** mutate the source model, and verifying private `[SerializeField]` observable fields on a base model class are copied.
   - Add test in `MMVCRestrictionsTests.cs`: Dynamic `BaseLogic` instantiation (`AddComponent` / `Instantiate` after `ServiceLocator.Awake`) auto-registers with `ServiceLocator` and `DestroyImmediate` auto-unregisters via `OnDestroy`.
   - Add test in `UIAdapterTests.cs`: Setting model value bound to a two-way `Slider` / `Toggle` / `TMP_InputField` updates the UI control and invokes model `OnValueChanged` **once** (no bounce-back duplicate notification), while changing the UI control still updates the model (`UI -> Model`).
2. **Self-Test & Test Runner Execution:**
   - Run all EditMode and PlayMode tests and `MmvcSelfTest` to verify 100% pass rate and zero console errors/warnings.
