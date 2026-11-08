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
   When no live editor is reachable, automatically launches Unity in batch mode (`-batchmode -nographics`) using `unity run` with raw runner flags (`-runTests -testPlatform <Mode> -testResults <XmlPath> -testCategory <Categories> -assemblyNames <Assemblies>`).

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
| `-ListOnly` | `switch` | `$false` | Discovers and prints test counts by assembly and fixture without executing tests. |
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
