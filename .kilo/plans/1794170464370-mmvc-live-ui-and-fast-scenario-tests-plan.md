# Plan: deep live-UI tests, fast scenarios, universal runner, rewritten testing docs

Repos: Framework `F:\github\repo\unity-savableobservable` (package `Packages/com.evialdev.savableobservable`) and AttaxUnity `F:\Unity_projects\AttaxUnity`.
Starting state (verified): both repos have a clean working tree; the framework migration and Attax test work are committed.

## 0. Findings that shape the plan

1. The universal runner already exists and has no project names or paths: `Packages/com.evialdev.savableobservable/Tools/Invoke-UnityTestRunner.ps1`. Each project has a thin wrapper `Tools/Invoke-UnityTests.ps1`. Attax's wrapper resolves the package through embedded path, `packages-lock.json`, `manifest.json` (relative `file:`) and PackageCache. `-SkipMultiplayer` (excludes `[Category("Online")]`) already exists in the Attax wrapper. A third-party user needs only a small wrapper, plus docs that do not exist yet.
2. The runner is one 1234-line file. Splitting is optional and low-risk if done as dot-sourced files with unchanged function names.
3. `.kilo/test-batches.md` has drifted from reality: `edit-all` says 476 but the 2026-11-06 report shows about 550 executed + 41 skipped. Nothing validates the manifest.
4. The last report (2026-11-06) excluded scenarios. `VisualAndAbilityBehaviorTests` (edited 11-08) has probably never run through the runner.
5. Existing visual tests mostly write model fields directly (`legalMoveMarker`, `hintRole`, `isReviewed`, `currentIssues`). They prove presenter reactions only, not that real gameplay (select chip, jump, capture, ability) produces the right marks.
6. Why quick matches are slow (3-4 min): menu, start reveal, blocked-cell fall, per-move animations, settle waits. A speed profile is missing. `AtaxxGameManagerModel.disableAnimation` exists (forces `AnimationSpeedSettings.MaxStep`) but no test uses it.
7. Ability facts from `Attax.Core.Utils/Rules/game-rules.json`: 9 abilities (superJump, convert, block, wall, doubleMove, spawn, superClone, unblock, bomb); each costs 10; `maxAbilityActionsPerTurn = 1`; capture reward 10, footprint reward 5, start credits 0; both sides have all abilities. `AbilityKind`: SuperJump..Bomb = 1..9; `AbilityCatalog.OrderedAbilities` lists all 9.
8. Mark rules (from `AtaxxBoardMoveHelper.SetCellOwner`, `PlayerChipLogic`): Blue owner sets `isReviewed` (DevOps mark); Red or Blocked owner clears it; a Red landing adds a `VulnIssue` (Bug / dev mark); Blocked clears issues. A review mark stays on the cell when Blue jumps away. In PvP both sides are humans but marks still follow colour (Blue = DevOps, Red = Bug).
9. `AbilityPopupPresenter.RefreshTilesDisplay` sets per tile: name, price `"<cost> BC"`, icon sprite, `button.interactable = CanUseAbility`, `canvasGroup.alpha` = 1 or `disabledTileAlpha` (0.45). Targeting highlights: `SetLegalMoveMarker` for empty-cell abilities (Wall, Spawn, SuperClone), `SetAbilityTargetMarker` for chip/blocked-cell abilities, SuperJump tier markers for destinations.
10. The 41 EditMode skips are Attax triads whose lifecycle needs a scene/prefab. A live-scene audit (Phase 2) covers them.
11. Open risks from the last report: an intermittent DOTween access to a destroyed Image during MenuNavigation setup, and 5 `online_*` scenarios failing with fingerprint mismatches (11-06). Neither is in scope, but the first can make Visual tests flaky, so the plan gates on it (Phase 3 step 1).

## 1. Decisions (defaults assumed)

| # | Decision | Default |
|---|---|---|
| D1 | Speed for logic tests | `disableAnimation = true` on the manager model, blocked cells = 0 where the test allows it, AI depth 1, no quiescence. Visual tests keep animations (they check loops) but run from prepared positions. `Time.timeScale` stays 1 unless measurement shows the 60 s budget is missed; then 3x for Quick only, restored in TearDown. |
| D2 | Prepared positions | New ASCII position builder in the driver (row index = y, column = x; `.` empty, `B` Blue, `R` Red, `#` blocked, plus mark suffixes and credits). Applies through the same logic APIs the game uses (`SetCellOwner`/`ApplyOwnerAndReview`/`ClearCell`), then syncs engine and `RulesSession`. Not direct model writes. |
| D3 | Structured cell state | Add a read-only `PlayerChipPresenter.GetVisualState()` (struct: chip sprite/alpha, move marker enabled/sprite/colour/tweening per colour, ability-target marker, bug/review visible/shine/glitch, blocked image, button enabled, hint shine). `DescribeEffects()` stays for the command and delegates. Tests stop parsing strings. |
| D4 | Subscription types | Test all three: `[AutoBind]`, `[ObservableHandler]`, manual tracked (`OnValueChanged.Add(h, owner)`), each in both directions where a UI control exists. |
| D5 | Runner split | Do it last, as dot-sourced files under `Tools/Runner/`, only if `Test-RunnerUnitTests.ps1` passes unchanged. |
| D6 | Script vs batches | Keep both. Script = full/CI gate and profiles. Batches = agent inner loop per fixture. A validator keeps the manifest honest. |
| D7 | AI-side abilities in PvAI | Out of scope (non-deterministic). Covered by EditMode rules tests and the PvP Red-side ability runs. |

## 2. Phase 1 - Fast match infrastructure (Attax)

Files: `Assets/Editor/PlaytestDriver/*`, `Assets/Tests/Scenarios/*`.

1. `FastProfile` in the driver: sets `disableAnimation`, blocked cells, `ConfigureAi(1, true)`; restores everything in `finally`. Applied by Quick/Ability fixtures only.
2. `PositionBuilder.Apply(string[] rows, PlayerColor sideToMove, int blueCredits, int redCredits)`:
   - Writes the engine board and `RulesSession.State` (bitboards, `SideToMove`, Zobrist hash, result) and the chip models through `PlayerChipLogic` methods, so marks follow the rules in finding 8.
   - Extend `AttaxPlaytestPositions` (keep its existing presets); add driver `GrantCredits(side, n)` (move the test-local `SetCredits` into it: writes `State.BlueCredits/RedCredits` and `ByteCodeLogic.SetBalance`).
   - Fails fast with a clear message if called when not Running, on AI's turn in PvAI, or while the board is starting.
3. Click helpers in the driver: `ClickCell(x, y)` invokes the real `CellButton.onClick` (and asserts the button is enabled first, so a disabled cell is reported as the failure); `ClickAbilityTile(kind)` invokes `tile.button.onClick` only when interactable; `PlayHumanMoveByClick(from, to)`. Reuse `TryPlayHumanMove` where it already clicks.
4. Per-test time guard: scenarios record elapsed seconds; Quick scenarios fail above 60 s with the step breakdown (start, moves, banner).
5. EditMode tests for the builder (row parsing, bitboard round trip, bad input) in `Assets/Tests/EditMode/Playtest`. These stay fast and have no Editor Play Mode dependency.

Validation: builder unit tests; one smoke scenario that builds a position and compares `driver.Snapshot()` with the rows.

## 3. Phase 2 - `MmvcLiveUiAssert` in the framework (TestContracts)

Location: `Packages/com.evialdev.savableobservable/Tests/Shared`. First verify the asmdef allows PlayMode and runtime-assembly use (no Editor-only restriction; `UNITY_INCLUDE_TESTS` constraint). Fix the asmdef if needed.

API (namespace `SavableObservable.Tests.Contracts`):
- Drivers (simulate the user, not the model): `Click(Button)` (checks `interactable` and `enabled`), `SetToggle`, `SetSlider`, `Type(TMP_InputField|InputField, text)`, each followed by `yield return null`.
- Readers: `Text(TMP|Text)`, `SpriteName(Image)`, `Alpha(CanvasGroup|Graphic)`, `IsEnabled(Behaviour)`, `Interactable(Selectable)`.
- Assertions: `ModelToView<T>(ObservableVariable<T>, value, read, expected)` and `ViewToModel<T>(…)` with: exact notification count (once), no bounce-back, value converged in model and every bound view, after `Observable.SetListeners` rebind, after disable/enable, after presenter destroy (no throw, manual subscription keeps working), and re-entrant clamp.
- `Subscriptions` helper: attaches a counting manual subscriber and returns a disposable that removes it, so the three paths are compared in one test.
- `MmvcLiveUiAudit`: given the live scene (or a prefab instance), enumerates every `[AutoBind]` field, picks sample values by type (bool, int/float with slider bounds, string, enum, sprite-bearing handler), runs ModelToView + ViewToModel, then restores the original value. Options: skip list/attribute for side-effecting models (audio volume writes), prefs sandbox on by default (`PrefsSandboxUtility`). Reports each unit as audited or skipped with a reason.

Framework tests (Tests/Runtime, PlayMode, `[Category("LiveUi")]`):
- Refactor `MmvcThreePathPlayModeTests` to use the helper (same assertions, less code).
- New: TMP text, Image sprite and enabled/colour handler, CanvasGroup alpha and `interactable` via handler, Slider/Toggle/InputField both directions with all three subscription types at once, `ObservableList` add/remove/clear rendered by handler and notifying manual subscriber, enum-driven sprite swap, `ObservableHandler` with (current, previous) signature, handler-driven animation start/stop (DOTween tween present / killed), destroyed presenter, inactive prefab cloned then activated.
- Negative tests: audit fails with a readable message on a deliberately broken sample presenter (wrong field name, missing adapter); proves the audit can fail.
- EditMode counterpart for pure logic of the helper (sample generation, skip rules).

Batches: `play-mmvc` grows; add `edit-mmvc-live-ui-helper`. Update counts in `.kilo/test-batches.md`.

## 4. Phase 3 - Live UI audit and settings round trip in Attax

1. Gate: fix or isolate the destroyed-Image DOTween failure in MenuNavigation (reproduce with 3 repeated runs; fix root cause: `SetLink`/`DOKill` on destroy), otherwise Visual tests inherit the flake.
2. `LiveUiAuditTests` (ScenarioTests assembly, `[Category("LiveUi")]`): run `MmvcLiveUiAudit` over the live scene presenters: PlayerSettings (13 AutoBind: sliders, toggles, labels), PlayerProgress (8), GameOverBanner, BaseAISettings, ByteCode, MoveHint, NoticeModal, OnlineLobby/PrivateRoom (view only, no network). Each unit: model change -> real UI value/text/sprite/enabled changes, UI interaction -> model changes, and the dependent labels follow (e.g. `volumeSlider` -> `volumeLabel`). Attax-specific expectations in a table (field -> expected label format).
3. Settings that have a game effect: `reduceMotion`, `lowFx`, `menuAnimationEnabled`, `showMoveHints`/`hintDepth`: after toggling in UI, assert the effect (`Const.UiTweenParam.reduceMotion`, tween durations 0, hints enabled) and restore.
4. Prefs sandbox on for the whole fixture; assert restoration.
5. The units skipped by the EditMode contract (41 skips) must each appear in the audit either audited or skipped with a reason; a test lists any unit that is neither.

## 5. Phase 4 - Marks, highlights, hints with real gameplay (Attax)

Fixture `CellMarkAndHighlightTests` (`[Category("Visual")]`), all from prepared positions, all moves by real clicks, asserting through `GetVisualState()`.

1. Selection highlights: select a Blue chip -> each legal cell gets the Blue marker; Clone sprite for distance 1, Jump sprite for distance 2 (distinct sprites, tweening loop running, `Button.enabled` true); illegal cells have no marker and a disabled button; the selected chip shines (`shinyEnabled`); deselect (click same chip) clears everything; select another own chip moves the highlight; clicking an illegal cell changes nothing; after the move no marker or shine remains and cells of the next side are clickable per rules.
2. Move outcomes: clone (source stays, destination Blue + review mark); jump (source emptied but keeps the review mark, uncover animation requested; destination Blue + review); capture of a Red chip with a bug (bug dissolve, review appears); Red move in PvP onto/over a review cell (review removed, bug added). Both PvP sides and Blue-in-PvAI.
3. Marks lifecycle with animation state: appear (reverse dissolve), shine/glitch loop only while exposed, stops when covered, resumes when uncovered, dissolves when cleared (existing test kept, now also reached via gameplay, not only model writes).
4. Invariant rule (EditMode, added to `BoardInvariants`, unit tested): Blue-owned cell has review; Red-owned cell has none; Blocked has no marks and shows the blocked image. Checked after every scripted move in all scenarios.
5. Hints: `RequestManualHint` on a prepared position with one clearly best move: Source chip gets `HintRole.Source` and shine; target cell gets `Target` with the pulsing marker; selecting a chip or moving cancels both; `showMoveHints=false` produces none. Wait on state with bounded timeouts (search depth from settings), no fixed sleeps.
6. Cell visuals: chip sprite per owner (red/blue/null), chip alpha, blocked cell backdrop and image, clickable button state per owner and mode (PvAI: Red chips not clickable; PvP: both own chips by turn).

Existing `VisualAndAbilityBehaviorTests` keep the model-driven presenter tests (rename the class `CellPresenterReactionTests`), remove ability tests (moved to Phase 5).

## 6. Phase 5 - Abilities, one test per ability (Attax)

Fixture `AbilityTests` (`[Category("Ability")]`), table-driven from `AbilityCatalog.OrderedAbilities` and the live rules (`RulesSession.Rules.GetAbilityText`), credits granted with `driver.GrantCredits`, no match played to the end. Run each case for Blue (PvAI and PvP) and for Red in PvP.

Common per-ability steps and assertions:
1. Before: popup closed -> open via the real avatar/button; tile state: name equals the rules text, price `"10 BC"`, icon sprite non-null (and distinct per ability), `interactable == CanUseAbility`, `canvasGroup.alpha` 1 or 0.45, balance text shows the granted credits.
2. Tile click arms: `isTargeting` (or `isDoubleMoveArmed` for DoubleMove), targeting bar visible with the instruction text from rules, correct highlights (expected cell set from a position, with correct marker kind: `legalMoveMarker` for Wall/Spawn/SuperClone, `abilityTargetMarker` for chip/blocked-cell abilities, SuperJump: sources clickable, then destinations with the SuperJump marker), all other cells disabled.
3. Cancel path: cancel button/`CancelTargeting` clears markers, bar closes, cells clickable again, credits unchanged.
4. Wrong click (non-target cell) is ignored and keeps targeting.
5. Execute by real cell click(s); wait for settle; assert result:

| Ability | Position idea | Board result |
|---|---|---|
| SuperJump | Blue chip with empty cells at distance 3+ | chip moved, source empty but keeps review mark, distance-2 cells not offered |
| Convert | Red chip among Blue | only that chip becomes Blue, neighbours unchanged, marks follow rules |
| Block | Red chip | cell Blocked (blocked image on, black backdrop, marks gone, not clickable) |
| Wall | empty cell | cell Blocked |
| DoubleMove | any | armed, bar text, first move keeps the turn, bar shows "Double Move: 1 move left" and hides cancel, second move passes the turn |
| Spawn | empty cell next to enemy | new chip, no neighbour captured |
| SuperClone | empty cell next to enemies | new chip, all adjacent enemies captured |
| Unblock | blocked cell | becomes empty; tile disabled when no blocked cell exists |
| Bomb | Red chip with bug (and a Blue chip with review in the Red-side run) | chip removed, bug and review marks gone |

6. After: credits = before - 10 (+ rewards where the rules give them; assert exact numbers from `game-rules.json` values), ByteCode text updated, `isTargeting`/armed reset, popup closed, turn passed (except DoubleMove first step), `maxAbilityActionsPerTurn = 1`: every tile disabled for the rest of that turn, invariants hold.
7. Boundaries: credits 9 -> all tiles disabled, click does nothing (shake/error sound path only); credits 10 -> enabled; abilities with no valid target (Unblock without blocked cells, Bomb/Convert/Block without enemies) -> tile disabled even with credits (verify against `AttaxRules.CanUseAbility` before coding the expectation).
8. Rules-disabled ability (`playerAbilities` false) is an EditMode `RulesSession` test, not a scene test.

## 7. Phase 6 - Quick win/loss matches under one minute (Attax)

1. Replace the engine-policy-driven matches by recorded lines. Discovery step: with the speed profile, play candidate lines in the live Editor and save the winning/losing move lists with the position hash after each ply:
   - PvAI win: AI depth 1, quiescence off. Blue plays the best 2-ply move each turn (policy `engine2`); measured earlier to wipe Red out in 5 plies. Record the moves; keep the policy as fallback because AI tie-breaks vary.
   - PvAI loss: Blue only jumps (no clone, so the source cell is left empty and the AI captures isolated chips); wiped out in about 6 plies. Record it. Add the reverse trap as its own case: two jumps next to Red without capturing -> Red captures (this is the behaviour you described).
   - PvP: Blue plays `engine2`, Red only jumps -> Blue wins fast; mirror for Red. Optionally one PvP draw-by-block line.
2. Scenario scripts take a `ScriptedLine` (moves + expected winner/score); on deviation they re-plan with the policy and log the deviation. Assertions: winner, domination flag, scores, banner, progress counters (+1 win/loss in PvAI, unchanged in PvP), menu return, invariants after each ply.
3. Add end-state scenarios that need no game play (each about 10 s): prepared positions `stuck_blue`, `stuck_red`, `draw`, `full_blue`, `full_red` through the builder (finish via `TryContinueGame`), surrender, replay from banner.
4. Budget: Quick category total <= 60 s per scenario, enforced by the time guard; total Quick category reported with the slowest tests.
5. Update `ScenarioRegistryTests` counts and `[Category]` values (Quick, Visual, Ability, LiveUi).

## 8. Phase 7 - Runner, wrappers, skip keys, manifest

Framework (package `Tools/` and docs):
1. Runner: add `-IncludeCategories` (batch: `-testCategory a;b`; live: filter) alongside `-ExcludeCategories`; add `-ListOnly` (discover and print per-assembly/fixture counts); add "slowest 10 tests" and per-category totals to the Markdown/JSON report (from NUnit XML).
2. Template wrapper under `Samples~/ConsumerWrapper/Invoke-UnityTests.ps1` (about 40 lines, no hardcoded names: `-ProjectPath` default = parent of script, optional `-SelfTestCommand`, `-Label`) and `Documentation~/Testing.md`; link from the package README, replace the README "Unattended Tests" section with a short pointer; `CHANGELOG.md` entry; bump package version only if the user asks.
3. D5 split (after 1-2 are green): `Tools/Runner/*.ps1` (Logging, Process, UnityCli, EditorStatus, NUnitParser, LiveSuite, BatchSuite, SelfTest, Report) dot-sourced by `Invoke-UnityTestRunner.ps1`; same function names; run `Test-RunnerUnitTests.ps1` before and after, require identical results.
4. Extend `Test-RunnerUnitTests.ps1`: new switches, template wrapper resolves a fake package, report contains slowest tests.

Attax wrapper `Tools/Invoke-UnityTests.ps1`:
5. Add `-Profile`: `Fast` (EditMode + PlayMode + self-test, no scenarios; default), `Quick` (+ `Quick;Visual;Ability;LiveUi` categories, scenarios assembly, Online excluded), `Full` (+ Mode, Long, Soak; Online excluded), `Online` (only Online). `-SkipMultiplayer` stays and works with every profile; `-IncludeScenarios` remains for explicit control. Environment variable `ATTAX_SKIP_ONLINE=1` is an alternative default for the machine (documented, optional).
6. `Tools/Sync-TestBatches.ps1` (Attax): uses runner `-ListOnly` to compare discovered counts with `.kilo/test-batches.md`; `-Check` exits non-zero on drift (also run by `-Profile Fast`), `-Write` rewrites counts. Fix current drift (`edit-all` 476) and add batches: `scn-quick`, `scn-visual`, `scn-ability`, `scn-liveui`, `edit-playtest-position-builder`, plus `scn:` entries for the new scenarios.

Validation: runner unit tests, a mocked run per profile, a real `-Profile Quick -SkipMultiplayer` run with report archived.

## 9. Phase 8 - Documentation rewrite

Attax `F:\Unity_projects\AttaxUnity`:
- `TESTING.md`: full rewrite (script and small batches together). Structure: 1 which tool for what (inner loop vs gate table); 2 quick start (profile commands); 3 suites and categories (Fast/Quick/Full/Online; every assembly and category incl. new ones); 4 the script runner (parameters, exit codes 0/6/8, report files, wall vs NUnit time, slowest tests, `-SkipMultiplayer`); 5 small batches and `small-model` (manifest format, when to use, `Sync-TestBatches.ps1`); 6 combined workflow (after a change: recompile -> smallest batches -> `-Profile Quick` -> evidence; before merge: `-Profile Full -SkipMultiplayer`; online separately); 7 scenario tests: driver, fast profile, prepared positions, scripted lines, ability tests, mark tests, how to add a scenario/ability/mark (checklists of files to touch); 8 live-UI test rules (`MmvcLiveUiAssert`, what each subscription type must show); 9 online prerequisites and skip; 10 evidence rules; 11 known issues (online fingerprint failures, MenuNavigation tween flake status); 12 maintenance triggers (rules JSON change, new ability, new mark, new setting).
- `AGENTS.md`: lines about tests (item 7 near line 197, "Automated Tests" near line 335, the `attax_scenario` table row near line 394): point to the new sections, mention profiles.
- `docs/DEVELOPMENT_WORKFLOW.md`: "Checks after a change" (step 2/3) and "Unattended Regression Runner" section.
- `docs/MENU.md` (line ~77), `docs/AUDIO.md` (line ~93): batch references.
- `.kilo/agent/small-model.md`: only if the manifest format changes (it does not); check IDs.
- Leave historic `IMPLEMENTATION_CHECKLIST*.md` untouched except a new checklist for this work.

Framework: `Documentation~/Testing.md`, package README section, repo `README`/`mmvc-unity-skill/references/framework-guide.md` addition describing `MmvcLiveUiAssert` and the three subscription types; `mmvc-unity-skill/SKILL.md` one line (copy under `C:\Users\alexx\.skills\mmvc-unity` is outside the repo; tell the user to sync it).

## 10. Order of work and gates

1. Phase 2 (helper) and Phase 7 steps 1-2 can start independently; Phase 1 precedes 4, 5, 6.
2. Gate after each phase: recompile with 0 errors, smallest batches pass, then the profile that covers the phase; counts and evidence recorded in a new checklist file in each repo. Tests that cannot run (Editor busy, timeout) are reported as blockers, not passes.
3. Commit per phase, per repo, only when asked. Framework changes land first, then Attax adopts them through its package reference.

## 11. Validation summary

- Framework: `Invoke-UnityTests.ps1 -Suite All` (EditMode, PlayMode incl. LiveUi tests, MMVC self-test), `Test-RunnerUnitTests.ps1`.
- Attax: `-Profile Fast`, then `-Profile Quick -SkipMultiplayer`, then `-Profile Full -SkipMultiplayer`; two consecutive Play runs without recompiling (AGENTS.md Multi-Play rule); each Quick scenario <= 60 s and each Ability/Visual test <= 30 s in the report; manifest check clean; repeat the Quick profile 3 times to catch flakes.
- Negative proof: deliberately break one mark rule, one ability rule and one AutoBind field and confirm the matching test fails with a readable message, then revert.

## 12. Out of scope / open items

- AI-side ability use and telegraph/callout checks (D7); online scenarios and their fingerprint failures; pixel/screenshot comparison (state-based checks only).
- To verify while implementing (not assumed): TestContracts asmdef platforms; whether `CanUseAbility` also requires valid targets; mark behaviour on captures in `HandleMoveStart` beyond `SetCellOwner`; how the live-editor route (`--filter`) expresses include categories.
