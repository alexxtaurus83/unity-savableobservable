# AttaxUnity: changes needed for the framework GC plan (K1-K4)

> **Before implementing, read `AGENTS.md` (project root) and follow it.**
>
> **Required for this plan:**
> - **`AGENTS.md`**: sections that apply: "Work Tiers" (Feature tier), "Finding Code", "MMVC Unit Shape", "Domain Reload Disabled", "Tests and Editor Tools", "Stop and Ask"
> - **`codegraphcontext`**: impact analysis: `MoveHintLogic.Settings`/`HintsOn`/`AdvancedOn`, `HapticService.IsHapticsEnabled`, `BoardGraphLogic.TryBindToManager`/`ForEachRegistered`, `OnlineMatchController.Update`; callers checked by content search in this plan, re-run `find_callers` before editing
> - **`unity-cli`**: verification: `attax_preflight --action save --expect edit` (`READY=yes`), recompile, `attax_wiring_audit --scope all`; tests per `TESTING.md` section 1
> - **`mmvc-unity`**: MMVC units touched: `MoveHint` (Logic only). All other items are "no change"
> - **`zengram-mcp`**: memories consulted: `attax-unity-mmvc-2-0`; to store after verification: `attax-unity-gc-framework-bump`
> - **`docs`**: consulted / to update: N/A (no behavior or workflow change; `docs/DEVELOPMENT_WORKFLOW.md` only if the Profiler step below is kept)
> - **Checklist**: N/A (Feature tier)

Companion plans (same `.kilo/plans/` folder of `unity-savableobservable`):
- Framework side: `1794410955965-gc-allocation-reduction.md` (K1-K4, kept as "KEEP").

---

## 1. Goal and scope

**Observable result:** AttaxUnity takes the framework GC changes (K1-K4) without a regression, and its own runtime code does not add avoidable lookups on top of them.

**Answer to "what must I change in Attax to comply": nothing is required.** K1-K4 are internal to `ServiceLocator.cs` and `Observable.cs`. No public type, attribute, method signature, lifecycle order, notification rule or serialized field changes, so no Attax source file has to change for the build to compile or behave the same. What remains in Attax is: a package bump, a test gate, and one small optional code edit (C1).

**Out of scope:** the command/listener adoption work (planned and executed separately), gameplay rules, AI search, Addressables, the rules engine repo, and client allocation sources unrelated to the framework (`AtaxxBoardMoveHelper.cs:188` `new List<VulnIssue>`, `AtaxxGameLogLogic.cs:33` `ToList`, `$"..."` logging). Those are listed in section 6 only so they are not mistaken for framework work.

## 2. Verified facts about the client (code, not assumption)

| Fact | Evidence |
|---|---|
| Package is consumed from git branch `feature/fix`, not a local path | `Packages/manifest.json:7`; lock entry `Packages/packages-lock.json:36` |
| Client uses **ZLinq** (`using ZLinq`), never `System.Linq`, in 8 runtime files | `ControlSys.cs:15`, `AtaxxAIEngine.cs:6`, `CsvLoader.cs:7`, `CellMetadataService.cs:8`, `AtaxxGameLogLogic.cs:10`, `StartPosition.cs:3`, `ScrollingLogView.cs:11`, `Extensions.cs:4`. The "LINQ" request therefore has no `System.Linq` target in Attax; ZLinq is a struct-enumerator library and is left alone |
| Attax tests do not touch framework internals (`_services`, `_commandMethodCache`, resolver methods) | content search over `Assets/` returned only locator creation via public API |
| Attax tests build a locator through the public test seam `Services.CaptureTestState()` / `SetTestLocator()` / `RestoreTestState()` | `MenuBackgroundLogicTests.cs:12-27`, `AttaxMmvcContractTests.cs:90-139` |
| `DelegatedCoverageTests` **pins 11 package tests by assembly, type and method name** (all in `ServiceLocator`/binding areas) | `Assets/Tests/EditMode/DelegatedCoverageTests.cs:25-37` |
| Unnamed `Services.Get<T>()` on a registered default key returns early (no allocation today); `Get`/`GetModel` with a custom `singletonName` and no name is the only path K1 changes | `ServiceLocator.cs:113-143` |
| Client `singletonName` use: `BoardGraphLogic` (`SquareSingletonName`, `StretchedSingletonName`), `BaseAISettingsLogic` (`DevSettings`, `BlueAISettings`, `RedAISettings`), screen units from `SceneSetupUtility.cs:41-52`. All client reads of those types are **named** except `BoardGraphLogic.ForEachRegistered` line 15 fallback, which runs only when the square graph is missing | grep of `Services.Get<...>(` call sites |
| `Services.GetModel<M>` callers: `HapticService.cs:9`, `MoveHintLogic.cs:69`, `PlayerSettingsLogic.cs:254`, `MenuBackgroundLogic.cs:55-56,76-77,86,91`, `OnlineService.cs:72` | grep |

## 3. Mandatory steps (no source edit)

1. **Wait for the right package commit.** The framework work (K1-K4) lands on `unity-savableobservable` and is pushed to branch `feature/fix`. This plan runs first; the Phase 3 adoption plan is executed afterwards, on top of the bumped package.
2. **Bump** by letting Unity re-resolve the git package (Package Manager -> refresh, or `unity-package-management`). Do not hand-edit `packages-lock.json`, and never edit `Library/PackageCache/com.evialdev.savableobservable@*`.
3. **Recompile: 0 errors, 0 warnings.** Expected: no Attax file needs editing. If anything fails to compile, STOP and ask: it means a framework change broke the "no API change" rule.
4. **Test gate** (`TESTING.md` section 1, "Any C# change" and "Gameplay, board, UI, menu or flow" rows):
   - `pwsh -File Tools/Invoke-TestBatches.ps1 -All` (runs the package tests, including the 11 pinned by `DelegatedCoverageTests`)
   - `pwsh -File Tools/Invoke-TestBatches.ps1 -Fixture Assets.Tests.EditMode.AppStateNavigationTests`
   - `-Fixture Assets.Tests.EditMode.MenuBackgroundLogicTests`
   - `-Fixture Assets.Tests.EditMode.MoveHintLogicGuardTests`
   - `-Category MmvcContract` (`AttaxMmvcContractTests`)
   - `-All -IncludeScenarios` plus `-Fixture Assets.Tests.Scenarios.MenuNavigationTests`
   - `unity command attax_wiring_audit --scope all` -> `WIRING=ok`
   - Two consecutive Play Mode runs without recompiling (domain reload is disabled).
   - Expected result: counts identical to the pre-bump baseline recorded in step 0 below. Any difference is a regression to investigate, not to "fix" in Attax.
0. **Record the baseline first** (before the bump): run the same commands on the current package commit and write down suite, counts and failures. Report what actually ran; skipped suites and "source review only" are not passes.

### Compatibility constraints the framework side must keep (checked here, also written into the framework plan)

- **Do not rename or remove the 11 package tests pinned by `DelegatedCoverageTests`** (`MMVCRestrictionsTests.ServiceLocator_*` x7, `ObservableBindingTests.SetListeners_ReplacesManagedBindings_ButKeepsManualSubscriptions`, `TrackedActionAndForeignSubTests.SetListeners_Rebind_PreservesManualSubscriptions...`, `MmvcThreePathPlayModeTests` x2). New tests are added next to them, not instead of them. Renaming one makes Attax fail with "no longer exists".
- **K3 must stay an instance field** on `ServiceLocator` (cleared in `OnDestroy`), not a static. Attax swaps locators per test through `Services.SetTestLocator`; a static cache would leak between tests and between Play sessions.
- **The `Services` test seam (`TestState`, `CaptureTestState`, `SetTestLocator`, `RestoreTestState`) stays public and unchanged.**
- **Ambiguity and not-found behavior stays identical**: `AppStateNavigationTests.cs:281-341` assert exact results (named lookup does not fall back, `null` after destroy, `Duplicate registration of` log).

## 4. Client code changes

### C1 (recommended, optional): read the settings model once in `MoveHintLogic`

File: `Assets/Scripts/Ataxx.Unity/MoveHint/MoveHintLogic.cs:69-72`. Type: Logic. Risk: low.

Today `AdvancedOn` calls the `Settings` property three times (`AdvancedOn` -> `HintsOn` -> `Settings` twice, then `Settings` again). Each call is a full `Services.GetModel<PlayerSettingsDataModel>()` scan of every registered service. Before K2 each call also allocates a `List<string>`. After K2 it stays a CPU cost only. The callers are event handlers (`MoveHintLogic.cs:108, 128, 281, 313`), not `Update`.

Replace the two properties (keep `Settings`, it is used at `:234`):

```csharp
private PlayerSettingsDataModel Settings => Services.GetModel<PlayerSettingsDataModel>();

private bool HintsOn {
    get { var s = Settings; return s != null && s.showMoveHints.Value; }
}

private bool AdvancedOn {
    get { var s = Settings; return s != null && s.showMoveHints.Value && s.advancedHints.Value; }
}
```

Behavior is identical: `AdvancedOn` is false whenever hints are off or the model is missing. One lookup instead of three. Do **not** cache the model in a field: the settings unit can be destroyed and replaced (test locators, scene reload), and a stale reference would silently read dead data. The existing cached-property idiom in the client (`AtaxxActionExecutor.cs:24`) re-resolves when `== null`, which is fine, but it is not needed for this gain.

Tests: `MoveHintLogicGuardTests` must still pass. Add one EditMode case to `MoveHintLogicGuardTests.cs`: with hints off `AdvancedOn` path does not run (observable through `OnChipSelected`), with hints and advanced on it does. If the private properties make that hard to observe, say so and rely on the existing guard tests plus the `MoveHint` scenario; do not widen visibility only for the test.

### No change (checked, each with the reason)

| Item | Why no edit |
|---|---|
| `HapticService.cs:7-12` `IsHapticsEnabled` | One `GetModel` per haptic call. After K2 there is no allocation. Caching the model has the stale-reference risk above. Haptics fire per move, not per frame |
| `BoardGraphLogic.cs:202-214` `Update` -> `Services.Get<AtaxxGameManagerLogic>()` | Registered default key, early return, no allocation. It polls only until the first bind (`isBound`), a few frames at startup, so a cache saves nothing measurable. Do **not** disable the component after binding to stop `Update`: `ResetGraph`/`BindToBoardCells` are re-entered from match flow and a disabled logic is a lifecycle risk for no GC gain |
| `OnlineMatchController.cs:170-235` `Update` | Early-returns when no match session and when `_pending.Count == 0`; `Services.Get` runs only while an action is queued. The `onActionComplete: () => {...}` closure at `:211` allocates once per submitted action, not per frame |
| `BoardGraphLogic.ForEachRegistered(Action<...>)` call sites (`AtaxxMatchFlow.cs:219,355,505,573`, `AtaxxAnimations.cs:135`, `AtaxxAnimations.Moves.cs:97,484`) | Each capturing lambda allocates a closure and delegate per call, once per move or reset. Removing it means a larger API change (pass the arguments through a struct or add overloads) for roughly one small object per move. Not worth the churn |
| `MatchTimerLogic.Update`, `BaseTimerLogic.Update`/`Tick`, `OnlineTurnTimerLogic.Tick` | Write the model only when the value changes (equal assignment is silent by contract); `GetModel()` is cached by `ComponentCache`. No allocation found |
| `AppStatePresenter.cs:18-22` `Update` | `Input.GetKeyDown` plus a cached `GetLogic()`. No allocation |
| `MenuBackgroundPresenter.cs:276-308` `Update` | Loops over arrays and `ObservableList` by index; `GetMaxBoltEndpointError` uses `foreach` over `List<T>` (struct enumerator). No allocation found in this method. `MenuBackgroundLogic.SimulateStep` was **not** audited line by line; profile it (section 5) before claiming it is clean |
| ZLinq usage (8 files) | See section 2. It is a struct-enumerator library, not `System.Linq`; touching it is outside the request |

## 5. Verification of the GC effect (Attax side)

Use a **Development Player** (not the Editor): `ObservableList` also takes `ToArray()` snapshots under `#if UNITY_EDITOR` (`ObservableList.cs:55-79, 413-417`), so Editor numbers overstate list cost.

1. **Baseline and after, same scenario, same device:** Profiler -> CPU Usage -> Hierarchy, `GC.Alloc` column, 300+ frames each:
   - A. Main menu idle (menu background running): expect no per-frame `GC.Alloc` from the framework; record `MenuBackgroundPresenter.Update` and `UpdateSimulation`.
   - B. Human vs AI match, select chips repeatedly with advanced hints on (exercises `MoveHintLogic.AdvancedOn`, C1) and `HapticService` (exercises `GetModel` per call).
   - C. Online match with queued actions (exercises `OnlineMatchController.Update`).
   - D. Open Settings and drag a slider. Today the presenter wires these controls by hand, so this records a baseline before any later move to `Command` bindings (which would use the dropped H8 `MethodInfo.Invoke` path).
2. Expected after K2 (and C1): zero `GC.Alloc` attributed to `ServiceLocator.GetModel` frames. Expected after K1/K3: none from `ServiceLocator.Get` on the custom-key path. Any non-zero result means the framework change is incomplete; report it, do not patch it in Attax.
3. Deep Profile only to find callers of a non-zero marker (it adds its own allocations).
4. Memory Profiler is not needed (no retained-object change).
5. IL2CPP: repeat A and B once on an IL2CPP Development build. This settles the open K3 question (does `Type.Name` allocate per call).

## 6. Known client allocation sources outside this plan

Not caused by the framework and not changed here: `AtaxxBoardMoveHelper.cs:188` (`new List<VulnIssue>(...)` per capture), `AtaxxGameLogLogic.cs:33` and `CellMetadataService.cs:62,69` (`ToList` at match start), `AtaxxAnimations.cs:108` and `AtaxxFx*.cs` (`ToArray` snapshots of active sequences/particles), `OnlineTurnTimerLogic.Format` (string concatenation per displayed second), `$"..."` in logging. If wanted, they need their own plan with a Profiler baseline first.

## 7. Risks and open points

- **Risk: the optional C1 edit** changes nothing observable; skip it if you prefer zero client edits. The plan stands without it.
- **Open point (no user decision needed now):** whether K3 survives depends on the IL2CPP/Mono `Type.Name` measurement in section 5.5. If K3 is dropped, nothing in this plan changes.

### Rules for the implementing agent
- Follow this plan exactly; do not redesign it. For Large plans, work from `IMPLEMENTATION_CHECKLIST.md`.
- Find code with `codegraphcontext`, not grep (`AGENTS.md` "Finding Code"). Check callers, callees and subclasses before changing a symbol.
- If you are unsure, or the plan contradicts the code: STOP and ask the user. Do not guess or invent a workaround.
