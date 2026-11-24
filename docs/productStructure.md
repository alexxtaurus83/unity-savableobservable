# Product structure

## Product

`com.evialdev.savableobservable` is the MMVC (SavableObservable) framework package for Unity. Consumer projects use it as a Git UPM dependency; Flexible-save-system is a related persistence package.

## Related repositories

| Repository | Path | Relation |
|---|---|---|
| AttaxUnity | `F:\Unity_projects\AttaxUnity` | main consumer; receives the deployed guard plugin |
| AttaxAnaliser | `F:\github\repo\AttaxAnaliser` | Roslyn analyzers (`MMVC*`, `ATTAX*`, `TATTAX*`, `UNITY*`) and `Attax.AnalyzerCli` |
| Flexible-save-system | `F:\github\repo\Flexible-save-system` | related save-system package; the framework stays save-system agnostic |

## Repository map

| Path | What it is |
|---|---|
| `Packages/com.evialdev.savableobservable/Runtime/` | Framework runtime code (the product) |
| `Packages/com.evialdev.savableobservable/Editor/` | Inspector drawers, `MMVCExecutionOrderSynchronizer` |
| `Packages/com.evialdev.savableobservable/Tests/{Editor,Runtime,Shared}/` | EditMode tests, PlayMode tests, reusable contract tests (`Shared/` ships to consumers) |
| `Packages/com.evialdev.savableobservable/Samples/` | Sample triad and `CommandSettings` sample used by contract tests |
| `Packages/com.evialdev.savableobservable/Tools/` | Universal test runner (`Invoke-UnityTestRunner.ps1`, `Runner/*.ps1`) |
| `Tools/` | Repo wrapper `Invoke-UnityTests.ps1`, runner self-tests `Test-RunnerUnitTests.ps1` |
| `mmvc-unity-skill/` | `mmvc-unity` skill for consumer projects (`SKILL.md`, `references/`) |
| `mmvc-kilo-plugin/` | `mmvc-guard` Kilo plugin for consumer projects (TypeScript, own `npm` tests) |
| `.kilo/plans/` | Past implementation plans |
