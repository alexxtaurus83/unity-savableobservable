# Agent Instructions

Guide for agents working **on** the SavableObservable (MMVC) framework, package `com.evialdev.savableobservable` (Unity 6000.5+). Read a linked file when its topic comes up.

## Shared base

- Base: `F:\github\repo\Agents\baseAgents.md` (read first), with `baseTesting.md`, `basePLANNING.md`, `baseDocs.md`, `baseSubagents.md` beside it.
- Stack: `F:\github\repo\Agents\stacks\unity.md`
- Personal: `F:\github\repo\Agents\personal\zengram.md`, `codegraph.md`, `mmvc.md`

## Project values

- Memory scope: N/A (the repository had none; its main consumer's scope is `attax-unity`)
- Code graph: `unity_savableobservable` (set in `.kilo/kilo.json`)
- Required skills (plan header lines): `codegraphcontext`, `unity-cli`, `zengram-mcp`
- Plans: `.kilo/plans/` (design history). Ignore `docsbackup/` (archive, not instructions).

## Core rules (do not break)

- **Data flow:** input -> Logic -> Model -> notification -> Presenter. Logic never gets, stores or calls a Presenter. Presenters and Models never implement `ISharedSingleton`.
- **One MMVC unit per GameObject:** one Model, at most one Logic, at most one Presenter, optional Loader. `SetListeners` rejects cross-GameObject binding.
- **Strict generic constraints:** `M : BaseObservableDataModel`, `P : BaseObservablePresenter<M>`, `LO : BaseLogic`.
- **No execution-order dependency** on models, loaders or logic (lazy through `GetModel()`). Only `ServiceLocator` (`-200`) and `BaseObservablePresenter<M>` (`-150`) have explicit order.
- **Domain reload is off.** Every new static in `Runtime/` or `Editor/` follows `Unity.Scripting.LifecycleManagement`: `[NoAutoStaticsCleanup]` for immutable caches, `[AutoStaticsCleanup]` plus a `SubsystemRegistration` reset for mutable state. Verify with two consecutive Play runs without recompiling.
- **Save-system agnostic:** loaders implement no specific save interface.
- **Same-value notification is silent** by default; `[NotifyOnSameValue]` and `ForceNotify()` are the only opt-ins.
- **IL2CPP:** bindings stay stripping-safe or document `[Preserve]`; do not claim allocation-free bindings.
- **Never edit** `Library/PackageCache/com.evialdev.savableobservable@*` (edit `Packages/com.evialdev.savableobservable/`) or generated folders (`Library/`, `Temp/`, `Logs/`, `UserSettings/`, `*.csproj`, `*.sln`).
- **Tests are the only verification:** every code change ends with matching tests ([`docs/TESTING.md`](docs/TESTING.md)).
- **After any framework change** stop and ask the user (`question`) whether `mmvc-kilo-plugin/` or the Roslyn MMVC rules in `F:\github\repo\AttaxAnaliser` need new rules ([`docs/ProjectSpecificDocs.md`](docs/ProjectSpecificDocs.md)).
- No new package dependencies without asking.

## Docs

| Doc | Read when you... |
|---|---|
| [`docs/ProjectSpecificDocs.md`](docs/ProjectSpecificDocs.md) | need the framework docs index and the doc/analyzer/guard sync rules |
| [`docs/TESTING.md`](docs/TESTING.md) | run or choose tests |
| [`docs/productStructure.md`](docs/productStructure.md) | find a folder |
