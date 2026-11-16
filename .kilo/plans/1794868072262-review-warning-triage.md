# Warning Triage

## Scope And Outcome

Read-only assessment of the eight supplied findings against the current package source, tests, changelog, and historical plans. No source changes or test runs were requested or performed. Follow AGENTS.md for any subsequent implementation.

| Item | Verdict | Recommendation |
|---|---|---|
| 1 | Repeated invalid ClickCommand diagnostics confirmed; low-severity diagnostic issue, already expected twice by ClickCommandTests. | Worth a small fix if repeated rebind noise is a problem. Prefer per-presenter logging over metadata-constructor logging to retain object context and avoid cache-dependent visibility. |
| 2 | Substantial resolver duplication confirmed; no demonstrated behavioral bug. | Optional isolated refactor; preserve resolution order, diagnostics, caches, and private entry points used by characterization tests. |
| 3 | Derived-first dictionary is deliberate, but handlers iterate all observable fields while AutoBind uses the dictionary. Hidden base and derived fields can both invoke the same handler. | Worth addressing the binding inconsistency. Choosing derived-only binding versus rejecting name collisions is a public-behavior decision, outside this assessment. Do not change persistence/initialization enumeration or unrelated presenter method hiding. |
| 4 | Same-kind case collisions ordinarily throw and are swallowed, not arbitrarily selected. However, case-insensitive properties precede exact fields, and derived fallback precedes exact base members. | Original ambiguity explanation is overstated. Characterize lookup precedence before changing compatibility behavior. Do not remove case-insensitive loading or choose arbitrary candidates. |
| 5 | List equality deliberately shares the identity-aware scalar comparison. | No equality fix. Optional regression coverage. |
| 6 | Different destroyed Unity objects are distinct references; same destroyed reference is equal via ReferenceEquals. Destroyed/null transitions intentionally notify. | False positive as a correctness finding. Preserve the current behavior and existing null-transition tests. |
| 7 | Runtime debug-build check is redundant in common compilation configurations, but does not cause incorrect behavior. | Leave unchanged unless doing focused cleanup; preserve throw versus log behavior across build configurations. |
| 8 | += supplies no subscriber, so it bypasses registry-based removal. Explicit Add(handler, subscriber) is the documented tracked path. Invocation still filters direct destroyed Unity-object delegate targets, but cannot infer objects captured by closures. | Real ownership limitation, not a demonstrated critical leak in every use. Document operator ownership and recommend explicit Add for lifecycle cleanup. Automatic owner inference is out of scope and cannot solve arbitrary closure/static/multicast ownership. |

## Evidence

All paths below are relative to Packages/com.evialdev.savableobservable/.

- Runtime/Observable.cs:64,97-104,482-485; Runtime/BaseObservablePresenter.cs:104-108; Tests/Editor/ClickCommandTests.cs:316-328 establish repeated diagnostics.
- Runtime/Observable.cs:581-685,713-828 establish resolver duplication.
- Runtime/ModelDescriptor.cs:16-20,60-64 and Runtime/Observable.cs:185-193,303 establish divergent hidden-field lookup.
- Runtime/BaseObservableDataModel.cs:136-198 establishes property/field, case, and hierarchy precedence; Tests/Editor/ModelPersistenceTests.cs:416-449 covers exact property collisions only.
- Runtime/ObservableVariable.cs:159-166 and Runtime/ObservableList.cs:484-490 establish identity-aware equality; CHANGELOG.md:13 explicitly records this policy; Tests/Editor/ObservablePrimitivesTests.cs:752-795 covers destroyed/null transitions.
- Runtime/ObservableTrackedAction.cs:56-61,80-104,151-163,200-215 and Runtime/Observable.cs:865-884,891-904 establish ownership, destroyed-target filtering, and thread checks.

## Follow-Up Sequence

1. If implementation is requested, run the baseline and write regressions before fixes. Start with the small diagnostic change; avoid bundling resolver cleanup and API ownership changes.
2. For item 1, test repeated rebind on one invalid presenter and first bind on a second instance, preserving per-object diagnostics and valid click-command lifecycle behavior. Run ClickCommandTests.
3. For item 3, first decide the collision policy. Test hidden model fields with same and different value types, inherited handlers, InvokeOnBind, AutoBind, and StrictBinding. Keep all physical fields available to initialization and persistence. Run ObservableBindingTests and ModelPersistenceTests.
4. For item 4, characterize exact property, exact field versus differently cased property, inheritance, unique fallback, and ambiguous fallback. Resolve precedence explicitly before changing loading behavior. Run ModelPersistenceTests.
5. For item 8, prefer documenting += as caller-owned and Add(handler, owner) as tracked. Add tests distinguishing registry cleanup, direct destroyed method targets, and captured closures; any lifecycle implementation change requires the full suite and consecutive PlayMode runs without recompiling.
6. Optional equality tests: same destroyed reference remains silent; different destroyed references notify and replace stored state; equivalent lists follow the same policy. No runtime equality change.
7. Any user-visible behavior change must update README.md, CHANGELOG.md, and repository mmvc-unity-skill guidance, with no package version bump unless releasing. No migration is needed for assessment or tests; behavior changes in lookup/ownership need a documented compatibility decision.

## Validation Limits

This is source-based triage, not a verified runtime reproduction. Existing tests were inspected, not executed. No implementation or rollout is authorized by this assessment. Policy decisions for hidden fields, persistence precedence, and automatic subscription ownership remain explicitly outside its scope.
