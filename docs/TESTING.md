# Testing

Generic selection, evidence and writing rules: `F:\github\repo\Agents\baseTesting.md`; Unity stack rules: `F:\github\repo\Agents\stacks\unity.md`. Tests are the only verification; a change is not verified until they ran. Report what actually ran (suite, counts, failures); skipped suites, timeouts and "source review only" are not passes.

## Commands

```powershell
# All framework tests (EditMode + PlayMode + self-test)
pwsh -File Tools/Invoke-UnityTests.ps1 -Suite All
# Targeted
pwsh -File Tools/Invoke-UnityTests.ps1 -Suite EditMode -Filter CommandAutoBindTests
pwsh -File Tools/Invoke-UnityTests.ps1 -Suite PlayMode -Filter CommandBindingPlayModeTests
# Discover without running
pwsh -File Tools/Invoke-UnityTests.ps1 -ListOnly
# Runner scripts themselves (only when changing Tools/ or Tools/Runner/)
pwsh -File Tools/Test-RunnerUnitTests.ps1
# Guard plugin (only when changing mmvc-kilo-plugin/)
cd mmvc-kilo-plugin; npm ci; npm test; npm run typecheck
```

The runner uses a live Unity Editor when one is open (seconds) and falls back to batch mode otherwise; details and parameters are in the package's `Documentation~/Testing.md`. Use the `unity-cli` skill for Editor operations instead of guessing commands.

## When code changes, update tests

Decide before editing, not after. The fixtures named are those present now; find the current ones with `-ListOnly`.

| You changed | Add or update | Run |
|---|---|---|
| Binding, `[AutoBind]`, `[ObservableHandler]`, `Observable`, `UIAdapters` | `Tests/Editor/` (`ObservableBindingTests`, `CommandAutoBindTests`, `UIAdapterTests`, ...) | `-Suite EditMode -Filter <Fixture>` |
| `[ClickCommand]`, `ListenerSet` | `ClickCommandTests`, `ListenerSetTests` | `-Suite EditMode -Filter <Fixture>` |
| `ObservableVariable`, `ObservableList`, change notification | `ObservablePrimitivesTests`, plus the same-value contract in `Tests/Shared/` | `-Suite EditMode` |
| Lifecycle, `ServiceLocator`, init order, prefabs, pooling, domain reload | `Tests/Editor/` lifecycle fixtures and `Tests/Runtime/` PlayMode tests | `-Suite All` (PlayMode is required) |
| Model persistence, `LoadDataFromModel`, `[NonSaved]` | `ModelPersistenceTests` | `-Suite EditMode -Filter ModelPersistenceTests` |
| `Tests/Shared/` contracts or `MmvcLiveUi*` helpers | contract tests and the samples that use them (`SampleMmvcContractTests`, `CommandSettingsContractTests`) | `-Suite All` |
| `Tools/` or `Tools/Runner/` scripts | `Tools/Test-RunnerUnitTests.ps1` | `pwsh -File Tools/Test-RunnerUnitTests.ps1` |
| `mmvc-kilo-plugin/` | `mmvc-kilo-plugin/tests/` | `npm test; npm run typecheck`, then deploy to `F:\Unity_projects\AttaxUnity\.kilo\plugin\mmvc-guard.ts` |

- **Bug fix:** write the failing test first, confirm it fails without the fix, then fix. **Intentional behavior change:** update the tests that encoded the old behavior in the same change and say why in the `CHANGELOG.md` entry. Never weaken or delete a test just to pass.
- **`Tests/Shared/` is shipped to consumers:** a changed or new contract can fail consumer projects. Treat it as a public API change and record it in `CHANGELOG.md`.
- **New public API or attribute:** an EditMode test, plus a PlayMode test if it depends on frame timing, destruction order or Play Mode lifecycle. If a sample triad changes, re-run the contract tests that use it.

## Affected tests

```
node F:\github\repo\Agents\tools\affected-tests.mjs --json
```

Config `.agents/affected-tests.json`: `unity-asmdef` over the package assemblies plus `map` edges for `mmvc-kilo-plugin/` and `Tools/`. Exit `1` (a changed source file reached no test) means add a test or a `map` edge.
