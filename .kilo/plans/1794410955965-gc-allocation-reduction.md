# GC allocation reduction in the MMVC runtime (revised after pre-review)

Rules: [AGENTS.md](../../AGENTS.md). Do not claim allocation-free bindings. Every code change needs tests, a CHANGELOG entry, and docs kept in sync. No edits have been made yet.

Scope scanned: `Packages/com.evialdev.savableobservable/Runtime/*.cs` (29 files). Client checked: `F:\Unity_projects\AttaxUnity` (consumes the package from git branch `feature/fix`, see its `Packages/manifest.json`).

## Corrections to the original premises

| Premise | Finding |
|---|---|
| LINQ is used in many places | LINQ is in one runtime file, `Observable.cs`. The chains sit in `ResolveClickCommandMethod` (lines 576-640) and `ResolveCommandMethod` (about 665-745). They run once per `(logicType, command[, valueType])` and the result is cached. They never run per frame. |
| Hot paths are Update/LateUpdate | The package has no `Update`, `LateUpdate`, `FixedUpdate`, coroutine, `yield`, async, `SendMessage`, `Find*`, `.material` or `Input` use. Its hot paths are event-driven. `ObservableTrackedAction.Invoke` and `ObservableList` Add/Insert/RemoveAt/indexer are already allocation-free. |
| Client garbage comes from the framework | In AttaxUnity the only per-frame framework lookups are `Services.Get<AtaxxGameManagerLogic>()` in `BoardGraphLogic.Update` (until bound, `BoardGraphLogic.cs:202-214`) and `Services.Get<AtaxxGameManagerLogic>()` in `OnlineMatchController.Update`. These are unnamed lookups whose default key is registered, so they take the early-return path: no list is allocated today. K1 does not help them; only K3 could (if `Type.Name` allocates). The other `Update` methods use `GetModel()` (cached) or do not touch the framework. The client has about 300 `Services.Get` call sites, mostly in event handlers. Client-side garbage (`new List<VulnIssue>(...)`, `$"..."`) is outside this task. Expect a small real-world gain: the measurable per-call win is K2 (`GetModel<M>` allocates a list on every call) and K3 if confirmed. |

## Decision per item (effect on the framework, pre-review result, client effect)

"Lifecycle" = Unity lifecycle, inactive/disabled objects, pooling, domain reload (Play Mode runs with domain reload disabled).

### KEEP (minimal impact)

| Item | What changes | Pre-review: new bugs? | AttaxUnity effect |
|---|---|---|---|
| **K1** `ServiceLocator.Get(Type,name)` (`ServiceLocator.cs:113-143`) | Replace `new List<string>()` by `int count` plus `string firstKey`. The key list is rebuilt only in the ambiguity error branch, and only when `enableLogging`. | No behavior change. Dead-entry removal, the `IsLive` check, the "sole instance" fallback and the error text stay identical. The registry only holds live-or-destroyed objects; `IsLive` checks destruction, not `activeSelf`, so inactive logics found by the two-pass discovery (`GetComponentsInChildren(true)`) behave as before. No Unity API is touched. | Small. It only matters when the default key misses (a Logic registered under a custom `singletonName` looked up without a name, or an unregistered type returning null). Registered default-key lookups already return early and allocate nothing. No API change; named lookups (`BaseAISettingsLogic`, `BoardGraphLogic`) unaffected. |
| **K2** `ServiceLocator.GetModel<M>` (`ServiceLocator.cs:156-196`) | Same list-to-counter change. It currently allocates a list on every call, even with a name. The error branch re-collects keys with the same filter. | No behavior change. The key order in the message stays the same (same dictionary, entries already pruned). Keep `found`-is-last semantics and the `break` on the named match. Keep the null return on ambiguity even when logging is disabled. | Positive. `Services.GetModel<PlayerSettingsDataModel>()` is called by `HapticService`, `MoveHintLogic.Settings`, `OnlineService`, `MenuBackgroundLogic`. |
| **K3** default-key string cache (`ServiceLocator.cs:103,118`) | `Type.Name` is read on every unnamed `Get`/`IsRegistered`. Replace with an **instance** field `Dictionary<Type,string>` on `ServiceLocator`, cleared in `OnDestroy`. | Gated: first prove that `Type.Name` allocates per call (step 1). If it does not on Mono or IL2CPP, drop K3. It is instance state, so there is no new static, no `[AutoStaticsCleanup]` question and no domain-reload leftover. A string cached per type is immutable. Thread-safety matches `_services`, which is main-thread only. Do not change `ResolveKey` (register/unregister are cold). | Largest benefit: every unnamed `Services.Get<T>()` (hundreds of sites). Keys stay equal by value to the registered key, so `AppStateNavigationTests`-style assertions keep working. |
| **K4** remove LINQ from `Observable.cs` (lines 3, 577-578, 584, 588, 592, 633, 670-671, 677, 681, 685, 737) | Plain loops over `MethodInfo[]` / `List<MethodInfo>`. `using System.Linq` is removed. | Pure refactor if ported exactly: preserve list order and the `filtered` rule (drop `c` when another candidate has a different declaring type derived from `c.DeclaringType`). Runs on type metadata only; no Unity objects, no lifecycle interaction. Add characterization tests **before** the rewrite (hiding `new` methods, overloads, static, private, generic, `Task`, wrong arity or parameter type) because the existing fixtures may not pin the ambiguity and error texts. No runtime gain (cold path); it is the requested LINQ cleanup. | None: the client currently uses neither `[ClickCommand]` nor `[AutoBind(Command=...)]`, so these resolvers do not run in the client today. |

### DROPPED (risk or benefit not worth it)

| Item (earlier id) | Why dropped | What pre-review found |
|---|---|---|
| Typed AutoBind/handler paths, `IUIAdapter.SetValue<T>` (H5, H6) | Touches the public adapter interface and `IObservableVariable`. | Custom adapters would keep working only via default interface methods; this does not fit the AGENTS "stripping-safe" rule and gives per-edit, not per-frame, savings. The client has no custom adapters and uses label/text `[AutoBind]` only. |
| Typed setter for UI -> model (H7) | Replaces `PropertyInfo.SetValue`. | Needs a new generic hook. The client's `[AutoBind]` fields are label/text bindings, so UI -> model edits are not exercised. |
| `Delegate.CreateDelegate` instead of `MethodInfo.Invoke` for `Command` / `[ClickCommand]` (H8, H9) | Changes exception shape (`TargetInvocationException` wrapping) and adds IL2CPP generic value-type AOT risk. A closed delegate also captures `logic`, a lifecycle question for pooled units. | Dropped. The client does not use these features today. If it adopts them later, `Command` sliders would pay one `MethodInfo.Invoke` per drag tick; revisit only if a Profiler run then shows it matters. |
| Exact-type check instead of try/catch in `CreateHandler` (H10) | Would break handlers that bind today through delegate contravariance (a method taking a base type bound to `Action<T>`); they would silently fall to the slow path or fail. | Real regression found in review. |
| `TMP_Text.SetText(string, float/int)` and skip-if-equal (H4) | `SetText("{0}", float)` formats differently from `ToString()` (precision, culture). Skipping a write changes dirty/rebuild behavior. | Visible text could change. Only fires on value change anyway. |
| `ObservableList.PreviousValue` scratch buffer (H11) | Behavior change: callers that keep `PreviousValue` after the callback would see overwritten data. | Needs an explicit decision, so not in this scope. |
| `ObservableList` struct `GetEnumerator` (H12) | Changes a public method signature (binary break for precompiled consumers). | The client iterates `ObservableList` with `for` loops, plus one `foreach` in `OnlineLobbyPresenter.cs:81` (lobby, rare). No benefit. |
| `EnsureFieldsInitialized` "already initialized" latch (M6) | **Dangerous.** | It would skip `SetParentDataModel` re-linking after `LoadDataFromModel` replaces observables, after Editor undo/serialization, and for models that hide the method (`BoardGraphDataModel.EnsureFieldsInitialized` uses `new`); the client also calls it explicitly. A skipped re-link leaks subscriptions. The call allocates nothing; the cost is CPU only. |
| Static `GetComponents(List<T>)` buffer in `ValidateSingleMmvc` (M1) | Adds mutable static state (AGENTS statics rule: `[AutoStaticsCleanup]` plus reset), and `SetListeners` can recurse through replay handlers. | Bind-time only, two small arrays per bind. |
| `GetParameters`/`GetGenericArguments` caching, replay list, `RemoveAll` closure (M2-M5) | Bind/unbind only. | Marginal; more code than it saves. |
| `PresenterLifetimeTracker` HashSet (M7) | CPU (O(n^2) at scene init), not GC; changes sweep order. | Not a GC issue. |
| ServiceLocator `(modelType,name)` result cache | Needs invalidation on register/unregister/destroy. | Correctness risk for little gain after K2. |

Left as is, noted only: `ObservableTrackedAction.Add/Remove` `ToArray` (copy-on-write, intentional); reflection, `$"..."` and `Debug.Log` in error paths; `LoadDataFromModel` (load operation); Editor-only `ToArray` snapshots in `ObservableList` (`#if UNITY_EDITOR`); the `Enumerable.Select` in `Tests/Editor/ModelPersistenceTests.cs:281` (test code, harmless).

## Ordered tasks

1. **Baseline (no source edits).** Run `pwsh -File Tools/Invoke-UnityTests.ps1 -Suite EditMode`. Record counts. Note the result of `ObservableList_Mutations_DoNotAllocateGCMemory`: `ObservableList.NotifyChanged` calls `ToArray()` under `#if UNITY_EDITOR` (line 414), so confirm that test is green on the baseline before relying on it. A failure there is pre-existing and must be reported, not hidden.
2. **Add tests first** (`Tests/Editor/`, new `ServiceLocatorAllocationTests` plus additions):
   - `Services.Get<T>()` unnamed with a default key, unnamed with a custom `singletonName` (default-key miss, sole instance), unnamed unregistered type, `GetModel<M>()` unnamed and named. Assert `Is.Not.AllocatingGCMemory()` after one warm-up call. These tests are expected to fail before K1-K3; if the K3 test already passes on Mono, K3 only needs the IL2CPP Profiler check in step 7 (decide there).
   - Behavior tests that must pass before and after: ambiguity returns null and logs with all keys listed (`LogAssert.Expect`), a destroyed entry is pruned, an inactive Logic is still found, a named lookup does not fall back.
   - Characterization tests for the command resolvers (K4) on a test Logic with overloads, hiding `new` methods, static/private/generic/`Task` methods and wrong arity. Assert the exact returned method or error text.
3. **K1 + K2** in `ServiceLocator.cs`.
4. **K3** in `ServiceLocator.cs` (only if justified by step 2).
5. **K4** in `Observable.cs`. Remove `using System.Linq`. Re-run the step 2 characterization tests unchanged.
6. **CHANGELOG entry** (no version bump): `Services.Get`/`GetModel` allocate less; LINQ removed from command resolution; no behavior or API change. Do not claim allocation-free bindings. README, `Documentation~/` and `mmvc-unity-skill/` need no change (no user-visible behavior).
7. **Verify** (below).

Tests/Shared contracts are not touched, so consumers' contract tests are unaffected.

**Client compatibility constraints (from `1794411000000-attax-gc-compliance-and-client-changes.md`):**
- Do not rename or remove the 11 package tests that AttaxUnity pins in `Assets/Tests/EditMode/DelegatedCoverageTests.cs:25-37` (`MMVCRestrictionsTests.ServiceLocator_TryRegister_Rejects_PresentersAndModels`, `..._Rejects_PlainClass`, `ServiceLocator_Duplicate_FirstWins_AndNamedInstanceIsUnaffected`, `ServiceLocator_Duplicate_FirstKeepsKey_RejectedInstanceNeverRegistered_And_KeyEmptiesWhenFirstDies`, `ServiceLocator_Unregister_OfNonStoredInstance_IsNoOp`, `ServiceLocator_GetModel_Unique_Named_Ambiguous_AndNullAfterLogicDestroyed`, `ServiceLocator_DestroyedLogic_With_SurvivingModel_PrunedOnGetModel`; `ObservableBindingTests.SetListeners_ReplacesManagedBindings_ButKeepsManualSubscriptions`; `TrackedActionAndForeignSubTests.SetListeners_Rebind_PreservesManualSubscriptionsAddedAfterBinding_OfPresenterAndForeignSubscriber`; `MmvcThreePathPlayModeTests.ModelChange_RendersThroughAutoBind_Handler_AndManualSubscription_ExactlyOnce`, `Rebind_KeepsManualSubscription_AndDoesNotDuplicateBindings`). Add new tests beside them.
- K3 stays an instance field (Attax swaps locators through the public `Services.SetTestLocator` seam; a static would leak between tests). Keep `Services.CaptureTestState`/`SetTestLocator`/`RestoreTestState` public and unchanged.
- AttaxUnity requires no source change for K1-K4. Its only optional edit is C1 (`MoveHintLogic` reads the settings model once).

## Verification

- Framework: `pwsh -File Tools/Invoke-UnityTests.ps1 -Suite EditMode` (all, not only the new fixture), then `-Suite PlayMode` (the ServiceLocator two-pass discovery and lifecycle fixtures run there). Report suite, counts, failures. Source review alone is not a pass.
- Client: bump the client to the branch with the change (it pulls `feature/fix` via git, so push first; never edit its `Library/PackageCache`), then rerun its EditMode suite (`AppStateNavigationTests` and `MenuBackgroundLogicTests` exercise named lookups, destroy/unregister and handler counts), `AttaxMmvcContractTests` and `attax_wiring_audit --scope all`. API surface is unchanged, so no client code change is expected. Details: `1794411000000-attax-gc-compliance-and-client-changes.md`.
- Profiler (Development Player, not the Editor): CPU Usage -> Hierarchy, `GC.Alloc` column. Scenario A: `OnlineMatchController.Update` during an online match and `BoardGraphLogic.Update` before the board binds (baseline for the unnamed default-key path; this tells you whether K3 is worth keeping). Scenario B: a loop of `Services.Get<T>()` (default key, custom-key, named) and `Services.GetModel<M>()` for 300 frames. Expect 0 B for the Services calls after K1-K3. Run once on Mono and once on IL2CPP to settle the K3 question. Use Deep Profile only to find callers.
- Memory Profiler is not needed for this scope (no retained-object changes).
