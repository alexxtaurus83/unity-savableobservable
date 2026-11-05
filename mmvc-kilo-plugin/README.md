# MMVC Kilo Plugin (`mmvc-guard`)

Reusable Kilo plugin providing workflow enforcement, context guidance, and conservative linting for the Unity MMVC framework ([`com.evialdev.savableobservable`](https://github.com/evialdev/unity-savableobservable)).

---

## 1. Rationale and Client Target

### Why This Plugin Exists
When AI agents implement or refactor features in Unity MMVC projects, they frequently repeat specific structural mistakes:
- Implementing `ISharedSingleton` on Presenters or Models to bypass architecture boundaries.
- Attempting to retrieve Models or Presenters directly via `Services.Get<T>()`.
- Directly referencing, instantiating, or looking up Presenters from inside `BaseLogic<M, P>`.
- Overriding `InitializePresenter()` in custom presenters without invoking `base.InitializePresenter()`.
- Modifying C# gameplay code without loading the `mmvc-unity` skill and its architecture references.

The `mmvc-guard` plugin intercepts tool execution inside Kilo to enforce mandatory skill loading before C# mutations and reject newly introduced, syntactically unambiguous violations before changes touch disk.

### Target Environment & Client Specificity
- **Target Client:** Designed specifically for the **Kilo Code** client runtime exposing `@kilocode/plugin` extension hooks (`experimental.chat.system.transform`, `experimental.session.compacting`, `tool.execute.before`, `tool.execute.after`).
- **SDK Compatibility:** Initially developed and tested against `@kilocode/plugin@7.4.5`.
- **Not Universal:** This plugin requires Kilo's specific hook lifecycle. It does not run in vanilla Claude Code, generic LSP editors, or agent clients lacking `@kilocode/plugin` hook support.

---

## 2. Canonical Source vs. Deployment

- **Canonical Repository:** `mmvc-kilo-plugin/mmvc-guard.ts` in this repository is the single authoritative source of truth.
- **Self-Contained Entrypoint:** Kilo auto-discovers plugins matching `.kilo/plugin/*.{ts,js}`. To prevent discovery errors and dependency bundling issues, the deployed plugin is packaged as a single self-contained TypeScript entrypoint with no relative companion file requirements.
- **Deployed Target (e.g., AttaxUnity):** Deployed to `F:\Unity_projects\AttaxUnity\.kilo\plugin\mmvc-guard.ts` alongside any existing project guards (such as `attax-guard.ts`).

---

## 3. Installation and Discovery

1. Ensure the target project has `.kilo/plugin/` configured.
2. Copy `mmvc-guard.ts` into the project's plugin directory:
   ```powershell
   Copy-Item -LiteralPath "mmvc-kilo-plugin\mmvc-guard.ts" -Destination ".kilo\plugin\mmvc-guard.ts"
   ```
3. Restart or reload Kilo session to initialize the plugin.

---

## 4. Skill Prerequisites and Resolution

The plugin requires the `mmvc-unity` skill to be available for agent guidance and mandatory loading validation.

### Resolution Precedence
The plugin resolves `SKILL.md` in the following strict order:
1. **Explicit Directory:** Path provided via `MMVC_GUARD_SKILL_DIR` (absolute or relative to project root).
2. **Project-local Skill:** `.kilo/skills/mmvc-unity/SKILL.md`.
3. **User-level Installed Skill:** `~/.skills/mmvc-unity/SKILL.md` (resolved portably via user home directory).

If `SKILL.md` is missing, unreadable, or contains malformed frontmatter, the plugin flags a configuration issue in system prompt guidance and rejects C# mutations until resolved.

---

## 5. Configuration and Environment Options

The plugin optionally reads **`.kilo/mmvc-guard.json`** at startup. Copy `mmvc-guard.example.json` there to maintain project settings without editing TypeScript. Missing default JSON uses built-in defaults. Restart the Kilo client after changes; no live watcher or agent-authorized self-reconfiguration is installed.

Precedence for mode, opt-in and skill path: **environment > existing plugin options > JSON > defaults**. `MMVC_GUARD_CONFIG` (environment or plugin option) selects another JSON path, resolved relative to the project. An explicitly selected missing file is an error. JSON is strict (no comments/trailing commas); unknown keys, invalid types and unknown rule IDs are rejected. An invalid config retains blocking mutation hooks in active projects, rather than relying on a plugin-load exception Kilo might ignore. Explicit environment/option mode `off` remains an operator recovery path.

```json
{
  "mode": "enforce",
  "excludedDirectories": [".kilo/temp", "scratch"],
  "rules": { "MMVC003": "warn", "MMVC005": "error" }
}
```

Supported JSON keys:

| Key | Meaning |
| --- | --- |
| `mode` | `enforce`, `warn`, `off` |
| `enabled` | Boolean explicit opt-in; false does not override manifest auto-activation. Use mode off to disable. |
| `skillDir` | Explicit skill directory, absolute or project-relative |
| `requireSkill` | Boolean, default true |
| `protectPackageCache` | Boolean, default true |
| `excludeOsTemp` | Boolean, default true; a temp ancestor never exempts a whole project residing under temp |
| `excludedDirectories` | Unmonitored directory subtrees, default `[".kilo/temp"]` |
| `scriptExtensions` | Extensions checked by MMVC005 (leading dot); see example for full defaults |
| `commandDirectories` | MMVC005 command subtrees, default `[".kilo/command", ".kilo/commands"]` |
| `rules` | Partial map of MMVC001 through MMVC005 to `error`, `warn`, `off`; MMVC-REVIEW supports warn/off (error does not promote uncertain findings to hard errors) |

Lists **replace**, rather than extend, defaults. Paths are literal directories, not globs; relative paths use the project root, not the config file directory. No `%VAR%`, `$VAR` or tilde expansion: use resolved paths. Per-rule settings merge with built-in rule defaults. Global warn downgrades all hard errors; per-rule off removes that rule only. Gate/cache/reconstruction checks remain separate. Treat changes as operator policy changes: agents must not weaken settings without approval. JSON contains data only; new semantic rules still require TypeScript and regression tests. Adding a helper extension or another excluded/command directory requires only JSON.

The plugin also checks environment variables (or plugin options passed at initialization):

| Variable / Option | Allowed Values | Default | Purpose |
| :--- | :--- | :--- | :--- |
| `MMVC_GUARD_MODE` | `enforce`, `warn`, `off` | `enforce` | Controls diagnostic handling. `enforce` throws errors blocking mutations. `warn` permits mutations but injects diagnostics. `off` disables the plugin. |
| `MMVC_GUARD_ENABLED` | `1`, `true` | (Auto) | Explicit opt-in for unusual layouts. Otherwise auto-activates when `Packages/manifest.json` declares `com.evialdev.savableobservable`, including embedded/local dependency values. Use mode `off` to disable. |
| `MMVC_GUARD_SKILL_DIR` | Directory path | `null` | Explicit path to directory containing `mmvc-unity/SKILL.md`. Overrides automatic resolution. |

*Note:* Configuration options serve as operator escape hatches and workflow controls, not cryptographic security boundaries.

---

## 6. The 4 Conservative Linting Rules

The plugin implements a lexical C# scanner that masks comments and literal strings (regular, verbatim, interpolated, raw strings) and parses class boundaries and generic base arguments. It enforces 4 conservative, syntactically provable rules:

1. **`MMVC001` (Singleton Model or Presenter):**
   - *Rule:* Blocks application classes deriving directly from known framework models (`BaseObservableDataModel`) or presenters (`BasePresenter`, `BaseObservablePresenter`, `ObservablePresenterWithLogic`) while implementing `ISharedSingleton`.
   - *Fix:* Remove `ISharedSingleton`. Expose access via `BaseLogic<M, P>` or a dedicated coordinator service.

2. **`MMVC002` (Invalid `Services.Get<T>`):**
   - *Rule:* Blocks calls to `Services.Get<T>()` where `T` resolves within the inspected source to a known model or presenter type.
   - *Fix:* Use `Services.GetModel<TModel>()` for models, or `Services.Get<TLogic>()` to communicate with another unit's logic.

3. **`MMVC003` (Direct Presenter References inside Logic):**
   - *Rule:* Inside a class deriving from `BaseLogic`, blocks explicit fields of a known Presenter type, explicit `new PresenterName()`, or component lookups like `GetComponent<PresenterName>()` / `FindObjectOfType<PresenterName>()`.
   - *Exemption:* The required generic presenter parameter in `BaseLogic<M, P>` is explicitly exempted.
   - *Fix:* Logic communicates to Presenters strictly through Model observable variables or one-shot event tokens (`eventToken.Value++`).

4. **`MMVC004` (Missing `base.InitializePresenter()`):**
   - *Rule:* Blocks overrides of `InitializePresenter()` that fail to invoke `base.InitializePresenter()`.
   - *Warning:* Warns if `base.InitializePresenter()` is invoked late or conditionally within control flow.
   - *Fix:* Place `base.InitializePresenter();` at the beginning of the override.

*No Semantic Promises:* The plugin does not perform full semantic AST compilation, global cross-file symbol resolution, or type-inference across assemblies. Ambiguous or multi-declaration names are not hard-blocked. Custom inheritance and late base calls receive bounded review warnings; other uncertain constructs remain covered by the injected review guidance.

---

## 7. Mutation Hooks and Baseline Handling

The plugin intercepts mutation tools: `write`, `edit`, `multiedit`, `apply_patch`, and `patch`.

### Mandatory Skill-Load Gate
- Before any supported C# file mutation (including creation, update, or deletion), the session must have successfully loaded the `mmvc-unity` skill via the `skill` tool or a complete direct `read` of `SKILL.md`.
- Failed, partial, or truncated reads do not satisfy the gate.
- Child agent sessions do not inherit parent load state and must satisfy the gate independently.

### Baseline vs. Newly Introduced Violations
- **Existing Files:** The pre-mutation content is scanned for baseline violations. If an existing violation remains unchanged, it is reported as a non-blocking warning. Only *newly introduced* occurrences or count increases are blocked.
- **New Files (Created from Scratch):** Files created via `write` or patch `*** Add File` have an empty baseline. Any detected violation in the proposed content is treated as newly introduced and blocked immediately before creation.
- **Excluded Paths:** `Library/`, `Temp/`, and `obj/` are excluded from application linting.
- **Cache Protection:** In enforce mode, mutations targeting cached framework package sources under `Library/PackageCache/com.evialdev.savableobservable@...` are blocked; warn/off remain explicit operator escape hatches.

### Concrete Examples

After loading the skill, `write { filePath: "Assets/Score.cs", content: "using SavableObservable; class Score : BaseObservableDataModel {}" }` is allowed. Creating the same file with `, ISharedSingleton` added to its base list is rejected as `MMVC001`; no baseline exists to exempt it. The equivalent new-file patch is also rejected:

```diff
*** Begin Patch
*** Add File: Assets/Score.cs
+using SavableObservable;
+class Score : BaseObservableDataModel, ISharedSingleton {}
*** End Patch
```

For an existing file already containing that singleton violation, an exact `edit { filePath: "Assets/Score.cs", oldString: "class Score", newString: "\nclass Score" }` only shifts its line and is allowed with an existing-violation warning. Adding a second violating class is blocked. Fixing the old singleton violation while introducing a `Services.Get<Score>()` violation is also blocked. Deleting a C# file still requires the skill, even though deletion adds no lint diagnostic.

Patch reconstruction deliberately requires exact, unique context. Ambiguous fuzzy/whitespace-matching hunks, context-free insertions and repeated operations on the same path are rejected with a supported-edit instruction rather than guessed. Use `write` or exact `edit` when a patch cannot be reconstructed. Renames entering application scope are treated as additions.

---

## 8. PowerShell Commands: Update, Byte-Compare, and Rollback

### Deploy Canonical Plugin to AttaxUnity
```powershell
Copy-Item -LiteralPath "mmvc-kilo-plugin\mmvc-guard.ts" -Destination "F:\Unity_projects\AttaxUnity\.kilo\plugin\mmvc-guard.ts" -Force
```

### Verify Canonical vs. Deployed Byte Equality
```powershell
$canonical = "mmvc-kilo-plugin\mmvc-guard.ts"
$deployed = "F:\Unity_projects\AttaxUnity\.kilo\plugin\mmvc-guard.ts"
$hash1 = (Get-FileHash -LiteralPath $canonical -Algorithm SHA256).Hash
$hash2 = (Get-FileHash -LiteralPath $deployed -Algorithm SHA256).Hash
if ($hash1 -eq $hash2) {
    Write-Output "VERIFIED: Deployed plugin matches canonical source ($hash1)"
} else {
    Write-Error "MISMATCH: Deployed plugin does not match canonical source!"
}
```

### Independent Attax Guard Coexistence
- `F:\Unity_projects\AttaxUnity\.kilo\plugin\attax-guard.ts` remains independent and unmodified.
- Both plugins load side-by-side in Kilo with distinct IDs (`attax-guard` and `mmvc-guard`).

### Session Reload Gate
- After installing, updating, or removing a plugin, restart the Kilo session or reload the client to trigger plugin re-discovery.
- Because plugin state is held in memory, session reload resets the skill-load gate, requiring agents to load `mmvc-unity` in the new session before C# edits.

### Rollback Procedure
If rollback is necessary:
1. Remove only `F:\Unity_projects\AttaxUnity\.kilo\plugin\mmvc-guard.ts`.
2. Do not delete or edit `attax-guard.ts`.
3. Revert any documentation pointers.
4. Restart/reload Kilo.

---

## 9. Bypass Limitations and Boundaries

- **Shell / External Tool Bypass:** Terminal operations (`bash`, `pwsh`, external IDEs like Rider/VS Code, git checkouts) do not run through Kilo's tool hooks and bypass the guard entirely.
- **Uncertain Aliases and Type Suffixes:** The scanner does not infer framework roles from class name conventions (e.g. `FooPresenter` is not treated as a presenter unless it directly inherits a known base class in the scanned file or is explicitly mapped).
- **Lexical Scope Limits:** This is not Roslyn: complex namespace scoping, preprocessor branches, generic/custom inheritance, partial declarations, shadowed receivers and alias-heavy code require manual review. Whole interpolated literals are masked, including their embedded expressions, so violations inside interpolation expressions are outside the initial rule coverage.
- **Not a Security Sandbox:** This plugin provides workflow discipline and sanity checking for cooperative AI agents, not adversarial protection.

---

## Temporary Support Files

`MMVC005` checks new destinations through `write`, patch `Add File`, and patch `Move to` before C# reconstruction. New PowerShell, shell, batch, Python, JavaScript/TypeScript, Lua, Ruby, Perl, notebook and `.csx` scripts, and files under `.kilo/command/` or `.kilo/commands/`, are rejected in the monitored project. Mixed-file patches are rejected before any part executes. This is a conservative location policy, not a semantic determination that a script is temporary. Existing scripts remain editable. Ordinary Unity `.cs` source retains the MMVC gate/lint behavior rather than being blanket-blocked; arbitrary text/data files are not blocked solely for being new.

Put one-off Unity CLI helpers and support files outside the project in standard OS temporary directories (Windows: `%LOCALAPPDATA%\Temp`, `%TEMP%`, or `C:\Users\<user>\AppData\Local\Temp\kilo`; Linux: `/tmp`, `/var/tmp`, or `$XDG_RUNTIME_DIR`), or use the exact project subtree **`.kilo/temp/`**. The guard announces both choices in system guidance, compaction reminders and rejection messages. Prefer a direct Unity CLI command when a wrapper is unnecessary.

**Standard OS temporary directories and `.kilo/temp/` are not monitored by this guard, including C# files, skill gating and MMVC lint.** Only this exact project subtree is exempt: `.kilo/temp-other/` is not. Paths are normalized and symlinks resolved before checking scope. Do not place production source or permanent commands in scratch storage. This exemption does not disable other plugins or change filesystem permissions. External tools and shell writes still bypass this plugin entirely.

For intentionally permanent tooling, obtain explicit operator authorization for `MMVC_GUARD_MODE=warn` while creating it, then restore enforcement. The plugin never grants that approval or changes the mode itself. No scratch folder is created automatically, and `.gitignore` is not modified; add an ignore entry when appropriate under the project's own rules.

## 10. Development and Testing Commands

Run isolated tests and typecheck inside `mmvc-kilo-plugin`:
```powershell
# Install dev dependencies (isolated to mmvc-kilo-plugin, never touching root Unity package)
npm ci

# Run test suite
npm test

# Run TypeScript typecheck
npm run typecheck
```

---

## 11. Validation and Test Results

On 2026-11-05, Node 24.11.0 and SDK types 7.4.5: `npm run typecheck` passed; `npm test` passed all 27 tests, exit 0, including JSON validation, precedence, custom lists/rule levels, temporary-helper creation, patch/move rejection, existing-script editing, scratch-boundary and warn-mode regressions. Tests invoke the canonical entrypoint rather than a second scanner implementation. The client smoke also loads the shipped JSON example.

The installed Kilo 7.8.25 client discovered both plugin IDs in a disposable Unity-project fixture. An in-runtime probe verified system guidance, blocked pre-load writes, successful-load clean writes, violation rejection, and coexistence with the unchanged Attax guard. Repeat with:

```powershell
$env:KILO_SMOKE_BINARY = "C:\path\to\kilo.exe"
$env:KILO_SMOKE_ATTAX_GUARD = "F:\Unity_projects\AttaxUnity\.kilo\plugin\attax-guard.ts"
npx tsx tests/client-smoke.ts
```

This starts and terminates a disposable local server, without a model call or gameplay mutation. Probe tool calls are mocked inside the real client process: actual model-driven dispatch/success metadata and reload of an already-open Attax client are not verified by this smoke. Restart the client to discover the deployed copy; restarting only an agent conversation may retain plugin state.

Read-only inspection of the installed runtime confirmed the skill wrapper includes the complete indexed skill body and that the read engine also uses a `path` argument. The direct-read fallback accepts both `path` and `filePath`; complete-content matching remains mandatory. This source inspection is not a substitute for the unverified model-driven boundary above.

`npm audit` reported four high-severity development dependency findings inherited through pinned `@kilocode/plugin@7.4.5` (`effect`, `toml`, `zod`, and the plugin aggregate). The entrypoint uses a type-only SDK import; these packages are not runtime imports of the deployed guard. No forced SDK upgrade or root Unity dependency modification was performed.
