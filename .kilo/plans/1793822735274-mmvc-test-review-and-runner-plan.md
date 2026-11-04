# MMVC Framework (`com.evialdev.savableobservable`) & `AttaxUnity` Comprehensive Review, Test Architecture, and Execution Plan

---

## Executive Summary & Key Findings

1. **Confirmation of Runtime Prefab Instantiation Testing Gap (Goal 1):**
   - **Confirmed with file and line evidence.** In `AttaxUnity`, the 49 board cells are instantiated at runtime from an Addressable prefab (`BoardCell`) in `AtaxxBoardBuilder.cs:58–95`. Each cell carries a full MMVC triad (`PlayerChipDataModel`, `PlayerChipLogic`, `PlayerChipPresenter`). In contrast, `unity-savableobservable` has **zero tests** that call `UnityEngine.Object.Instantiate(...)` on a prefab hierarchy containing MMVC components; all existing EditMode and PlayMode tests construct empty `new GameObject()` instances and attach components imperatively via `AddComponent<T>()`.
   - Furthermore, dynamically instantiating 49 `PlayerChipLogic` instances triggers **5 critical lifecycle interactions** that have no test coverage today:
     1. `BaseLogic.Awake()` (`Runtime/BaseLogic.cs:28–32`) vs `AbilityPopupLogic.Awake()` (`AttaxUnity\Assets\Scripts\Ataxx.Unity\Abilities\AbilityPopupLogic.cs:11`) and `PlayerChipLogic.Awake()` (`PlayerChipLogic.cs:38`): derived classes declare `private void Awake()` without `override` or `base.Awake()`, silently hiding `BaseLogic.Awake()`!
     2. If a multi-instance prefab (like `PlayerChipLogic`, 49 instances) *did* call `base.Awake()`, all 49 unnamed instances would collide in `ServiceLocator.TryRegister`, evicting `PlayerChipLogic` and `PlayerChipDataModel` and logging errors on any `Services.Get<PlayerChipLogic>()` call.
     3. Prefab instantiation when the prefab asset is **active** (`Awake()` fires synchronously inside `Object.Instantiate` before caller code can configure fields like `chipLogic.x`/`y`) vs **inactive** (`Awake()` is deferred until `SetActive(true)`, or forced early via `chipLogic.GetModel()`).
     4. Pooled object lifecycle (`GameObjectPool.cs:44–217` used by `OnlineLobbyPresenter.cs:66–94` and `MenuBackgroundPresenter.cs:175–233`), where `SetActive(false)` -> `SetActive(true)` cycles occur repeatedly.
     5. Cross-unit foreign subscriptions (`BoardGraphLogic.cs:52–81` subscribing to all 49 `chipModel.owner.OnValueChanged` and `MenuBackgroundLogic.cs:45–93` subscribing to `AppStateDataModel` and `PlayerSettingsDataModel`) where destroying the subscriber before the foreign model leaves dangling delegates in the foreign model unless manually detached.

---

## 1. Usage Gap Analysis: `AttaxUnity` vs. Existing MMVC Tests (Goal 1)

### 1.1 How `AttaxUnity` Consumes MMVC in Production (`Assets\Scripts`)
Across `F:\Unity_projects\AttaxUnity\Assets\Scripts`, there are **23 `BaseObservableDataModel` classes**, **25 `BaseLogic` classes**, **26 `BasePresenter` / `BaseObservablePresenter` / `ObservablePresenterWithLogic` classes**, **3 `LoaderWithModel` classes**, and **22 custom `ISharedSingleton` services**.

#### Pattern A: Runtime-Instantiated Prefabs with Full MMVC Triads (`PlayerChip`)
- **Where in `AttaxUnity`:**
  - `F:\Unity_projects\AttaxUnity\Assets\Scripts\Ataxx.Unity\AtaxxBoardBuilder.cs:58–95`:
    ```csharp
    GameObject cellWithChip = ManageAddressables.InstantiateSyncWithAutoRelease(AddressableNames.Prefab.BoardCell, parent);
    ...
    var chipLogic = cellWithChip.GetComponentInChildren<PlayerChipLogic>();
    chipLogic.x = row;
    chipLogic.y = col;
    managerLogic.chips.Add(new Vector2(row, col), chipLogic);

    // Ensure component is initialized
    chipLogic.GetModel();
    if (!chipLogic.IsArtReady) { ... }
    chipLogic.RefreshVisuals();
    ```
  - `F:\Unity_projects\AttaxUnity\Assets\Scripts\Ataxx.Unity\PlayerChipPresenter.cs:45–62`:
    - `InitializePresenter()` calls `InitializeCellArt()` which loads sprites and calls `PopulateModelViewRefs()` to populate `[NonSerialized]` view caches on `PlayerChipDataModel` (`PlayerChipDataModel.cs:37–45`).
    - Declares **11 `[ObservableHandler]` methods** (`PlayerChipPresenter.cs:233–310`), including 0-arg (`OnShinyEnabledValueChanged`, `OnCurrentIssuesChanged`), 1-arg (`OnLegalMoveMarkerChanged(PlayerColor)`, `OnIsClickableChanged(bool)`), and 2-arg (`OnOwnerValueChanged(PlayerColor currentValue, PlayerColor previousValue)` at line 255).
  - `F:\Unity_projects\AttaxUnity\Assets\Scripts\Ataxx.Unity\PlayerChipLogic.cs:38–43`:
    ```csharp
    private void Awake() {
        managerLogic = Services.Get<AtaxxGameManagerLogic>();
        if (button != null) {
            button.onClick.AddListener(OnClick);
        }
    }
    ```
- **What Existing Framework Tests Do Instead:**
  - In `Tests\Runtime\MmvcPlayModeTests.cs:42–60` and `Tests\Editor\MMVCRestrictionsTests.cs:506–527`, tests create an empty `new GameObject("PlayerUnit")`, call `SetActive(false)`, call `AddComponent<SamplePlayerModel>()`, `AddComponent<SamplePlayerLogic>()`, `AddComponent<SamplePlayerPresenter>()`, manually assign UI fields in C#, and call `EnsureInitialized()`.
- **Specific Gaps Confirmed:**
  1. **Zero `Object.Instantiate(prefab)` tests:** No test instantiates a pre-configured prefab where `Model`, `Logic`, `Presenter`, and child UI controls (`Button`, `Image`, `TMP_Text`, `Toggle`, `Slider`) are already attached and serialized prior to instantiation.
  2. **Child-object MMVC unit (`GetComponentInChildren`):** On `BoardCell`, the MMVC unit is resolved via `cellWithChip.GetComponentInChildren<PlayerChipLogic>()` (`AtaxxBoardBuilder.cs:74`) and instantiated under an active Canvas parent (`parent`). During `Instantiate(prefab, activeParent)`, Unity fires `Awake()` on the cloned hierarchy *inside* `Instantiate` before `AtaxxBoardBuilder` even receives the returned `GameObject` reference (`line 58`)!
  3. **Execution Order on Runtime `Instantiate`:** When Unity instantiates an active prefab at runtime, `Awake()` runs on all components in `[DefaultExecutionOrder]` order:
     - `BaseObservableDataModel.Awake()` (`-300`) -> `EnsureFieldsInitialized()`
     - `BaseObservablePresenter.Awake()` (`-150`) -> `EnsureInitialized()` -> `InitializePresenter()` + `Observable.SetListeners(this, model)`
     - `BaseLogic.Awake()` (`0`) -> `ServiceLocator.instance.TryRegister(this)`
     This means by line 86 (`chipLogic.GetModel()`), `Awake()` has *already* run! If the prefab was saved inactive, `Awake()` has *not* run and `chipLogic.GetModel()` initializes it early. Neither path via `Object.Instantiate` is tested in `unity-savableobservable`.

#### Pattern B: Runtime Pooling of UI Elements Driven by `ObservableList<T>`
- **Where in `AttaxUnity`:**
  - `F:\Unity_projects\AttaxUnity\Assets\Scripts\Base\SharedScripts\GameObjectPool.cs:135–217`: Pre-creates instances via `Instantiate(prefab, targetParent, false)`, calls `instance.SetActive(false)`, hands them out via `Get()`, and deactivates/reparents them on `Release()`.
  - `F:\Unity_projects\AttaxUnity\Assets\Scripts\Ataxx.Unity\Online\OnlineLobbyPresenter.cs:61–94`: `[ObservableHandler(nameof(OnlineLobbyDataModel.players))]` calls `RebuildPlayerRows()`, which releases existing rows to `GameObjectPool` (`line 73`) and checks out/activates rows (`lines 83–87`).
  - `F:\Unity_projects\AttaxUnity\Assets\Scripts\Ataxx.Unity\MenuBackground\MenuBackgroundPresenter.cs:133–233`: `[ObservableHandler(nameof(MenuBackgroundDataModel.chips))]` reconciles `ObservableList<MenuChipRecord>` against pooled chip views via `GameObjectPool`.
- **Specific Gaps Confirmed:**
  - No test in `unity-savableobservable` tests `SetActive(false)` -> model mutation while inactive -> `SetActive(true)` -> `SetActive(false)` pooling cycles on MMVC units or `ObservableList`-driven view pools.

#### Pattern C: Deep Generic Inheritance Hierarchy (`ModalWindow<M, L>` -> `BaseConfirmModalPresenter<M, L>`)
- **Where in `AttaxUnity`:**
  - `ModalWindow<M, L>` (`Assets\Scripts\Base\Windows\ModalWindow.cs:9–165`): Abstract generic base presenter with `[AutoBind(nameof(ModalWindowDataModel.title))] protected TextMeshProUGUI titleText` (`line 18`) and `[ObservableHandler(nameof(ModalWindowDataModel.isOpen))] protected virtual void OnIsOpenChanged(bool isOpen)` (`line 68`).
  - `BaseConfirmModalPresenter<M, L>` (`Assets\Scripts\Base\Windows\BaseConfirmModalPresenter.cs:7–76`): Second-level generic abstract presenter adding 4 `[AutoBind]` fields (`lines 12–22`) and **7 `private` `[ObservableHandler]` methods** (`lines 50–69`), inherited by concrete `BaseConfirmModalPresenter` (`line 75`).
  - `NoticeModalPresenter` (`Assets\Scripts\Ataxx.Unity\NoticeModal\NoticeModalPresenter.cs:7`): Overrides `protected override void OnIsOpenChanged(bool isOpen)` WITHOUT repeating `[ObservableHandler]` on the override.
- **Specific Gaps Confirmed:**
  - `ObservableBindingTests.cs:221` tests a 1-level base class with a private handler, but does **not** test a derived class that **overrides** a `protected virtual [ObservableHandler]` method without re-declaring the `[ObservableHandler]` attribute on the override (to verify the override is called once and the base implementation is not called twice).

#### Pattern D: Two-Way `[AutoBind]` Combined with `[ObservableHandler]` and Logic Validation/Clamping on the Same Field
- **Where in `AttaxUnity`:**
  - `PlayerSettingsPresenter.cs:16–40, 119–148` & `PlayerSettingsLogic.cs:27–142`:
    - `blockedCellCount`, `hintDepth`, `masterVolume`, `soundEnabled`, `showMoveHints`, `advancedHints` have **three simultaneous listeners** on the same `ObservableVariable<T>`:
      1. Two-way `[AutoBind]` on `Slider` / `Toggle` (`PlayerSettingsPresenter.cs:16–39`).
      2. `[ObservableHandler]` on `PlayerSettingsPresenter` (`lines 119–148`, e.g., `OnBlockedCellCountChanged`, `OnMasterVolumeChanged`, `OnShowMoveHintsChanged`).
      3. Manual `model.field.OnValueChanged.Add(OnFieldChanged, this)` on `PlayerSettingsLogic.Start()` (`PlayerSettingsLogic.cs:33–42`), where the logic callback **clamps out-of-range values or mutates dependent observables re-entrantly** (e.g., `OnShowMoveHintsChanged` at line 67 sets `model.advancedHints.Value = false` when `showMoveHints` becomes `false`; `OnHintDepthChanged` at line 86 clamps `model.hintDepth.Value = clamped` inside its own `OnValueChanged` callback!).
- **Specific Gaps Confirmed:**
  - **Re-entrant clamping inside `OnValueChanged` with Two-Way `[AutoBind]`:** When a `Slider` or `TMP_InputField` sends an out-of-range value (e.g. `0` or `99`) via two-way binding (`UI -> Model`), the Logic's `OnValueChanged` handler immediately sets `model.hintDepth.Value = clamped` *while `ObservableTrackedAction.Invoke` is already mid-flight (`_isInvoking == true`)*. `ObservableTrackedAction` sets `_pending = true` and runs a second pass (`ObservableTrackedAction.cs:119–151`). No test verifies that when Logic clamps a value re-entrantly during a UI-originated two-way update, the UI control (`Slider` / `TMP_InputField` / `Toggle`) is updated to the clamped value on the second pass! (See **Defect #2** in the Issues Document below — `_isSettingValue` or re-entrancy ordering can interfere).

#### Pattern E: Cross-Unit Foreign Model Subscriptions (`BoardGraphLogic` & `MenuBackgroundLogic`)
- **Where in `AttaxUnity`:**
  - `BoardGraphLogic.cs:52–81`: Subscribes to `chipModel.owner.OnValueChanged.Add(h, this)` across all 49 `PlayerChipDataModel` units (`line 68`), and manually detaches in `DetachCellSubscriptions()` (`lines 74–81`) called from `OnDestroy()` (`line 160`).
  - `MenuBackgroundLogic.cs:45–93`: Subscribes to `AppStateDataModel.currentScreen`, `AppStateDataModel.hasActivePopup`, and `PlayerSettingsDataModel.menuAnimationEnabled` via `.OnValueChanged.Add(handler, this)`, and manually detaches in `OnDestroy()`.
- **Specific Gaps Confirmed:**
  - No test in `unity-savableobservable` tests cross-unit manual subscriptions (`foreignModel.field.OnValueChanged.Add(handler, subscriber)`) when the **subscriber** is destroyed while the **foreign model** remains alive.

---

## 2. MMVC Feature Coverage Matrix (Goal 2)

| # | Feature Area | Specific MMVC Feature / Scenario | Existing Test in Framework | Coverage Status & Identified Gap |
|---|---|---|---|---|
| **1. Lifecycle** | Creation & Provisioning | `AddComponent<M>()`, `EnsureFieldsInitialized()` auto-instantiates null `ObservableVariable<T>` and `ObservableList<T>` wrappers and links `ParentDataModel` | `ModelPersistenceTests.cs`, `MMVCRestrictionsTests.cs` | **Covered** for `AddComponent`. **Gap:** Pre-serialized fields on instantiated prefabs where Unity serializer already constructed the wrapper vs null fields, verifying `ParentDataModel` is still linked. |
| **1. Lifecycle** | Editor Auto-Provisioning | `ComponentAutoRequire` on `BaseLogic<M,P>`, `LoaderWithModel<M>`, `LoaderWithModelAndLogic<M,L>` (`Reset` immediate, `OnValidate` delayed, skip prefab assets) | `MMVCRestrictionsTests.cs:57, 386` | **Partially Covered.** **Gap:** Delayed `OnValidate` provisioning via `EditorApplication.delayCall` and prefab-asset exclusion (`PrefabUtility.IsPartOfPrefabAsset`) are not tested. |
| **1. Lifecycle** | 5-Stage Execution Order | `MMVCExecutionOrderSynchronizer`: Model (`-300`), Loader (`-250`), ServiceLocator (`-200`), Presenter (`-150`), Logic (`0`) | `MMVCRestrictionsTests.cs:565` | **Covered** for `GetTargetExecutionOrder`. **Gap:** PlayMode test verifying actual `Awake()` firing sequence across a scene hierarchy without manual `EnsureInitialized()` calls. |
| **1. Lifecycle** | Runtime Prefab Instantiation (Active Prefab) | `Object.Instantiate(activePrefab, parent)`: `Awake()` fires synchronously during `Instantiate`; Presenter binds `AutoBind` & `ObservableHandler` automatically | *None* | **UNCOVERED (Critical Gap).** Need PlayMode & EditMode tests instantiating a built prefab with UI children and verifying immediate reactivity. |
| **1. Lifecycle** | Runtime Prefab Instantiation (Inactive Prefab) | `Object.Instantiate(inactivePrefab)`: `Awake()` does not fire; `logic.GetModel()` or `presenter.EnsureInitialized()` initializes and binds while inactive; later `SetActive(true)` does not double-bind | `MMVCRestrictionsTests.cs:506` (uses `AddComponent`, not `Instantiate`) | **Partially Covered.** **Gap:** Must test with `Object.Instantiate(inactivePrefab)` followed by `SetActive(true)` in PlayMode to confirm `Awake()` after early `GetModel()` causes zero duplicate notifications. |
| **1. Lifecycle** | Enable / Disable / Pooling Cycles | `gameObject.SetActive(false)` -> model mutations while disabled -> `gameObject.SetActive(true)` -> `enabled = false` / `enabled = true` on Presenter | *None* | **UNCOVERED.** Must verify whether bindings stay active while `GameObject` is inactive (needed by `ModalWindow` and `GameObjectPool`), and how pooled units behave across checkout/release cycles. |
| **1. Lifecycle** | Destruction Order | 1) Destroy whole unit GO; 2) Destroy Presenter before Model; 3) Destroy Logic before Model; 4) Destroy foreign subscriber before foreign Model | `DomainReloadLifecycleTests.cs:212`, `MMVCRestrictionsTests.cs:151` | **Partially Covered.** **Gap:** Destroying a foreign subscriber that called `foreignModel.field.OnValueChanged.Add(h, subscriber)` when `foreignModel` survives (see Defect #4). |
| **1. Lifecycle** | Domain Reload Disabled (Fast Play Mode) | `[AutoStaticsCleanup]`, `[NoAutoStaticsCleanup]`, `SubsystemRegistration` resets on `Observable`, `UIAdapterRegistry`, `ServiceLocator`, `Services` | `DomainReloadLifecycleTests.cs:44–208` | **Covered.** |
| **2. Bindings** | `[ObservableHandler]` Signatures (0, 1, 2 args) | `ObservableVariable<T>`: `void M()`, `void M(T cur)`, `void M(T cur, T prev)`. `ObservableList<T>`: `void M()`, `void M(ObservableList<T>)`, `void M(List<T>)`, `void M(List<T>, IReadOnlyList<T>)` | `ObservableBindingTests.cs:90` (`ObservableVariable` only) | **Partially Covered.** **Gap:** `ObservableList<T>` handler signatures (0-arg, 1-arg `ObservableList<T>`, 1-arg `List<T>`, 2-arg `(List<T>, IReadOnlyList<T>)`, and `object` fallback as used in `OnlineLobbyPresenter.cs:62`) are not all unit-tested. |
| **2. Bindings** | Inheritance & Virtual Override Deduplication | Private/protected `[ObservableHandler]` and `[AutoBind]` in abstract generic base classes; overriding a `protected virtual [ObservableHandler]` method in a subclass | `ObservableBindingTests.cs:221` (private base only) | **Partially Covered.** **Gap:** Overriding a `virtual` `[ObservableHandler]` method (like `ModalWindow.OnIsOpenChanged` overridden by `PlayerSettingsPresenter.OnIsOpenChanged` and `NoticeModalPresenter.OnIsOpenChanged`) is not tested to ensure base + override aren't invoked twice. |
| **2. Bindings** | Multiple Handlers for Same Field | Multiple `[ObservableHandler("field")]` methods on the same Presenter (or base + derived) + simultaneous `[AutoBind("field")]` | `ObservableBindingTests.cs:30–44, 221` | **Covered.** |
| **2. Bindings** | Re-binding & Idempotency | `EnsureInitialized()` idempotency vs explicit `Observable.SetListeners(presenter, model)` re-binding (replaces tracked listeners without duplicating) | `MMVCRestrictionsTests.cs:539`, `ObservableBindingTests.cs:108` | **Partially Covered.** **Gap:** Calling `Observable.SetListeners` when manual subscriptions (`OnValueChanged.Add(h, presenter)`) also exist on the same presenter, and calling `SetAutoBindListeners` standalone (see Defect #3). |
| **2. Bindings** | Unbinding (`RemoveListeners`, `CleanupSubscriptions`) | `Observable.RemoveListeners(model, subscriber)` and `Observable.CleanupSubscriptions(model)` detach both `Model -> UI` and `UI -> Model` listeners | `ObservableBindingTests.cs:133, 164`, `DomainReloadLifecycleTests.cs:27` | **Covered.** |
| **3. Primitives** | `ObservableVariable<T>` | Value assignment, `PreviousValue`, `ForceNotify()`, `null` reference handling, structs/enums, Editor `OnBeginGui()`/`OnValidate()` | `ObservablePrimitivesTests.cs:17–102, 358` | **Covered.** **Gap:** Subclasses of `ObservableVariable<T>` via `ReflectionBinding` (`Observable.cs:277`). |
| **3. Primitives** | `ObservableList<T>` | `Add`, `AddRange`, `Insert`, `Remove`, `RemoveAt`, `Clear`, indexer `this[i]`, `Value` replacement, `ForceNotify()`, zero-GC delta `PreviousValue`, external mutation warning, Editor `OnBeginGui()`/`OnValidate()` | `ObservablePrimitivesTests.cs:109–355` | **Mostly Covered.** **Gap:** `ObservableList<T>.OnBeginGui()` and `OnValidate()` in Editor are not tested; `Remove(missingItem)` returning `false` without firing `OnChanged` is not tested. |
| **3. Primitives** | `ObservableTrackedAction<T>` | `Add`, `Remove`, `+=`, `-=`, duplicate prevention, `HandlerCount`, main-thread guard, re-entrant `Invoke` loop & `maxIterations = 32` cap, exception isolation per handler | `ObservablePrimitivesTests.cs:373–402` | **Partially Covered.** **Gap:** Re-entrant `Invoke` (handler mutates the same `ObservableVariable` during callback), `maxIterations` (32) infinite-loop breaker, exception isolation (one throwing handler does not block subsequent handlers), and background-thread access throwing `InvalidOperationException` have **zero tests**. |
| **4. Two-Way UI** | Built-in Display Adapters (One-Way) | `TextMeshProUGUI` / `TMP_Text`, `UnityEngine.UI.Text`, `UnityEngine.UI.Image` | `UIAdapterTests.cs:39–57, 126` | **Covered.** **Gap:** Warning log when `ImageAdapter` receives non-`Sprite` value (`UIAdapters.cs:658`). |
| **4. Two-Way UI** | Built-in Interactive Adapters (Two-Way) | `Toggle` (`bool`), `Slider` (`float`, `int`, `double`), `TMP_InputField` (`string`, `int`, `float`), legacy `InputField` (`string`, `int`, `float`) | `UIAdapterTests.cs:60–123, 208` | **Partially Covered.** **Gap:** `Slider` bound to `ObservableVariable<int>` (used in `PlayerSettingsPresenter.blockedCellCount` and `hintDepth`), `TMP_InputField` bound to numeric `int`/`float` (including empty string `""` resetting to `0` and invalid text `"abc"` ignored), and legacy `InputField` two-way binding are not tested! |
| **4. Two-Way UI** | Feedback Loop & Re-entrant Clamping | 1) `Model -> UI` uses `Set*WithoutNotify` (no bounce-back); 2) `UI -> Model` when Logic clamps value in `OnValueChanged` (`UI -> Model -> Logic Clamp -> Model -> UI`) | `UIAdapterTests.cs:147, 208` (tests direct adapter, not full `[AutoBind]` + Logic clamping) | **Critical Gap.** Full `[AutoBind]` two-way binding with re-entrant Logic clamping (e.g. `Slider` sets `10`, Logic clamps to `6`, `Slider.value` must become `6`) is untested! |
| **4. Two-Way UI** | Binding During Initialization | `[AutoBind]` setup does not overwrite model with default UI value during `SetListeners`, nor does `SetListeners` push initial model value to UI unless `InitializePresenter` or `ForceNotify` runs | *None* | **UNCOVERED.** Must explicitly test that `SetListeners` on a `Toggle` (`isOn = false`) bound to a Model (`Value = true`) does not clobber the Model during binding, and verify how initial UI sync works. |
| **5. Services & Persistence** | `ServiceLocator` & `Services` | Pass 1 / Pass 2 startup, `TryRegister`, rejection of Presenters/Models/interfaces/plain classes, named singletons, collision eviction & recovery, dynamic `BaseLogic` register/unregister | `MMVCRestrictionsTests.cs:112–359, 576`, `MmvcPlayModeTests.cs:117` | **Covered.** **Gap:** Multi-instance prefabs inheriting `BaseLogic` without distinct `singletonName` colliding in `ServiceLocator` (see Defect #1). |
| **5. Services & Persistence** | `BaseObservableDataModel.LoadDataFromModel` & `LoaderWithModel` | Copy properties, `ObservableVariable`, `ObservableList`, plain fields, `[NonSaved]` skip, list cloning, loader rebind | `ModelPersistenceTests.cs:28–202`, `MMVCRestrictionsTests.cs:368` | **Covered.** |

---

## 3. Separate Issues Document: Code Defects Found (Goal 5)

*(Note: Per session rules, no source files are modified in this session. These defects are documented here with exact file/line references and root-cause analysis for a dedicated implementation pass.)*

### Issue 1: `BaseLogic` Auto-Registration in `Awake()` Collides for Multi-Instance Prefabs (`PlayerChipLogic`) and Is Silently Hidden by Derived `private void Awake()`
- **Severity:** High (Architectural & Lifecycle Defect)
- **Locations:**
  - Framework: `F:\github\repo\unity-savableobservable\Runtime\BaseLogic.cs:28–38, 63–65`
  - AttaxUnity: `F:\Unity_projects\AttaxUnity\Assets\Scripts\Ataxx.Unity\PlayerChipLogic.cs:38` (`private void Awake()`), `F:\Unity_projects\AttaxUnity\Assets\Scripts\Ataxx.Unity\Abilities\AbilityPopupLogic.cs:11` (`private void Awake()`)
- **Description & Root Cause:**
  1. In `BaseLogic.cs:28–32`, `protected virtual void Awake()` unconditionally calls `ServiceLocator.instance.TryRegister(this)`.
  2. In `AttaxUnity`, `PlayerChipLogic` (`PlayerChipLogic.cs:38`) and `AbilityPopupLogic` (`AbilityPopupLogic.cs:11`) declare `private void Awake()` without the `override` keyword ( causing compiler warning `CS0114` and hiding `BaseLogic.Awake()`).
  3. **Why this is a trap in both directions:**
     - For `AbilityPopupLogic` (a scene singleton): If `AbilityPopupLogic` is instantiated dynamically or after `ServiceLocator.Awake()`, its `private void Awake()` hides `BaseLogic.Awake()`, so it **fails to auto-register** with `ServiceLocator`!
     - For `PlayerChipLogic` (a multi-instance board cell prefab instantiated 49 times by `AtaxxBoardBuilder.cs:58`): `PlayerChipLogic` derives from `BaseLogic<PlayerChipDataModel, PlayerChipPresenter>`, which derives from `BaseLogic : ISharedSingleton`. All 49 instances have `singletonName == ""` (defaulting to `"PlayerChipLogic"`). Currently, `PlayerChipLogic` only avoids registering 49 colliding instances during `AtaxxBoardBuilder.Build()` because its `private void Awake()` accidentally hides `BaseLogic.Awake()`, AND if `ServiceLocator.InitializeSharedSingletons()` runs while the 49 cells exist, all 49 `PlayerChipLogic` instances are registered in Pass 1 and immediately evicted (`RegistrationResult.EvictedDuplicate`)!
- **Recommended Fix (for later implementation):**
  - Provide an opt-out attribute or virtual property on `BaseLogic` (e.g., `protected virtual bool RegisterAsSharedSingleton => true;` or `[NonSingletonLogic]`) for multi-instance prefab unit logics like `PlayerChipLogic` that are managed by a parent collection (`managerLogic.chips`) rather than `ServiceLocator`, AND fix `AbilityPopupLogic.Awake()` / `PlayerChipLogic.Awake()` to use `protected override void Awake() { base.Awake(); ... }`.

### Issue 2: `ObservableTrackedAction<T>.Invoke` Re-Entrancy Drops Intermediate `PreviousValue` Context and Interacts Subtlely with UI Adapter Guards
- **Severity:** Medium (Behavioral Edge Case in Two-Way Binding + Clamping)
- **Locations:**
  - Framework: `F:\github\repo\unity-savableobservable\Runtime\ObservableTrackedAction.cs:115–152`
  - Framework: `F:\github\repo\unity-savableobservable\Runtime\ObservableVariable.cs:56–63`
  - AttaxUnity consumer: `F:\Unity_projects\AttaxUnity\Assets\Scripts\Ataxx.Unity\PlayerSettings\PlayerSettingsLogic.cs:67–142`
- **Description & Root Cause:**
  In `ObservableVariable<T>.Value` setter (`ObservableVariable.cs:58–62`):
  ```csharp
  set {
      PreviousValue = _value;
      _value = value;
      OnValueChanged?.Invoke(this);
  }
  ```
  When handler #1 (e.g. `PlayerSettingsLogic.OnHintDepthChanged` at `PlayerSettingsLogic.cs:86`) mutates `model.hintDepth.Value = clamped` *inside* `OnValueChanged`:
  1. The inner setter overwrites `PreviousValue` and `_value` **immediately while the outer `for (int i = 0; i < handlers.Length; i++)` loop in `ObservableTrackedAction.Invoke` is still iterating over the remaining handlers (`handlers[1..N]`)!**
  2. Consequently, handlers `i + 1 .. N` in the *first* pass observe the *already-mutated* `_value` and `PreviousValue` from the inner assignment, and then `ObservableTrackedAction` runs a *second* full pass (`while (_pending)`) with the exact same `_value` and `PreviousValue`, invoking handlers a second time with identical values!
  3. Furthermore, if a 2-parameter `[ObservableHandler]` (`void OnChanged(T current, T previous)`) runs after a clamping handler, `current` and `previous` can both equal the clamped value or reflect the transient invalid value as `PreviousValue`.

### Issue 3: Public `Observable.SetAutoBindListeners` Does Not Clear Existing `[AutoBind]` Subscriptions When Called Directly
- **Severity:** Medium (Duplicate Listener / Leak Hazard)
- **Location:** `F:\github\repo\unity-savableobservable\Runtime\Observable.cs:559–595`
- **Description & Root Cause:**
  While `Observable.SetListeners(subscriber, model)` calls `RemoveAllSubscriptions(model, subscriber)` (`Observable.cs:443`) before wiring `[ObservableHandler]` and `[AutoBind]`, the public method `Observable.SetAutoBindListeners(obj)` / `Observable.SetAutoBindListeners(obj, dataModel)` (`Observable.cs:559–595`) calls `SetAutoBindListenersInternal` **without** removing previous `[AutoBind]` subscriptions or calling `model.EnsureFieldsInitialized()`. Calling `SetAutoBindListeners` twice on the same object duplicates both the `Model -> UI` handler and the `UI -> Model` `UnityEvent` listener.

### Issue 4: Foreign Model Subscriptions (`foreignModel.field.OnValueChanged.Add(handler, subscriber)`) Are Not Cleaned Up When the Subscriber Is Destroyed Before the Foreign Model
- **Severity:** High (Memory Leak / `MissingReferenceException` Hazard)
- **Locations:**
  - Framework: `F:\github\repo\unity-savableobservable\Runtime\ObservableTrackedAction.cs:79–92` & `F:\github\repo\unity-savableobservable\Runtime\Observable.cs:766–777`
  - AttaxUnity workarounds: `F:\Unity_projects\AttaxUnity\Assets\Scripts\Ataxx.Unity\MenuBackground\MenuBackgroundLogic.cs:53–93` & `F:\Unity_projects\AttaxUnity\Assets\Scripts\Ataxx.Unity\Graph\BoardGraphLogic.cs:68–81`
- **Description & Root Cause:**
  `ObservableTrackedAction.Add(handler, subscriber)` registers the subscription in `Observable._instanceData[ParentDataModel].Subscriptions[subscriber]`.
  Automatic cleanup only runs in `BaseObservableDataModel.OnDestroy()` -> `Observable.CleanupSubscriptions(this)` (`BaseObservableDataModel.cs:49–52`).
  - If `subscriber` is on a **different** GameObject (a foreign unit, which is the *required* pattern for cross-unit observation since `SetListeners` rejects cross-GameObject binding) and `subscriber` is destroyed while `ParentDataModel` remains alive, `ParentDataModel`'s `ObservableTrackedAction._handlers` still holds `handler` (which captures `subscriber`).
  - Moreover, `ObservableTrackedAction.Invoke` (`ObservableTrackedAction.cs:135–140`) does **not** check if the subscriber `UnityEngine.Object` (`handler.Target as UnityEngine.Object`) has been destroyed before invoking `handlers[i](variable)`.

### Issue 5: `Observable.SetListeners` Re-Bind Wipes Out Manual Tracked Subscriptions Registered on the Same Subscriber
- **Severity:** Medium (Surprise Subscription Loss on Load)
- **Locations:**
  - Framework: `F:\github\repo\unity-savableobservable\Runtime\Observable.cs:443, 850–873` (`RemoveAllSubscriptions`)
  - Framework: `F:\github\repo\unity-savableobservable\Runtime\LoaderWithModel.cs:50–54`
- **Description & Root Cause:**
  When a `Presenter` or `Logic` registers a manual tracked subscription on its own model via `model.someField.OnValueChanged.Add(handler, this)`, `Observable.RegisterSubscription` stores it under `_instanceData[model].Subscriptions[this]`.
  Later, when `LoaderWithModel.LoadDataFromModel` (or `PrefsModelLoader.LoadDataFromModel`) calls `Observable.SetListeners(presenter, model)`, `SetListenersCore` calls `RemoveAllSubscriptions(model, subscriber)` (`Observable.cs:443`).
  `RemoveAllSubscriptions` wipes **both** `instanceData.UnbindActions[subscriber]` (the attribute-based bindings) **and** `instanceData.Subscriptions[subscriber]` (any manual `.OnValueChanged.Add(h, presenter)` callbacks), but `SetListenersCore` only re-establishes the attribute-based `[ObservableHandler]` and `[AutoBind]` bindings! Any manual subscription registered by that `presenter` in `InitializePresenter()` or `Awake()` is permanently lost after `LoadDataFromModel`!

### Issue 6: `ObservableList<T>.OnValidate()` and `ObservableVariable<T>.OnValidate()` Can Fire Editor Notifications Before `ParentDataModel` Is Linked
- **Severity:** Low (Editor Inspector Edge Case)
- **Locations:**
  - `F:\github\repo\unity-savableobservable\Editor\PropertyResolver.cs:27–32`
  - `F:\github\repo\unity-savableobservable\Runtime\ObservableList.cs:166–195`
- **Description & Root Cause:**
  In `PropertyResolver.GetTargetObjectOfProperty` (`PropertyResolver.cs:27–32`), if an `ObservableVariable` or `ObservableList` is nested inside an array/list element whose path contains multiple indexed segments or if `GetValueByPathSegment` encounters a null intermediate during Inspector multi-object editing, `GetTargetObjectOfProperty` can fail or invoke `OnValidate()` when `_items` was just deserialized by Unity without `EnsureFieldsInitialized()` having linked `ParentDataModel`.

### Issue 7: `MmvcSceneBuilder.cs` Hardcodes Legacy Scene Directory `"Assets/Scripts/unity-savableobservable/Scenes"`
- **Severity:** Low (Tooling Path Mismatch)
- **Location:** `F:\github\repo\unity-savableobservable\Tests\Editor\Pipeline\MmvcSceneBuilder.cs:21`
- **Description & Root Cause:**
  `MmvcSceneBuilder.SceneDirectory` is hardcoded to `"Assets/Scripts/unity-savableobservable/Scenes"`, which does not match either the UPM package structure (`Scenes/`) or the new default Unity project layout (`Assets/Scenes/`).

---

## 4. Default Unity Test Project Structure for the Framework (Deliverable 2)

To convert `F:\github\repo\unity-savableobservable` into a clean default Unity `6000.6.7f1` project (matching `AttaxUnity`'s exact editor version `6000.6.7f1 (b22767f04f04)`) with the UPM package embedded at `Packages/com.evialdev.savableobservable`, the repository will have the following layout:

```text
F:\github\repo\unity-savableobservable\                  <-- Unity Project Root (Unity 6000.6.7f1)
├── Assets\
│   ├── Scenes\
│   │   ├── Demo.unity                                   (moved from root Scenes/)
│   │   └── MmvcVerificationScene.unity                  (moved from root Scenes/)
│   └── Tests\                                           (optional project-level integration fixtures if needed)
├── Packages\
│   ├── manifest.json                                    (declares com.unity.ugui, com.unity.test-framework, com.unity.pipeline, etc.)
│   ├── packages-lock.json
│   └── com.evialdev.savableobservable\                  <-- Embedded UPM Package Root
│       ├── package.json
│       ├── README.md
│       ├── CHANGELOG.md
│       ├── LICENSE
│       ├── Runtime\
│       │   ├── com.evialdev.savableobservable.asmdef
│       │   ├── AutoBindAttribute.cs
│       │   ├── BaseLogic.cs
│       │   ├── BaseObservableDataModel.cs
│       │   ├── BaseObservablePresenter.cs
│       │   ├── BasePresenter.cs
│       │   ├── ComponentAutoRequire.cs
│       │   ├── IObservablePresenter.cs
│       │   ├── IObservableVariable.cs
│       │   ├── ISharedSingleton.cs
│       │   ├── LifecycleManagementCompatibility.cs
│       │   ├── LoaderWithModel.cs
│       │   ├── LoaderWithModelAndLogic.cs
│       │   ├── NonSavedAttribute.cs
│       │   ├── Observable.cs
│       │   ├── ObservableHandlerAttribute.cs
│       │   ├── ObservableList.cs
│       │   ├── ObservablePresenterWithLogic.cs
│       │   ├── ObservableTrackedAction.cs
│       │   ├── ObservableVariable.cs
│       │   ├── ServiceLocator.cs
│       │   ├── Services.cs
│       │   └── UIAdapters.cs
│       ├── Editor\
│       │   ├── com.evialdev.savableobservable.Editor.asmdef
│       │   ├── MMVCExecutionOrderSynchronizer.cs
│       │   ├── ObservableListDrawer.cs
│       │   ├── ObservableVariableDrawer.cs
│       │   └── PropertyResolver.cs
│       ├── Samples\
│       │   ├── com.evialdev.savableobservable.Samples.asmdef
│       │   └── Scripts\
│       │       ├── SamplePlayerLoader.cs
│       │       ├── SamplePlayerLogic.cs
│       │       ├── SamplePlayerModel.cs
│       │       └── SamplePlayerPresenter.cs
│       └── Tests\
│           ├── Shared\                                  <-- NEW: Reusable Generic Contract & Parameterized Test Base
│           │   ├── com.evialdev.savableobservable.TestContracts.asmdef
│           │   ├── MmvcUnitContractTestsBase.cs
│           │   ├── MmvcModalContractTestsBase.cs
│           │   ├── MmvcPersistenceContractTestsBase.cs
│           │   └── PrefsSandboxUtility.cs
│           ├── Editor\
│           │   ├── com.evialdev.savableobservable.EditorTests.asmdef
│           │   ├── DomainReloadLifecycleTests.cs
│           │   ├── MMVCRestrictionsTests.cs
│           │   ├── ModelPersistenceTests.cs
│           │   ├── ObservableBindingTests.cs
│           │   ├── ObservablePrimitivesTests.cs
│           │   ├── UIAdapterTests.cs
│           │   ├── PrefabAndPoolingLifecycleTests.cs    <-- NEW: Covers runtime Instantiate, inactive/active prefabs, pooling
│           │   ├── TrackedActionAndForeignSubTests.cs   <-- NEW: Covers re-entrancy, maxIterations, thread safety, foreign subs
│           │   ├── SampleMmvcContractTests.cs           <-- NEW: Concrete instantiation of generic contract tests for SamplePlayer
│           │   └── Pipeline\
│           │       ├── com.evialdev.savableobservable.Tests.Pipeline.asmdef
│           │       ├── MmvcSceneBuilder.cs
│           │       └── MmvcSelfTest.cs
│           └── Runtime\
│               ├── com.evialdev.savableobservable.RuntimeTests.asmdef
│               ├── MmvcPlayModeTests.cs
│               └── MmvcPrefabPlayModeTests.cs           <-- NEW: PlayMode Awake order, Instantiate, OnEnable/OnDisable/OnDestroy
├── ProjectSettings\
│   ├── ProjectVersion.txt                               (6000.6.7f1 (b22767f04f04))
│   ├── ProjectSettings.asset
│   ├── EditorSettings.asset
│   └── ... (minimal clean Unity 6 settings)
├── Tools\
│   └── Invoke-UnityTests.ps1                            <-- NEW: Unified Unattended PowerShell Test Runner & Reporter
├── mmvc-unity-skill\
└── .gitignore                                           (ignores Library/, Temp/, Logs/, obj/, UserSettings/, TestResults/)
```

### Consumer Reference Update (`AttaxUnity`)
In `F:\Unity_projects\AttaxUnity\Packages\manifest.json`:
- Update line 7 from:
  `"com.evialdev.savableobservable": "file:F:/github/repo/unity-savableobservable"`
  to:
  `"com.evialdev.savableobservable": "file:F:/github/repo/unity-savableobservable/Packages/com.evialdev.savableobservable"`
- And for external Git UPM consumers, `https://github.com/alexxtaurus83/unity-savableobservable.git?path=/Packages/com.evialdev.savableobservable` works natively with UPM's `?path=` subfolder syntax.

---

## 5. Test Reuse & Separation Classification (`AttaxUnity` -> Framework vs. Attax) (Goal 4)

Every one of the **26 test files** found across `F:\Unity_projects\AttaxUnity` (`Assets\Tests\EditMode` and `Assets\Tests\Scenarios`) has been inspected and classified below:

| # | Test File in `F:\Unity_projects\AttaxUnity` | Classification | Action & Destination | Rationale |
|---|---|---|---|---|
| 1 | `Assets\Tests\EditMode\AppStateNavigationTests.cs` (Lines 269–389: `ServiceLocator_RejectsPresenterAndModelRegistration`, `ServiceLocator_CollisionEviction_AndModelLookup`, `ServiceLocator_TypeCacheValidation_EnsuresNoPresentersOrModelsImplementISharedSingleton`, `ServiceLocator_DistinctNames_PruningAndNonSavedInspection`) | **Universal (Split / Adapt)** | **Copy & Generalize** lines 269–389 into `Packages/com.evialdev.savableobservable/Tests/Editor/MMVCRestrictionsTests.cs` (especially `ServiceLocator_TypeCacheValidation_EnsuresNoPresentersOrModelsImplementISharedSingleton` which is currently **missing** in the framework repo!). Keep `OpenPopup`/`Back`/`HandleBackKey`/`Resolver` tests (lines 37–266) in `AttaxUnity`. | The `TypeCache` validation test (`lines 340–348`) is 100% universal and automatically validates whatever project it runs in! The other 3 tests verify universal `ServiceLocator` rules using Attax types and can be parameterized via the new shared contract fixture. |
| 2 | `Assets\Tests\EditMode\ModalWindowLifecycleTests.cs` | **Hybrid (Extract Universal Pattern + Keep Attax Modals)** | **Adapt universal lifecycle pattern** into `Packages/com.evialdev.savableobservable/Tests/Editor/PrefabAndPoolingLifecycleTests.cs` and `MmvcModalContractTestsBase.cs`; **keep** `ModalWindowLifecycleTests.cs` in `AttaxUnity` refactored to inherit from `MmvcModalContractTestsBase<TModel, TLogic, TPresenter>`. | Tests defensive initialization before `Awake` (`model.isOpen = null` -> `logic.IsOpen` / `logic.Open()`), `EnsureInitialized()` idempotency preserving state, and subclass `InitializePresenter()` running before `base.InitializePresenter()` wires listeners. |
| 3 | `Assets\Tests\EditMode\Playtest\PrefsSandboxTests.cs` | **Hybrid (Extract Universal PlayerPrefs Sandbox + Keep Attax Keys)** | **Copy & Generalize** core `PrefsSandbox` PlayerPrefs/directory backup-and-restore tests into `Packages/com.evialdev.savableobservable/Tests/Editor/ModelPersistenceTests.cs` (if `PrefsSandboxUtility` is shared in `TestContracts`); **keep** Attax-specific key tracking tests (`line 199`) in `AttaxUnity`. | `PrefsSandbox` is a reusable test isolation utility for any `PlayerPrefs`/file-backed `LoaderWithModel<M>`, with only the tracked key list being Attax-specific. |
| 4 | `Assets\Tests\EditMode\Online\OnlineSessionAndClientTests.cs` (`OnlineProfileServiceTests`, lines 18–89) | **Hybrid (Adapt Corrupt-JSON Loader Pattern)** | **Adapt** the corrupt-JSON backup & refusal-to-overwrite test pattern (`lines 48–88`) into `Packages/com.evialdev.savableobservable/Tests/Shared/MmvcPersistenceContractTestsBase.cs`; **keep** `OnlineSessionTests` & `OnlineGameClientTests` (`lines 91–489`) in `AttaxUnity`. | `OnlineProfileServiceTests` verifies two universal MMVC patterns: (1) a service with no view/model does not provision a dummy `BaseObservableDataModel` (`line 56`), and (2) corrupt persisted JSON is backed up to `_CorruptBackup` and never overwritten on subsequent saves (`lines 77–88`, identical to `PrefsModelLoader.cs:21–24, 65–70`). |
| 5 | `Assets\Tests\EditMode\BoardGraphLogicTests.cs` | **Specific to Attax (with Universal Cross-Unit Subscription Pattern)** | **Keep** in `AttaxUnity`; **extract** the cross-unit cell-subscription pattern (`BindToBoardCells` / `DetachCellSubscriptions` on `ObservableList` + `eventToken`) into `TrackedActionAndForeignSubTests.cs` in the framework. | Tests `BoardGraphLogic` layout math (7x7 grid) and `nodeOwners` `ObservableList` / `eventToken` notifications. |
| 6 | `Assets\Tests\EditMode\BaseSharedSystemsTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`** (`Assets\Tests\EditMode\BaseSharedSystemsTests.cs`). | Tests `CsvLoader`, `JsonDataStore`, `MatchTimerLogic`, and `AbilityCalloutFx` duration scaling. |
| 7 | `Assets\Tests\EditMode\RulesSessionParityTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Bitboard Ataxx engine vs `RulesSession` parity. |
| 8 | `Assets\Tests\EditMode\RulesSessionComprehensiveTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Ataxx move, clone, jump, pass, and scoring rules. |
| 9 | `Assets\Tests\EditMode\RulesSessionAbilityTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Ataxx abilities (`SuperJump`, `DoubleMove`, `Convert`, `Block`, `Bomb`, `Unblock`). |
| 10 | `Assets\Tests\EditMode\Playtest\BoardInvariantsTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Ataxx board piece count and coordinate invariants. |
| 11 | `Assets\Tests\EditMode\Playtest\BotOutputParserTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | External bot CLI output parser. |
| 12 | `Assets\Tests\EditMode\Playtest\ScenarioRegistryTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Playtest scenario registration and lookup. |
| 13 | `Assets\Tests\EditMode\Playtest\ServerEnvironmentTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Online server environment URL resolution. |
| 14 | `Assets\Tests\EditMode\PlatformProgressSyncTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Google Play / Game Center achievement & cloud save sync. |
| 15 | `Assets\Tests\EditMode\MatchResultSnapshotTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Match outcome and score snapshot calculation. |
| 16 | `Assets\Tests\EditMode\AtaxxTensorEncoderTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Sentis neural network board tensor encoding. |
| 17 | `Assets\Tests\EditMode\Online\ActionIntentMapperTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Network protocol `ActionIntentDto` mapping. |
| 18 | `Assets\Tests\EditMode\Online\ContractsAndSnapshotTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Server DTO serialization contracts. |
| 19 | `Assets\Tests\EditMode\Online\DisplayNameRulesTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Player display name validation rules. |
| 20 | `Assets\Tests\EditMode\Online\SecureStoreTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Encrypted credential storage. |
| 21 | `Assets\Tests\EditMode\Online\OnlineTestSupport.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Fake transport/clock helpers for online tests. |
| 22 | `Assets\Tests\Scenarios\ScenarioFixtureBase.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Enters PlayMode on `Merge Conflict.unity` and pumps `ScenarioRunner`. |
| 23 | `Assets\Tests\Scenarios\MenuNavigationTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | End-to-end PlayMode menu navigation scenario. |
| 24 | `Assets\Tests\Scenarios\ModeTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | End-to-end PlayMode PvAI, PvP, AIvAI scenarios. |
| 25 | `Assets\Tests\Scenarios\OnlineTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | End-to-end PlayMode online scenarios. |
| 26 | `Assets\Tests\Scenarios\SoakTests.cs` | **Specific to Attax** | **Stays in `AttaxUnity`**. | Long-running PlayMode soak scenarios. |

---

## 6. Design for Generic, Parameterized Tests Shared Between Framework and `AttaxUnity` (Goal 6b)

### 6.1 Architectural Goal
Write the MMVC lifecycle, binding, restriction, two-way UI, and persistence verification logic **once** inside the `com.evialdev.savableobservable` package so that:
1. In the **MMVC framework project**, the tests run against `SamplePlayerModel` / `SamplePlayerLogic` / `SamplePlayerPresenter` / `SamplePlayerLoader` (and synthetic test fixtures).
2. In **`AttaxUnity`**, the **exact same test methods** automatically run against every real game MMVC unit (`PlayerSettings`, `NoticeModal`, `GameOverBanner`, `PlayerChip`, `AbilityPopup`, `OnlineLobby`, `AppState`, `BoardGraph`, etc.) with minimal 3-line subclass declarations or `[TestFixture(typeof(TModel), typeof(TLogic), typeof(TPresenter))]` attributes!

### 6.2 Assembly Structure (`com.evialdev.savableobservable.TestContracts.asmdef`)
Create a shared test-contract assembly inside the package:
- **Path:** `Packages/com.evialdev.savableobservable/Tests/Shared/com.evialdev.savableobservable.TestContracts.asmdef`
- **Configuration:**
  - `"name": "com.evialdev.savableobservable.TestContracts"`
  - `"defineConstraints": ["UNITY_INCLUDE_TESTS"]`
  - `"includePlatforms": []` (usable from both EditMode and PlayMode test assemblies!)
  - `"references": ["UnityEngine.TestRunner", "com.evialdev.savableobservable", "Unity.TextMeshPro", "UnityEngine.UI"]`
  - `"overrideReferences": true`, `"precompiledReferences": ["nunit.framework.dll"]`
- Because `"com.evialdev.savableobservable"` is listed in `"testables"` in `AttaxUnity/Packages/manifest.json` (`line 67`), `AttaxUnity.EditModeTests.asmdef` and `AttaxUnity.ScenarioTests.asmdef` can directly reference `"com.evialdev.savableobservable.TestContracts"`!

### 6.3 Generic Parameterized Base Test Fixtures

#### Fixture 1: `MmvcTriadContractTestsBase<TModel, TLogic, TPresenter>`
Verifies every universal contract for any `(Model, Logic, Presenter)` triad:
```csharp
namespace SavableObservable.Tests.Contracts {
    [TestFixture]
    public abstract class MmvcTriadContractTestsBase<TModel, TLogic, TPresenter>
        where TModel : BaseObservableDataModel
        where TLogic : BaseLogic<TModel, TPresenter>
        where TPresenter : BaseObservablePresenter<TModel>
    {
        protected GameObject Host;
        protected TModel Model;
        protected TLogic Logic;
        protected TPresenter Presenter;

        // Hook for subclasses (e.g. PlayerChipPresenter) that need mock child UI wired before InitializePresenter
        protected virtual void ConfigurePrefabOrViewReferences(GameObject host, TModel model, TLogic logic, TPresenter presenter) { }

        [Test]
        public void Contract_SingleUnitRestrictions_Enforced() {
            // Verifies [DisallowMultipleComponent] on TModel, TLogic, TPresenter
            // Verifies ServiceLocator rejects TModel and TPresenter registration
        }

        [Test]
        public void Contract_ModelFields_AllInitializeNonNull_AndLinkParentModel() {
            // Calls Model.EnsureFieldsInitialized()
            // Reflects all GetCachedObservableFields(), asserts every ObservableVariable/ObservableList is non-null
            // Asserts ParentDataModel == Model on every field's TrackedAction
        }

        [Test]
        public void Contract_InactivePrefabInstantiation_GetModelInitializesAndBinds_AndSetActiveDoesNotDoubleBind() {
            // 1. Builds inactive template GO with TModel, TLogic, TPresenter + ConfigurePrefabOrViewReferences
            // 2. Calls Object.Instantiate(template) while inactive
            // 3. Calls clonedLogic.GetModel() -> asserts clonedPresenter is initialized & bound
            // 4. Calls clone.SetActive(true) -> asserts EnsureInitialized is idempotent with 0 duplicate handlers
        }

        [Test]
        public void Contract_EveryObservableHandlerMethod_HasValidSignatureAndMatchingModelField() {
            // Inspects all [ObservableHandler] methods across TPresenter's inheritance chain (including private base methods)
            // Verifies every attr.VariableName exists in TModel.GetCachedObservableFields()
            // Verifies parameter count (0, 1, or 2) and parameter type compatibility with the field's Value / PreviousValue
        }

        [Test]
        public void Contract_EveryAutoBindField_HasMatchingModelFieldAndRegisteredUIAdapter() {
            // Inspects all [AutoBind] fields across TPresenter's inheritance chain
            // Verifies target ObservableVariable exists on TModel
            // Verifies UIAdapterRegistry.GetAdapter(uiField.FieldType) != null
        }

        [Test]
        public void Contract_TwoWayAutoBindFields_RoundtripAndNoBounceBack() {
            // Automatically discovers all [AutoBind] fields on TPresenter whose FieldType implements IUIListenerAdapter (Toggle, Slider, TMP_InputField, InputField)
            // Instantiates dummy UI component for any unpopulated [AutoBind] field
            // Binds via Observable.SetListeners(Presenter, Model)
            // Tests Model -> UI (verifies UI updates and Model OnValueChanged count == 1, no bounce-back)
            // Tests UI -> Model (simulates UI control change and verifies Model field updates)
            // Tests Observable.RemoveListeners(Model, Presenter) detaches UI -> Model listener
        }

        [Test]
        public void Contract_DestroyPresenterBeforeModel_And_DestroyModel_CleansUpAllSubscriptions() {
            // Binds Presenter to Model, mutates every ObservableVariable field via default/sample values
            // Destroys Presenter first, mutates all fields again -> asserts zero exceptions
            // Destroys Model -> asserts CleanupSubscriptions empties all tracked handlers
        }
    }
}
```

#### Fixture 2: `MmvcAssemblyAuditTestsBase` (Zero-Config Reflection & `TypeCache` Suite)
A single base class where a subclass only specifies `protected abstract Assembly[] TargetAssemblies { get; }`:
- Automatically discovers **every** `BaseObservableDataModel`, `BaseLogic`, `BasePresenter`, `LoaderWithModel`, and `ISharedSingleton` across the target assemblies using NUnit `[TestCaseSource]`!
- Runs parameterized tests for **every discovered class in the project**:
  1. `Audit_AllModels_EnsureFieldsInitialized_And_LoadDataFromModel_Roundtrip(Type modelType)`
  2. `Audit_AllPresenters_ObservableHandlers_And_AutoBinds_MatchModelFields(Type presenterType)` (This automatically checks all 26 Presenters in `AttaxUnity` for typos in `[ObservableHandler("...")]` or `[AutoBind("...")]` and invalid parameter types!)
  3. `Audit_AllLogics_ExecutionOrder_And_AwakeDoesNotHideBaseAwake(Type logicType)` (Uses reflection to check if any `BaseLogic` subclass declares `Awake` with `MethodAttributes.Private` / without overriding `BaseLogic.Awake()`, which immediately catches `PlayerChipLogic.Awake` and `AbilityPopupLogic.Awake`!)
  4. `Audit_AllSharedSingletons_AreMonoBehaviours_AndNotPresentersOrModels(Type singletonType)`

#### How Both Projects Consume These Shared Tests
- **In `unity-savableobservable` (`SampleMmvcContractTests.cs`):**
  ```csharp
  public class SamplePlayerContractTests : MmvcTriadContractTestsBase<SamplePlayerModel, SamplePlayerLogic, SamplePlayerPresenter> { }
  public class FrameworkAssemblyAuditTests : MmvcAssemblyAuditTestsBase {
      protected override Assembly[] TargetAssemblies => new[] { typeof(BaseObservableDataModel).Assembly, typeof(SamplePlayerModel).Assembly };
  }
  ```
- **In `AttaxUnity` (`AttaxMmvcContractTests.cs`):**
  ```csharp
  public class PlayerSettingsContractTests : MmvcTriadContractTestsBase<PlayerSettingsDataModel, PlayerSettingsLogic, PlayerSettingsPresenter> { }
  public class NoticeModalContractTests : MmvcTriadContractTestsBase<NoticeModalDataModel, NoticeModalLogic, NoticeModalPresenter> { }
  public class GameOverBannerContractTests : MmvcTriadContractTestsBase<GameOverBannerDataModel, GameOverBannerLogic, GameOverBannerPresenter> { }
  public class OnlineLobbyContractTests : MmvcTriadContractTestsBase<OnlineLobbyDataModel, OnlineLobbyLogic, OnlineLobbyPresenter> { }
  public class AttaxAssemblyAuditTests : MmvcAssemblyAuditTestsBase {
      protected override Assembly[] TargetAssemblies => new[] { typeof(AtaxxGameManagerLogic).Assembly };
  }
  ```

---

## 7. Test Execution in Unity & Automated PowerShell Runner Design (Goals 3 & 6a)

### 7.1 How Tests Run Across Modes (EditMode, PlayMode, and Batch/CLI)
Using the `unity-cli` and `unity-pipeline` skills, there are **two distinct execution paths** depending on whether the project is open in an interactive Unity Editor or closed:

| Scenario | How Tests Execute | Command / Mechanism | Manual Action Required? |
|---|---|---|---|
| **Mode A: Live Open Unity Editor** (via `com.unity.pipeline`) | Fast in-process execution via Pipeline server (`ports 7800–7849`). Avoids Editor startup cost and runs even when Editor is open. | 1. `unity status --project-path <path> --format json`<br>2. `unity command set_autotick --enable true --project-path <path>`<br>3. `unity recompile --project-path <path> --format json`<br>4. `unity command run_tests --mode editor --project-path <path>`<br>5. `unity command run_tests --mode playmode --project-path <path>`<br>6. `unity command mmvc_selftest --mode all --project-path <path>` | **None**, unless the Editor is in Safe Mode, blocked by a modal OS/Editor dialog (`blocked_by_dialog`), or the Pipeline package was just added to `manifest.json` while the Editor was open (requiring a one-time window focus to trigger asset import). |
| **Mode B: Unattended Batch / CLI** (No Editor open for that project, or CI) | Headless/batch execution via `unity test` and `unity run --command`. Writes NUnit3 XML + JUnit XML reports directly. | 1. `unity test <path> --mode EditMode --report-format nunit,junit --output <dir>/editmode.xml --timeout 600 --format json`<br>2. `unity test <path> --mode PlayMode --report-format nunit,junit --output <dir>/playmode.xml --timeout 900 --format json`<br>3. `unity run <path> --command mmvc_selftest --format json --timeout 300 -- --mode all` | **None**, unless a Unity Editor is *already open* on that project (in which case `unity test` exits `6` with `"already open in a running Editor"` — our PowerShell script automatically detects this and routes to Mode A or fails fast if the Editor is mid-PlayMode). |

### 7.2 Design of the Unified PowerShell Test Runner (`Tools/Invoke-UnityTests.ps1`)
A single, self-contained PowerShell 7+ script (`Invoke-UnityTests.ps1`) that runs unattended against **either or both** repositories (`F:\github\repo\unity-savableobservable` and `F:\Unity_projects\AttaxUnity`), requiring zero subagents or test-by-test manual runs.

#### Parameters & Interface
```powershell
[CmdletBinding()]
param(
    [ValidateSet('Framework', 'Attax', 'Both')]
    [string]$Target = 'Both',

    [ValidateSet('EditMode', 'PlayMode', 'SelfTest', 'All')]
    [string]$Suite = 'All',

    [string]$Filter = '',                     # Optional NUnit test filter (e.g. "SavableObservable.Tests")
    [switch]$ExcludeSlowScenarios,            # In Attax PlayMode, excludes long soak/scenario tests by default unless -IncludeScenarios is set
    [switch]$IncludeScenarios,                # Includes Attax PlayMode Scenario tests
    [string]$OutputDirectory = "$env:TEMP\mmvc-test-reports",
    [int]$TimeoutSeconds = 900,
    [string]$FrameworkProjectPath = 'F:\github\repo\unity-savableobservable',
    [string]$AttaxProjectPath = 'F:\Unity_projects\AttaxUnity'
)
```

#### Execution Pipeline Inside `Invoke-UnityTests.ps1`
1. **Preflight & Environment Discovery:**
   - Resolves `unity` CLI binary and checks `unity status --format json`.
   - Stores all XML/JSON/Markdown artifacts **outside** both git repositories (default `$env:TEMP\mmvc-test-reports\<timestamp>\`) so git working trees remain 100% clean (as required by `unity-cli` `build-run-test.md` guidelines).
2. **Smart Dual-Path Routing (Live Pipeline vs. Batch CLI) per Project:**
   - Queries `unity status --project-path $ProjectPath --format json`.
   - **If `state == "ready"` (Editor is open and idle):**
     - Enables background ticking: `unity command set_autotick --enable true --project-path $ProjectPath`.
     - Runs `unity recompile --project-path $ProjectPath --format json` and checks for compile errors (`exitCode != 0` or `failed == true`). If compilation fails, records exact file/line compiler diagnostics and skips test runs for that project.
     - Executes EditMode and PlayMode tests via `unity command run_tests` (or if full NUnit XML artifacts are needed and user permits, uses a custom Pipeline `[CliCommand("mmvc_export_test_xml")]` wrapper over `TestRunnerApi` that runs tests in the live Editor AND writes standard NUnit XML to disk!).
   - **If `STATUS_NO_INSTANCES` (Editor is closed):**
     - Runs batch CLI `unity test $ProjectPath --mode EditMode --report-format nunit,junit --output $editXml --timeout $TimeoutSeconds --format json --no-banner`.
     - Runs batch CLI `unity test $ProjectPath --mode PlayMode --report-format nunit,junit --output $playXml --timeout $TimeoutSeconds --format json --no-banner`.
     - Runs `unity run $ProjectPath --command mmvc_selftest --format json --timeout 300 -- --mode all`.
   - **If `STATUS_SAFE_MODE` or `blocked_by_dialog`:**
     - Immediately captures `safeMode.compileErrors` or `dialog.title`/`dialog.message` from JSON and writes it to the failure report without hanging.
3. **Report Aggregation & Readable Output Generation:**
   - Parses the NUnit3 XML files (`<test-run>`, `<test-case>`, `<failure><message>`, `<stack-trace>`) and `mmvc_selftest` output.
   - Generates two outputs:
     1. **`test-summary.md` (Readable Markdown Report):**
        - Overall Verdict (`PASS` / `FAIL`), total duration, Editor version, execution mode (`Live Editor Pipeline` vs `Batch CLI`).
        - Per-repository, per-mode summary table (`Total`, `Passed`, `Failed`, `Skipped`, `Duration`).
        - Grouped failure details with exact test full name, duration, assertion message, and trimmed stack trace (`file:line`).
        - Console warnings/errors captured during the run.
     2. **`test-summary.json` (Machine-Readable Summary):**
        - Structured JSON for CI or agent consumption in a single file read.
   - Prints a concise color-coded summary table to the terminal and exits with `0` (all passed), `8` (test failures), or `6` (compile/infrastructure error).

---

## 8. Explicit List of Steps Requiring Manual Action From You (Deliverable 8)

In accordance with Goal 3 and Deliverable 8, here is every step where manual action in the Unity Editor or CI is (or may be) required, and why:

| Step | When It Occurs | Manual Action Required? | Reason & What You Need to Do (If Applicable) |
|---|---|---|---|
| **1. Converting `unity-savableobservable` into a Unity 6000.6.7f1 Project** | Phase 1 of execution | **NO (Automated via CLI)**, *unless* Unity license/auth is expired | `unity projects create` or scaffolding `ProjectSettings/` + `Packages/manifest.json` + `unity pipeline install` runs headlessly via CLI. You only need to act if `unity license status` reports no active license (`unity auth login`). |
| **2. Updating `AttaxUnity` Package Path if `AttaxUnity` Editor Is Currently Open** | Phase 1 (after moving package into `Packages/com.evialdev.savableobservable`) | **YES, if `AttaxUnity` is open in Unity Editor** | When `AttaxUnity/Packages/manifest.json` changes its local `file:` path, an already-open Unity Editor will not resolve the new package path until the Editor window regains OS focus (or is closed and reopened). If `AttaxUnity` is closed during Phase 1, **no manual action is needed**. |
| **3. Dismissing First-Time TMP Essential Resources / Safe Mode Modal Dialogs** | First open of the new framework Unity project in GUI Editor | **ONLY if opened interactively and blocked by a modal dialog** | In batch mode (`unity test`), Unity runs with `-batchmode` and suppresses modal popups. If you open the new framework project interactively in the Unity Editor GUI and a modal popup appears, `unity command editor_status` will report `status: "blocked_by_dialog"`, which requires a manual click or `unity command editor.dialog dismiss`. |
| **4. Running EditMode, PlayMode, and CLI Tests via `Invoke-UnityTests.ps1`** | Phase 4 (Validation) | **YES, ONLY if you have the target project open in Play Mode or with unsaved dirty scenes** | - Batch `unity test` refuses to launch if the project is already open in a Unity Editor (`exit 6`).<br>- Live Pipeline `run_tests` and `ScenarioFixtureBase` (`ScenarioFixtureBase.cs:38–40`) refuse to run if the open Editor is currently in Play Mode or if the active scene has unsaved changes (`EditorSceneManager.GetActiveScene().isDirty`).<br>- **Action:** Before running the unattended test script, either save any dirty scene and exit Play Mode in the open Editor, or close the Editor so batch mode can run cleanly. |
| **5. CI Execution (GitHub Actions)** | When running in CI | **YES (One-time CI repository setup)** | In GitHub Actions, `F:\github\repo\unity-savableobservable` is referenced by `AttaxUnity` via a local path (`file:F:/github/repo/...`). For CI runners to run `AttaxUnity` tests, either the CI workflow must checkout `unity-savableobservable` to the expected path/relative path or reference its git URL, and `UNITY_LICENSE_SERIAL` / service account secrets must be configured in the repo's GitHub Actions secrets. |

---

## 9. Step-by-Step Execution Plan & Ordering Rationale (Deliverable 1)

The implementation (to be executed after plan approval) is ordered specifically so that **project structure and package resolution are stabilized first**, **shared test contracts are built second**, **universal tests and new gap-filling tests are added third**, **AttaxUnity consumes the shared contracts fourth**, and **the unattended PowerShell test runner validates both repositories last**.

### Phase 1: Convert `unity-savableobservable` into a Clean Default Unity `6000.6.7f1` Project
* **Reasoning for Ordering:** Everything else depends on the repository having a valid Unity project structure (`Assets/`, `Packages/`, `ProjectSettings/`) and `AttaxUnity` resolving the package at its new location without broken references.
1. **Create Unity Project Structure at `F:\github\repo\unity-savableobservable`:**
   - Create `ProjectSettings/ProjectVersion.txt` pinned to `6000.6.7f1 (b22767f04f04)` (matching `AttaxUnity`) and minimal clean Unity 6 `ProjectSettings/*.asset` files.
   - Create `Packages/manifest.json` including:
     - `"com.unity.ugui": "2.6.0"`
     - `"com.unity.test-framework": "1.8.0"`
     - `"com.unity.pipeline": "0.9.1-exp.1"`
     - `"com.unity.ide.visualstudio": "2.0.28"`
     - `"testables": ["com.evialdev.savableobservable"]`
2. **Relocate Package Files into `Packages/com.evialdev.savableobservable/`:**
   - Move `package.json`, `README.md`, `CHANGELOG.md`, `LICENSE`, `Runtime/`, `Editor/`, `Samples/`, and `Tests/` into `F:\github\repo\unity-savableobservable\Packages\com.evialdev.savableobservable\`.
   - Move `Scenes/Demo.unity` and `Scenes/MmvcVerificationScene.unity` into `F:\github\repo\unity-savableobservable\Assets\Scenes\`.
   - Update `MmvcSceneBuilder.cs:21` so `SceneDirectory` points to `"Assets/Scenes"`.
   - Update `.gitignore` at the repo root to ignore Unity generated folders (`[Ll]ibrary/`, `[Tt]emp/`, `[Ll]ogs/`, `[Oo]bj/`, `[Bb]uild/`, `[Bb]uilds/`, `[Uu]ser[Ss]ettings/`, `[Tt]est[Rr]esults/`, `[Cc]ode[Cc]overage/`).
3. **Update `AttaxUnity` Package Reference:**
   - Update `F:\Unity_projects\AttaxUnity\Packages\manifest.json:7` to `"com.evialdev.savableobservable": "file:F:/github/repo/unity-savableobservable/Packages/com.evialdev.savableobservable"`.

### Phase 2: Build the Shared Generic Parameterized Test Framework (`Tests/Shared/`)
* **Reasoning for Ordering:** Creating `com.evialdev.savableobservable.TestContracts` before writing new tests allows both the framework's own test suite and `AttaxUnity`'s test suite to inherit from the same contract classes without duplicating code.
1. **Create `Packages/com.evialdev.savableobservable/Tests/Shared/com.evialdev.savableobservable.TestContracts.asmdef`.**
2. **Implement Generic Base Contract Fixtures:**
   - `MmvcTriadContractTestsBase<TModel, TLogic, TPresenter>`: Automated verification of single-unit rules, field initialization, `ParentDataModel` linkage, active & inactive prefab `Instantiate` lifecycle, `[ObservableHandler]` signature & field matching, `[AutoBind]` adapter resolution, two-way `AutoBind` roundtrip without bounce-back, and destruction cleanup order.
   - `MmvcAssemblyAuditTestsBase`: `[TestCaseSource]` reflection-driven audit of every Model, Presenter, Logic, Loader, and `ISharedSingleton` in the target assemblies, plus `TypeCache` validation (moved/adapted from `AttaxUnity\Assets\Tests\EditMode\AppStateNavigationTests.cs:340–348`).
   - `MmvcPersistenceContractTestsBase<TModel, TLoader>`: Reusable persistence, `[NonSaved]` preservation, list cloning, and corrupt-state backup verification (adapted from `PrefsSandboxTests.cs` and `OnlineProfileServiceTests`).
3. **Wire `SampleMmvcContractTests.cs` in `Packages/com.evialdev.savableobservable/Tests/Editor/`:**
   - Instantiate `MmvcTriadContractTestsBase` and `MmvcAssemblyAuditTestsBase` for `SamplePlayerModel`, `SamplePlayerLogic`, `SamplePlayerPresenter`, and `SamplePlayerLoader`.

### Phase 3: Close All Identified MMVC Feature & Lifecycle Test Gaps in the Framework Repo
* **Reasoning for Ordering:** With the project and shared contract base in place, add focused EditMode and PlayMode tests for every gap identified in Section 2.
1. **Create `Packages/com.evialdev.savableobservable/Tests/Editor/PrefabAndPoolingLifecycleTests.cs`:**
   - Runtime `Object.Instantiate` of an **active** prefab with pre-wired `Model`, `Logic`, `Presenter`, and child UI (`Toggle`, `Slider`, `TMP_Text`, `TMP_InputField`).
   - Runtime `Object.Instantiate` of an **inactive** prefab -> `logic.GetModel()` early initialization while inactive -> model mutations while inactive -> `SetActive(true)` -> verify zero duplicate handler invocations.
   - Pooling lifecycle (`SetActive(false)` -> release -> `SetActive(true)` -> rebind/reuse) matching `GameObjectPool` + `OnlineLobbyPresenter` / `MenuBackgroundPresenter`.
   - Virtual `[ObservableHandler]` override deduplication test (base class declares `protected virtual void OnIsOpenChanged(bool)` with `[ObservableHandler("isOpen")]`, derived class overrides `protected override void OnIsOpenChanged(bool)` without attribute -> verify derived override is called **once** per change).
2. **Create `Packages/com.evialdev.savableobservable/Tests/Editor/TrackedActionAndForeignSubTests.cs`:**
   - `ObservableTrackedAction<T>` re-entrant mutation test (handler 1 clamps/mutates the same `ObservableVariable<T>` during callback -> verify all handlers and two-way UI converge to the clamped value).
   - `ObservableTrackedAction<T>` infinite-loop protection test (mutually recursive handlers stop at `maxIterations = 32` and log the expected error via `LogAssert.Expect`).
   - `ObservableTrackedAction<T>` exception isolation test (handler 1 throws -> `LogAssert.Expect(LogType.Exception, ...)` -> handler 2 still executes).
   - `ObservableTrackedAction<T>` background thread guard test (`Task.Run` calling `Add`/`Remove`/`Invoke` throws `InvalidOperationException` on non-main thread).
   - `ObservableList<T>` all 4 `[ObservableHandler]` parameter signatures (`0` params, `ObservableList<T>`, `List<T>`, `(List<T>, IReadOnlyList<T>)`, and `object`), `Remove(missing)` returning `false` with `0` notifications, and Editor `OnBeginGui()`/`OnValidate()`.
   - Two-way `SliderAdapter` with `int` (`ObservableVariable<int>`), `TMP_InputField` with `int`/`float` (including empty string `""` -> `0` and non-numeric input ignored), and legacy `InputField` two-way binding.
   - Cross-unit foreign subscription lifecycle (`foreignModel.field.OnValueChanged.Add(h, subscriber)`).
3. **Create `Packages/com.evialdev.savableobservable/Tests/Runtime/MmvcPrefabPlayModeTests.cs`:**
   - PlayMode verification of the 5-stage `Awake()` execution order (`-300`, `-250`, `-200`, `-150`, `0`) on scene activation and `Object.Instantiate` without manual `EnsureInitialized()` calls.
   - PlayMode verification of `OnEnable` / `OnDisable` / `SetActive(false)` / `SetActive(true)` and end-of-frame `Object.Destroy` cleanup.

### Phase 4: Integrate Shared Parameterized Tests into `AttaxUnity`
* **Reasoning for Ordering:** Once the shared contracts are proven in the framework repo, wire them into `AttaxUnity` to eliminate duplicate boilerplate and automatically test all 23+ Attax MMVC units.
1. **Update `F:\Unity_projects\AttaxUnity\Assets\Tests\EditMode\AttaxUnity.EditModeTests.asmdef`:**
   - Add reference to `"com.evialdev.savableobservable.TestContracts"`.
2. **Create `F:\Unity_projects\AttaxUnity\Assets\Tests\EditMode\AttaxMmvcContractTests.cs`:**
   - Add concrete subclasses of `MmvcTriadContractTestsBase` for `PlayerSettings`, `NoticeModal`, `GameOverBanner`, `OnlineLobby`, `PrivateRoom`, `HelpModal`, `PlayerProgress`, `AIvsAIMenu`, `BaseAISettings`, `AbilityPopup`, `AbilityTargetingBar`, `AppState`, `MatchTimer`, `BoardGraph`, `ByteCode`, `MoveHint`, `PlayerChip`, and `MenuBackground`.
   - Add `AttaxAssemblyAuditTests : MmvcAssemblyAuditTestsBase` to automatically audit every MMVC class in `AttaxUnity`.

### Phase 5: Implement & Verify the Unattended PowerShell Test Runner (`Tools/Invoke-UnityTests.ps1`)
* **Reasoning for Ordering:** With both projects structured and all tests in place, build the unattended runner script to execute EditMode, PlayMode, and Pipeline self-tests across both repositories and generate the unified Markdown/JSON report.
1. **Create `F:\github\repo\unity-savableobservable\Tools\Invoke-UnityTests.ps1`:**
   - Implement live-Editor Pipeline detection (`unity status`), automatic `set_autotick`, `recompile`, `unity test` batch execution (`EditMode` & `PlayMode`), `mmvc_selftest` execution, NUnit3 XML parsing, and `test-summary.md` + `test-summary.json` report generation.
2. **Run Full Verification Across Both Repositories:**
   - Execute `.\Tools\Invoke-UnityTests.ps1 -Target Both -Suite All` and verify 100% pass rate and clean reports.
