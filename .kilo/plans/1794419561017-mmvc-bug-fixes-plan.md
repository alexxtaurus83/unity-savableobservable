# Validation & Fix Plan for SavableObservable (MMVC) Bug Review

## 1. Validation of Findings & API/Contract Impact Assessment

Every finding from the 3-phase review has been cross-checked against the runtime source, editor tooling, samples, and test suites (`Tests/Editor/`, `Tests/Runtime/`, `Tests/Shared/`).

### Summary Matrix

| ID | Finding | Verdict | API / Contract Impact | Action |
|---|---|---|---|---|
| **1.1** | `ObservableList<T>` re-entrant mutation overwrites `_items` and delta fields mid-notification and double-notifies later subscribers | **VALID BUG** | **Non-breaking** (aligns `ObservableList<T>` re-entrancy with `ObservableVariable<T>` FIFO transition delivery while keeping zero-allocation on the non-re-entrant fast path) | **Fix** |
| **1.2** | `ObservableVariable<T>.ApplyAndNotify` drainage loop lacks `try/finally` around `OnValueChanged.Invoke` | **VALID (Defensive)** | **None** | **Fix** |
| **1.3** | `Observable.SubscribeAutoBindHandler` uses `observableField.FieldType.GetGenericArguments()` directly instead of walking base types for subclassed `ObservableVariable<T>` / `ObservableList<T>` | **VALID BUG** | **Non-breaking** (fixes broken `[AutoBind]` conversion and `Command` resolution on subclassed observables) | **Fix** |
| **1.4** | `SubscriberMetadata` virtual `[ObservableHandler]` deduplication skips base attribute if override has a different attribute, or double-invokes on `new` method hiding | **VALID BUG** | **Non-breaking** (fixes edge-case inheritance binding) | **Fix** |
| **1.5** | `BaseObservableDataModel.TryGetMemberValue` calls `GetProperty`/`GetField` with `BindingFlags.IgnoreCase` outside `try/catch`, throwing uncaught `AmbiguousMatchException` on shadowed or case-colliding members | **VALID BUG** | **Non-breaking** (prevents crash in `LoadDataFromModel`) | **Fix** |
| **1.6** | `BaseObservableDataModel.CloneIfListOrArray` shallow-copies outer lists and falls back on read-only `IList` | **PARTIALLY VALID** | Guarding read-only/custom `IList` fallback is **Non-breaking**; deep-cloning nested reference elements would change shallow-copy semantics | **Fix `IList` fallback only** |
| **1.7** | `SubscriberMetadata` logs `[ClickCommand]` structural errors inside the static metadata constructor (only once per AppDomain, with `null` Unity context) | **VALID BUG** | **Non-breaking** (stores structural errors on `SubscriberMetadata` and logs them per-bind with `presenter` context) | **Fix** |
| **1.8** | `UIAdapterRegistry.TryConvertValue` converts `""` to `default(T)` (`0`), causing Command-Aware `[AutoBind]` on numeric `TMP_InputField`/`InputField` to immediately invoke `Command(0)` and snap text to `"0"` when cleared | **VALID BUG** (in Command mode; non-command `"" -> 0` is locked by `UIAdapterTests:410` & `TrackedActionAndForeignSubTests:516`) | **Non-breaking** (keep `TryConvertValue` `"" -> default(T)` for non-command `[AutoBind]`, and treat empty/whitespace string on non-string/non-nullable value types as malformed/incomplete in Command-Aware `[AutoBind]`) | **Fix in Command-Aware `[AutoBind]`** |
| **1.9** | `Enum.TryParse` in `UIAdapterRegistry.TryConvertValue` accepts undefined integer strings (e.g. `"99999"`) for non-`[Flags]` enums | **VALID BUG** | **Minor behavior refinement** (rejects undefined numeric enum strings via `Enum.IsDefined` unless `[Flags]` is present) | **Fix** |
| **1.10** | `ResolveClickCommandMethod` / `ResolveCommandMethod` checks `ReturnType.Name == "ValueTask"`, missing generic `ValueTask<T>` | **VALID (Diagnostic)** | **None** | **Fix** |
| **2.1** | Destroyed `UnityEngine.Object` ("fake null") stored in `ObservableVariable<T>` compares equal to `null` via `EqualityComparer<T>.Default.Equals(_value, value)`, suppressing `model.sprite.Value = null` after `Destroy(sprite)` | **VALID BUG** | **Non-breaking bug fix** (assigning real `null` over a destroyed fake-null `UnityEngine.Object`, or vice versa, now updates `_value` and notifies UI so `Image.sprite` clears properly) | **Fix** |
| **2.2** | `Observable.CleanupSubscriptions(model)` calls `CleanupPresenterLifetime()` on *foreign* presenters subscribed to `model`, and forgets `MarkUnbound()` on the owning presenter | **VALID BUG** | **Non-breaking** (prevents destroying a foreign presenter's `Listeners`/`[ClickCommand]`s when an observed model dies, and marks the same-GameObject presenter unbound so it can rebind) | **Fix** |
| **2.3** | `BaseObservablePresenter.RunInitializePresenter()` calls `WireClickCommands()` before `InitializePresenter()` and does not roll back `_clickCommandListeners` / `_clickCommandsWired` if `InitializePresenter()` throws | **VALID BUG** | **Non-breaking** (wires `[ClickCommand]` after `InitializePresenter()` or rolls back on exception; allows buttons resolved in `InitializePresenter()` to wire) | **Fix** |
| **2.4** | `ServiceLocator.Awake()` checks `if (instance == null)` only; if the previous `ServiceLocator` was `Destroy()`ed in the same frame, its deferred `OnDestroy()` nulls out `ServiceLocator.instance` | **VALID BUG** | **Non-breaking** (`Awake()` takes over `instance = this` when replacing a dying/previous locator so `oldLocator.OnDestroy()`'s `ReferenceEquals(instance, this)` check does not clear the new locator) | **Fix** |
| **2.5** | `ComponentAutoRequire` uses `AddComponent` instead of `Undo.AddComponent` in Editor | **REJECTED / DO NOT CHANGE** | Using `Undo.AddComponent` during EditMode tests pollutes the Editor's global Undo stack. Current behavior is deliberate. | **Skip** |
| **2.6** | `SliderAdapter.SetValue` guards with `!Mathf.Approximately(slider.value, floatValue)` and does not guard `float.IsNaN(floatValue)` | **PARTIALLY VALID** | Guarding `float.IsNaN` is **Non-breaking**; removing `Mathf.Approximately` could cause float precision churn | **Fix `NaN` guard only** |
| **2.7** | `Editor/PropertyResolver.ResolvePathOnObject` fails if a single segment has multiple array indexers (e.g. `matrix[0][1]`) | **VALID BUG** | **None** (Editor-only internal helper) | **Fix** |
| **2.8** | `MMVCExecutionOrderSynchronizer` checks `type.GetCustomAttribute<DefaultExecutionOrder>()` vs `MonoImporter.GetExecutionOrder(script)` | **VALID BUG** | **None** (Editor-only: if a user script in `Assets/` explicitly declares `[DefaultExecutionOrder(X)]`, `GetTargetExecutionOrder` should respect it or skip resetting to `0` to avoid a domain-reload asset-edit loop) | **Fix** |
| **3.1** | `SamplePlayerPresenter` declares `private void OnDestroy()` hiding `BaseObservablePresenter.OnDestroy()` without `base.OnDestroy()` and calling `GetModel()` in `OnDestroy()`; `MmvcAssemblyAuditTestsBase` only checks `BaseLogic.Awake` | **VALID BUG** | **Contract Test Addition** (`Tests/Shared/MmvcAssemblyAuditTestsBase.cs` is shipped to consumers; adding lifecycle method-hiding checks is noted in `CHANGELOG.md`) | **Fix** |
| **3.2** | `BaseLogic<M, P>.GetModel()` calls `_presenter.EnsureInitialized()` before caller finishes configuring fields on an inactive prefab | **BY DESIGN (Confirmed)** | Confirmed with user: `logic.GetModel()` initializing and binding the presenter is an intentional, tested contract (`MMVCRestrictionsTests:659`). | **Keep as-is** |
| **3.3** | `WireClickCommands()` runs before `InitializePresenter()`, so buttons assigned inside `InitializePresenter()` are skipped | **VALID BUG** (same root cause as 2.3) | **Non-breaking** (existing test `ClickCommand_Survives_ListenersClearInInitializePresenter` still passes because `[ClickCommand]` uses `_clickCommandListeners`, not `Listeners`) | **Fix** |
| **3.4** | `[ClickCommand]` is not re-wired on `Observable.SetListeners` rebind or after `CleanupPresenterLifetime()` | **VALID BUG** | **Non-breaking** (moving `WireClickCommands` into the binding/rebind lifecycle alongside `SetListeners` while clearing `_clickCommandListeners` first ensures idempotency and recovery after `CleanupPresenterLifetime`) | **Fix** |
| **3.5** | `CommandSettingsPresenter` sample has `legacyDifficultySlider` bound without `Command` to `difficultyLevel` | **BY DESIGN** | `CommandSettingsContractTests.Sample_LegacyAutoBind_MutatesModelDirectly` explicitly tests this field to demonstrate side-by-side legacy and command `[AutoBind]`. | **Keep as-is** |
| **3.6** | `SamplePlayerLoader.LoadSampleState` manually calls `inventory.Clear()` + `AddRange()` (firing 2 notifications even on equal list) and omits `lastRoll` | **VALID BUG** | **Sample-only** (`SamplePlayerLoader` can use `LoadDataFromModel(dto)` or `model.inventory.Value = new List<string>(dto.inventory)`) | **Fix** |
| **3.7** | `MmvcUnitContractTestsBase.Contract_TwoWayAutoBindFields_RoundtripAndNoBounceBack` hardcodes `slider.value = 42f` and asserts `floatVar.Value == 42f` (breaks on `[0, 1]` clamped Command-Aware `[AutoBind]`) and calls `SetListeners` inside the field loop | **VALID BUG** | **Non-breaking improvement to `Tests/Shared/`** (for Command-Aware `[AutoBind]`, assert reconciliation `slider.value == floatVar.Value` or use `0.5f` within `[0, 1]`, and initialize mock UI controls before calling `SetListeners` once) | **Fix** |
| **3.8** | `MmvcUnitContractTestsBase.Contract_EveryObservableHandlerMethod_HasValidSignatureAndMatchingModelField` does not walk base types for subclassed `ObservableVariable<T>` / `ObservableList<T>` | **VALID BUG** | **Non-breaking** (fixes false-negative/skip in shared contract test) | **Fix** |

---

## 2. Ordered Implementation Plan

### Step 1: Phase 1 Fixes (Native .NET / C# Bugs)

1. **`Runtime/ObservableList.cs` (Finding 1.1 — Re-entrant list mutation safety & zero-alloc fast path)**
   - Keep the existing zero-allocation delta representation (`_deltaKind`, `_deltaIndex`, `_deltaCount`, `_deltaItem`, `_deltaSnapshot`) for the non-re-entrant fast path (verified by `ObservableList_Mutations_DoNotAllocateGCMemory`).
   - Add a re-entrancy guard (`_isNotifying` and a lazy `Queue<PendingListTransition> _pendingTransitions`, allocated only if a subscriber mutates the list while `_isNotifying` is true).
   - When a mutation (`Add`, `AddRange`, `Insert`, `Remove`, `RemoveAt`, `Clear`, `this[int]`, `Value`, `ForceNotify`) occurs while `_isNotifying == true`, capture the transition (snapshot of new items + previous items) into `_pendingTransitions` instead of mutating `_items` and overwriting delta fields mid-pass.
   - Drain `_pendingTransitions` in FIFO order after the current pass finishes, inside a `try/finally` block, and update `ObservableTrackedAction.Invoke` / `ObservableList` so each queued transition invokes subscribers once without a duplicate trailing pass.

2. **`Runtime/ObservableVariable.cs` (Finding 1.2 — `try/finally` in `ApplyAndNotify`)**
   - Wrap the notification and `_pendingTransitions` drainage loop in `ApplyAndNotify` (`ObservableVariable.cs:117-147`) in a `try/finally` block that resets `_isNotifying = false` and clears `_pendingTransitions` if an unhandled exception aborts the loop.

3. **`Runtime/Observable.cs` & `Runtime/TypeUtil.cs` (Finding 1.3 — Subclassed `ObservableVariable<T>` / `ObservableList<T>` value-type resolution)**
   - Add a helper in `Observable` (or `TypeUtil`) that walks `Type.BaseType` to find the closed `ObservableVariable<T>` or `ObservableList<T>` base type and extract `T`.
   - Use this helper in `Observable.SubscribeAutoBindHandler` (`Observable.cs:344`) instead of `observableField.FieldType.GetGenericArguments()`.

4. **`Runtime/Observable.cs` (Finding 1.4 — Virtual / overridden / `new` `[ObservableHandler]` discovery)**
   - In `SubscriberMetadata` (`Observable.cs:66-98`), key virtual method deduplication by `(method.GetBaseDefinition(), attr.VariableName)` when an override re-declares `[ObservableHandler]`, and if an override has no `[ObservableHandler]` of its own, allow the base definition's `[ObservableHandler]` to bind once (invoking the virtual dispatch on the derived instance).

5. **`Runtime/BaseObservableDataModel.cs` (Finding 1.5 & 1.6 — `TryGetMemberValue` & `CloneIfListOrArray`)**
   - In `TryGetMemberValue` (`BaseObservableDataModel.cs:105-127`), walk the type hierarchy with `BindingFlags.DeclaredOnly` (preferring exact-case match first, then case-insensitive match) and wrap the lookup in `try/catch` so shadowed properties or case collisions never throw an uncaught `AmbiguousMatchException`.
   - In `CloneIfListOrArray` (`BaseObservableDataModel.cs:69-99`), when `value is System.Collections.IList list` and `targetType` is an interface (like `IList<T>`, `ICollection<T>`, `IEnumerable<T>`, `IReadOnlyList<T>`) or `value.GetType()` is read-only/lacks a parameterless constructor, construct a `List<T>` of the element type rather than calling `Activator.CreateInstance(value.GetType())`.

6. **`Runtime/Observable.cs` (Finding 1.7 & 1.10 — `SubscriberMetadata` diagnostics & `ValueTask<T>` check)**
   - Instead of calling `Debug.LogError(..., null)` inside `SubscriberMetadata` constructor (`Observable.cs:106, 109, 111`), record invalid `[ClickCommand]` declarations in a `readonly List<string> InvalidClickCommandErrors` on `SubscriberMetadata` and log them with `presenter` as context inside `Observable.WireClickCommands`.
   - In `ResolveClickCommandMethod` (`Observable.cs:632`) and `ResolveCommandMethod` (`Observable.cs:764`), check `(m.ReturnType.IsGenericType && m.ReturnType.GetGenericTypeDefinition().Name == "ValueTask`1")` alongside `m.ReturnType.Name == "ValueTask"`.

7. **`Runtime/Observable.cs` & `Runtime/UIAdapters.cs` (Finding 1.8 & 1.9 — Command-Aware empty input & undefined enum integers)**
   - **Finding 1.8**: Keep `UIAdapterRegistry.TryConvertValue` returning `default(T)` for empty/whitespace strings so existing non-command `[AutoBind]` behavior and tests remain 100% unchanged. In `Observable.SubscribeAutoBindHandler`'s command branch (`Observable.cs:432`), before calling `UIAdapterRegistry.TryConvertValue`, check if `rawVal is string rawStr && string.IsNullOrWhiteSpace(rawStr)` when `valueType != typeof(string)` and `Nullable.GetUnderlyingType(valueType) == null`; if so, return early without invoking the command or reconciling the input field to `"0"`.
   - **Finding 1.9**: In `UIAdapterRegistry.TryConvertValue` (`UIAdapters.cs:314`), after `Enum.TryParse(underlying, s, ignoreCase: true, out var parsed)`, verify `Enum.IsDefined(underlying, parsed) || underlying.IsDefined(typeof(FlagsAttribute), false)` before accepting the parsed value.

---

### Step 2: Phase 2 Fixes (Unity Lifecycle, Fake-Null, Domain Reload, Editor Tooling)

1. **`Runtime/ObservableVariable.cs` (Finding 2.1 — Destroyed `UnityEngine.Object` fake-null comparison)**
   - Add a static helper `AreEqual(T a, T b)`:
     - When `typeof(UnityEngine.Object).IsAssignableFrom(typeof(T))` (or `a is UnityEngine.Object` / `b is UnityEngine.Object`), check `ReferenceEquals(a, b)` first; if one is a destroyed `UnityEngine.Object` (`uo == null` in Unity's overloaded operator while `!ReferenceEquals(uo, null)`) and the other is real `null` (or a different destroyed `UnityEngine.Object`), treat them as **not equal** when transitioning from a destroyed object to `null` so that assigning `null` clears the dangling reference and fires `OnValueChanged`!
     - Wait: what if `_value` is already real `null` and `value` is real `null`? `ReferenceEquals(null, null)` is `true` -> equal (silent).
     - What if `_value` holds a destroyed `Sprite` (`!ReferenceEquals(_value, null)` && `(UnityEngine.Object)(object)_value == null`) and the user assigns `value = null` (`ReferenceEquals(value, null)`)? They are NOT reference-equal, and `_value` was a destroyed object while `value` is `null` -> treat as **changed** so `_value` becomes real `null` and subscribers (e.g. `ImageAdapter`) are notified to clear `image.sprite = null`!
     - Use this helper in `ObservableVariable<T>.Value` setter (`ObservableVariable.cs:91`) and `ObservableList<T>.ItemsEqual` (`ObservableList.cs:402`).

2. **`Runtime/Observable.cs` & `Runtime/BaseObservablePresenter.cs` (Finding 2.2 — `CleanupSubscriptions` foreign presenter bug & missing `MarkUnbound()`)**
   - In `Observable.CleanupSubscriptions(BaseObservableDataModel dataModel)` (`Observable.cs:863-868`):
     - Always call `bindingState.MarkUnbound()` when removing a presenter's subscriptions.
     - Only call `bindingState.CleanupPresenterLifetime()` and `PresenterLifetimeTracker.Untrack(bindingState)` if the presenter belongs to the same `GameObject` as `dataModel` (e.g. `bindingState is Component comp && (comp == null || comp.gameObject == dataModel.gameObject)`). Foreign presenters on other GameObjects that only had manual subscriptions to `dataModel` must **not** have their own `Listeners` or `[ClickCommand]`s disposed!

3. **`Runtime/BaseObservablePresenter.cs` & `Runtime/Observable.cs` (Finding 2.3, 3.3, 3.4 — `[ClickCommand]` wiring order, exception rollback, and rebind recovery)**
   - In `BaseObservablePresenter.RunInitializePresenter()` (`BaseObservablePresenter.cs:86-94`):
     - Move `WireClickCommands()` to run **after** `InitializePresenter()` (so buttons looked up or created in `InitializePresenter()` are wired, while `Listeners.Clear()` inside `InitializePresenter()` still never affects `_clickCommandListeners`).
     - In the `catch` block of `RunInitializePresenter()`, also clear `_clickCommandListeners` and reset `_clickCommandsWired = false`.
   - In `Observable.SetListenersCore` (`Observable.cs:195`), after `bindingState.EnsureInitializedForBinding()`, ensure `[ClickCommand]`s are wired if not currently wired (or re-wired cleanly by clearing `_clickCommandListeners` first on rebind), so that:
     - Replacing a `Button` field and calling `Observable.SetListeners(presenter, model)` wires the new button without duplicating listeners.
     - Calling `EnsureInitialized()` or `Observable.SetListeners()` after `Observable.CleanupSubscriptions(model)` re-wires `[ClickCommand]`s.

4. **`Runtime/ServiceLocator.cs` (Finding 2.4 — Same-frame `Destroy(oldLocator)` + new `ServiceLocator.Awake()`)**
   - In `ServiceLocator.Awake()` (`ServiceLocator.cs:56-61`), set `instance = this;` unconditionally (or when `instance == null || instance != this`). Because `oldLocator.OnDestroy()` checks `if (ReferenceEquals(instance, this)) instance = null;`, updating `instance = this` in the new locator's `Awake()` prevents the dying locator's end-of-frame `OnDestroy()` from wiping out the new `ServiceLocator.instance`.

5. **`Runtime/UIAdapters.cs` (Finding 2.6 — `SliderAdapter` `NaN` guard)**
   - In `SliderAdapter.SetValue` (`UIAdapters.cs:581`), guard against `float.IsNaN(floatValue) || float.IsInfinity(floatValue)` before calling `slider.SetValueWithoutNotify`.

6. **`Editor/PropertyResolver.cs` (Finding 2.7 — Multi-bracket segment parsing)**
   - In `PropertyResolver.ResolvePathOnObject` (`PropertyResolver.cs:52-59`), loop over all `[index]` occurrences within an element segment instead of assuming at most one `[` per dot-separated segment.

7. **`Editor/MMVCExecutionOrderSynchronizer.cs` (Finding 2.8 — Respect explicit `[DefaultExecutionOrder]` on user scripts)**
   - In `MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder(Type type)` (`MMVCExecutionOrderSynchronizer.cs:59-72`), before returning `0` for `BaseObservableDataModel`, `LoaderWithModel<>`, or `BaseLogic` subclasses, check `type.GetCustomAttribute<DefaultExecutionOrder>(false)`: if the user's class explicitly declares `[DefaultExecutionOrder(order)]`, return `order` (or `int.MinValue` to leave it alone) so Unity does not loop on `SetExecutionOrder(..., 0)` every domain reload.

---

### Step 3: Phase 3 Fixes (MMVC Usage, Samples, and Shared Contract Tests)

1. **`Samples/Scripts/SamplePlayerPresenter.cs` & `Tests/Shared/MmvcAssemblyAuditTestsBase.cs` (Finding 3.1 — Lifecycle method hiding)**
   - In `SamplePlayerPresenter.cs:44-49`, remove the redundant `private void OnDestroy()` (or change it to `protected override void OnDestroy() { base.OnDestroy(); }` without calling `GetModel()`), since `BaseObservablePresenter<M>.OnDestroy()` already cleans up subscriptions and `ListenerSet`s.
   - In `Tests/Shared/MmvcAssemblyAuditTestsBase.cs`, expand `Audit_AllLogics_AwakeDoesNotHideBaseAwake` (or add a companion audit test) to verify that subclasses of `BaseLogic`, `BaseObservablePresenter<M>`, and `BaseObservableDataModel` do not declare non-override `Awake` or `OnDestroy` methods that hide the base lifecycle methods.

2. **`Samples/Scripts/SamplePlayerLoader.cs` & `Samples/Scripts/SamplePlayerModel.cs` (Finding 3.6 — Sample loader best practices)**
   - Mark `SamplePlayerModel.lastRoll` with `[NonSaved]` (since it is a transient roll event) or include it in `SampleSaveDto`.
   - In `SamplePlayerLoader.LoadSampleState(SampleSaveDto dto)`, use `LoadDataFromModel(dto)` (from `LoaderWithModel<SamplePlayerModel>`) so `inventory` is loaded via `ObservableList<string>.Value` (single notification on change, silent when equal) instead of `Clear()` + `AddRange()`.

3. **`Tests/Shared/MmvcUnitContractTestsBase.cs` (Finding 3.7 & 3.8 — Shared contract test fixes)**
   - **Finding 3.7**: In `Contract_TwoWayAutoBindFields_RoundtripAndNoBounceBack` (`MmvcUnitContractTestsBase.cs:336-403`):
     - Create temporary mock UI controls for all unassigned two-way `[AutoBind]` fields *before* calling `Observable.SetListeners(Presenter, Model)` once.
     - For `Slider` bound to `ObservableVariable<float>`, use a test value within `[0, 1]` (e.g. `0.5f`, or a value distinct from current within `[0, 1]`) so `Mathf.Clamp01` commands (like `SetMusicVolume`) converge to the same value without false failure; if the field has `Command` set, assert that `slider.value` and `floatVar.Value` converge (`Assert.AreEqual(floatVar.Value, slider.value, 0.01f)`).
   - **Finding 3.8**: In `Contract_EveryObservableHandlerMethod_HasValidSignatureAndMatchingModelField` (`MmvcUnitContractTestsBase.cs:229-234`), walk `fieldType.BaseType` (using `FindObservableVariableBase` and an equivalent `FindObservableListBase`) so subclassed `ObservableVariable<T>` and `ObservableList<T>` fields have their handler parameter types validated.

---

### Step 4: Verification & Documentation Updates

1. **Unit & PlayMode Tests**:
   - Add regression tests in:
     - `Tests/Editor/ObservablePrimitivesTests.cs` (for `ObservableList<T>` re-entrant mutations and `ObservableVariable<UnityEngine.Object>` fake-null transition to `null`).
     - `Tests/Editor/CommandAutoBindTests.cs` and `Tests/Editor/UIAdapterTests.cs` (for Command-Aware `[AutoBind]` empty string suppression on numeric input fields, and undefined enum integer rejection).
     - `Tests/Editor/ClickCommandTests.cs` (for `[ClickCommand]` button assigned inside `InitializePresenter()`, rollback on `InitializePresenter()` throw, rebind after button replacement, and rebind after `CleanupSubscriptions`).
     - `Tests/Editor/TrackedActionAndForeignSubTests.cs` (for foreign presenter `Listeners` surviving when an observed foreign model is destroyed).
     - `Tests/Editor/MMVCRestrictionsTests.cs` (for `ServiceLocator` same-frame replacement before `OnDestroy`, and `MMVCExecutionOrderSynchronizer` respecting explicit `[DefaultExecutionOrder]`).
     - `Tests/Editor/ModelPersistenceTests.cs` (for `TryGetMemberValue` with shadowed/case-colliding properties and `CloneIfListOrArray` with `IReadOnlyList<T>`).
2. **Run Test Suites**:
   - Run `pwsh -File Tools/Invoke-UnityTests.ps1 -Suite All` and verify 0 failures across EditMode, PlayMode, and self-tests.
3. **Documentation, Samples & Skill Updates (What Needs Updating vs. Transparent Fixes)**:
   - **Transparent fixes (no user-facing doc or skill rule changes needed)**: Findings 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 1.10, 2.1, 2.2, 2.4, 2.6, 2.7, 2.8, 3.7, 3.8 all make the implementation match the existing documented contract.
   - **Samples (`Samples/Scripts/`)**:
     - `SamplePlayerPresenter.cs`: Remove the hiding `private void OnDestroy()` (`BaseObservablePresenter<M>.OnDestroy()` already handles cleanup).
     - `SamplePlayerModel.cs` & `SamplePlayerLoader.cs`: Mark `lastRoll` with `[NonSaved]` and use `LoadDataFromModel(dto)` in `SamplePlayerLoader.LoadSampleState`.
   - **Docs & Skill (`Documentation~/UnityLifecycle.md`, `mmvc-unity-skill/SKILL.md`, `mmvc-unity-skill/references/framework-guide.md`)**:
     - Three places currently state that `[ClickCommand]` is wired *"before `InitializePresenter()` runs"* (`Documentation~/UnityLifecycle.md:256`, `mmvc-unity-skill/SKILL.md:38`, `mmvc-unity-skill/references/framework-guide.md:190`). Update that single phrase in those 3 files to say *"after `InitializePresenter()` runs (and on `Observable.SetListeners` rebind) through an internal `ListenerSet`; survives consumer `Listeners.Clear()`"*.
   - **Guard Plugin (`mmvc-kilo-plugin/`)**:
     - **No changes needed** (no new `MMVC00x` syntax rules).
   - **Changelog (`Packages/com.evialdev.savableobservable/CHANGELOG.md`)**:
     - Add an entry under `## [Unreleased]` summarizing the bug fixes and the `Tests/Shared/` contract test updates (`MmvcAssemblyAuditTestsBase` lifecycle-hiding check and `MmvcUnitContractTestsBase` slider/handler fixes).
