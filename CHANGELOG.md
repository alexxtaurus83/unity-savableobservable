# Changelog

## [1.0.15] - 2026-10-07 - Strict MMVC architecture guards & generic model constraints
- Added `where M : BaseObservableDataModel` compile-time generic constraint to `BasePresenter<M>`, `BaseObservablePresenter<M>`, `BaseLogic<M>`, and `LoaderWithModel<M>`. Non-observable models and interfaces are strictly prohibited and caught at compile time.
- Added `where LO : BaseLogic<M>` generic constraint to `ObservablePresenterWithLogic<M, LO>` and `LoaderWithModelAndLogic<M, LO>`, ensuring type safety between presenters/loaders and their corresponding logic.
- Simplified `BaseObservablePresenter<M>.GetObservableModel()` and `LoaderWithModel<M>.LoadDataFromModel()` now that `M` is statically known to derive from `BaseObservableDataModel`.
- Enforced strict "One MMVC unit per GameObject" architecture:
  - Added `[DisallowMultipleComponent]` to `BaseObservableDataModel`.
  - Added runtime duplicate validation logging `LogError` when multiple models or presenters are found on the same GameObject during listener setup.
  - Added strict same-GameObject validation in `Observable.SetListeners(subscriber, model)` and `Observable.SetAutoBindListeners(subscriber, model)`: cross-GameObject binding is rejected with `LogError` to prevent subscription and lifecycle leaks.
- Factored out `Observable.ResolveModel`, `SetListenersCore`, and `SetAutoBindListenersCore` to eliminate duplicate model discovery code and prevent duplicate error logs.

## [1.0.14] - 2026-10-07 - Explicit model listener binding
- Added `IObservablePresenter` and overloads of `Observable.SetListeners` / `SetAutoBindListeners` that accept an explicit model, supporting presenters when a GameObject contains multiple data models.
- Updated loader and singleton integration to pass the presenter's associated model when wiring listeners.

## [2026-02-21] - Editor/runtime assembly separation
- Added a dedicated Editor assembly with property drawers for observable variables and lists, plus a shared serialized-property resolver.
- Split display-only UI adapters from listener-capable adapters and added explicit lookup for two-way binding support.
- Removed Odin Inspector-specific drawer integrations.

## [2026-02-18] - Binding and serialization hardening
- Added safer model-state loading with null, type, and reflection validation, and ensured reactive listeners are initialized after state restoration.
- Made listener setup idempotent and added deterministic cleanup for UI listeners and observable subscriptions.
- Added observable-field caching and re-entrant event dispatch safeguards.

## [2026-02-12] - Two-way UI binding and observable lists
- Added two-way model/UI binding for toggles and text input fields, including conversion of basic input values to their observable types.
- Added \ObservableList<T>\ with change notifications, previous-state snapshots, and Unity Inspector editing support.
- Improved binding validation and stability, and declared Textmesh Pro as a package dependency.

## [2026-02-09] - Declarative UI binding
- Added \[AutoBind]\ and UI adapter APIs for binding observable values to Textmesh Pro, Unity UI text, toggles, buttons, and images.
- Added adapter registration and registry lookup for custom UI components.

## [2026-01-16] - Subscription lifecycle management
- Added tracked observable subscriptions with automatic cleanup when subscribers or models are removed, plus cached observable-field reflection data.
- Added observable model ownership and listener initialization checks to improve lifecycle validation.

## [2026-01-12] - Observable serialization and editor support
- Added Unity Inspector property drawing and explicit notifications for observable values edited in the Inspector; avoided firing events during deserialization.
- Expanded \ObservableVariable<T>\ support to arbitrary generic value types and improved observable-field discovery and model-state restoration.

## [2025-10-11] - Presenter event handlers
- Added \[ObservableHandler]\ for per-variable presenter methods with handlers accepting no arguments, the new value, or current and previous values.
- Added validation for conflicting handler strategies and missing observable handlers.

## [2025-09-25] - Save/load integration refactor
- Reworked loaders around \LoaderWithModel<M>\ and \LoaderWithModelAndLogic<M, LO>\, exposing model access and model-state restoration without coupling the framework to one save system.
- Restored observable model initialization and state-copy support for observable and regular model fields.

## [2025-09-24] - Type-safe change notifications
- Changed observable updates to publish the changed variable through \IObservableVariable\, including current and previous values, instead of type-specific previous/current/name arguments.
- Made observable presenters require a model-change handler and updated the example flow to keep business actions in logic and UI reactions in presenters.
- Added a separate non-reactive \BasePresenter<M>\ base class.

## [2025-09-22] - Observable value support
- Added nullable boolean observables and refined observable value serialization behavior.

## [2025-08-19] - Package layout adjustment
- Moved runtime sources into a Unity \Assets/Plugins/SavableObservable\ layout, then restored the package's \Runtime\ layout and updated package contents/version metadata.

## [2025-07-23] - Observable listener setup
- Moved listener registration into the core \Observable\ API, allowing model changes to notify presenter handlers through reflection-based subscriptions.

## [2025-06-30] - Package assembly setup
- Added a Unity assembly definition for the framework and aligned runtime code under the \SavableObservable\ namespace.
- Removed hard dependencies on the sample project's save-system types from the loader base.

## [2025-06-29] - Initial framework foundation
- Added the initial Unity MMVC framework with observable variables, reflection-based model initialization and change subscriptions, presenter/logic base classes, and save/load loader abstractions.
- Added Unity package metadata and a pipeline example demonstrating reactive model, logic, presenter, and loader components.
