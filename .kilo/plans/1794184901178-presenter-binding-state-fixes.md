# Presenter binding state fixes (Failure Modes 1 & 2)

## Verdict
Both are real but narrow. Neither breaks the normal flow (scene start, prefab spawn, pooling with no manual unbind). Both are cheap to fix and make the documented contract true, so fix them rather than only documenting them.

| # | Issue | Severity | Why fix |
|---|---|---|---|
| 2 | Reentrant bind during `InitializePresenter()` | Medium | Breaks the contract in `BaseObservablePresenter.cs:59-62` ("runs BEFORE SetListeners"). Real effect: `SubscribeAutoBindHandler` reads the UI field **once at bind time** for the UI->Model listener (`Observable.cs:676-690`). If `InitializePresenter()` calls `GetLogic().GetModel()` (`ObservablePresenterWithLogic` makes this natural) and *then* assigns/creates a UI control, that control's two-way listener is silently never attached. |
| 1 | `RemoveListeners` leaves `_isBound = true` | Low | Only matters when unbinding then expecting `EnsureInitialized()` to rebind (e.g. pool release/acquire). Terminal uses in `OnDestroy` (`SamplePlayerPresenter.cs:41-46`) are unaffected. State flag disagreeing with real state is still a latent trap. |

## Fix 2 – defer bind while initializing
`Runtime/BaseObservablePresenter.cs`, `EnsureInitialized()`:
- Add at top: `if (_initState == InitState.Initializing) return;`
- Effect: nested call from `BaseLogic.GetModel()` (`BaseLogic.cs:86-101`) returns without binding; the outer `EnsureInitialized()` binds after `InitializePresenter()` finishes. Model is still returned to the caller with fields initialized.
- `BaseLogic._presenterInitialized` becomes `true` in the nested call; acceptable because the outer frame completes binding. If `InitializePresenter()` throws, `BaseLogic` already resets its flag only on its own catch — verify the nested path does not throw; outer exception resets `_initState` (existing behavior).
- Explicit `Observable.SetListeners(presenter, model)` during initialization still binds (loader path unchanged).

## Fix 1 – keep `_isBound` truthful
- Add internal interface in Runtime, e.g. `internal interface IPresenterBindingState { void MarkUnbound(); void MarkBound(); }`, implemented by `BaseObservablePresenter<M>` (sets `_isBound`).
- `Observable.RemoveAllSubscriptions(model, subscriber)` (`Observable.cs:904`): if `subscriber is IPresenterBindingState s` -> `s.MarkUnbound()`.
- `Observable.SetListenersCore` (`Observable.cs:437`): after successful wiring, `MarkBound()` so explicit rebinds (loader) are reflected and a later `EnsureInitialized()` stays a no-op.
- Do not touch `CleanupSubscriptions` (model destruction; presenter is on same GameObject).

## Tests (EditMode, `Tests/Editor/PrefabAndPoolingLifecycleTests.cs` or `ObservableBindingTests.cs`)
1. Presenter (`ObservablePresenterWithLogic`) whose `InitializePresenter()` calls `GetLogic().GetModel()` then assigns a `Toggle` `[AutoBind]` field: after `EnsureInitialized()`, toggling UI updates model; handler fires exactly once per change.
2. Same presenter: assert `InitializePresenter` ran before first handler registration (e.g. record `HandlerCount` of an observable inside `InitializePresenter` == 0 for managed handlers).
3. `EnsureInitialized()` -> `RemoveListeners(model, presenter)` -> change model: no handler -> `EnsureInitialized()` -> change model: handler fires exactly once, AutoBind text updates, two-way works.
4. `EnsureInitialized()` then `Observable.SetListeners(presenter, model)` then `EnsureInitialized()`: still exactly one invocation per change (no double bind).
5. Run existing suites: `.\Packages\com.evialdev.savableobservable\Tools\Invoke-UnityTestRunner.ps1 -ProjectPath . -Suite All` (all must stay green, especially `PrefabAndPoolingLifecycleTests`, `MmvcThreePathPlayModeTests`, `MmvcPrefabPlayModeTests`).

## Docs follow-up
Architecture docs describe current behavior only. No bug lists, failure-mode write-ups, "fixed in" notes, or source line references. Bug history goes to `CHANGELOG.md` only.

`Documentation~/UnityLifecycle.md`:
- Rename section 8 `Limitations, Pitfalls & Source-Reviewed Failure Modes` -> `Limitations & Pitfalls`.
- Delete the whole `### Source-Reviewed Failure Modes (No Runtime Reproduction Claimed)` subsection (Failure Mode 1 and 2, including the `GetLogic().GetModel()` remedy).
- Section 2 (`Core Hook Mechanics`): state the contract plainly — `InitializePresenter()` always completes before `SetListeners`; nested `EnsureInitialized()`/`logic.GetModel()` calls during `InitializePresenter()` return the model without binding; `RemoveListeners` marks the presenter unbound so `EnsureInitialized()` rebinds. Drop any "normal non-reentrant path" qualifiers that existed only because of the bug.
- Keep: no-replay limitation, inactive-GameObject pitfall.

`Documentation~/RuntimeObjects.md`:
- Rename section 6 `Failure Modes & Pitfalls with Runtime Objects` -> `Pitfalls with Runtime Objects`.
- Delete item 1 (`Observable.RemoveListeners` Breaks `EnsureInitialized`); renumber the rest.
- Quick Summary line 12: replace "Do not call `RemoveListeners` during normal pooling reuse." with "Bindings can stay alive across pool cycles; if you unbind on release with `Observable.RemoveListeners`, `EnsureInitialized()` rebinds on checkout."
- Pooling Principles: add the same optional unbind/rebind note.

Search both docs (and README) for leftover `_isBound`, `Failure Mode`, `Source-Reviewed`, `No Runtime Reproduction` and remove them.

`CHANGELOG.md` (Unreleased, Fixed):
- Nested `EnsureInitialized()` during `InitializePresenter()` (e.g. via `GetLogic().GetModel()`) no longer binds listeners before initialization completes.
- `Observable.RemoveListeners` now resets presenter binding state so `EnsureInitialized()` rebinds.

## Order
1. Fix 2, Fix 1 in runtime.
2. Add tests; run full suite green.
3. Only then apply doc edits (docs must not claim behavior the code lacks).

## Out of scope
- Initial value replay on bind (behavior change; separate decision).
- Loader notifications during `LoadDataFromModel` (separate).
