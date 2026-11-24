# Project docs index

Read these before changing the matching area. Do not duplicate their content in plans; link to them.

| Document | Read it for | Update when... |
|---|---|---|
| [README.md](../Packages/com.evialdev.savableobservable/README.md) | The architectural rules and public behavior: one MMVC unit per GameObject, Logic -> Model -> Presenter data flow, change-notification semantics, sync-on-bind, command-aware `[AutoBind]`, `[ClickCommand]`, `ListenerSet`, service locator duplicate rule. The behavior contract | user-visible behavior changes |
| [UnityLifecycle.md](../Packages/com.evialdev.savableobservable/Documentation~/UnityLifecycle.md) | Execution order, `ServiceLocator` two-pass discovery, `EnsureInitialized()` / `SetListeners`. Required before touching initialization or binding | lifecycle or init order changes |
| [RuntimeObjects.md](../Packages/com.evialdev.savableobservable/Documentation~/RuntimeObjects.md) | Inactive prefabs, pooling, `AddComponent` limits. Required before touching lifecycle or provisioning code | pooling or provisioning changes |
| [Testing.md](../Packages/com.evialdev.savableobservable/Documentation~/Testing.md) | Test runner modes, parameters, contract tests, live UI assertions, suite map | a runner mode or suite changes |
| [CHANGELOG.md](../Packages/com.evialdev.savableobservable/CHANGELOG.md) | What changed and why | every user-visible change (add an entry) |
| [mmvc-unity-skill/SKILL.md](../mmvc-unity-skill/SKILL.md) and [references/](../mmvc-unity-skill/references/) | The rules consumer agents follow; must stay consistent with the README | a rule changes |
| [mmvc-kilo-plugin/README.md](../mmvc-kilo-plugin/README.md) | Guard plugin rules (`MMVC001`–`MMVC003`), install, tests. Only for plugin work | a guard rule changes |
| [.kilo/plans/](../.kilo/plans/) | Design history; search before redesigning something that looks deliberate | never |
| [`TESTING.md`](TESTING.md), [`productStructure.md`](productStructure.md) | tests and update matrix, folder map | their topic changes |

## Keeping docs, analyzers and the guard plugin in sync

When user-visible behavior changes, update in the same change:

1. `README.md` (and `Documentation~/*.md` if lifecycle, pooling or testing is affected).
2. `CHANGELOG.md` (bump `package.json` only when the user asks for a release).
3. `mmvc-unity-skill/SKILL.md` and `references/`, so consumer agents get the same rules.
4. **Mandatory analyzer and guard check:** whenever the framework changes, stop and ask the user (`question`) whether updates are needed in:
   - Roslyn MMVC rules (`MMVC001`–`MMVC004`, `MMVC006`–`MMVC019`, maintained in `F:\github\repo\AttaxAnaliser\src\Attax.Analyzers/` and executed via `Attax.AnalyzerCli`), and
   - `mmvc-kilo-plugin/mmvc-guard.ts` (rules must be syntactically provable; if updated: also `mmvc-kilo-plugin/tests/`, run `npm test; npm run typecheck`, deploy to `F:\Unity_projects\AttaxUnity\.kilo\plugin\mmvc-guard.ts`).

Docs are user-facing: keep them accurate and short; development-only notes go in the agent docs here.
