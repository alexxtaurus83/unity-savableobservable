# AttaxUnity Adoption: Command-Aware AutoBind & Framework ListenerSet

> **Before implementing, read `AGENTS.md` (project root) and follow it.**
>
> **Required for this plan:**
> - **`AGENTS.md`**: sections that apply: "Work Tiers" (Large tier), "Finding Code", "MMVC Unit Shape", "Domain Reload Disabled", "Tests and Editor Tools", "Stop and Ask"
> - **`codegraphcontext`**: impact analysis: `PlayerSettingsPresenter`, `PlayerSettingsLogic`, `BaseAISettingsPresenter`, `BaseAISettingsLogic`, `PrivateRoomPresenter`, `PrivateRoomLogic`, `OnlineLobbyPresenter`, `OnlineLobbyLogic`, `OnlineLobbyRow`, `ModalWindow`, `AtaxxGameManagerPresenter`, `MoveHintPresenter`, `ByteCodePresenter`, `ListenerSet`, `WiringAudit`
> - **`unity-cli`**: verification: `attax_preflight --action save --expect edit` (`READY=yes`), recompile, `attax_wiring_audit --scope all`, Multi-Play verification; tests per `TESTING.md`
> - **`mmvc-unity`**: MMVC units touched: `PlayerSettings`, `BaseAISettings`, `PrivateRoom`, `OnlineLobby`, `ModalWindow` (and derived modal presenters), `AtaxxGameManager`, `MoveHint`, `ByteCode`
> - **`zengram-mcp`**: memories consulted: `attax-unity-player-settings`, `attax-unity-mmvc-2-0`; to store after verification: `attax-unity-command-autobind-and-listenerset`
> - **`docs`**: consulted / to update: `docs/DEVELOPMENT_WORKFLOW.md`, `docs/MENU.md`
> - **Checklist**: `IMPLEMENTATION_CHECKLIST.md` written before implementation (Large tier)

---

## 1. Prerequisites & Execution Order

This is **Phase 3** of the 3-part MMVC upgrade sequence:
1. **Phase 1 (`unity-savableobservable`):** `.kilo/plans/1794258080625-mmvc-unity-and-dotnet-bugfixes.md` — Unity 6000.5 baseline & 12 core framework bug fixes.
2. **Phase 2 (`unity-savableobservable`):** `.kilo/plans/1794369600000-command-aware-autobind-and-listener-set.md` — Adds `[AutoBind(..., Command = ...)]`, `IAutoBindCommandTarget`, `TMP_DropdownAdapter`, and `SavableObservable.ListenerSet` (`BaseObservablePresenter<M>.Listeners`).
3. **Phase 3 (`F:\Unity_projects\AttaxUnity` — This Plan):** Update `AttaxUnity`'s `com.evialdev.savableobservable` package reference to the new commit and adopt Command-Aware `AutoBind` and framework `Listeners` across `AttaxUnity` to eliminate manual UI listener and `Set...WithoutNotify` boilerplate.

Required skills to load in the implementing session:
- `C:\Users\alexx\.skills\mmvc-unity\SKILL.md`
- `C:\Users\alexx\.skills\codegraphcontext\SKILL.md`
- `C:\Users\alexx\.skills\unity\unity-cli\SKILL.md`
- `C:\Users\alexx\.skills\unity\ui-ugui\SKILL.md`
- `C:\Users\alexx\.skills\zengram-mcp\SKILL.md`

> **Startup-order note (read before Section 3.9):** `ServiceLocator.Awake` (`-200`) registers services and binds every presenter (`InitializePresenter()` + `SetListeners`) **before** `ControlSys.Start` (`-98`) loads `ResourceStore` / `JsonDataStore` from Addressables. `Start` always runs after every `Awake`/`OnEnable`, so execution-order values cannot reverse this. Phase 1 Task 5 (`[DefaultExecutionOrder(-200)]` on `ServiceLocator`) only guarantees registration-before-bind; it does not make resources available at bind time. Rule for this plan: **no presenter reads loaded resources at bind time** (Section 3.9).

---

## 2. Goal and Scope

### Observable Result
Eliminate ~220 lines of manual `listeners.Toggle` / `listeners.Slider` / `listeners.InputField` / `listeners.Dropdown` lambdas, manual `SetIsOnWithoutNotify` / `SetValueWithoutNotify` / `SetTextWithoutNotify` render handlers, and duplicate `ListenerSet` / `OnDestroy` boilerplate across `F:\Unity_projects\AttaxUnity` by adopting:
1. **Command-Aware `[AutoBind(nameof(Model.field), Command = nameof(Logic.Command))]`** on all interactive bound controls (`PlayerSettings`, `BaseAISettings`, `PrivateRoom`, `OnlineLobby`).
2. **Display-only `[AutoBind]`** on unformatted numeric value labels (`blockedCellsValueText`, `hintDepthValueText`, `depthValueText`, `aiVsAiSpeedValueText`, and `ByteCodePresenter`'s `blueText`/`redText`).
3. **`[ClickCommand(nameof(Logic.Method))]`** on 20 action buttons whose target is a synchronous, zero-argument `public void` method on the paired Logic (Section 3.8).
4. **Framework-owned `BaseObservablePresenter<M>.Listeners` (`SavableObservable.ListenerSet`)** for the remaining explicit wiring (argument-taking, presenter-local, or `async void` targets), retiring the local `Assets/Scripts/Base/UiListeners.cs` wrapper.
5. **Extended `WiringAudit.cs` verification** that checks `[AutoBind(..., Command = ...)]` and `[ClickCommand(...)]` method names and signatures on the paired `BaseLogic` component in EditMode.

### Out of Scope
- Changing any serialized field names on any Presenter or Model (scene `Merge Conflict.unity` and prefabs keep 100% of their serialized field references intact without rewiring). `[ClickCommand]` adds no serialized data.
- Changing gameplay rules, AI search behavior, audio mix formulas, or PlayerPrefs JSON format.
- Converting `async void` Logic methods (`CreateRoom`, `CancelOrCloseRoom`, `JoinRoom`, `SubmitDisplayNameAndConnect`, `RefreshLobby`) to `Task` or wrapping them for `[ClickCommand]`; their buttons stay on `Listeners.Listen`.
- Adding a model -> Logic "command `[ObservableHandler]`" (rejected in Phase 2).

---

## 3. Comprehensive Boilerplate Audit Across `AttaxUnity`

### 3.1 `PlayerSettingsPresenter` & `PlayerSettingsLogic` (Biggest Reduction: ~145 lines removed)
* **Current Boilerplate (`Assets/Scripts/Ataxx.Unity/PlayerSettings/PlayerSettingsPresenter.cs:59-162, 194-283`):**
  * **13 manual input listeners** in `InitializePresenter()` (`difficultyDropdown`, `blockedCellsSlider`, `hapticsToggle`, `soundToggle`, `volumeSlider`, `musicVolumeSlider`, `sfxVolumeSlider`, `menuAnimationToggle`, `reduceMotionToggle`, `lowFxToggle`, `showHintsToggle`, `advancedHintsToggle`, `hintQuiescenceToggle`, `hintDepthSlider`, `serverUrlInput`) that each call `GetLogic()?.SetX(val)` and then manually call `OnXChanged(m.x.Value)` to reconcile rejected/clamped/trimmed input.
  * **14 `[ObservableHandler(..., InvokeOnBind = true)]` methods** (`lines 199-278`), 9 of which do nothing except `control.SetIsOnWithoutNotify(current)` / `SetValueWithoutNotify(current)` / `SetTextWithoutNotify(current)`.
  * Manual `RefreshSurrenderButton()` and `OnIsOpenChanged` override (`lines 166, 169-174, 280-283`) that query `GetLogic().CanSurrender()` directly from the presenter because `OnCanSurrenderChanged` lacked `InvokeOnBind = true`.
* **After Migration:**
  * All **13 interactive controls** use `[AutoBind(nameof(PlayerSettingsDataModel.<field>), Command = nameof(PlayerSettingsLogic.<SetMethod>))]`:
    1. `difficultyDropdown` -> `[AutoBind(nameof(PlayerSettingsDataModel.selectedDifficulty), Command = nameof(PlayerSettingsLogic.SetDifficulty))]`
    2. `blockedCellsSlider` -> `[AutoBind(nameof(PlayerSettingsDataModel.blockedCellCount), Command = nameof(PlayerSettingsLogic.SetBlockedCellCount))]`
    3. `hapticsToggle` -> `[AutoBind(nameof(PlayerSettingsDataModel.hapticsEnabled), Command = nameof(PlayerSettingsLogic.SetHaptics))]`
    4. `soundToggle` -> `[AutoBind(nameof(PlayerSettingsDataModel.soundEnabled), Command = nameof(PlayerSettingsLogic.SetSound))]`
    5. `volumeSlider` -> `[AutoBind(nameof(PlayerSettingsDataModel.masterVolume), Command = nameof(PlayerSettingsLogic.SetVolume))]`
    6. `musicVolumeSlider` -> `[AutoBind(nameof(PlayerSettingsDataModel.musicVolume), Command = nameof(PlayerSettingsLogic.SetMusicVolume))]`
    7. `sfxVolumeSlider` -> `[AutoBind(nameof(PlayerSettingsDataModel.sfxVolume), Command = nameof(PlayerSettingsLogic.SetSfxVolume))]`
    8. `menuAnimationToggle` -> `[AutoBind(nameof(PlayerSettingsDataModel.menuAnimationEnabled), Command = nameof(PlayerSettingsLogic.SetMenuAnimation))]`
    9. `reduceMotionToggle` -> `[AutoBind(nameof(PlayerSettingsDataModel.reduceMotion), Command = nameof(PlayerSettingsLogic.SetReduceMotion))]`
    10. `lowFxToggle` -> `[AutoBind(nameof(PlayerSettingsDataModel.lowFx), Command = nameof(PlayerSettingsLogic.SetLowFx))]`
    11. `showHintsToggle` -> `[AutoBind(nameof(PlayerSettingsDataModel.showMoveHints), Command = nameof(PlayerSettingsLogic.SetShowMoveHints))]`
    12. `advancedHintsToggle` -> `[AutoBind(nameof(PlayerSettingsDataModel.advancedHints), Command = nameof(PlayerSettingsLogic.SetAdvancedHints))]`
    13. `hintQuiescenceToggle` -> `[AutoBind(nameof(PlayerSettingsDataModel.hintQuiescence), Command = nameof(PlayerSettingsLogic.SetHintQuiescence))]`
    14. `hintDepthSlider` -> `[AutoBind(nameof(PlayerSettingsDataModel.hintDepth), Command = nameof(PlayerSettingsLogic.SetHintDepth))]`
    15. `serverUrlInput` -> `[AutoBind(nameof(PlayerSettingsDataModel.serverUrlOverride), Command = nameof(PlayerSettingsLogic.SetServerUrlOverride))]`
  * Plain value counters `blockedCellsValueText` and `hintDepthValueText` use display-only `[AutoBind(nameof(PlayerSettingsDataModel.blockedCellCount))]` and `[AutoBind(nameof(PlayerSettingsDataModel.hintDepth))]`.
  * **11 `[ObservableHandler]` methods are completely deleted** (`OnAdvancedHintsChanged`, `OnHintQuiescenceChanged`, `OnHintDepthChanged`, `OnDifficultyChanged`, `OnBlockedCellCountChanged`, `OnHapticsChanged`, `OnSoundChanged`, `OnMenuAnimationChanged`, `OnReduceMotionChanged`, `OnLowFxChanged`, `OnServerUrlOverrideChanged`).
  * Only **5 focused `[ObservableHandler]` methods remain** on `PlayerSettingsPresenter`:
    * `OnCanSurrenderChanged(bool can)` (with `InvokeOnBind = true`, allowing `RefreshSurrenderButton()` and `OnIsOpenChanged` to be deleted from `PlayerSettingsPresenter`; `PlayerSettingsLogic.LoadSettings()` and `OnOpening()` keep `model.canSurrender.Value` current).
    * `OnShowMoveHintsChanged(bool current)` (only calls `RefreshHintDependentControls(current)` — no `SetIsOnWithoutNotify`).
    * `OnMasterVolumeChanged(float current)`, `OnMusicVolumeChanged(float current)`, `OnSfxVolumeChanged(float current)` (only format the `"80%"` text labels — no `SetValueWithoutNotify`).
  * `InitializePresenter()` only configures static dropdown options (`Easy`, `Normal`, `Hard`) and static slider min/max/wholeNumbers. The 2 navigation buttons move to `[ClickCommand]` (Section 3.8).
  * The `Services.Get<JsonDataStore>()?.onlineConfig` read at `PlayerSettingsPresenter.cs:51-54` is **deleted from the presenter** and moves to `PlayerSettingsLogic.LoadSettings()` (Section 3.9.1): at bind time `onlineConfig` is still `null`, so that read never executed.

---

### 3.2 `BaseAISettingsPresenter` & `BaseAISettingsLogic` (All 5 Controls Unified Under Commands)
* **Current State (`Assets/Scripts/Ataxx.Unity/AIvsAI/BaseAISettingsPresenter.cs` & `BaseAISettingsLogic.cs`):**
  * `useHeuristicToggle`, `aiAbilitiesToggle`, and `disableQuiescenceToggle` use direct model-writing `[AutoBind]`, while `depthSlider` uses a lambda `listeners.Slider(depthSlider, v => GetModel().aiDepth.Value = (int)v)` without clamping in Logic, and `aiVsAiSpeedSlider` uses `listeners.Slider(aiVsAiSpeedSlider, v => GetLogic().SetAiVsAiSpeed((int)v))`.
  * `OnAiDepthChanged` and `OnAiVsAiSpeedChanged` manually call `SetValueWithoutNotify` and `text = val.ToString()`.
* **After Migration:**
  * Add synchronous command methods on `BaseAISettingsLogic` (with `[Preserve]`):
    * `public void SetUseHeuristic(bool enabled)`
    * `public void SetAiAbilitiesEnabled(bool enabled)`
    * `public void SetDisableQuiescence(bool disabled)`
    * `public void SetAiDepth(int depth)` (clamped to `1..5`)
    * `public void SetAiVsAiSpeed(int step)` (existing, clamped to `MinStep..MaxStep`)
  * On `BaseAISettingsPresenter`:
    * Decorate all 5 controls (`useHeuristicToggle`, `aiAbilitiesToggle`, `disableQuiescenceToggle`, `depthSlider`, `aiVsAiSpeedSlider`) with `[AutoBind(nameof(BaseAISettingsDataModel.<field>), Command = nameof(BaseAISettingsLogic.<SetMethod>))]`.
    * Decorate `depthValueText` with `[AutoBind(nameof(BaseAISettingsDataModel.aiDepth))]` and `aiVsAiSpeedValueText` with `[AutoBind(nameof(BaseAISettingsDataModel.aiVsAiSpeed))]`.
    * **Delete `OnAiDepthChanged` and `OnAiVsAiSpeedChanged` completely** and remove both `listeners.Slider` calls from `InitializePresenter()`.
    * Keep `resetProgressButton` on `Listeners.Listen(resetProgressButton.onClick, OnResetProgressClicked)`: `OnResetProgressClicked` is a `protected virtual` presenter extension point, so it is not a `[ClickCommand]` target.

---

### 3.3 `PrivateRoomPresenter` & `PrivateRoomLogic` (Eliminate Raw Untracked `onValueChanged` Sanitization Loop)
* **Current State (`Assets/Scripts/Ataxx.Unity/Online/PrivateRoomPresenter.cs:21, 35-42`):**
  * `PrivateRoomDataModel` already defines `public ObservableVariable<string> inputCode;`, which was left unused.
  * `PrivateRoomPresenter.InitializePresenter()` attaches an untracked `roomCodeInput.onValueChanged.AddListener` that calls `RoomCodeInput.Sanitize(val)` and writes `roomCodeInput.text = sanitized` directly, and `joinRoomButton` reads `roomCodeInput.text` from the UI component inside a lambda.
* **After Migration:**
  * Add `[Preserve] public void SetInputCode(string rawCode)` on `PrivateRoomLogic` that assigns `GetModel().inputCode.Value = RoomCodeInput.Sanitize(rawCode);`, and `public void JoinCurrentRoom() => JoinRoom(GetModel().inputCode.Value);` (also clearing `model.inputCode.Value = string.Empty` in `CancelOrCloseRoom()`).
  * Decorate `roomCodeInput` on `PrivateRoomPresenter` with `[AutoBind(nameof(PrivateRoomDataModel.inputCode), Command = nameof(PrivateRoomLogic.SetInputCode))]`.
  * The framework automatically dispatches `SetInputCode` on edit and reconciles `roomCodeInput` to the sanitized uppercase 6-char code even when typing invalid characters leaves `inputCode.Value` unchanged!
  * Delete the manual `roomCodeInput.onValueChanged.AddListener` block.
  * `copyCodeButton` -> `[ClickCommand(nameof(PrivateRoomLogic.CopyCodeToClipboard))]` (sync).
  * `createRoomButton` (`async void CreateRoom`), `cancelHostButton` (`async void CancelOrCloseRoom`) and `joinRoomButton` (`JoinCurrentRoom` delegates to `async void JoinRoom`) stay on `Listeners.Listen(btn.onClick, GetLogic().X)`. `JoinCurrentRoom` is declared `public void` but wraps an async operation, so it must not be a `[ClickCommand]` target either.

---

### 3.4 `OnlineLobbyPresenter`, `OnlineLobbyLogic` & `OnlineLobbyRow`
* **`OnlineLobbyPresenter` & `OnlineLobbyLogic`:**
  * Add `[Preserve] public void SetSuggestedDisplayName(string name)` on `OnlineLobbyLogic` (`GetModel().suggestedDisplayName.Value = name ?? string.Empty;`) and `public void SubmitCurrentDisplayName() => SubmitDisplayNameAndConnect(GetModel().suggestedDisplayName.Value);`.
  * Update `displayNameInput` on `OnlineLobbyPresenter` to `[AutoBind(nameof(OnlineLobbyDataModel.suggestedDisplayName), Command = nameof(OnlineLobbyLogic.SetSuggestedDisplayName))]`.
  * Wire `submitNameButton` via `Listeners.Listen(submitNameButton.onClick, GetLogic().SubmitCurrentDisplayName)` (wraps `async void SubmitDisplayNameAndConnect`, so not a `[ClickCommand]` target).
  * `privateRoomButton` -> `[ClickCommand(nameof(OnlineLobbyLogic.OpenPrivateRoom))]` (sync).
  * `refreshButton` stays on `Listeners.Listen`: it plays a presenter-side spin animation before calling `async void RefreshLobby`.
* **`OnlineLobbyRow` (`Assets/Scripts/Ataxx.Unity/Online/OnlineLobbyRow.cs:26-27`):**
  * Replace `challengeButton.onClick.RemoveAllListeners();` (which strips any attached `ButtonFeedback` or persistent listeners) with a row-owned `private readonly SavableObservable.ListenerSet _listeners = new ListenerSet();`, calling `_listeners.Clear()` in `Bind(...)` and `OnDestroy()`.

---

### 3.5 `ByteCodePresenter` (Replace Manual First Paint & `ToString()` with `[AutoBind]`)
* **Current State (`Assets/Scripts/Ataxx.Unity/ByteCode/ByteCodePresenter.cs:17-43`):**
  * Manually paints `blueText` and `redText` in `InitializePresenter()` and formats `value.ToString()` in `OnBlueChanged` / `OnRedChanged` before calling `AnimateByteCodeCounter`.
* **After Migration:**
  * Add `[AutoBind(nameof(ByteCodeDataModel.blueByteCodes))]` to `blueText` and `[AutoBind(nameof(ByteCodeDataModel.redByteCodes))]` to `redText` (exact same pattern used by `AtaxxGameManagerPresenter.redCountText` and `blueCountText`).
  * `SyncOnBind` paints initial values automatically; `InitializePresenter()` only logs the null-reference check; `OnBlueChanged` / `OnRedChanged` only trigger `if (value > 0) Services.Get<AtaxxAnimations>()?.AnimateByteCodeCounter(...)`.

---

### 3.6 Migrate All Presenters & Modals to Framework `SavableObservable.ListenerSet` (`Presenter.Listeners`)
* **`ModalWindow<M, L>` (`Assets/Scripts/Base/Windows/ModalWindow.cs:55-80`):**
  * Remove `protected readonly ListenerSet listeners = new ListenerSet();`.
  * In `InitializePresenter()`, call `Listeners.Clear()` and wire `closeButton` and `backdropButton` through `Listeners.Listen(btn.onClick, OnCloseButtonClicked)`. These stay explicit: `OnCloseButtonClicked` is a `protected virtual` presenter method, not a Logic command.
  * Change `protected virtual void OnDestroy()` to `protected override void OnDestroy()` calling `base.OnDestroy(); KillActiveSequence();` (framework `base.OnDestroy()` disposes `Listeners`, `[ClickCommand]` registrations and model subscriptions).
* **`AtaxxGameManagerPresenter` & `MoveHintPresenter`:**
  * Remove `private readonly ListenerSet listeners = new ListenerSet();`, `listeners.Clear()`, and their redundant `private void OnDestroy() { listeners.Clear(); }` methods (which previously shadowed `BaseObservablePresenter.OnDestroy()`!).
  * Their buttons move to `[ClickCommand]` (Section 3.8).
* **Modal subclasses with remaining explicit wiring:**
  * `HelpModalPresenter`: `helpButton` (calls `AppStateLogic` via `Services`, not its paired Logic) and `tabButtons[i]` (`SelectTab(i)`, array + argument) -> `Listeners.Listen`.
  * `AbilityPopupPresenter`: avatar buttons (`OnAvatarClicked(color)`) and tile buttons (`OnTileClicked(k)`) -> `Listeners.Listen`.
  * `BaseAISettingsPresenter.resetProgressButton`, `OnlineLobbyPresenter.submitNameButton`/`refreshButton`, `PrivateRoomPresenter.createRoomButton`/`cancelHostButton`/`joinRoomButton` -> `Listeners.Listen` (see 3.2-3.4).
  * All other modal buttons move to `[ClickCommand]` (Section 3.8).
* **`StartMenuPresenter` (`Assets/Scripts/Ataxx.Unity/StartMenu/StartMenuPresenter.cs`):**
  * As a standalone `MonoBehaviour`, use `SavableObservable.ListenerSet` (`_listeners.Listen(...)` and `_listeners.Dispose()` in `OnDestroy()`).
* **Retire `Assets/Scripts/Base/UiListeners.cs` (+ `.meta`):**
  * Delete `Assets/Scripts/Base/UiListeners.cs` once all callers use `SavableObservable.ListenerSet`.

---

### 3.7 Extend `WiringAudit.cs` to Audit `[AutoBind(..., Command = ...)]` and `[ClickCommand]`
* **File:** `Assets/Editor/PlaytestDriver/WiringAudit.cs`
* In `CheckBindingNames`:
  * When `attr.Command` is non-empty on an `[AutoBind]` field, verify that the GameObject has a `BaseLogic` component and that the `BaseLogic` type declares or inherits a `public` instance `void` method named `attr.Command` with 1 parameter matching the observable field's value type `T`. Report any mismatch as an `AUTOBIND_COMMAND` problem in `WiringAuditResult`.
  * For each `[ClickCommand]` field: the field type is `Button`, the paired `BaseLogic` declares or inherits a `public` instance, non-async, zero-parameter `void` method named `MethodName`, and the field does not also carry `[AutoBind]`. Report mismatches as `CLICK_COMMAND`. An unassigned optional button is not a problem (it is covered by the existing optional-field list).

### 3.8 Adopt `[ClickCommand]` On Synchronous Action Buttons
Add `[ClickCommand(nameof(<Logic>.<Method>))]` to the existing `[SerializeField]` button field, add `[UnityEngine.Scripting.Preserve]` to the target Logic method, and delete the corresponding `listeners.Button(...)` line.

| Presenter | Button field | `[ClickCommand]` target (verified sync `public void`, zero args) |
|---|---|---|
| `BaseConfirmModalPresenter` | `primaryButton` / `secondaryButton` / `tertiaryButton` | `BaseConfirmModalLogic.OnPrimary` / `OnSecondary` / `OnTertiary` (`BaseConfirmModalLogic.cs:56-62`) |
| `AIvsAIMenuPresenter` | `blueSettingsButton` / `redSettingsButton` / `startAiVsAiButton` | `AIvsAIMenuLogic.OpenBlueSettings` / `OpenRedSettings` / `StartAiVsAi` (`AIvsAIMenuLogic.cs:9-17`) |
| `GameOverBannerPresenter` | `okButton` / `replayButton` / `mainMenuButton` | `GameOverBannerLogic.Hide` / `OnReplayClicked` / `OnMainMenuClicked` (`GameOverBannerLogic.cs:112-140`) |
| `PlayerProgressPresenter` | `signInButton` / `achievementsButton` / `leaderboardsButton` | `PlayerProgressLogic.SignIn` / `ShowAchievements` / `ShowLeaderboards` (`PlayerProgressLogic.cs:285-295`) |
| `PlayerSettingsPresenter` | `surrenderButton` / `devSettingsButton` | `PlayerSettingsLogic.SurrenderFromSettings` / `OpenDevSettings` (`PlayerSettingsLogic.cs:265-270`) |
| `AtaxxGameManagerPresenter` | `startButton` / `settingsBtn` | `AtaxxGameManagerLogic.PlayButtonClick` / `OpenPlayerSettings` (`AtaxxGameManagerLogic.cs:209, 385`) |
| `MoveHintPresenter` | `hintButton` | `MoveHintLogic.RequestManualHint` (`MoveHintLogic.cs:89`) |
| `PrivateRoomPresenter` | `copyCodeButton` | `PrivateRoomLogic.CopyCodeToClipboard` (`PrivateRoomLogic.cs:72`) |
| `OnlineLobbyPresenter` | `privateRoomButton` | `OnlineLobbyLogic.OpenPrivateRoom` (`OnlineLobbyLogic.cs:290`) |

Total: 20 buttons. Before editing, re-verify each target with CodeGraphContext (`find_code`) is still `public void`, zero-arg, not `async`. If any changed, STOP and ask.

Remaining explicit `Listeners.Listen` wiring after migration (documented, intentional): `ModalWindow` close/backdrop, `HelpModalPresenter` help/tabs, `AbilityPopupPresenter` avatars/tiles, `BaseAISettingsPresenter.resetProgressButton`, `OnlineLobbyPresenter.submitNameButton`/`refreshButton`, `PrivateRoomPresenter.createRoomButton`/`cancelHostButton`/`joinRoomButton`. `StartMenuPresenter` and `OnlineLobbyRow` are plain `MonoBehaviour`s with standalone `SavableObservable.ListenerSet`.

---

### 3.9 Startup-Order Contract: No Resource Reads at Bind Time (Option A)

**Verified facts (code, not assumptions):**
1. `ServiceLocator.Awake` (`-200`) -> `InitializeSharedSingletons()`: Pass 1 registers every `ISharedSingleton` (`ControlSys`, `ResourceStore`, `JsonDataStore`, ...); Pass 2 calls `EnsureInitialized()` on every scene presenter (`InitializePresenter()` then `Observable.SetListeners`). Presenters also self-init in `Awake` (`-150`); `EnsureInitialized` is idempotent.
2. `ControlSys.Start` (`-98`, `ControlSys.cs:38-49`) -> `StartUp()` clears and fills `ResourceStore` (audio, sprites, textures via `ManageAddressables.LoadAssetSync`) and `JsonDataStore` (vulnerabilities, engine params, rules, AI config, `onlineConfig`, error texts, achievements).
3. Unity runs all `Awake`/`OnEnable` of the scene before the first `Start`; execution order only sorts within a phase. So **presenter bind always precedes the Addressables load**, and no `DefaultExecutionOrder` value on `ControlSys` can change that. Do not "fix" it with a different order value. Moving the load before Pass 2 (a bootstrap component below `-200`) was considered and rejected (Option B): it changes the startup contract and the sync load would run before services are registered.
4. Logic `Start` (default order `0`; no `DefaultExecutionOrder` on `BaseLogic`, asserted by `MMVCRestrictionsTests.cs:723-725`) runs after `ControlSys.Start` (`-98`) in the same frame. Logic `Start` and any use-time code are therefore safe places to read loaded resources.

**Invariant for this plan and for AttaxUnity from now on:** nothing in `InitializePresenter()`, `Awake`, `OnEnable`, `SyncOnBind`, `[ObservableHandler(..., InvokeOnBind = true)]` handlers, or Command-`AutoBind` reconcile may read the *content* of `ResourceStore` or `JsonDataStore`. Resolve lazily at use time (the `MenuBackgroundPresenter.InitializeVisuals()` pattern) or in `Logic.Start()` / `LoadSettings()`.

#### 3.9.1 Server URL flow (`PlayerSettings`): JSON -> model -> UI input -> online client
Required behavior (from the user): read the URL from `online-config.json`, show it in the Settings input and keep it in the model, let the developer edit it, and use the edited value when an online match starts. Local tests use the local server `http://127.0.0.1:5000` (the code default in `OnlineConfig.serverBaseUrl`, `OnlineCommon.cs:51`; the shipped JSON currently holds `https://evial.dev:5080`).

* **Current defect:** `PlayerSettingsPresenter.InitializePresenter()` (`:51-54`) reads `onlineConfig` while it is still `null` (the write never runs), and `PlayerSettingsLogic.LoadSettings()` (`:65`) then resets `serverUrlOverride` to `string.Empty` anyway. The input therefore starts empty.
* **Change on `PlayerSettingsLogic`** (`Assets/Scripts/Ataxx.Unity/PlayerSettings/PlayerSettingsLogic.cs`):
  * Add `internal static string ResolveInitialServerUrlOverride(string savedOverride, Ataxx.Online.OnlineConfig config, bool seedFromConfig)`: returns `savedOverride.Trim()` when it is not blank; otherwise returns `config?.serverBaseUrl ?? string.Empty` when `seedFromConfig` is true, else `string.Empty`. Pure function, unit-testable.
  * In `LoadSettings()`, right after `Loader?.Load()` and before the clamp block, assign `model.serverUrlOverride.Value = ResolveInitialServerUrlOverride(model.serverUrlOverride.Value, Services.Get<JsonDataStore>()?.onlineConfig, Debug.isDebugBuild);`. Do **not** set `changed = true` and do not call `Loader.Save()` for the seed. `LoadSettings()` is called from `Start` (after `ControlSys.Start`), so `onlineConfig` is loaded.
  * The model change repaints `serverUrlInput` through the `serverUrlOverride` binding (Command `AutoBind` from Section 3.1, or the existing handler), so no presenter code reads the config.
* **Change on `PlayerSettingsPresenter`:** delete the `onlineConfig` read (`:51-54`). Remove the `JsonDataStore` `using`/reference only if nothing else in the file needs it.
* **Behavior after the change:**
  * Debug builds and the Editor (the only places the input is visible): the input shows the config URL on first launch; the developer types `http://127.0.0.1:5000`; `SetServerUrlOverride` trims and saves it; `OnlineLobbyLogic` calls `OnlineService.ApplyServerUrlSetting()` on the next open, rebuilds the client when the URL differs, and the online match uses the edited URL. Clearing the field means "use the config URL" (existing `ResolveServerUrl` rule), and the next launch seeds it again.
  * Release builds: no seeding (`seedFromConfig == false`), the override stays empty and the config URL is used, exactly as today. Seeding there would be persisted by any `Loader.Save()` (for example a volume change) and would pin the URL, so a later `online-config.json` change would never reach those players.
  * Known, accepted debug-only side effect: in debug builds any `Loader.Save()` also persists the seeded URL, so a later JSON change is ignored until the developer clears the field. Document it in `docs/DEVELOPMENT_WORKFLOW.md`.

#### 3.9.2 `OnlineService` reads `onlineConfig` in `Awake` (audit finding)
* **Defect:** `OnlineService.Awake()` -> `InitializeIfNeeded()` (`OnlineService.cs:33-47`) runs before `ControlSys.Start`, so `store.onlineConfig` is `null` and the client is built from `new OnlineConfig()` defaults (`127.0.0.1:5000`, default `protocolClientVersion`, default `requestTimeoutSeconds`). `ApplyServerUrlSetting()` later rebuilds only when the **URL** differs, so JSON-provided `protocolClientVersion` / `requestTimeoutSeconds` are silently ignored whenever the URL happens to match.
* **Change on `OnlineService`:** remove the `Awake()` initialization. Make `Session`, `Lobby`, `Rooms`, `GameClient` and `Transport` getters call `InitializeIfNeeded()` first (idempotent). Keep `InitializeIfNeeded()` public (used by `AttaxDriver.cs:80`, `AttaxDriver.Online.cs:44`, `AttaxOnlineCommands.cs:37`).
* **Change on `OnlineLobbyLogic`:** remove `SubscribeEvents()` from `Awake()` (`OnlineLobbyLogic.cs:20-23`); `Start()` and `OnOpening()` already call it and both run after `ControlSys.Start`.
* Before editing, run `codegraphcontext` callers of `OnlineService` members and `InitializeIfNeeded`; if any caller runs in `Awake`/`OnEnable`/`InitializePresenter` of the scene, STOP and ask.

#### 3.9.3 Audit result (no change needed unless stated)
| Unit | Reads at bind time? | Action |
|---|---|---|
| `PlayerSettingsPresenter` | Yes: `JsonDataStore.onlineConfig` in `InitializePresenter` | Fix (3.9.1) |
| `OnlineService` | Yes: `JsonDataStore.onlineConfig` in `Awake` | Fix (3.9.2) |
| `PlayerChipPresenter.InitializePresenter` -> `InitializeCellArt` | Reads `ResourceStore` sprites | Safe today only because board cells are instantiated by `AtaxxBoardBuilder` from `AtaxxGameManagerLogic.Start` (order 0, after `ControlSys.Start`). Leave unchanged; re-verify no `PlayerChip*` is placed in any scene or prefab loaded at scene start (a pre-placed chip would bind before the load and disable its button). Document the invariant in a code comment on `InitializeCellArt`. |
| `MenuBackgroundPresenter` | No: lazy in `InitializeVisuals()` | Reference pattern, unchanged |
| `AbilityPopupPresenter`, `AbilityCalloutPresenter`, `AtaxxBoardBuilder`, `SoundManager.TryResolveClip`, `PlatformProgressService.ResolvePlatformId` | Use-time only | Unchanged |
| `AppStatePresenter` | `InitializePresenter` calls `ApplyScreen(..., playMusic: false)`; music only on later `currentScreen` change | Unchanged |

#### 3.9.4 Guard against regression
* Add the invariant to `AGENTS.md` (MMVC Unit Shape section): "Presenters must not read `ResourceStore` / `JsonDataStore` content in `InitializePresenter`, `Awake`, `OnEnable` or bind-time handlers; `ControlSys.Start` fills them after presenters bind. Resolve at use time or in `Logic.Start`."
* Add the same rule to the Popup recipe in `docs/MENU.md`.

---

## 4. Files Table

| Path | Action | Type | One-Line Purpose |
|---|---|---|---|
| `Packages/manifest.json` & `Packages/packages-lock.json` | Modify | Config | Point `com.evialdev.savableobservable` to updated commit with Phase 1 + Phase 2 |
| `Assets/Scripts/Base/UiListeners.cs` (+ `.meta`) | Delete | Runtime C# | Retire local `ListenerSet` in favor of `SavableObservable.ListenerSet` |
| `Assets/Scripts/Base/Windows/ModalWindow.cs` | Modify | Presenter Base | Use inherited `Listeners` (`SavableObservable.ListenerSet`) and `override void OnDestroy()` |
| `Assets/Scripts/Base/Windows/BaseConfirmModalPresenter.cs` | Modify | Presenter | Switch primary/secondary/tertiary buttons to `Listeners.Listen` |
| `Assets/Scripts/Ataxx.Unity/PlayerSettings/PlayerSettingsPresenter.cs` | Modify | Presenter | Replace 13 manual listeners and 11 `Set...WithoutNotify` handlers with Command `AutoBind` |
| `Assets/Scripts/Ataxx.Unity/PlayerSettings/PlayerSettingsLogic.cs` | Modify | Logic | Add `[Preserve]` to command setters; update `canSurrender` in `LoadSettings()`; seed `serverUrlOverride` from `onlineConfig` in `LoadSettings()` via `ResolveInitialServerUrlOverride` (Section 3.9.1) |
| `Assets/Scripts/Ataxx.Unity/Online/OnlineService.cs` | Modify | Service | Drop `Awake` init; lazy `InitializeIfNeeded()` in getters so `onlineConfig` is loaded first (Section 3.9.2) |
| `Assets/Scripts/Ataxx.Unity/PlayerChipPresenter.cs` | Modify | Presenter | Comment only: document the "cells are built after `ControlSys.Start`" invariant on `InitializeCellArt` |
| `Assets/Tests/EditMode/PlayerSettingsServerUrlTests.cs` | Modify | EditMode Test | Cover `ResolveInitialServerUrlOverride` (saved wins, seed in debug, empty in release, trimming, null config) |
| `Assets/Scripts/Ataxx.Unity/AIvsAI/BaseAISettingsLogic.cs` | Modify | Logic | Add `[Preserve]` commands `SetUseHeuristic`, `SetAiAbilitiesEnabled`, `SetDisableQuiescence`, `SetAiDepth` |
| `Assets/Scripts/Ataxx.Unity/AIvsAI/BaseAISettingsPresenter.cs` | Modify | Presenter | Use Command `AutoBind` on all 5 controls and `[AutoBind]` on both value texts; delete handlers |
| `Assets/Scripts/Ataxx.Unity/AIvsAI/AIvsAIMenuPresenter.cs` | Modify | Presenter | Switch navigation buttons to `Listeners.Listen` |
| `Assets/Scripts/Ataxx.Unity/Online/PrivateRoomLogic.cs` | Modify | Logic | Add `[Preserve] SetInputCode(string)` and `JoinCurrentRoom()`; reset `inputCode` on close |
| `Assets/Scripts/Ataxx.Unity/Online/PrivateRoomPresenter.cs` | Modify | Presenter | Bind `roomCodeInput` with Command `AutoBind` (`SetInputCode`); remove raw `onValueChanged` lambda |
| `Assets/Scripts/Ataxx.Unity/Online/OnlineLobbyLogic.cs` | Modify | Logic | Add `[Preserve] SetSuggestedDisplayName(string)` and `SubmitCurrentDisplayName()` |
| `Assets/Scripts/Ataxx.Unity/Online/OnlineLobbyPresenter.cs` | Modify | Presenter | Bind `displayNameInput` with Command `AutoBind` and buttons with `Listeners.Listen` |
| `Assets/Scripts/Ataxx.Unity/Online/OnlineLobbyRow.cs` | Modify | View | Replace `RemoveAllListeners()` with scoped `SavableObservable.ListenerSet` |
| `Assets/Scripts/Ataxx.Unity/ByteCode/ByteCodePresenter.cs` | Modify | Presenter | Use `[AutoBind]` on `blueText` and `redText` for initial paint and text updates |
| `Assets/Scripts/Ataxx.Unity/AtaxxGameManagerPresenter.cs` | Modify | Presenter | Use inherited `Listeners` and remove duplicate `listeners` field and `OnDestroy` |
| `Assets/Scripts/Ataxx.Unity/MoveHint/MoveHintPresenter.cs` | Modify | Presenter | Use inherited `Listeners` and remove duplicate `listeners` field and `OnDestroy` |
| `Assets/Scripts/Ataxx.Unity/Abilities/AbilityPopupPresenter.cs` | Modify | Presenter | Switch avatar and tile buttons to `Listeners.Listen` |
| `Assets/Scripts/Ataxx.Unity/GameOverBanner/GameOverBannerPresenter.cs` | Modify | Presenter | Switch ok/replay/mainMenu buttons to `Listeners.Listen` |
| `Assets/Scripts/Ataxx.Unity/HelpModalPresenter.cs` | Modify | Presenter | Switch help and tab buttons to `Listeners.Listen` |
| `Assets/Scripts/Ataxx.Unity/PlayerProgress/PlayerProgressPresenter.cs` | Modify | Presenter | Switch platform buttons to `Listeners.Listen` |
| `Assets/Scripts/Ataxx.Unity/StartMenu/StartMenuPresenter.cs` | Modify | View | Use `SavableObservable.ListenerSet` (`Listen` / `Dispose`) |
| `Assets/Editor/PlaytestDriver/WiringAudit.cs` | Modify | Editor Tool | Validate `[AutoBind(..., Command = ...)]` and `[ClickCommand]` against paired `BaseLogic` methods |
| `Assets/Scripts/Base/Windows/BaseConfirmModalLogic.cs`, `AIvsAI/AIvsAIMenuLogic.cs`, `GameOverBanner/GameOverBannerLogic.cs`, `PlayerProgress/PlayerProgressLogic.cs`, `AtaxxGameManagerLogic.cs`, `MoveHint/MoveHintLogic.cs`, `Online/PrivateRoomLogic.cs`, `Online/OnlineLobbyLogic.cs` | Modify | Logic | Add `[Preserve]` to the 20 `[ClickCommand]` targets (Section 3.8); no behavior change |
| `Assets/Tests/EditMode/PlayerSettingsCommandTests.cs` | Modify | EditMode Test | Verify declarative Command `AutoBind` wiring, clamping, hint coupling, and URL trimming |
| `Assets/Tests/EditMode/Playtest/WiringAuditTests.cs` | Modify | EditMode Test | Add test coverage for `WiringAudit` command validation |
| `docs/DEVELOPMENT_WORKFLOW.md` | Modify | Docs | Update Settings section to document `[AutoBind(..., Command = ...)]`, `[ClickCommand]` and `Listeners.Listen`; document the server URL flow, the debug-only seeding and the startup order (Section 3.9) |
| `docs/MENU.md` | Modify | Docs | Popup recipe: value controls -> command `[AutoBind]`, sync zero-arg buttons -> `[ClickCommand]`, rest -> `Listeners.Listen`; add the bind-time resource rule (Section 3.9.4) |
| `docs/UNITY_CLI.md` | Modify | Docs | `attax_wiring_audit` row: add `AUTOBIND_COMMAND` and `CLICK_COMMAND` problem types |
| `AGENTS.md` | Modify | Docs | Line 57 example `listeners.Button(btn, Show)` -> `Listeners.Listen(btn.onClick, Show)`; note `[ClickCommand]`/`Command =` method names are reflection-only (no CGC `CALLS` edge); add the "no `ResourceStore`/`JsonDataStore` reads at bind time" rule (Section 3.9.4) |
| `.kilo/plugin/mmvc-guard.ts` | Sync | Tooling | Byte-for-byte copy of the framework repo's `mmvc-kilo-plugin/mmvc-guard.ts` after Phase 2 (only if Phase 2 changed it) |

---

## 5. Ordered Implementation Steps

1. **Context & Baseline Check:**
   - Recall Zengram (`attax-unity`) and query CodeGraphContext (`find_callers` for `ListenerSet`, `SetDifficulty`, `SetAiVsAiSpeed`, `RoomCodeInput.Sanitize`).
   - Run `unity command attax_preflight --action save --expect edit` and verify `READY=yes`.
2. **Update `com.evialdev.savableobservable` Package Reference:**
   - Ensure Phase 1 and Phase 2 are implemented in `unity-savableobservable` and resolved in `AttaxUnity`.
3. **Migrate `ModalWindow<M, L>` and All Presenters to `SavableObservable.ListenerSet`:**
   - Update `ModalWindow.cs`, `BaseConfirmModalPresenter.cs`, `AIvsAIMenuPresenter.cs`, `AtaxxGameManagerPresenter.cs`, `MoveHintPresenter.cs`, `AbilityPopupPresenter.cs`, `GameOverBannerPresenter.cs`, `HelpModalPresenter.cs`, `PlayerProgressPresenter.cs`, `StartMenuPresenter.cs`, and `OnlineLobbyRow.cs` to use `SavableObservable.ListenerSet` (`Listeners.Listen`), and remove `Assets/Scripts/Base/UiListeners.cs`.
   - Ensure `PlayerChipPresenter.OnDestroy()`, `AbilityCalloutPresenter.OnDestroy()`, `BoardGraphPresenter.OnDestroy()`, `MenuBackgroundPresenter.OnDestroy()`, and `AtaxxGameLogPresenter.OnDestroy()` are `protected override void OnDestroy()` calling `base.OnDestroy()`.
4. **Migrate `PlayerSettingsPresenter` & `PlayerSettingsLogic` to Command-Aware `AutoBind`:**
   - Add `[UnityEngine.Scripting.Preserve]` to all 15 command methods on `PlayerSettingsLogic` and update `LoadSettings()` to call `RefreshCanSurrender()`.
   - Add `[AutoBind(..., Command = ...)]` to all 13 interactive controls on `PlayerSettingsPresenter` and `[AutoBind(...)]` to `blockedCellsValueText` and `hintDepthValueText`.
    - Remove the 13 manual listener registrations in `InitializePresenter()`, delete the 11 redundant `[ObservableHandler]` methods, mark `OnCanSurrenderChanged` with `InvokeOnBind = true`, and remove `RefreshSurrenderButton()` / `OnIsOpenChanged`.
    - Delete the `onlineConfig` read from `PlayerSettingsPresenter.InitializePresenter()` (it moves in step 5a).
5a. **Startup-Order Contract: Server URL Flow & Bind-Time Resource Reads (Section 3.9):**
    - Run `codegraphcontext` callers for `OnlineService` members / `InitializeIfNeeded` and `PlayerSettingsLogic.LoadSettings`; confirm no caller runs in `Awake`/`OnEnable`/`InitializePresenter`. If one does, STOP and ask.
    - `PlayerSettingsLogic`: add `ResolveInitialServerUrlOverride(...)` and call it in `LoadSettings()` right after `Loader?.Load()` (no `changed = true`, no `Save()` for the seed; `seedFromConfig = Debug.isDebugBuild`).
    - `OnlineService`: remove `Awake()`; make `Session`/`Lobby`/`Rooms`/`GameClient`/`Transport` getters call `InitializeIfNeeded()`. `OnlineLobbyLogic`: remove `SubscribeEvents()` from `Awake()`.
    - `PlayerChipPresenter.InitializeCellArt`: add the invariant comment only.
    - Extend `PlayerSettingsServerUrlTests` for `ResolveInitialServerUrlOverride`.
    - Verify in the Editor: with `online-config.json` set to a non-local URL, open Settings and confirm `ServerUrlInput` shows it; type `http://127.0.0.1:5000`, close and reopen the lobby, confirm `OnlineService` rebuilds (`ApplyServerUrlSetting` returns true) and connects to 127.0.0.1:5000; clear the field and confirm the config URL is used. Confirm the `[ServiceLocator]` / `[ResourceStore]` console shows no "could not find Sprite/Texture" errors at startup.
5. **Migrate `BaseAISettingsPresenter` & `BaseAISettingsLogic` to Command-Aware `AutoBind`:**
   - Add `[Preserve]` command methods `SetUseHeuristic`, `SetAiAbilitiesEnabled`, `SetDisableQuiescence`, `SetAiDepth` to `BaseAISettingsLogic` (plus `[Preserve]` on `SetAiVsAiSpeed`).
   - Decorate all 5 controls and 2 value labels on `BaseAISettingsPresenter` with `[AutoBind]` and delete `OnAiDepthChanged`, `OnAiVsAiSpeedChanged`, and the manual slider listeners.
6. **Migrate `PrivateRoom`, `OnlineLobby`, and `ByteCode` Presenters:**
   - Add `[Preserve] SetInputCode` and `JoinCurrentRoom` on `PrivateRoomLogic`; bind `roomCodeInput` with `[AutoBind(nameof(PrivateRoomDataModel.inputCode), Command = nameof(PrivateRoomLogic.SetInputCode))]` on `PrivateRoomPresenter`.
   - Add `[Preserve] SetSuggestedDisplayName` and `SubmitCurrentDisplayName` on `OnlineLobbyLogic`; bind `displayNameInput` with Command `AutoBind` on `OnlineLobbyPresenter`.
   - Add `[AutoBind]` to `blueText` and `redText` on `ByteCodePresenter`.
7. **Adopt `[ClickCommand]` (Section 3.8):**
   - Re-verify the 20 targets with CodeGraphContext, add `[Preserve]`, add `[ClickCommand]` to the button fields, delete the matching `listeners.Button(...)` lines.
   - Keep the documented explicit `Listeners.Listen` buttons unchanged in behavior.
8. **Extend `WiringAudit` & Tests:**
   - Update `WiringAudit.CheckBindingNames` to validate `AutoBindAttribute.Command` and `[ClickCommand]` against the paired `BaseLogic`.
   - Update `PlayerSettingsCommandTests.cs` and `WiringAuditTests.cs` (add a `CLICK_COMMAND` mismatch case).
   - In `LiveUiAuditTests` / `MenuNavigationTests`, confirm each migrated button still triggers its action exactly once per click after popup close/reopen.
9. **Update Documentation & Verify:**
   - Update `docs/DEVELOPMENT_WORKFLOW.md` ("Settings and saved data") and `AGENTS.md`-referenced `docs/MENU.md` popup recipe: value controls use command `[AutoBind]`, sync zero-arg action buttons use `[ClickCommand]`, everything else uses `Listeners.Listen`.
    - Update `docs/UNITY_CLI.md` (`attax_wiring_audit` problem types) and `AGENTS.md:57` (listener example + reflection-only command names note).
    - Add the bind-time resource rule to `AGENTS.md` (MMVC Unit Shape) and `docs/MENU.md`, and the server URL flow plus startup order to `docs/DEVELOPMENT_WORKFLOW.md` (Section 3.9.4).
   - Sync `.kilo/plugin/mmvc-guard.ts` from the framework repo if Phase 2 changed it; verify SHA256 equality.
   - Search the project for `Assets.Scripts.Base.ListenerSet`, `listeners.Button`, `listeners.Slider`, `listeners.Toggle`, `listeners.Dropdown`, `listeners.InputField`: expect zero matches in `Assets/` (historical `.kilo/plans/*.md` are left unchanged).
   - Recompile with 0 errors/warnings.
   - Run `unity command attax_wiring_audit --scope all` -> expect `WIRING=ok`.
   - Run EditMode batches (`Tools/Invoke-TestBatches.ps1 -All`), `PlayerSettingsCommandTests`, `AttaxMmvcContractTests`, `LiveUiAuditTests`, and `MenuNavigationTests`.
   - Perform Multi-Play verification (two consecutive Play Mode runs without recompilation).
   - Refresh CodeGraphContext (`cgc update .`) and store verified outcome in Zengram (`attax-unity-command-autobind-and-listenerset`).

---

### Rules for the implementing agent
- Follow this plan exactly; do not redesign it. For Large plans, work from `IMPLEMENTATION_CHECKLIST.md`.
- Find code with `codegraphcontext`, not grep (`AGENTS.md` "Finding Code"). Check callers, callees and subclasses before changing a symbol.
- If you are unsure, or the plan contradicts the code: STOP and ask the user. Do not guess or invent a workaround.
