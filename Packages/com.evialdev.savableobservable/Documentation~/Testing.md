# Testing Guide & Universal Unity Test Runner

SavableObservable provides a complete unattended testing suite and runner designed for fast inner-loop local development and robust CI/CD validation.

---

## 1. Universal Modular Test Runner

The runner script is located at:
`Packages/com.evialdev.savableobservable/Tools/Invoke-UnityTestRunner.ps1`

### Execution Modes
1. **Live Editor (Fast Inner Loop):**
   Connects to an open Unity Editor instance via Unity Pipeline CLI.
   - Executes tests through `mmvc_run_tests_unattended`.
   - Bypasses slow editor startups (executes in 2-5 seconds).
   - Automatically handles domain reloads, dirty scene backups, and transient PlayMode recovery.
2. **Batch Mode (CI / Headless):**
   When no live editor is reachable, automatically launches Unity in batch mode through `unity test <project> --mode <Mode> --report-format nunit --output <XmlPath>` (Unity CLI 1.0.0-beta.17 or newer).
   - `--output` is a **file** path. A pre-created directory fails with `TEST_RESULTS_MISSING` (exit 6).
   - Name filters map to `--filter` (`;` separated, `!` negates).
   - Category and assembly filters are forwarded to the editor after the `--` separator: `-- -testCategory <Categories> -assemblyNames <Assemblies>`.
   - Batch self-tests run `unity run --log-file <log> -- -executeMethod <Method>`; the `RESULT:` line is read back from the Editor log because `unity run` does not forward it to stdout.

---

## 2. Runner Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `-ProjectPath` | `string[]` | *Mandatory* | One or more Unity project paths (semicolon-separated or array). |
| `-Suite` | `string` | `'All'` | Test suite to execute: `'EditMode'`, `'PlayMode'`, `'SelfTest'`, or `'All'`. |
| `-Filter` | `string` | `''` | Substring or regex filter for test names or fixtures. |
| `-IncludeCategories` | `string[]` | `@()` | Categories to explicitly include (e.g. `Quick`, `Visual`, `LiveUi`). |
| `-ExcludeCategories` | `string[]` | `@()` | Categories to exclude (e.g. `Online`, `Flaky`). |
| `-ExcludeAssemblies` | `string[]` | `@()` | Test assemblies to exclude (e.g. `ScenarioTests`). |
| `-ListOnly` | `switch` | `$false` | Discovers and prints test counts by assembly and fixture without executing tests. With a live Editor it counts NUnit test **cases** (`unity command test list`: `[TestCase]`/`[TestCaseSource]` expansions and inherited tests included, `[Explicit]` excluded), matching a run's `Summary.Total`; `-Filter` accepts `;`-separated patterns with `!` negation. `test-inventory.json` contains `Fixtures[]` and the per-case `Tests[]`. Without an Editor it falls back to counting `[Test]` methods, which under-counts parameterized tests. |
| `-SelfTestCommand` | `string` | `''` | Custom CLI command for live self-tests (e.g. `mmvc_selftest --mode all`). |
| `-SelfTestMethod` | `string` | `''` | Static C# method for batch self-tests. |
| `-OutputDirectory` | `string` | `Temp` | Directory for NUnit XML and markdown/JSON summary reports. |
| `-TimeoutSeconds` | `int` | `900` | Global timeout per suite execution. |

---

## 3. Consumer Project Thin Wrapper

Consumer projects should not copy the 1200+ line runner script. Instead, copy the thin wrapper from:
`Packages/com.evialdev.savableobservable/Samples~/ConsumerWrapper/Invoke-UnityTests.ps1`
into the consumer repository's `Tools/` directory.

The wrapper automatically resolves the package runner from:
1. `Packages/com.evialdev.savableobservable` (embedded or local file path).
2. `Packages/manifest.json` (relative `file:` references).
3. `Library/PackageCache/com.evialdev.savableobservable*`.

Usage:
```powershell
# Run all EditMode tests
.\Tools\Invoke-UnityTests.ps1 -Suite EditMode

# Run specific categories excluding multiplayer
.\Tools\Invoke-UnityTests.ps1 -Suite PlayMode -IncludeCategories Quick,LiveUi -SkipMultiplayer

# Discover test inventory without executing
.\Tools\Invoke-UnityTests.ps1 -ListOnly
```

---

## 3a. Contract Tests for Change Semantics

`MmvcTriadContractTestsBase<TModel, TLogic, TPresenter>` (namespace `SavableObservable.Tests.Contracts`) includes the contract `Contract_EqualAssignment_IsSilent_UnlessNotifyOnSameValue`. For every `ObservableVariable<T>` field of the model it assigns the field its own current value and asserts:

- the assignment is **silent** by default (no notification, `Value` / `PreviousValue` unchanged), and
- it notifies exactly once when the field opts in with `[NotifyOnSameValue]` (or its `NotifyOnSameValue` flag is on).

`ObservableList<T>` fields are skipped: a list notifies on every mutation by design. Consumer projects that already derive a contract class from `MmvcTriadContractTestsBase` pick this contract up automatically, so a model that relied on "assign the same value to refresh the view" fails here until it uses `ForceNotify()` or `[NotifyOnSameValue]` (see "Change notification semantics" in the README).

`ObservableAccess` (same namespace) resolves a field's tracked change event by its type (`OnValueChanged` for variables, `OnChanged` for lists) for custom contract code.

### Contract: AutoBind text targets show the model value right after bind

The same base includes `Contract_AutoBindTextTargets_ShowTheModelValueRightAfterBind`. For every `[AutoBind]` field of a `TMP_Text` type bound to an `ObservableVariable<T>`, it binds the presenter (`EnsureInitialized()`) and asserts that the text equals the model value (`ToString()`), without any manual first paint (`ObservableSettings.SyncOnBind`, default on). A target the unit leaves unassigned gets a temporary TMP child, so a naked instantiation is covered. A presenter that overwrites an AutoBind target in `InitializePresenter()`, or a model whose default differs from the text the scene shows, fails here. The contract is ignored when `SyncOnBind` is off or the presenter has no such target. Consumer projects that derive a contract class from `MmvcTriadContractTestsBase` pick it up automatically.

---

## 4. Live UI Assertions & Audit (`MmvcLiveUiAssert`)

Located in namespace `SavableObservable.Tests.Contracts`:

### Drivers (User Simulation)
Simulate realistic user interactions rather than directly setting model values:
- `MmvcLiveUiAssert.Click(button)` - asserts enabled and interactable, invokes click, yields 1 frame.
- `MmvcLiveUiAssert.SetToggle(toggle, value)` - asserts interactable, sets `isOn`, yields 1 frame.
- `MmvcLiveUiAssert.SetSlider(slider, value)` - asserts interactable, sets `value`, yields 1 frame.
- `MmvcLiveUiAssert.Type(input, text)` - asserts interactable, sets text, yields 1 frame.

### Readers (Visual State)
Inspect UI components safely:
- `MmvcLiveUiAssert.Text(tmpOrText)`
- `MmvcLiveUiAssert.SpriteName(image)`
- `MmvcLiveUiAssert.Alpha(canvasGroupOrGraphic)`
- `MmvcLiveUiAssert.IsEnabled(behaviour)`
- `MmvcLiveUiAssert.Interactable(selectable)`

### Model-to-View & View-to-Model Round Trip
```csharp
// Model to View
yield return MmvcLiveUiAssert.ModelToView(model.score, 42, () => presenter.scoreText.text, "42");

// View to Model
yield return MmvcLiveUiAssert.ViewToModel(
    MmvcLiveUiAssert.SetSlider(presenter.scoreSlider, 10f),
    model.score,
    10);
```

### Automated Scene / Prefab Audit (`MmvcLiveUiAudit`)
Audits all `[AutoBind]` fields across scene presenters:
```csharp
var report = new LiveUiAuditReport();
yield return MmvcLiveUiAudit.RunAudit(sceneRoot, report);
Assert.AreEqual(0, report.FailedCount, report.SummaryText());
```
Supports `[LiveUiAuditSkip("reason")]` and `LiveUiAuditOptions` for side-effecting controls.
