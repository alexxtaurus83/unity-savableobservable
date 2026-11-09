# MMVC simplification + opt-in same-value notification

Package root: `Packages/com.evialdev.savableobservable` (paths below are relative to it).

## Ground rules (apply to every phase)
1. Execute phases in order. Each phase = code + **its tests** + **its docs** in one commit. A phase is not done until all three are updated.
2. After each phase run: EditMode suite, PlayMode suite, `Tests/Editor/Pipeline/MmvcSelfTest` (runner: `Tools/Invoke-UnityTestRunner.ps1 -Suite All`). All green before next phase.
3. Tests: never delete a test just because it fails — update it to assert the **new** contract, and add tests for every new behavior. Deleted APIs → delete/replace tests that exist only to test them, replacing with a test of the successor behavior.
4. Docs: update `README.md`, `Documentation~/UnityLifecycle.md`, `Documentation~/RuntimeObjects.md`, `Documentation~/Testing.md`, `CHANGELOG.md` (under one `## [Unreleased]` entry, accumulated per phase), plus XML doc comments on touched public APIs.
5. Samples (`Samples/Scripts/*.cs`) must compile and follow the new APIs.
6. Final phase (8) does the full-doc consistency pass and leftover-symbol grep.

## Decisions (agreed)
- All review findings §1–§7 accepted.
- `ObservableVariable<T>` does not notify on equal assignment **by default**; opt-in per field to always fire.
- Opt-in = `[NotifyOnSameValue]` field attribute **plus** public runtime property `NotifyOnSameValue`.
- Suppression applies to `ObservableVariable<T>` only; `ObservableList<T>` unchanged.
- ServiceLocator: Logic auto-registers by default; duplicate live key → `Debug.LogError`, not registered, first keeps the key. No eviction. Multi-instance Logic uses `RegisterAsSharedSingleton => false` or distinct `singletonName`.

---

## Phase 1 — Behavior fixes

### Code
**1.1 Same-value suppression** — `Runtime/ObservableVariable.cs`, new `Runtime/NotifyOnSameValueAttribute.cs`, `Runtime/BaseObservableDataModel.cs`.
- `[AttributeUsage(AttributeTargets.Field)] public sealed class NotifyOnSameValueAttribute : Attribute` (XML doc explaining semantics).
- `ObservableVariable<T>`: `[NonSerialized] bool _notifyOnSameValue` + `public bool NotifyOnSameValue {get;set;}` (XML doc). Setter:
  ```csharp
  set {
      if (!_notifyOnSameValue && EqualityComparer<T>.Default.Equals(_value, value)) return;
      PreviousValue = _value; _value = value; OnValueChanged?.Invoke(this);
  }
  ```
  Suppressed → `_value`/`PreviousValue` untouched. Flag on + equal → fires with `PreviousValue == Value`. `ForceNotify()` always fires. Editor `OnValidate` unchanged.
- `EnsureFieldsInitialized()`: for fields with `[NotifyOnSameValue]`, set flag `true` (never reset to false, so runtime opt-in survives re-init). Cache attribute lookup per model type. Set via internal interface (`ISameValuePolicy`) — no generic reflection.
- Attribute on `ObservableList<T>` / non-observable field → one-time `LogWarning` "has no effect".
- Verify `NotifyOnSameValue` is never serialized nor copied by `LoadDataFromModel`.

**1.2 Remove loader rebind** — `Runtime/LoaderWithModel.cs`: delete `GetComponent<BaseObservablePresenter<M>>` + `Observable.SetListeners` block and misleading comment.
- `Samples/Scripts/SamplePlayerLoader.cs` line ~57 also calls `Observable.SetListeners(presenter, model)` after load → remove the same way.

### Tests
- `Tests/Editor/ObservablePrimitivesTests.cs` — add: equal assign no notify & `PreviousValue` unchanged; different assign notifies; `[NotifyOnSameValue]` field notifies on equal with `PreviousValue == Value`; runtime flag without attribute notifies and survives `EnsureFieldsInitialized()`; `ForceNotify` ignores flag; same reference type instance suppressed; equal `IEquatable` struct suppressed; attribute on list field → `LogAssert.Expect(Warning)`; flag not serialized (`JsonUtility.ToJson` doesn't contain it).
- `Tests/Editor/Pipeline/MmvcSelfTest.cs` ~L121 Phase 1.2: invert to "identical value does NOT fire"; add 1.2b for flag.
- `Tests/Editor/ModelPersistenceTests.cs` L90 `LoaderWithModel_LoadDataFromModel_RebindsPresenterWithoutCorruptingListeners` → rename `..._KeepsExistingBindingAndNotifies`; assert presenter observes loaded value **during** load (70) without rebind, `InitializePresenter` count stays 1. Add: load with values equal to current → no handler call; flagged field → handler call.
- `Tests/Editor/MMVCRestrictionsTests.cs` ~L556 "Explicit loader rebind" — update to new no-rebind contract.
- `Tests/Shared/MmvcPersistenceContractTestsBase.cs` / `MmvcUnitContractTestsBase.cs`: any contract that sets a field to its current value and expects notify (e.g. ~L332 ForceNotify loop is fine) — audit and fix; add contract "equal assign is silent unless `NotifyOnSameValue`".
- Grep all tests for patterns `x.Value = x.Value`, assigning defaults then expecting notify (e.g. `InitializeDefaults()` then asserting handler count) and fix expectations.

### Docs
- README: new section **"Change notification semantics"** (content in Phase 8 spec below — write it now, polish in Phase 8).
- `RuntimeObjects.md` L129: link to that section; pooled checkout note (same value is silent → `ForceNotify` / flag).
- `UnityLifecycle.md` L280: remove "if value equality is checked" hedge; state rule. Remove §6 loader-rebind note.
- README loader section (~L232–259): remove rebind claims.
- CHANGELOG: behavior change + migration (re-assign to poke UI → `ForceNotify()` or `[NotifyOnSameValue]`); loader no longer rebinds.

---

## Phase 2 — Remove duplication (no behavior change except listed API removals)

### Code
- Shared model/logic cache helper for `BasePresenter/BaseObservablePresenter.GetModel`, `BaseLogic.ResolveAndInitializeModel`, `LoaderWithModel.GetModel`, both `GetLogic()`.
- `ComponentAutoRequire.EnsureComponents(owner, delayed, params Type[])`; used by `BaseLogic<M,P>`, `LoaderWithModel`, `LoaderWithModelAndLogic`.
- `internal static class TypeUtil` (`IsSubclassOfRawGeneric`, hierarchy walk); Editor asm uses it (add `InternalsVisibleTo` if missing).
- Merge `ModelMetadata` (Observable.cs) + `ModelLoadDescriptor` (BaseObservableDataModel.cs) → one cached `ModelDescriptor` (incl. `[NotifyOnSameValue]` flags from Phase 1).
- Single multiple-model check (keep `Observable.ValidateSingleMmvc`, delete `ServiceLocator.BindPresenter` copy).
- `UIAdapters.cs`: `StringInputAdapterBase<TField>`; TMP/legacy InputField adapters derive. Keep priorities and re-entrancy guard per adapter.
- `ObservableList`: remove `OnValueChanged` alias (keep `OnChanged`).
- Remove `LoaderWithModel.GetModelToSave()` and no-op `BaseLogic<M,P>.Awake` override.

### Tests
- `Tests/Shared/MmvcUnitContractTestsBase.cs` L102 and `MmvcPersistenceContractTestsBase.cs` L99/128/165: reflection `GetProperty("OnValueChanged") ?? GetProperty("OnChanged")` → resolve via type: `ObservableVariable` → `OnValueChanged`, `ObservableList` → `OnChanged` (or via new `IObservableVariable` member after Phase 4; leave TODO-free working code now).
- `Tests/Editor/UIAdapterTests.cs`: ensure both InputField adapters covered for: string, int, float (invariant culture), empty→default for value types, empty→ignored for reference non-string, no echo during `SetValue`. Add missing cases before refactor (characterization), keep green after.
- `Tests/Editor/ModelPersistenceTests.cs`: confirm `LoadDataFromModel` cases cover properties, observable fields, plain fields, `[NonSaved]`, list/array cloning — add any missing before merging descriptors.
- Any `GetModelToSave` use → `GetModel`.
- Add `ComponentAutoRequire` test for Logic auto-adding M+P and LoaderWithModelAndLogic auto-adding M+LO (if not present).

### Docs
- README: remove `GetModelToSave` (~L233, L259); remove `OnValueChanged` alias mention for lists; list examples use `OnChanged`.
- CHANGELOG: breaking removals (`GetModelToSave`, `ObservableList.OnValueChanged`).

---

## Phase 3 — Presenter state collapse

### Code
- Merge `BasePresenter<M>` into `BaseObservablePresenter<M>`; delete `Runtime/BasePresenter.cs` (+ `.meta`).
- `BaseLogic<M,P>.GetModel()`: remove `_presenterInitialized` + try/catch; call `GetComponent<P>()?.EnsureInitialized()`.
- `EnsurePresenterInitialized` → internal; removed from `IObservablePresenter`. `Observable.SetListenersCore` uses internal `IPresenterBindingState.EnsureInitializedForBinding()`; keep "InitializePresenter before listeners" guarantee and `_initState` reentrancy guard.
- `ServiceLocator` rejection check: drop `BasePresenter<>` branch.

### Tests
- `Tests/Editor/MMVCRestrictionsTests.cs` L48 (`BasePresenter` DisallowMultiple) → assert on `BaseObservablePresenter<>`; L108 `MockRawNonObservablePresenter` + L141–145 raw-presenter rejection → delete (type no longer exists); keep observable-presenter rejection.
- `Tests/Shared/MmvcAssemblyAuditTestsBase.cs` L66: drop `BasePresenter<>` comparison.
- Keep/verify tests: `InitializePresenter` runs exactly once; runs before handlers attach; Logic.GetModel during InitializePresenter (reentrancy) works; exception in InitializePresenter does not latch (retry succeeds). Add any missing.
- Grep tests for `EnsurePresenterInitialized` callers → use `EnsureInitialized`.

### Docs
- README L145, L149, L181–193: remove `BasePresenter<M>` section; presenter hierarchy = `BaseObservablePresenter<M>` → `ObservablePresenterWithLogic<M,LO>`.
- `UnityLifecycle.md` §4 "GetModel() Asymmetry: BaseLogic vs BasePresenter" (L143–150): rewrite as Logic vs Presenter; remove `EnsurePresenterInitialized`, `_initState`, `_presenterInitialized` mentions (L53, L62, L83); L294 remove `BasePresenter.cs` link.
- CHANGELOG: `BasePresenter<M>` removed; `IObservablePresenter.EnsurePresenterInitialized` removed.

---

## Phase 4 — Observable subscription registry

### Code
- Single per-model registry `subscriber → List<SubscriptionRecord{ IObservableVariable source; Delegate handler; bool managed; }>` replacing `Subscriptions`, `HandlerUnbindActions`, `AutoBindUnbindActions`.
- `IObservableVariable` gains `CreateHandler`, `CreateAutoBindHandler`, `AddHandler`, `RemoveHandler`; implemented in `ObservableVariable<T>`/`ObservableList<T>`. Delete `IObservableBinding`, `ObservableVariableBinding<T>`, `ObservableListBinding<T>`, `ReflectionBinding`, `GetBinding` + cache (confirm no field type falls back to `ReflectionBinding` first).
- Remove locks in `ObservableTrackedAction` and `InstanceData`; keep main-thread assert; keep locks on static metadata caches.
- Remove public `SetAutoBindListeners` (both) and `SetListeners(object)`.
- Unbound-field warning gated by `ObservableSettings.StrictBinding` (default false).

### Tests
- `Tests/Editor/PrefabAndPoolingLifecycleTests.cs` L465–476 and `Tests/Editor/TrackedActionAndForeignSubTests.cs` L333–369 (`SetAutoBindListeners_Standalone_IsIdempotent...`): switch to `SetListeners(presenter, model)`; rename test to `SetListeners_Repeated_IsIdempotent_AndPreservesHandlerBindings`.
- Grep for `SetListeners(` with one arg → two-arg form.
- `Tests/Editor/DomainReloadLifecycleTests.cs` L27 and `MmvcSelfTest.cs` L356 `CleanupSubscriptions`: keep API, assertions must still pass.
- Keep green: `TrackedActionAndForeignSubTests` (manual subs survive rebind L304), `MmvcThreePathPlayModeTests.Rebind_KeepsManualSubscription...` L239, `PrefabAndPoolingLifecycleTests.RemoveListeners_ThenEnsureInitialized_RebindsExactlyOnce` L350.
- Add: `RemoveListeners` removes only that subscriber's records; managed-only removal keeps manual subs; two-way UI listener token removed on model destroy; no "unbound field" warning by default, warning present when `StrictBinding = true`; custom `IObservableVariable` is no longer supported unless it implements new members (compile-level, document only).
- Ensure `ObservableBindingTests.cs` covers 0/1/2-param handlers for variables and lists via new path.

### Docs
- README L147 & `UnityLifecycle.md`: remove `SetAutoBindListeners`; document `SetListeners(subscriber, model)` as the only bind API; document `StrictBinding`.
- Remove internal names (`RemoveManagedBindings`, binding classes) from docs.
- CHANGELOG: removed APIs; `IObservableVariable` new members (breaking for custom implementers); warning now opt-in.

---

## Phase 5 — ServiceLocator rewrite (behavior change)

### Code
- Store `Dictionary<(Type, string), object>` only; delete live lists, model bindings, `_logicToModelMap`, all prune/sync helpers.
- `RegistrationResult`: `Added`, `SameInstance`, `Duplicate` (replaces `EvictedDuplicate`), `Rejected`.
- `TryRegister`: duplicate live different instance → `LogError` (message names type, key, both GameObjects; suggests `singletonName` / `RegisterAsSharedSingleton => false`), returns `Duplicate`; dead entry replaced.
- `Unregister(instance)`: remove only if stored value is that instance.
- `Get`: by key; unnamed fallback to sole entry of type; multiple → LogError ambiguous.
- `GetModel<M>(name)`: derived from registered `BaseLogic` entries via `GetUntypedModel()`.
- Remove `Add`, `Remove<T>`, `Remove(Type)`; `Services` mirrors. `Services` reads `ServiceLocator.instance` directly; remove `_instance`, `ReleaseLocator`; `TestState` holds single locator (keep `CaptureTestState`/`RestoreTestState`/`SetTestLocator` signatures).

### Tests (`Tests/Editor/MMVCRestrictionsTests.cs` heavy)
- L183–195, L278–290, L309–322: `EvictedDuplicate` → `Duplicate`; expected errors → new duplicate-registration message at `TryRegister` time (`LogAssert.Expect` before the call); `Get`/`GetModel` for that key now return the **first** instance (not null).
- L221 `locator.Remove<SamplePlayerLogic>("PlayerA")` → `locator.Unregister(logicA)`.
- L349–350 recovery test: rewrite — after destroying first, key is empty (second was never registered); `Get` returns null with no error.
- L579–606: scene-level duplicate test → error logged at `InitializeSharedSingletons`, `Services.Get` returns first.
- `DomainReloadLifecycleTests.cs` L79–174 and `Tests/Runtime/MmvcPlayModeTests.cs` L121–143: `TestState` single-locator semantics; destroyed locator → `Services.instance == null`.
- Add: `Unregister` of non-stored instance is no-op; `Get` named/unnamed/ambiguous; `GetModel` unique/named/ambiguous/after logic destroyed returns null; `RegisterAsSharedSingleton=false` pooled units (49 copies) produce **no** errors (`PrefabAndPoolingLifecycleTests`); Presenter/Model/plain/non-ISharedSingleton rejection kept.
- `Tests/Runtime/MmvcPrefabPlayModeTests.cs`: audit for prefabs instantiated multiple times with registering Logic → set `RegisterAsSharedSingleton=false` on test types or expect errors.

### Docs
- README L15, L365 and singleton section: "duplicate = error, first wins"; pooled units must override `RegisterAsSharedSingleton => false` or set distinct `singletonName`.
- `UnityLifecycle.md` L69, L137; `RuntimeObjects.md` L205–214: replace eviction text.
- CHANGELOG: semantics change, `EvictedDuplicate → Duplicate`, removed `Services.Add/Remove`. Migration note for projects with multiple unnamed Logic of same type.

---

## Phase 6 — Execution order

### Code
- Remove `[DefaultExecutionOrder]` from `BaseObservableDataModel` and `LoaderWithModel`, and the explicit `0` from `BaseLogic`. Keep ServiceLocator -200, presenters -150.
- `MMVCExecutionOrderSynchronizer.GetTargetExecutionOrder`: ServiceLocator -200, presenters -150; models/loaders/logics → `0` (resets previously-synced -300/-250 in existing projects); others `int.MinValue`.
- Keep `BaseObservableDataModel.Awake` → `EnsureFieldsInitialized`.

### Tests
- `MMVCRestrictionsTests.cs` L565–572: update expected values (model 0, loader 0, locator -200, presenter -150, logic 0, others MinValue).
- Add: model added at default order with presenter on same GO → presenter binds, fields initialized (EditMode + PlayMode in `MmvcPlayModeTests`); loader `LoadDataFromModel` before model `Awake` works (lazy init).

### Docs
- README L16, L29, L344–346: 2-stage pipeline (ServiceLocator → Presenter); `UnityLifecycle.md` L19–40 table + mermaid chart rewritten.
- CHANGELOG entry.

---

## Phase 7 — Samples & Testing doc
- `Samples/Scripts/*`: compile against final APIs; demonstrate `nameof(...)` in `[AutoBind]`/`[ObservableHandler]`; optionally one `[NotifyOnSameValue]` example field with handler using `previous`.
- `Tests/Editor/SampleMmvcContractTests.cs`: green; add sample same-value case if sample field added.
- `Documentation~/Testing.md`: update any API names mentioned; add section "Contract tests for change semantics" briefly describing the new equal-value contract in `MmvcUnitContractTestsBase` so consumer projects know their contract subclasses now enforce it.

## Phase 8 — Final documentation pass & verification
**README "Change notification semantics" (required content):**
- Default: equal assign (`EqualityComparer<T>.Default`) → no notification; `Value`/`PreviousValue` unchanged.
- Opt-in: `[NotifyOnSameValue]` on field, or `model.field.NotifyOnSameValue = true`; every assign notifies; equal assign gives `PreviousValue == Value`.
  ```csharp
  [NotifyOnSameValue] public ObservableVariable<int> lastRoll;

  [ObservableHandler(nameof(MyModel.lastRoll))]
  void OnRoll(int value, int previous) {
      if (value == previous) HandleSameRoll(); else HandleNewRoll(value);
  }
  ```
- `ForceNotify()` always fires. Lists notify on every mutation; attribute on list → warning, no effect.
- Gotchas: reference types compared by reference (mutate + reassign same ref is silent → `ForceNotify`); `NaN == NaN` suppressed; flag is not serialized or saved; Inspector edits fire only on real change; loading equal values is silent.

**Consistency pass:**
- README: dedupe repeated rules; one concept + one example; `nameof` everywhere; qualified `Services.Get` guarantee.
- `UnityLifecycle.md`: guarantees only, no private field names; sequence diagrams reflect Phases 3–6.
- Finalize CHANGELOG `[Unreleased]` → version; bump `package.json` major version.
- Grep package (excluding CHANGELOG history) for zero hits: `EvictedDuplicate`, `evict`, `SetAutoBindListeners`, `GetModelToSave`, `BasePresenter<`, `EnsurePresenterInitialized`, `_presenterInitialized`, `_initState`, `-300`, `-250`, `Services.Add`, `identical value, no notification` (old wording), `if value equality is checked`.
- Full suite run (EditMode, PlayMode, SelfTest) green; record counts in commit message.

## Risks
- Same-value suppression silently stops "poke" re-assignments in user code → CHANGELOG migration note.
- Phase 5 changes multi-instance semantics → visible via error log; migration note.
- Phase 4 IL2CPP-sensitive generics → IL2CPP player smoke build if available; otherwise flag as unverified in summary.

## Out of scope
- Equality suppression for `ObservableList`.
- Custom per-variable `IEqualityComparer<T>`.
- Updating the external `mmvc-unity` skill (`C:\Users\alexx\.skills\mmvc-unity\SKILL.md`) — mention in final summary as follow-up.
