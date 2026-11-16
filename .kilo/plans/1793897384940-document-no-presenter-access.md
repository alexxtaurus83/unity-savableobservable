# Logic must never access Presenters: document the rule and fix AttaxUnity violations

Two parts:
- Part A: document the rule in the framework repo (README and both skill copies). Repo: `F:\github\repo\unity-savableobservable`.
- Part B: fix the two violations found in `F:\Unity_projects\AttaxUnity\Assets\Scripts`.

Rule text (reuse verbatim everywhere in Part A):
> Logic never gets, stores, or calls a Presenter (no `GetComponent<XPresenter>()`, no `[SerializeField]` presenter field, no `Find*`). Logic writes Model observables; the Presenter reacts through `[AutoBind]` / `[ObservableHandler]`. To affect another MMVC unit, call that unit's Logic (`Services.Get<OtherLogic>()`) or work with its Model (`Services.GetModel<M>()`). This is a convention: C# cannot block `GetComponent`, so it is enforced by review only.

## Audit result: AttaxUnity `Assets/Scripts`

Method: grepped all `*.cs` for `GetPresenter`, `GetComponent*<...Presenter>`, `Find*<...Presenter>`, `Services.Get<...Presenter>`, serialized or public Presenter-typed fields, and any `Presenter` token inside `*Logic*.cs`. Then read each hit.

Scope: 25 Logic classes. 23 are clean. They only call `GetModel()` and `Services.Get<OtherLogic>()`. Presenters calling `GetLogic()` is the allowed direction.

Violations (both in Logic):

| # | File | Code | Verdict |
|---|---|---|---|
| 1 | `Ataxx.Unity/Graph/BoardGraphLogic.cs:22-23, 89` | Caches `GetComponent<BoardGraphPresenter>()` and calls `Presenter?.SetNodeVisual(row, col, owner, animate, default)` from `OnChipOwnerChanged`. | Real violation. It is a push into the view that bypasses the model. |
| 2 | `Ataxx.Unity/HelpModalLogic.cs:9` | `GetComponent<HelpModalPresenter>()?.SelectTab(index)` | Real violation, but dead code. Nothing calls `HelpModalLogic.SelectTab`: the grep found no C# callers, and no `m_MethodName: SelectTab` in scenes or prefabs. The presenter already wires its own tab buttons to its own `SelectTab`. |

Not violations (noted so nobody re-flags them):
- `PrefsModelLoader.cs:81` calls `GetComponent<BaseObservablePresenter<M>>()` to rebind after load. It is a Loader, and it mirrors framework `LoaderWithModel.cs:51`.
- `PlayerSettingsLogic` and `PlayerProgressLogic` use `GetComponent<...ModelLoader>()`. A Loader is not a Presenter.
- `StartMenuPresenter` is a plain `MonoBehaviour` named "Presenter" and uses `Services.Get<AppStateLogic>()`. It is clean.
- `Assets/Editor/.../BoardGraphSceneBuilder.cs:140` and `AttaxPlaytestCommands.cs:912` are editor tooling that does `GetComponent<BoardGraphPresenter>()`. They are out of the runtime rule and left alone.
- Comments only: `SharedScripts.cs:45`, `AtaxxAnimations.cs:33`.

## Part B: fix AttaxUnity

### B1. `BoardGraphLogic`: replace the presenter push with a model event
The Presenter already reacts to `eventToken` through `[ObservableHandler]` and a `switch` on `latestEvent.Kind`. Reuse that channel.

1. `BoardGraphDataModel.cs`
   - Add `BoardGraphEventKind.NodeVisual = 4`.
   - Add `public bool Animate;` to `BoardGraphMoveEvent`. Reuse `ToRow`/`ToCol` for the cell and `Actor` for the owner (the `PlayerColor`). Alternatively add `Row`/`Col`/`Owner` fields if readability matters. Pick one and document it in the struct.
2. `BoardGraphLogic.cs`
   - Delete the `presenter` field and the `Presenter` property (lines 22-23).
   - In `OnChipOwnerChanged`, replace `Presenter?.SetNodeVisual(...)` (line 89) with: write the `NodeVisual` event (cell, owner, animate) to `model.latestEvent`, then `model.eventToken.Value++`. This is the same publish pattern as `PublishMoveSegment`.
   - Check that the `speed` argument is still `default`. It was `default` before, so no behavior change.
3. `BoardGraphPresenter.cs`
   - Add `case BoardGraphEventKind.NodeVisual:` to `OnEventTokenChanged`. It calls `SetNodeVisual(row, col, owner, animate, default)`. `SetNodeVisual` stays public for the presenter's own use, and nothing outside calls it anymore. Consider making it `private`.
4. Risk checks:
   - **Event overwrite.** `latestEvent` is a single slot. The handler runs synchronously on `eventToken++`, so back-to-back writes are safe. Verify there is no re-entrant `eventToken` write inside the handler's call chain.
   - **Presenter not yet initialized.** `GetModel()` already triggers presenter init through `BaseLogic`, so `nodeViews` is built before the first event. `OnEventTokenChanged` early-returns when `graphFx == null` (line 168). The `NodeVisual` case must not sit behind that guard if it needs to work before `graphFx` exists, so handle `NodeVisual` before the `graphFx` null check, or confirm `InitializePresenter` creates `graphFx` first (it does, line 51).
   - **Initial paint.** `InitializePresenter` already repaints from `model.nodeOwners` (lines 53-60). The `NodeVisual` events from `BindToBoardCells(animate:false)` become redundant but harmless.
   - **Per-frame cost.** `BindToBoardCells` fires up to 49 events at startup. This is acceptable, and each is one switch case.
5. Tests, `Assets/Tests/EditMode/BoardGraphLogicTests.cs` (the `SetNodeOwner_...` test at line 81):
   - Extend it to assert that `SetNodeOwner` bumps `eventToken` and sets `latestEvent.Kind == NodeVisual` with the expected cell and owner.
   - The current tests run without a presenter, so they keep passing.

### B2. `HelpModalLogic`: remove the dead method
- Delete `SelectTab` from `HelpModalLogic.cs`. The class body becomes empty (`ModalWindowLogic<ModalWindowDataModel, HelpModalPresenter>` with no members). Tab selection is pure view state and stays in the presenter.
- Before deleting, re-run the grep over `Assets` (`*.cs`, `*.unity`, `*.prefab`, `*.asset`) for `HelpModalLogic.*SelectTab` and `.SelectTab(` to confirm there are no callers. This audit found none.
- Do not add a model field for the selected tab unless something outside the presenter needs to drive it.

### B3. Guard against regressions (optional, recommended)
Add one EditMode test in AttaxUnity (for example `Assets/Tests/EditMode/LogicDoesNotReferencePresenterTests.cs`). It uses reflection to find every concrete `BaseLogic` subclass. It fails if a Logic type declares an instance or static field, property or method parameter whose type derives from `BasePresenter<>` or implements `IObservablePresenter`. It cannot see inline `GetComponent<X>()` calls, so pair it with a source-text scan of `*Logic*.cs` for `GetComponent<\w*Presenter` and `Find\w*<\w*Presenter`. That scan would have caught both violations here. Keep it as a short allowlist-free test.

## Part A: document the rule (framework repo)

1. `README.md`
   - Logic section (~line 103): replace the one-line mention with the rule above and a short Do/Don't snippet. Don't: `GetComponent<PlayerPresenter>().Flash()`. Do: `GetModel().isHit.Value = true;` with the Presenter handling `isHit`.
   - "Cross-Component Access" (~line 141): change "from outside" to "including Logic on the same GameObject".
   - Note that the `P` parameter on `BaseLogic<M, P>` is only for auto-provisioning and presenter initialization, not an access handle.
   - State plainly that this is enforced by convention and review only.
2. `mmvc-unity-skill/SKILL.md`
   - Line 28: rewrite as "never call methods on a Presenter, from outside or from Logic". Add the data flow `Logic -> Model -> notification -> Presenter`.
   - Line 47: keep, and add "Logic included".
   - In "Debug a Feature", add: if Logic needs to make the view do something, add an observable field or event token to the Model and a handler in the Presenter. Use the `BoardGraphLogic` event-token pattern as the reference for one-shot view commands.
3. `mmvc-unity-skill/references/framework-guide.md`
   - Line 13: extend to cover Logic and the same-GameObject case.
   - Add a "Presenter access rule" subsection with the rule text and a Don't/Do example. Include the one-shot command pattern (an `int` event token plus a payload field on the Model, handled with `[ObservableHandler]`), because that is the case that tempted the AttaxUnity code.
4. `mmvc-unity-skill/references/examples.md`
   - Lines 116-120: remove the `GetComponent<ScorePresenter>()` and `presenter.RefreshDisplay()` call from `ScoreLoader.LoadState`. `LoaderWithModel.LoadDataFromModel` already rebinds the presenter.
   - Add an "Anti-pattern: Logic reaching into Presenter" example next to "Manual Subscriber Lifetime", plus the event-token alternative.
5. `CHANGELOG.md`: add a Docs entry for the clarified rule.
6. Installed copy `C:\Users\alexx\.skills\mmvc-unity\`: apply the same edits to `SKILL.md`, `references/framework-guide.md` and `references/examples.md`. First diff it against `mmvc-unity-skill/` to confirm it mirrors the repo, and keep the two in sync.

## Validation
- AttaxUnity: compile. Run the EditMode tests (`BoardGraphLogicTests`, `AppStateNavigationTests`, and the new B3 test if added).
- AttaxUnity: re-run the audit greps. `GetComponent<\w*Presenter` and `Presenter` inside `*Logic*.cs` must return only generic-parameter lines (`BaseLogic<M, XPresenter>`).
- AttaxUnity play check: board graph nodes still recolor with and without animation on capture, and on reset and new game. The Help modal still switches tabs through its own buttons.
- Framework repo: `rg "RefreshDisplay|GetComponent<.*Presenter" README.md mmvc-unity-skill` returns no example that violates the rule.
- Framework repo: the rule text is present in the README, the repo skill and the installed skill. Diff the repo skill against the installed skill for parity.

## Out of scope
- A compile-time or analyzer enforcement in the framework itself. `GetComponent` cannot be blocked.
- Editor tooling in `Assets/Editor` that touches presenters.
- Changing `PrefsModelLoader`'s presenter rebind.
