# Agent Instructions

Development guide for agents working **on** the SavableObservable (MMVC) framework itself, package `com.evialdev.savableobservable` (Unity 6000.5+). Most docs in this repository are written for framework **users**. This file is the short development entry point and links only the user docs that are also useful when changing the framework.

## Repository Map

| Path | What it is |
|---|---|
| `Packages/com.evialdev.savableobservable/Runtime/` | Framework runtime code (the product). |
| `Packages/com.evialdev.savableobservable/Editor/` | Inspector drawers, `MMVCExecutionOrderSynchronizer`. |
| `Packages/com.evialdev.savableobservable/Tests/{Editor,Runtime,Shared}/` | EditMode tests, PlayMode tests, reusable contract tests (`Shared/` is shipped to consumers). |
| `Packages/com.evialdev.savableobservable/Samples/` | Sample triad and `CommandSettings` sample used by contract tests. |
| `Packages/com.evialdev.savableobservable/Tools/` | Universal test runner (`Invoke-UnityTestRunner.ps1`, `Runner/*.ps1`). |
| `Tools/` | Repo wrapper `Invoke-UnityTests.ps1` and runner self-tests `Test-RunnerUnitTests.ps1`. |
| `mmvc-unity-skill/` | `mmvc-unity` skill for consumer projects (`SKILL.md`, `references/`). |
| `mmvc-kilo-plugin/` | `mmvc-guard` Kilo plugin for consumer projects (TypeScript, own `npm` tests). |
| `.kilo/plans/` | Past implementation plans (history and design rationale). |

`Library/`, `Temp/`, `Logs/`, `UserSettings/`, `*.csproj`, `*.sln` and `.kilo/worktrees/` are generated or local. Never edit or commit them.

## Documents Worth Reading

Read these before changing the matching area. Do not duplicate their content here or in plans; link to them.

| Document | Read it for |
|---|---|
| [README.md](Packages/com.evialdev.savableobservable/README.md) | The architectural rules and public behavior: one MMVC unit per GameObject, Logic -> Model -> Presenter data flow, change-notification semantics, sync-on-bind, command-aware `[AutoBind]`, `[ClickCommand]`, `ListenerSet`, service locator duplicate rule. This is the behavior contract. |
| [UnityLifecycle.md](Packages/com.evialdev.savableobservable/Documentation~/UnityLifecycle.md) | Execution order, `ServiceLocator` two-pass discovery, `EnsureInitialized()` / `SetListeners` mechanics. Required before touching initialization or binding. |
| [RuntimeObjects.md](Packages/com.evialdev.savableobservable/Documentation~/RuntimeObjects.md) | Inactive prefabs, pooling, `AddComponent` limits. Required before touching lifecycle or provisioning code. |
| [Testing.md](Packages/com.evialdev.savableobservable/Documentation~/Testing.md) | Test runner modes, parameters, contract tests, live UI assertions, test suite map. |
| [CHANGELOG.md](Packages/com.evialdev.savableobservable/CHANGELOG.md) | What changed and why; add an entry for every user-visible change. |
| [mmvc-unity-skill/SKILL.md](mmvc-unity-skill/SKILL.md) and [references/](mmvc-unity-skill/references/) | The rules consumer agents are told to follow. Must stay consistent with the README. |
| [mmvc-kilo-plugin/README.md](mmvc-kilo-plugin/README.md) | Guard plugin rules (`MMVC001`..`MMVC005`), install, tests. Only for plugin work. |
| [.kilo/plans/](.kilo/plans/) | Design history. Search it before redesigning something that looks deliberate. |

## Core Rules (do not break)

- **Data flow**: input -> Logic -> Model -> notification -> Presenter. Logic never gets, stores or calls a Presenter. Presenters and Models never implement `ISharedSingleton`. `[ObservableHandler]` is render-only and never calls Logic.
- **One MMVC unit per GameObject**: one Model, at most one Logic, at most one Presenter, optional Loader. `SetListeners` rejects cross-GameObject binding.
- **Generic constraints stay strict**: `M : BaseObservableDataModel`, `P : BaseObservablePresenter<M>`, `LO : BaseLogic`.
- **No execution-order dependency** on models, loaders or logic: they initialize lazily through `GetModel()`. Only `ServiceLocator` (`-200`) and `BaseObservablePresenter<M>` (`-150`) have explicit order.
- **Static state and domain reload**: Play Mode runs with domain reload disabled. Every new static in `Runtime/` or `Editor/` follows Unity 6.5 `Unity.Scripting.LifecycleManagement`: `[NoAutoStaticsCleanup]` for immutable caches or locks, `[AutoStaticsCleanup]` plus a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset for mutable state. Verify with two consecutive Play Mode runs without recompiling.
- **Framework stays save-system agnostic**: loaders do not implement any specific save interface.
- **Same-value notification is silent by default**; `[NotifyOnSameValue]` and `ForceNotify()` are the only opt-ins. Do not change this without an explicit decision.
- **IL2CPP**: reflection-based bindings must remain stripping-safe or document `[Preserve]` requirements. Do not claim allocation-free bindings.
- **Never edit** `Library/PackageCache/com.evialdev.savableobservable@*`. Edit `Packages/com.evialdev.savableobservable/`.

## Build and Test

Tests are the only verification. A change is not verified until they ran.

```powershell
# All framework tests (EditMode + PlayMode + self-test)
pwsh -File Tools/Invoke-UnityTests.ps1 -Suite All

# Targeted
pwsh -File Tools/Invoke-UnityTests.ps1 -Suite EditMode -Filter CommandAutoBindTests
pwsh -File Tools/Invoke-UnityTests.ps1 -Suite PlayMode -Filter CommandBindingPlayModeTests

# Discover tests without running
pwsh -File Tools/Invoke-UnityTests.ps1 -ListOnly

# Runner scripts themselves (only when changing Tools/ or Tools/Runner/)
pwsh -File Tools/Test-RunnerUnitTests.ps1

# Guard plugin (only when changing mmvc-kilo-plugin/)
cd mmvc-kilo-plugin; npm ci; npm test; npm run typecheck
```

- The runner uses a live Unity Editor when one is open (fast, seconds) and falls back to batch mode otherwise. Details and parameters: [Testing.md](Packages/com.evialdev.savableobservable/Documentation~/Testing.md).
- Add or update tests with every behavior change: EditMode in `Tests/Editor/`, PlayMode in `Tests/Runtime/`, shared contracts in `Tests/Shared/`. Regressions need a test that fails without the fix.
- Report what actually ran (suite, counts, failures). Skipped suites, timeouts and "source review only" are not passes.
- Use the `unity-cli` skill for Editor operations, builds and scene work instead of guessing commands.

## When Code Changes, Update Tests

Every code change ends with tests that match it. Decide before editing, not after.

| You changed | Add or update | Run |
|---|---|---|
| Binding, `[AutoBind]`, `[ObservableHandler]`, `Observable`, `UIAdapters` | `Tests/Editor/` (`ObservableBindingTests`, `CommandAutoBindTests`, `UIAdapterTests`, ...) | `-Suite EditMode -Filter <Fixture>` |
| `[ClickCommand]`, `ListenerSet` | `ClickCommandTests`, `ListenerSetTests` | `-Suite EditMode -Filter <Fixture>` |
| `ObservableVariable`, `ObservableList`, change notification | `ObservablePrimitivesTests`, plus the same-value contract in `Tests/Shared/` | `-Suite EditMode` |
| Lifecycle, `ServiceLocator`, init order, prefabs, pooling, domain reload | `Tests/Editor/` lifecycle fixtures and `Tests/Runtime/` PlayMode tests | `-Suite All` (PlayMode is required here) |
| Model persistence, `LoadDataFromModel`, `[NonSaved]` | `ModelPersistenceTests` | `-Suite EditMode -Filter ModelPersistenceTests` |
| `Tests/Shared/` contracts or `MmvcLiveUi*` helpers | Contract tests and the samples that use them (`SampleMmvcContractTests`, `CommandSettingsContractTests`) | `-Suite All` |
| `Tools/` or `Tools/Runner/` scripts | `Tools/Test-RunnerUnitTests.ps1` | `pwsh -File Tools/Test-RunnerUnitTests.ps1` |
| `mmvc-kilo-plugin/` | `mmvc-kilo-plugin/tests/` | `npm test; npm run typecheck` |

- **Bug fix**: write the failing test first, confirm it fails without the fix, then fix.
- **Intentional behavior change**: update the existing tests that encoded the old behavior in the same change and say why in the `CHANGELOG.md` entry. Never weaken or delete a test just to make it pass.
- **`Tests/Shared/` is shipped to consumers**: a changed or new contract can fail consumer projects. Treat it as a public API change and record it in `CHANGELOG.md`.
- **New public API or attribute**: needs an EditMode test, plus a PlayMode test if it depends on frame timing, destruction order or Play Mode lifecycle.
- **Samples**: if a sample triad changes, re-run the contract tests that use it.
- The fixtures named above are the ones present now. Find the current ones with `-ListOnly` before relying on a name.
## Keeping Docs in Sync

When user-visible behavior changes, update in the same change:

1. `README.md` (and `Documentation~/*.md` if lifecycle, pooling or testing is affected).
2. `CHANGELOG.md` (and bump `package.json` version only when the user asks for a release).
3. `mmvc-unity-skill/SKILL.md` and `references/` so consumer agents get the same rules.
4. `mmvc-kilo-plugin/` only if a new lint rule or skill-load behavior is needed (rules must be syntactically provable; see its README).

Docs are user-facing. Keep them accurate and short, put development-only notes in this file.

## Code Navigation

- Use the CodeGraphContext MCP (`find_code`, `analyze_code_relationships`) for symbol lookups, callers and hierarchy instead of grep. The graph for this repository is `unity_savableobservable` (set in `.kilo/kilo.json`). If results are empty, check spelling, try `fuzzy_search`, refresh with `add_code_to_graph` for this repository root, then retry once. Grep is fine for strings, markdown, JSON and `.asmdef` files.
- Before changing a public type or method, check its callers and subclasses, including `Samples/` and `Tests/`.
- Graph limits for C#: property bodies, lambdas and method groups produce no call edges. Zero callers does not prove dead code; confirm with a content search.
- Read only the line ranges you need. Delegate wide exploration to the `explore` subagent.

## Working Rules

- Stop and ask the user when a design choice is unclear, the plan conflicts with the code or the docs above, or a behavior change is not covered by an existing decision. Do not guess or leave `TODO`s.
- Keep changes minimal and consistent with the surrounding code. No new dependencies in the package without asking (it depends only on `com.unity.ugui` and `com.unity.textmeshpro`).
- Put throwaway scripts in the OS temp directory (`C:\Users\alexx\AppData\Local\Temp\kilo`), not in the repository.
- Plans go to `.kilo/plans/`. Reference this file and the docs above instead of copying them.
- Project config for Kilo lives in `.kilo/` (commands, agents, `kilo.json`). Do not use `.kilocode/` or `.opencode/`.
- Commit only when asked. Never commit generated Unity folders or `node_modules`.
