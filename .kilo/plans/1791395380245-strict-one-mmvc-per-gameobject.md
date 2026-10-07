# Plan: Strict "one MMVC per GameObject" guards + review fixes (backward compatible)

## Context / findings
- Released versions: tags `1.0.13`, `1.0.5`. HEAD (`6dcaf35`, package.json `1.0.14`) is **unreleased** and introduced `IObservablePresenter`, `BaseObservablePresenter<M>.GetObservableModel()`, `Observable.SetListeners(object, BaseObservableDataModel)`, `Observable.SetAutoBindListeners(object, BaseObservableDataModel)`.
  - So the explicit-model overloads have no released users yet, and adding same-GameObject validation to them is safe for compatibility.
  - In 1.0.13, `SetListeners(obj)` / `SetAutoBindListeners(obj)` used `GetComponent<BaseObservableDataModel>()` on the subscriber's GameObject, so the same-GameObject rule was already implied.
- `BasePresenter<M>`, `BaseObservablePresenter<M>`, `ObservablePresenterWithLogic`, and `BaseLogic<M>` already have `[DisallowMultipleComponent]`. `BaseObservableDataModel` doesn't.
- Repo `SKILL.md` is a copy of the global skill at `C:\Users\alexx\.skills\mmvc-unity\` (which also has `references/framework-guide.md` and `references/examples.md`).

## Decisions (and the review items we reject)
1. **No generic constraints** (`where M : BaseObservableDataModel`) on `BaseObservablePresenter<M>` or `LoaderWithModel<M>`. Adding one breaks source compatibility for any `M` that is an interface or a non-observable model. Replace it with runtime diagnostics (items 3 and 5). Document why in an XML doc comment.
2. **Architecture rule (strict):** one GameObject holds exactly one Model, at most one Logic, and at most one Presenter (plus an optional Loader). Other components never take a model reference directly. They get the owning presenter (Inspector reference or service locator), then call `presenter.GetModel()` / `GetLogic()`. Manual `OnValueChanged.Add(handler, this)` subscriptions from other GameObjects are still allowed, but only through a model obtained from its presenter.
3. **Violations log `Debug.LogError`.** Whether the call still binds depends on the API:
   - `SetListeners(subscriber, model)` / `SetAutoBindListeners(obj, model)` where `model.gameObject != subscriber.gameObject` → **error and skip the binding**. These overloads are unreleased, so this is safe.
   - More than one `BaseObservableDataModel`, or more than one `IObservablePresenter`, on the GameObject → **error, but still bind** to the resolved model. This keeps 1.0.13 runtime behavior: before, it silently picked the first model.
4. Add `[DisallowMultipleComponent]` to `BaseObservableDataModel` as an Editor guard. It is inherited, and existing serialized duplicates aren't removed, so nothing breaks. Do **not** add `OnValidate`/`Reset` to the model base: user subclasses commonly declare those methods themselves, and they would hide the base versions.
5. Keep the version at `1.0.14` (still unreleased).

## Tasks
### Runtime/Observable.cs
1. Add private helpers to remove the duplicated resolution code:
   - `static BaseObservableDataModel ResolveModel(MonoBehaviour mb, object subscriber)`: prefer `IObservablePresenter.GetObservableModel()`, otherwise fall back to `GetComponents<BaseObservableDataModel>()[0]`. Always run `ValidateSingleMmvc(mb)`.
   - `static void ValidateSingleMmvc(MonoBehaviour mb)`: `LogError` (with context `mb`) if `GetComponents<BaseObservableDataModel>().Length > 1` or `GetComponents<IObservablePresenter>().Length > 1`. The message names the GameObject and component types, explains the rule, and states which model was chosen.
   - `static bool ValidateSameGameObject(MonoBehaviour mb, BaseObservableDataModel model, string api)`: if `model.gameObject != mb.gameObject`, `LogError` naming both GameObjects and pointing the user to "access other models via their presenter", then return false.
2. `SetListeners(object)` and `SetAutoBindListeners(object)`: replace the inline resolution blocks with `ResolveModel`. Keep the existing early returns (non-MonoBehaviour warning, silent return when no model).
3. `SetListeners(object, model)`: after the null-model check, `if (!ValidateSameGameObject(...)) return;`. Call `ValidateSingleMmvc` only when it hasn't already run in this call: give the parameterless path a private core method, or a `bool validated` internal parameter on a private core, so errors aren't logged twice.
4. Split `SetAutoBindListeners(object, model)` into:
   - a public method that keeps its signature, adds the MonoBehaviour check, null check, and same-GameObject validation, then calls the core;
   - `private static void SetAutoBindListenersCore(MonoBehaviour mb, object obj, BaseObservableDataModel model)` with the current body.
   - `SetListeners` calls the core directly, so validation isn't repeated.
5. Leave public signatures and cleanup/subscription logic unchanged.

### Runtime/BaseObservablePresenter.cs
6. `GetObservableModel()`: store `GetModel()` in a local. If it's non-null and not a `BaseObservableDataModel`, `Debug.LogWarning` (context `this`) naming `typeof(M)` and the runtime type. If it's null, return null without logging; callers handle that. Wrap the warning in `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.
7. Add an XML doc comment: `M` is expected to be a `BaseObservableDataModel` subtype. The constraint is intentionally omitted for compatibility.
8. Remove the dead commented-out `Start()` block (optional cleanup; it doesn't affect the API).

### Runtime/BaseObservableDataModel.cs
9. Add `[DisallowMultipleComponent]` to the class (keep `[Serializable]`).

### Runtime/LoaderWithModel.cs
10. In `LoadDataFromModel`, after `Invoke`: `var observableModel = GetModel() as BaseObservableDataModel; if (observableModel == null) { Debug.LogError(... typeof(M) ...); return; }`. Do this only when `presenter != null`, so non-observable loaders without a presenter keep working silently, as they do today.
11. Add the same XML doc note about `M`.

### Docs
12. `README.md`:
    - Add a "One GameObject = one MMVC unit" rule section near "Presenter Hierarchy": one model, logic, and presenter each, plus an optional loader. `[DisallowMultipleComponent]` guards it in the Editor; runtime errors cover duplicates and cross-GameObject binding.
    - Add a "Cross-component access" subsection: get another unit's presenter (Inspector or `Services.Get<T>()`), then `GetModel()`/`GetLogic()`. Never serialize or bind another GameObject's model directly. Show a short example.
    - Line 149: remove or replace the stale "Automatic Setup Validation" claim (the `Start()` check is commented out).
    - Lines 792–793: reword to match the rule, and state that the explicit-model overloads require the model to be on the subscriber's GameObject.
    - Note why there are no generic constraints.
13. Repo `SKILL.md` (Implement steps 2 and 5, Debug section): state the strict rule, the presenter-only cross-access pattern, and the new error messages as debug hints.
14. Sync the global skill `C:\Users\alexx\.skills\mmvc-unity\`:
    - `SKILL.md`: same edits as task 13.
    - `references/framework-guide.md`: update line 60 and the component table.
    - `references/examples.md`: change the "Manual Subscriber Lifetime" `ScoreObserver` to take `[SerializeField] ScorePresenter presenter` and use `presenter.GetModel()` instead of a serialized model.

## Failure modes to cover
- A presenter whose `M` is an interface implemented by the model: the cast succeeds, so there's no warning.
- A presenter whose `M` is not a model (misconfigured): warning, then fallback to `GetComponents`, then normal binding or a silent return.
- Duplicate models from an old prefab: an error is logged, and binding behaves as in 1.0.13.
- Cross-GameObject explicit binding: an error is logged and nothing is subscribed. This prevents dangling subscriptions to destroyed subscribers.
- Loader with a non-observable `M` and an observable presenter: an error is logged and there's no `SetListeners(null)` call.

## Validation
- There's no test assembly in the repo. Compile the package in a host Unity 2021.3+ project (embedded/local package) with zero new compiler errors or warnings.
- Manual scenarios (Play Mode):
  1. A normal single-unit presenter: `SetListeners(this)` and `SetListeners(this, GetObservableModel())` both bind; no errors.
  2. Calling setup twice: still no duplicate callbacks.
  3. Two models on one GameObject (added via script or an old prefab): one error, bound to the presenter's own `M`.
  4. `SetListeners(presenterA, modelOnB)` → error, no subscription. Same for `SetAutoBindListeners`.
  5. Loader reload path: state is applied, then a rebind with no errors. A loader with a non-observable `M` → clear error.
  6. In the Editor, adding a second model component is blocked by `[DisallowMultipleComponent]`. Verify whether this also blocks two *different* model subclasses. If it doesn't, the runtime error is the guard; document it either way.
- `git diff 1.0.13 -- Runtime` review: no public or protected signature removed or changed.

## Out of scope
- `[DisallowMultipleComponent]` on loaders; runtime duplicate check for `BaseLogic<M>` (it has no non-generic base type); adding an EditMode test assembly.
