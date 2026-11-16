# Command-Aware AutoBind And Generic Listeners

## 1. Goal

Reduce UI binding boilerplate at the framework level while preserving command-driven separation:

```text
Control input
    -> Framework dispatches Logic command
    -> Logic validates, updates model, applies effects and saves
    -> Model notifications render the view
    -> Framework reconciles the originating control with accepted state
```

Add three complementary capabilities:

1. Command-aware `AutoBind` for controls representing observable values.
2. `[ClickCommand]` for `Button` fields that trigger a zero-argument Logic action (no model value).
3. Generic, scoped `ListenerSet` (framework replacement for AttaxUnity's `Assets/Scripts/Base/UiListeners.cs`) for remaining explicit UnityEvent subscriptions (argument-taking buttons, `onEndEdit`, presenter-local handlers).

Existing `AutoBind` usage must continue working without migration.

Rejected design: a "command-driven `[ObservableHandler]`" that calls a Logic method on model change. It reverses MMVC data flow (model -> Logic), re-runs domain effects and saves on load/`ForceNotify`/`SyncOnBind`, and reintroduces the self-subscription cascades AttaxUnity removed in its command-driven PlayerSettings work. `[ObservableHandler]` stays render-only.

## 2. Scope And Constraints

- **Execution Order / Prerequisite:** Execute **AFTER** `.kilo/plans/1794258080625-mmvc-unity-and-dotnet-bugfixes.md` (Phase 1: MMVC Unity & .NET Bug-Fix Plan). This plan builds directly on Phase 1's `Unity 6000.5` baseline, `Observable.IsTargetAlive` fake-null guards, `UIAdapterRegistry` per-control `_activeSetValueTargets` reentrancy set, `UIAdapters.TryConvertValue` (`enum` and `Nullable<T>` support), and FIFO `ObservableVariable<T>` transition queuing.
- Implementation repository: `F:\github\repo\unity-savableobservable`.
- Keep package version **`2.0.0`**.
- Keep minimum Unity version **`6000.5`** (set by Phase 1 in `package.json`) and existing package dependencies. Do not reintroduce `< 6000.5` `#if` shims or `LifecycleManagementCompatibility.cs`.
- All new static fields in `Runtime/` or `Editor/` must follow Unity 6.5 `Unity.Scripting.LifecycleManagement` rules (`[NoAutoStaticsCleanup]` on immutable metadata caches/locks; `[AutoStaticsCleanup]` on `partial` classes plus `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` reset hooks for mutable runtime state).
- Preserve current public APIs, existing serialized data, interfaces, adapter behavior and legacy binding semantics.
- Update package documentation, compiling examples, tests, changelog and repository/installed MMVC skills.
- Do not modify AttaxUnity code, documentation, plans, package references, lock files or caches.
- The framework `ListenerSet` supersedes AttaxUnity's local `Assets/Scripts/Base/UiListeners.cs`; migrating AttaxUnity to it (and to `[ClickCommand]`) is Phase 3 (`.kilo/plans/1794370000000-attax-command-autobind-and-listeners-adoption.md`), not this task.
- Do not commit, tag, publish or push without a separate request.
- Preserve unrelated working-tree changes (including all Phase 1 changes).

Backward compatibility is relative to the **current 2.0.0 baseline (with Phase 1 bug fixes applied)**, not restoration of APIs removed earlier in that version.

## 3. Required Context

Before implementation:

1. Recheck repository instructions. No `AGENTS.md` was found in the framework repository during planning.
2. Load these skills:
   - `C:\Users\alexx\.skills\mmvc-unity\SKILL.md`
   - `C:\Users\alexx\.skills\codegraphcontext\SKILL.md`
   - `C:\Users\alexx\.skills\unity\unity-cli\SKILL.md`
   - `C:\Users\alexx\.skills\unity\ui-ugui\SKILL.md`
   - `C:\Users\alexx\.skills\zengram-mcp\SKILL.md`
3. Read the package README and `Documentation~/UnityLifecycle.md`, `RuntimeObjects.md`, and `Testing.md`.
4. Read the MMVC skill’s framework guide and examples.
5. Search Zengram for relevant framework decisions.
6. Refresh CodeGraphContext for the framework package before impact analysis.

The `unity_savableobservable` graph returned stale root-level source paths during planning. Current disk source under `Packages/com.evialdev.savableobservable` is authoritative.

In the sections below, **`P/`** means:

`Packages/com.evialdev.savableobservable/`

## 4. Approved Design

### 4.1 Command-Aware AutoBind

Extend `AutoBindAttribute` with an optional property:

```csharp
public string Command { get; set; }
```

Retain both existing constructors and `VariableName`.

Example:

```csharp
[SerializeField]
[AutoBind(
    nameof(SettingsModel.musicVolume),
    Command = nameof(SettingsLogic.SetMusicVolume))]
private Slider musicVolumeSlider;
```

The attribute declares two directions:

```text
Model -> Control:
    Existing adapter-based rendering, without input notification.

Control -> Logic:
    Call the declared command instead of assigning the model directly.
```

When `Command` is omitted, null, empty or whitespace, use the existing legacy binding path unchanged.

### 4.2 Synchronous Commands Only

Supported command signature:

```csharp
public void SetMusicVolume(float value)
```

The parameter type must exactly match the observable value type after existing adapter conversion.

Supported:

- Public instance methods.
- One required value parameter.
- `void` return.
- Inherited and overridden methods with deterministic resolution.

Rejected:

- `async void`.
- `Task` and `ValueTask`.
- Coroutine returns.
- Static or non-public methods.
- Generic methods.
- `ref` or `out` parameters.
- Optional or extra parameters.
- Ambiguous hidden method matches.

**Synchronous-only support must be explicit in API documentation, README, examples, skills and diagnostics.**

Async commands, pending state, cancellation, debouncing and completion policies are out of scope.

### 4.3 Paired Logic Resolution

Add an interface without modifying existing public interfaces:

```csharp
public interface IAutoBindCommandTarget
{
    BaseLogic GetAutoBindCommandTarget();
}
```

`ObservablePresenterWithLogic<M, LO>` implements it explicitly through its existing `GetLogic()`.

The binder validates that:

- The target is live (`IsTargetAlive(logic)` — checking both CLR null and `UnityEngine.Object` fake-null).
- The target is on the same GameObject as the Presenter and model (`logic.gameObject == monoBehaviour.gameObject`).
- The target Logic owns the model being bound, verified via `ReferenceEquals(logic.GetUntypedModel(), dataModel)` (do **not** call `logic.GetModel()` during binding, as `GetModel()` calls `presenter.EnsureInitialized()`).

Do not resolve commands through global services or search arbitrary components.

Existing plain Presenters and `MonoBehaviour` subscribers remain valid legacy consumers. An explicit `Command` without a valid paired target produces an error and never falls back to direct model mutation.

### 4.4 Command Processing

For each successfully converted control input:

1. Check that the binding remains active and its objects (`presenter`, `logic`, `dataModel`, and `currentUiComponent`) are live via `IsTargetAlive`.
2. Enter a per-binding reentrancy guard (and ensure `UIAdapterRegistry.IsSettingValue(currentUiComponent)` from Phase 1 Task 11 is respected so programmatic repaints never trigger commands).
3. Invoke the validated command on the paired Logic instance once with the converted value.
4. In `finally`, read and repaint the current accepted model value onto the originating control **without reflection boxing on `PropertyInfo.GetValue`**: invoke `observableVar.InvokeHandler(wrappedHandler)` (which calls `modelToUiHandler` -> `adapter.SetValue` using the control's "without notify" setter and Phase 1's per-control `EnterSetValue`/`ExitSetValue` guard) if the control and model remain live.
5. Release the per-binding guard.

The framework must not assign `ObservableVariable.Value` before or after calling the command.

Logic remains responsible for:

- Validation and clamping.
- Coupled settings.
- Model mutation.
- Runtime effects.
- Persistence.
- Deciding whether unchanged input requires work.

### 4.5 Automatic Reconciliation

Reconciliation must occur even when:

- Logic rejects input.
- Logic normalizes input to the existing value.
- The observable emits no notification.
- Logic throws after partially changing state.

Example:

```text
Stored value: "http://server"
Entered value: "  http://server  "
Accepted value: "http://server"

No model change occurs.
Framework still removes the extra whitespace from the control.
```

Reconciliation must not:

- Force model notifications.
- Rebind the entire Presenter.
- Invoke commands again.
- Save automatically.
- Retry a failed command.

Reconciliation is not a transaction. Effects performed before an exception are not rolled back.

### 4.6 Input Timing And Conversion

Preserve existing adapter input timing and conversion behavior, and add built-in two-way adapters for `TMPro.TMP_Dropdown` and `UnityEngine.UI.Dropdown` (`int` index / `enum` value via Phase 1's `TryConvertValue` and `SetValueWithoutNotify`):

- Commands run on existing `onValueChanged` events.
- Do not introduce an end-edit mode.
- Preserve existing slider integer conversion and rounding behavior.
- Built-in `TMP_DropdownAdapter` and `DropdownAdapter` bind `int` (or `enum`) values via `SetValueWithoutNotify` and `onValueChanged` (protected by `UIAdapterRegistry.EnterSetValue` / `ExitSetValue`), enabling `[AutoBind(nameof(Model.selectedDifficulty), Command = nameof(Logic.SetDifficulty))]` on dropdown controls.
- Successfully converted values are passed to Logic and reconciled.
- Temporarily unparsable numeric text, such as `-`, remains editable and invokes no command.

Do not change `IUIAdapter` or `IUIListenerAdapter` contracts.

Custom adapters must continue working without adding new interface members.

### 4.7 Generic ListenerSet

Add:

```csharp
public sealed class ListenerSet : IDisposable
{
    public void Listen(UnityEvent source, UnityAction handler);

    public void Listen<T>(
        UnityEvent<T> source,
        UnityAction<T> handler);

    public void Clear();

    public void Dispose();
}
```

Add a lazy property to `BaseObservablePresenter<M>`:

```csharp
public ListenerSet Listeners { get; }
```

Usage:

```csharp
protected override void InitializePresenter()
{
    base.InitializePresenter();

    Listeners.Listen(
        saveButton.onClick,
        GetLogic().Save);

    Listeners.Listen(
        filterInput.onEndEdit,
        GetLogic().ApplyFilter);
}
```

The caller explicitly selects the event. `ListenerSet` does not infer `onValueChanged` versus `onEndEdit`.

Rules:

- Ignore null event (`source == null`) or handler (`handler == null`) arguments.
- Deduplicate the same event and equal delegate within one set using `ReferenceEquals(entry.Source, source) && Equals(entry.Handler, handler)` (so method-group conversions like `GetLogic().Save` that allocate a fresh `UnityAction` pointing to the same `(Target, Method)` are properly deduplicated).
- Keep the exact registered `UnityAction` / `UnityAction<T>` delegate instance for `source.RemoveListener(exactDelegate)`.
- Remove only registrations owned by the set.
- Never use `RemoveAllListeners`.
- `Clear()` is idempotent and allows reuse.
- `Dispose()` clears and permanently closes the set.
- `Listen` after disposal throws `ObjectDisposedException`.
- Cleanup continues if one removal fails (guarding against destroyed `UnityEngine.Object` targets).
- Main-thread use only.

Raw event listeners provide no automatic conversion or reconciliation. Prefer command-aware `AutoBind` for bound values and `[ClickCommand]` for zero-argument button actions.

Typed helpers from AttaxUnity's `UiListeners` (`Button`, `Toggle`, `Slider`, `Dropdown`, `InputField`) are intentionally **not** ported: `Listen(btn.onClick, ...)` / `Listen(input.onEndEdit, ...)` covers all of them and keeps event selection explicit.

### 4.8 ClickCommand For Buttons

Add:

```csharp
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ClickCommandAttribute : Attribute
{
    public ClickCommandAttribute(string methodName);
    public string MethodName { get; }
}
```

Name chosen to avoid `CS0104` ambiguity with networking packages' `CommandAttribute` (Mirror, FishNet).

Usage:

```csharp
[SerializeField, ClickCommand(nameof(GameOverBannerLogic.Hide))]
private Button okButton;
```

Rules:

- Field type must be `UnityEngine.UI.Button` or a subclass; any other type is an invalid declaration (error, nothing wired).
- Command resolution reuses Section 4.2/4.3: public, instance, synchronous `void`, **zero parameters**, non-generic, on the paired Logic from `IAutoBindCommandTarget` (same GameObject, owns the model). Same rejected-signature list and diagnostics format.
- Presenters without a paired Logic (plain `BaseObservablePresenter<M>`) declaring `[ClickCommand]` get an error; no fallback.
- Inherited (base-class) `[ClickCommand]` fields are discovered, consistent with `[AutoBind]` field discovery.
- An unassigned or destroyed button field (CLR null or Unity fake-null via `IsTargetAlive`) is skipped silently, matching the optional-button pattern `if (btn != null) ...` used by consumers.
- Wiring happens once per presenter instance in the framework initialization path, registered through a **separate internal framework-owned `ListenerSet`** (not the public `Listeners`), guarded by a per-instance "wired" flag so a failed `InitializePresenter` retry or `SetListeners` rebinding never duplicates the listener. Consumers calling `Listeners.Clear()` in `InitializePresenter()` (existing pattern in AttaxUnity's `ModalWindow`, `AtaxxGameManagerPresenter`, `MoveHintPresenter`) must not remove `[ClickCommand]` registrations.
- `async void`, `Task`, `ValueTask` and coroutine-returning methods are rejected (same as Section 4.2). Async actions stay on `Listeners.Listen`.
- Wiring happens **before** `InitializePresenter()` runs, so consumer code may still configure the button (e.g. `interactable`) and add extra `Listeners.Listen` registrations.
- On click: check presenter, Logic and button liveness; invoke the cached `MethodInfo` once; catch and log exceptions once with presenter/button/Logic/method context. No model writes, no reconciliation.
- Cleanup disposes the internal click-command set on the same path as `Listeners` in Step 6 (presenter `OnDestroy` plus lifetime tracker).
- Immutable per-presenter-type metadata is cached in a `[NoAutoStaticsCleanup]` cache; no per-instance static state.
- `[ClickCommand]` and `[AutoBind]` on the same field is an invalid declaration.
- `nameof` does not preserve methods under IL2CPP stripping: document `[UnityEngine.Scripting.Preserve]` on click commands.

## 5. Compatibility Guarantees

| Existing Feature | Required Behavior |
|---|---|
| `[AutoBind]` | Unchanged field-name inference |
| `[AutoBind(nameof(Model.field))]` | Unchanged direct two-way binding |
| Display-only AutoBind | Unchanged |
| `ObservableHandler` | Unchanged signatures and invocation rules |
| `SyncOnBind` | Unchanged |
| Equal-value assignment | Remains silent unless already opted into notification |
| Custom adapters | No required API changes |
| Existing interfaces | No new required members |
| Existing initialization | No new required base lifecycle calls |
| Existing serialization | No schema or scene/prefab migration |
| Existing samples | Existing behavior remains valid |
| Existing package version | Remains `2.0.0` |

Initial binding, model loading, direct model changes and `ForceNotify()` must **never execute input commands**.

## 6. Files

| Path | Action | Purpose |
|---|---|---|
| `P/Runtime/AutoBindAttribute.cs` | Modify | Add optional `Command` property |
| `P/Runtime/IAutoBindCommandTarget.cs` | Add | Additive paired Logic resolution contract |
| `P/Runtime/ObservablePresenterWithLogic.cs` | Modify | Implement command-target interface |
| `P/Runtime/Observable.cs` | Modify | Metadata, validation, dispatch, reconciliation and cleanup |
| `P/Runtime/ListenerSet.cs` | Add | Generic scoped UnityEvent ownership |
| `P/Runtime/ClickCommandAttribute.cs` | Add | Declarative Button -> paired Logic zero-arg command |
| `P/Tests/Editor/ClickCommandTests.cs` | Add | ClickCommand validation, dispatch, rebinding and cleanup tests |
| `P/Runtime/BaseObservablePresenter.cs` | Modify | Lazy `Listeners` ownership and initialization integration |
| `P/Runtime/PresenterLifetimeTracker.cs` | Add | Internal lifetime cleanup infrastructure |
| `P/Tests/Editor/CommandAutoBindTests.cs` | Add | Command binding regression tests |
| `P/Tests/Editor/ListenerSetTests.cs` | Add | Generic listener ownership tests |
| Existing binding/adapter/lifecycle tests | Extend | Shared behavior and compatibility |
| `P/Tests/Runtime/CommandBindingPlayModeTests.cs` | Add | Runtime command and destruction-order checks |
| `P/Samples/Scripts/CommandSettings/` | Add | Compiling settings example |
| `P/README.md` | Modify | Public API, semantics and examples |
| `P/Documentation~/UnityLifecycle.md` | Modify | Ownership and cleanup guarantees |
| `P/Documentation~/RuntimeObjects.md` | Modify | Dynamic objects and pooling |
| `P/Documentation~/Testing.md` | Modify | New test coverage and player verification |
| `P/CHANGELOG.md` | Modify | Additions under existing 2.0.0 release |
| `mmvc-unity-skill/SKILL.md` | Modify | Recommended usage and restrictions |
| `mmvc-unity-skill/references/framework-guide.md` | Modify | Complete framework contract |
| `mmvc-unity-skill/references/examples.md` | Modify | Command and listener examples |
| `C:\Users\alexx\.skills\mmvc-unity` | Synchronize | Verified installed skill update |

New Unity-imported files require `.meta` files.

## 7. Ordered Implementation

### Step 1. Baseline And Impact

- [ ] Inspect status and diff; preserve unrelated changes.
- [ ] Confirm package version and framework Editor identity.
- [ ] Refresh the framework CGC scope.
- [ ] Check callers and references for binding metadata, adapters, Presenter initialization and subscription cleanup.
- [ ] Run existing binding tests before changes.
- [ ] Record unrelated baseline failures separately.

### Step 2. Add Public API

- [ ] Add `AutoBindAttribute.Command`.
- [ ] Add `IAutoBindCommandTarget`.
- [ ] Implement it explicitly on `ObservablePresenterWithLogic`.
- [ ] Add XML documentation.
- [ ] Confirm no existing public interface requires new members.

### Step 3. Metadata And Validation

- [ ] Extend binding metadata with command configuration.
- [ ] Validate commands once during binding.
- [ ] Cache only immutable reflection/type metadata.
- [ ] Support `ObservableVariable<T>` and supported subclasses.
- [ ] Reject command binding to `ObservableList<T>`.
- [ ] Produce precise diagnostics for invalid declarations.
- [ ] Retain valid model-to-view rendering but install no input mutation path for an invalid command declaration.

Diagnostics must identify the Presenter/control, observable, Logic, requested command and expected signature.

### Step 4. Dispatch And Reconciliation

- [ ] Add an explicit command branch alongside the unchanged legacy branch.
- [ ] Reuse existing UI adapters and tracked listener tokens.
- [ ] Invoke commands exactly once per input event.
- [ ] Reconcile accepted state without model writes or forced notifications.
- [ ] Catch command exceptions inside the callback so adapters cannot silently swallow them.
- [ ] Log the original failure once.
- [ ] Handle command destruction, unbinding or rebinding safely.

Do not use runtime IL generation or `Expression.Compile`. Cached `MethodInfo` invocation is acceptable, subject to player verification. Do not claim allocation-free dispatch.

### Step 5. Implement Generic ListenerSet

- [ ] Implement both `Listen` overloads.
- [ ] Implement deduplication and exact delegate removal.
- [ ] Implement reusable `Clear`.
- [ ] Implement terminal `Dispose`.
- [ ] Preserve other owners’ and Inspector-persistent listeners.
- [ ] Handle destroyed targets and release stored references.

### Step 6. Lifetime And Initialization

Existing derived Presenters may have private `OnDestroy` methods. Do not make compatibility depend solely on derived classes calling `base.OnDestroy()`.

- [ ] Add lazy `Listeners` ownership (`private ListenerSet _listeners;`) to `BaseObservablePresenter<M>`.
- [ ] Add `protected virtual void OnDestroy()` on `BaseObservablePresenter<M>` that disposes/clears `_listeners` (if allocated) and calls `Observable.RemoveAllSubscriptions(_model, this)` when `_model != null`. This directly handles individual Presenter destruction (`Destroy(presenter)` / `DestroyImmediate(presenter)`) when `OnDestroy` is not shadowed.
- [ ] To also cover derived Presenters that declare a non-overriding `private void OnDestroy()` (shadowing `BaseObservablePresenter<M>.OnDestroy()`):
  - Register a presenter-lifetime cleanup hook in `Observable` / `PresenterLifetimeTracker` (e.g., via `Observable`'s per-model `InstanceData` so `BaseObservableDataModel.OnDestroy()` -> `Observable.CleanupSubscriptions(model)` and `Observable.RemoveAllSubscriptions` / rebinding always clean up the presenter's bindings and dispose/clear its allocated `ListenerSet` when the presenter is `== null` or when the unit is torn down, plus an opportunistic liveness sweep if the Presenter component alone was destroyed while the Model stayed alive).
  - Ensure any static tracking structures in `PresenterLifetimeTracker` use `[AutoStaticsCleanup]` (on a `partial` class) and a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` reset hook.
- [ ] Track individual Presenter destruction, not only whole-GameObject destruction.
- [ ] Clean binding records and allocated listener sets.
- [ ] Work with inactive objects and EditMode tests.
- [ ] Do not allocate a listener set merely to clean it.
- [ ] Do not clear listeners on ordinary disable/re-enable.
- [ ] Preserve manual `Listeners` registrations during `Observable.SetListeners` rebinding.
- [ ] Remove registrations created during a failed `InitializePresenter` attempt before retrying (snapshot or clear newly added `Listeners` entries on exception in `RunInitializePresenter`), preserving earlier registrations.
- [ ] Document explicit `Clear` and rewiring for pooled views.

Standalone `ListenerSet` users remain responsible for disposal.

### Step 6b. Implement ClickCommand

- [ ] Add `ClickCommandAttribute` (Section 4.8) with XML docs stating synchronous, zero-argument, paired-Logic-only.
- [ ] Extend presenter metadata discovery with `[ClickCommand]` fields (including inherited fields), cached as immutable per-type metadata.
- [ ] Validate field type, method signature and paired Logic once; emit Section 4.3-style diagnostics.
- [ ] Wire valid commands through an internal framework-owned `ListenerSet` (separate from public `Listeners`) once per instance before `InitializePresenter()`; guard against duplicate wiring on init retry and rebinding; `Listeners.Clear()` must not affect them.
- [ ] Reject `async void` / `Task` / `ValueTask` / coroutine click commands with the standard diagnostic.
- [ ] Skip unassigned/destroyed buttons silently; reject `[ClickCommand]` + `[AutoBind]` on one field.
- [ ] Dispatch with liveness checks and single exception logging.

### Step 7. Add Tests

Implement the validation matrix in Section 8.

Keep existing tests and legacy samples intact as compatibility coverage.

### Step 8. Add Compiling Examples

Add a small settings Model, Logic and Presenter demonstrating:

- Command-bound float slider.
- Command-bound bool toggle.
- Trimmed string input.
- Preserved synchronous command methods.
- Custom percentage label through `ObservableHandler`.
- `[ClickCommand]` button (e.g. Reset to defaults).
- Generic `Listeners.Listen` for an argument-taking button.
- Legacy AutoBind alongside command-aware AutoBind.

Use one meaningful class per file.

Do not invent a dummy persistence service. Explain where production Logic would save.

Provide setup instructions and sample contract tests that construct and wire controls. Do not introduce a new scene or throwaway bootstrap script.

### Step 9. Documentation And Skills

- [ ] Update package API documentation and README.
- [ ] Explain both input directions and compatibility.
- [ ] Document synchronous-only commands prominently.
- [ ] Document malformed text and reconciliation behavior.
- [ ] Document ListenerSet lifecycle and event selection.
- [ ] Document `[ClickCommand]` (zero-arg, sync, paired Logic, `[Preserve]`), when to use `Listeners.Listen` instead, and why there is no model -> Logic handler attribute.
- [ ] Update lifecycle, runtime-object and testing documentation.
- [ ] Update all three repository skill files.
- [ ] Review guard plugin rules and examples without weakening policy.
- [ ] Synchronize the installed skill after verification.
- [ ] Preserve unrelated installed-skill files.

### Step 10. Same-Version Changelog

- [ ] Add command-aware AutoBind, `[ClickCommand]` and ListenerSet under the existing `2.0.0` additions.
- [ ] State that the feature is opt-in and backward compatible with the current baseline.
- [ ] Preserve historical release notes and date.
- [ ] Confirm package version remains `2.0.0`.
- [ ] Explain that Git consumers later need a new resolved revision despite the unchanged version.

### Step 11. Verification And Close-Out

- [ ] Run targeted and full framework suites.
- [ ] Verify two consecutive Play sessions without recompilation.
- [ ] Verify stripped IL2CPP execution.
- [ ] Validate the minimum supported Unity API surface.
- [ ] Check public API and serialization diffs.
- [ ] Verify docs, examples, skills and changelog agree.
- [ ] Refresh CGC.
- [ ] Store verified framework outcomes in Zengram.
- [ ] Report AttaxUnity adoption as deferred.

## 8. Validation Matrix

| Area | Required Cases |
|---|---|
| Legacy binding | Parameterless/named AutoBind; null/blank Command; plain MonoBehaviour and pure Presenter subscribers; custom adapters; existing notification and replay semantics |
| Command dispatch | Float/int/bool/string; inherited and overridden methods; one call per input; no binder model assignment |
| Logic decisions | Accept, clamp, normalize, reject, coupled settings and unchanged accepted value |
| Invalid declarations | Missing/wrong-unit Logic, missing field/method, display-only adapter, list observable, ambiguous method and every unsupported signature |
| Rendering | Initial bind; SyncOnBind on/off; direct model writes and loading without command execution |
| Conversion | Existing numeric conversion and rounding; malformed numeric input; custom adapters unchanged |
| Reconciliation | Unchanged normalized URL; rejected toggle; clamped numeric value; no feedback loop |
| Exceptions | Throw before/after mutation; one diagnostic; reconciliation where objects remain live |
| Reentrancy | Same-control recursion suppressed; unrelated controls remain functional |
| Destruction | Presenter-only, model-first, control-first, inactive host and command-triggered destruction |
| Rebinding | No duplicate listeners; owned tokens removed; manually owned ListenerSet retained |
| ListenerSet | Zero/one-value events; lambda removal; deduplication; multiple owners; persistent listeners preserved |
| Listener lifecycle | Clear/reuse; repeated Dispose; Listen after disposal; nulls; failed initialization retry |
| ClickCommand | One call per click; survives consumer `Listeners.Clear()` in `InitializePresenter`; async signatures rejected; inherited field; unassigned/destroyed button skipped; non-Button field, wrong signature, missing Logic, `[AutoBind]`+`[ClickCommand]` rejected; init retry and rebinding do not duplicate; presenter-only destruction removes listener; Inspector-persistent onClick calls preserved; exception logged once |
| Fast Play | Two consecutive Play runs without recompilation; no stale targets or duplicate callbacks |
| Player | IL2CPP with stripping; preserved reflection-only commands and inherited commands execute |
| Minimum version | Unity 6000.5 baseline (matching Phase 1 `package.json`), with unconditional `Unity.Scripting.LifecycleManagement` attributes |

## 9. Verification Commands

Run from the framework root, targeting only the framework project:

```powershell
pwsh -File Tools/Invoke-UnityTests.ps1 -Suite EditMode -Filter CommandAutoBindTests

pwsh -File Tools/Invoke-UnityTests.ps1 -Suite EditMode -Filter ListenerSetTests

pwsh -File Tools/Invoke-UnityTests.ps1 -Suite EditMode -Filter ClickCommandTests

pwsh -File Tools/Invoke-UnityTests.ps1 -Suite All

pwsh -File Tools/Test-RunnerUnitTests.ps1
```

Use fresh result directories and report actual counts.

Zero selected tests, stale XML, timeouts and unexecuted tests are not passes.

Do not use Attax-specific commands in the framework repository.

### Multi-Play Verification

Run two consecutive Play sessions without recompilation.

Confirm:

- Frames advance normally.
- Each input invokes one command.
- No stale target survives restart.
- Presenter-only destruction cleans its registrations.
- Reopening or re-enabling controls does not duplicate listeners.

### IL2CPP Verification

Build and run a framework sample/test harness using IL2CPP and the highest supported managed stripping level for the selected target.

Confirm:

- Attribute-only commands survive stripping when preserved.
- Inherited commands execute.
- Reconciliation works.
- Cleanup works.
- No runtime code generation is required.

Use `UnityEngine.Scripting.Preserve` on reflection-only commands in examples and tests. Document that `nameof` is not a linker-preservation guarantee.

Do not add blanket preservation of consumer assemblies.

Build artifacts belong outside source directories. Missing build modules, toolchains or runnable player environments are release blockers to report, not a basis for claiming compatibility.

## 10. Out Of Scope

- AttaxUnity changes or migration.
- Async commands.
- Debouncing.
- Transactional or automatic persistence.
- Rich validation UI.
- Configurable end-edit binding modes.
- Generalized conversion policies.
- Multi-argument UnityEvent helpers.
- `[ClickCommand]` with arguments, or on non-Button controls (use `Listeners.Listen`).
- A model -> Logic "command `[ObservableHandler]`" (rejected, see Section 1).
- Package version increases.
- Publishing or Git operations.

## 11. Completion Criteria

The work is complete when:

1. Existing consumers work unchanged.
2. A command-bound control needs one attribute instead of a handwritten input listener and control repaint handler, and a zero-argument action button needs one `[ClickCommand]` attribute instead of handwritten listener wiring.
3. Logic remains the sole owner of domain decisions.
4. Rejected and normalized input is reconciled automatically.
5. Listener ownership is explicit and reliably cleaned up.
6. All required compatibility, lifecycle and player checks pass.
7. Documentation, compiling examples, skills and changelog are updated consistently under version `2.0.0`.
8. No AttaxUnity files have changed.

### Rules For The Implementing Agent

- Follow this standalone plan; do not extend or rewrite existing plans.
- Use CodeGraphContext before symbol changes and verify stale results against current disk source.
- Do not weaken guards or silently alter legacy behavior.
- Stop and ask when the plan conflicts with current code or a required release gate cannot be completed.
- Report verification honestly; distinguish implementation completion from release readiness.