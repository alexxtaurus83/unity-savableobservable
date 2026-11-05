import type { Plugin, PluginModule } from "@kilocode/plugin"
import { existsSync, readFileSync, realpathSync } from "node:fs"
import { homedir, tmpdir } from "node:os"
import path from "node:path"

type Diagnostic = { rule: string; identity: string; line: number; reason: string; replacement: string; severity: "error" | "warning" }

function scan(source: string): Diagnostic[] {
  // Mask entire literals, including interpolation expressions. They are deliberately not linted.
  const masked = source.split("")
  const erase = (start: number, end: number) => { for (let n = start; n < end; n++) if (source[n] !== "\n" && source[n] !== "\r") masked[n] = " " }
  const literal = (start: number): number => {
    let p = start
    while (source[p] === "$" || source[p] === "@") p++
    if (source[p] !== '"' && !(p === start && source[p] === "'")) return start
    const quote = source[p], verbatim = source.slice(start, p).includes("@"), interpolated = source.slice(start, p).includes("$")
    let quotes = 1
    while (source[p + quotes] === '"' && quote === '"') quotes++
    if (quotes >= 3) {
      const end = source.indexOf('"'.repeat(quotes), p + quotes)
      return end < 0 ? source.length : end + quotes
    }
    p++
    while (p < source.length) {
      if (!verbatim && source[p] === "\\") { p += 2; continue }
      if (source[p] === quote) {
        if (verbatim && source[p + 1] === '"') { p += 2; continue }
        return p + 1
      }
      if (interpolated && source[p] === "{" && source[p + 1] === "{") { p += 2; continue }
      if (interpolated && source[p] === "{") {
        let depth = 1; p++
        while (p < source.length && depth) {
          if (source.startsWith("//", p)) { const end = source.indexOf("\n", p); p = end < 0 ? source.length : end; continue }
          if (source.startsWith("/*", p)) { const end = source.indexOf("*/", p + 2); p = end < 0 ? source.length : end + 2; continue }
          const end = literal(p)
          if (end !== p) { p = end; continue }
          if (source[p] === "{") depth++
          if (source[p] === "}") depth--
          p++
        }
        continue
      }
      p++
    }
    return source.length
  }
  for (let p = 0; p < source.length;) {
    if (source.startsWith("//", p)) { const end = source.indexOf("\n", p); const next = end < 0 ? source.length : end; erase(p, next); p = next; continue }
    if (source.startsWith("/*", p)) { const end = source.indexOf("*/", p + 2); const next = end < 0 ? source.length : end + 2; erase(p, next); p = next; continue }
    const end = literal(p)
    if (end !== p) { erase(p, end); p = end } else p++
  }
  const code = masked.join("")
  const tokens = [...code.matchAll(/@?[A-Za-z_]\w*|::|=>|[^\s]/g)].map(m => ({ value: m[0], position: m.index! }))
  const pairs = new Map<number, number>(), stack: number[] = []
  let balanced = true
  for (let i = 0; i < tokens.length; i++) {
    const value = tokens[i].value
    if (["{", "(", "["].includes(value)) stack.push(i)
    if (["}", ")", "]"].includes(value)) {
      const start = stack.pop()
      if (start === undefined || ({ "{": "}", "(": ")", "[": "]" } as Record<string, string>)[tokens[start].value] !== value) balanced = false
      else { pairs.set(start, i); pairs.set(i, start) }
    }
  }
  if (stack.length) balanced = false
  const diagnostics: Diagnostic[] = []
  const report = (rule: string, identity: string, index: number, reason: string, replacement: string, severity: "error" | "warning" = "error") => diagnostics.push({ rule, identity: `${rule}/${identity}`, line: source.slice(0, tokens[index]?.position ?? 0).split("\n").length, reason, replacement, severity })
  if (!balanced) { report("MMVC-REVIEW", "syntax", 0, "Unbalanced lexical scopes; framework relationships cannot be proved.", "Compile and review the complete C# declaration.", "warning"); return diagnostics }
  type Declaration = { name: string; identity: string; start: number; open: number; close: number; role?: "model" | "presenter" | "logic"; bases: string[] }
  const declarations: Declaration[] = []
  const values = (a: number, b: number) => tokens.slice(a, b).map(t => t.value).join("")
  const framework = new Map<string, Declaration["role"]>([["BaseObservableDataModel", "model"], ["BasePresenter", "presenter"], ["BaseObservablePresenter", "presenter"], ["ObservablePresenterWithLogic", "presenter"], ["BaseLogic", "logic"]])
  const imported = /\busing\s+SavableObservable\s*;/.test(code)
  const aliases = new Set([...code.matchAll(/\busing\s+(\w+)\s*=/g)].map(m => m[1]))
  const ownNames = new Set([...code.matchAll(/\b(?:class|struct|interface|record|enum)\s+(\w+)/g)].map(m => m[1]))
  const knownBase = (base: string) => {
    const name = base.split("<")[0].replace(/^global::/, "")
    if (name.startsWith("SavableObservable.") && name.split(".").length === 2) return framework.get(name.slice(18))
    if (!name.includes(".") && imported && !ownNames.has(name) && !aliases.has(name)) return framework.get(name)
    return undefined
  }
  for (let i = 0; i < tokens.length; i++) {
    if (tokens[i].value !== "class" || !/^@?[A-Za-z_]\w*$/.test(tokens[i + 1]?.value ?? "")) continue
    let open = i + 2
    while (open < tokens.length && !["{", ";"].includes(tokens[open].value)) open++
    const close = pairs.get(open)
    if (close === undefined || tokens[open].value !== "{") continue
    const bases: string[] = []
    let colon = i + 2, generic = 0
    while (colon < open) {
      if (tokens[colon].value === "<") generic++
      if (tokens[colon].value === ">") generic--
      if (tokens[colon].value === ":" && generic === 0) break
      colon++
    }
    if (colon < open) {
      let start = colon + 1; generic = 0
      for (let n = start; n <= open; n++) {
        if (tokens[n]?.value === "<") generic++
        if (tokens[n]?.value === ">") generic--
        if (n === open || (generic === 0 && [",", "where"].includes(tokens[n].value))) {
          bases.push(values(start, n)); start = n + 1
          if (tokens[n]?.value === "where") break
        }
      }
    }
    const owner = declarations.filter(d => i > d.open && i < d.close).at(-1)
    const namespace = [...code.slice(0, tokens[i].position).matchAll(/\bnamespace\s+([\w.]+)\s*[;{]/g)].at(-1)?.[1] ?? ""
    const name = tokens[i + 1].value
    const role = bases.map(knownBase).find(Boolean)
    declarations.push({ name, identity: owner ? `${owner.identity}.${name}` : `${namespace}.${name}`, start: i, open, close, role, bases })
  }
  const resolve = (type: string): Declaration | undefined => {
    const name = type.replace(/^global::/, "")
    if (aliases.has(name)) return undefined
    const found = declarations.filter(d => name.includes(".") ? d.identity.replace(/^\./, "") === name : d.name === name)
    return found.length === 1 ? found[0] : undefined
  }
  const ownerAt = (index: number) => declarations.filter(d => index > d.open && index < d.close).at(-1)
  const memberAt = (owner: Declaration, index: number): string => {
    let start = owner.open + 1
    for (let n = start; n < index; n++) {
      if (tokens[n].value === "{" && pairs.has(n)) {
        const end = pairs.get(n)!
        if (end >= index) return values(start, n)
        n = end; start = end + 1
      } else if (tokens[n].value === ";") start = n + 1
    }
    // For expression-bodied members, exclude the expression itself from identity.
    let end = index
    for (let n = start; n < index; n++) if (tokens[n].value === "=>" || tokens[n].value === "=") { end = n; break }
    return values(start, end)
  }
  for (const declaration of declarations) {
    const singleton = declaration.bases.some(base => base === "SavableObservable.ISharedSingleton" || (base === "ISharedSingleton" && imported && !ownNames.has(base) && !aliases.has(base)))
    if (["model", "presenter"].includes(declaration.role ?? "") && singleton) report("MMVC001", declaration.identity, declaration.start, "Model/Presenter directly implements ISharedSingleton.", "Remove ISharedSingleton; expose the unit through Logic or a plain MonoBehaviour service.")
    if (!declaration.role && declaration.bases.length) report("MMVC-REVIEW", declaration.identity, declaration.start, "Custom or external inheritance is not resolved by this file-local scanner.", "Review inherited MMVC roles, presenter access and lifecycle against mmvc-unity.", "warning")
  }
  const eventPattern = "Write Model observables; for one-shot view commands use [NonSaved] latestEvent plus eventToken.Value++ and a Presenter ObservableHandler (see mmvc-unity)."
  for (let i = 0; i < tokens.length; i++) {
    const owner = ownerAt(i)
    // Resolve only literal framework Services calls, not arbitrary suffix-matching receivers.
    if (tokens[i].value === "Services" && tokens[i + 1]?.value === "." && tokens[i + 2]?.value === "Get" && tokens[i + 3]?.value === "<") {
      const qualified = values(Math.max(0, i - 2), i) === "SavableObservable."
      const unqualified = ![".", "::"].includes(tokens[i - 1]?.value) && imported && !ownNames.has("Services") && !aliases.has("Services")
      let end = i + 4
      while (end < tokens.length && ![">", "<", ";", "{"].includes(tokens[end].value)) end++
      if ((qualified || unqualified) && tokens[end]?.value === ">" && tokens[end + 1]?.value === "(") {
        const type = values(i + 4, end), target = resolve(type)
        if (target?.role === "model" || target?.role === "presenter") report("MMVC002", `${owner?.identity}/${owner ? memberAt(owner, i) : "global"}/${target.identity}`, i, `Services.Get<${type}> resolves a ${target.role}.`, target.role === "model" ? `Use Services.GetModel<${type}>() or Services.Get<OwningLogic>().GetModel().` : `Use Services.Get<OwningLogic>(); ${eventPattern}`)
      }
    }
    if (!owner) continue
    if (owner.role === "presenter" && tokens[i].value === "InitializePresenter" && tokens[i + 1]?.value === "(" && tokens[i + 2]?.value === ")") {
      let begin = i - 1
      while (begin > owner.open && ![";", "}", "{"].includes(tokens[begin - 1].value)) begin--
      if (!tokens.slice(begin, i).some(t => t.value === "override") || tokens[i - 1]?.value !== "void") continue
      const open = i + 3
      let end = pairs.get(open)
      if (tokens[open]?.value === "=>") { end = open + 1; while (end < owner.close && tokens[end].value !== ";") end++ }
      if (end === undefined) continue
      const body = values(open + 1, end), call = "base.InitializePresenter()"
      if (!body.includes(call)) report("MMVC004", `${owner.identity}/InitializePresenter`, i, "InitializePresenter override does not invoke its base implementation.", "Begin with base.InitializePresenter(); before static UI setup.")
      else if (!body.startsWith(call + ";") && body !== call) report("MMVC-REVIEW", `${owner.identity}/InitializePresenter/base-order`, i, "Base initialization exists but is not the first statement; conditional/late execution requires review.", "Call base.InitializePresenter(); unconditionally first.", "warning")
    }
    if (owner.role !== "logic") continue
    const isNew = tokens[i].value === "new"
    const lookup = /^(?:GetComponent(?:InChildren|InParent)?|GetComponents(?:InChildren|InParent)?|TryGetComponent|AddComponent|FindObjectOfType|FindObjectsOfType|FindFirstObjectByType|FindAnyObjectByType|FindObjectsByType)$/.test(tokens[i].value) && tokens[i + 1]?.value === "<"
    if (isNew || lookup) {
      const start = i + (isNew ? 1 : 2)
      let end = start
      while (end < tokens.length && /^[\w@.:]+$/.test(tokens[end].value)) end++
      const target = resolve(values(start, end))
      if (target?.role === "presenter" && tokens[end]?.value === (isNew ? "(" : ">")) report("MMVC003", `${owner.identity}/${memberAt(owner, i)}/${isNew ? "new" : tokens[i].value}/${target.identity}`, i, `Logic ${isNew ? "constructs" : "looks up"} known Presenter ${target.name}.`, eventPattern)
    }
    // Field declarations are immediate class members; skip attributes, methods, properties and local variables.
    let immediate = true
    for (let n = owner.open + 1; n < i; n++) if (tokens[n].value === "{" && (pairs.get(n) ?? -1) >= i) { immediate = false; break }
    if (!immediate) continue
    if (i !== owner.open + 1 && ![";", "}"].includes(tokens[i - 1]?.value)) continue
    let start = i, end = i
    while (tokens[start]?.value === "[" && pairs.has(start)) start = pairs.get(start)! + 1
    while (/^(?:public|private|protected|internal|static|readonly|volatile|new|required)$/.test(tokens[start]?.value ?? "")) start++
    end = start
    while (end < owner.close && ![";", "{", "("].includes(tokens[end].value)) end++
    if (tokens[end]?.value !== ";") continue
    let typeEnd = start + 1
    while ([".", "::"].includes(tokens[typeEnd]?.value)) typeEnd += 2
    const target = resolve(values(start, typeEnd))
    if (target?.role !== "presenter") continue
    while (["?", "[", "]"].includes(tokens[typeEnd]?.value)) typeEnd++
    const name = tokens[typeEnd]?.value
    if (/^@?[A-Za-z_]\w*$/.test(name ?? "") && [";", "=", ","].includes(tokens[typeEnd + 1]?.value)) report("MMVC003", `${owner.identity}/field:${name}/${target.identity}`, typeEnd, `Logic stores known Presenter ${target.name} in a field.`, eventPattern)
  }
  return diagnostics
}

type Change = { from: string; to: string; before: string; after: string }
const mutations = new Set(["write", "edit", "multiedit", "apply_patch", "patch"])
const guidance = `[mmvc-guard] Load skill { name: "mmvc-unity" } before C# mutations, including new files and deletions. Read task-relevant references. Choose the smallest meaningful service/model/view shape; no dummy components. Logic -> Model -> notification -> Presenter; never access presenters from Logic. Bind only on the same GameObject. Initialize UI in InitializePresenter with base first; preserve lifecycle and initial display. Track subscriptions and tear down pooled/destroyed subscribers. Mark transient observables [NonSaved]; add persistence only when needed. Verify compilation and relevant runtime behavior, including two consecutive Play runs without recompilation for statics/services/lifecycles. This conservative guard is not a semantic compiler or a security sandbox.`

const MMVCGuard: Plugin = async ({ directory, worktree }, options = {}) => {
  const root = realpathSync(existsSync(path.join(directory, "Packages", "manifest.json")) ? directory : worktree || directory)
  type Level = "error" | "warn" | "off"
  type Config = { mode?: "enforce" | "warn" | "off"; enabled?: boolean; skillDir?: string; requireSkill?: boolean; protectPackageCache?: boolean; excludeOsTemp?: boolean; excludedDirectories?: string[]; scriptExtensions?: string[]; commandDirectories?: string[]; rules?: Record<string, Level> }
  const configPath = path.resolve(root, String(process.env.MMVC_GUARD_CONFIG ?? options.MMVC_GUARD_CONFIG ?? ".kilo/mmvc-guard.json"))
  let config: Config = {}
  let configError = ""
  try {
    const explicit = process.env.MMVC_GUARD_CONFIG !== undefined || options.MMVC_GUARD_CONFIG !== undefined
    if (existsSync(configPath)) {
      const value = JSON.parse(readFileSync(configPath, "utf8").replace(/^\uFEFF/, ""))
      if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error("expected a JSON object")
      const allowed = ["mode", "enabled", "skillDir", "requireSkill", "protectPackageCache", "excludeOsTemp", "excludedDirectories", "scriptExtensions", "commandDirectories", "rules"]
      for (const [key, item] of Object.entries(value)) {
        if (!allowed.includes(key)) throw new Error(`unknown setting ${key}`)
        if (["enabled", "requireSkill", "protectPackageCache", "excludeOsTemp"].includes(key) && typeof item !== "boolean") throw new Error(`${key} must be boolean`)
        if (key === "mode" && !["enforce", "warn", "off"].includes(String(item))) throw new Error("mode must be enforce, warn or off")
        if (key === "skillDir" && (typeof item !== "string" || !item.trim())) throw new Error("skillDir must be a nonempty path")
        if (["excludedDirectories", "scriptExtensions", "commandDirectories"].includes(key) && (!Array.isArray(item) || item.some(p => typeof p !== "string" || !p.trim()))) throw new Error(`${key} must be an array of nonempty strings`)
        if (key === "excludedDirectories" && (item as string[]).some(dir => path.resolve(root, dir.replace(/[\\/]/g, path.sep)) === root || (existsSync(path.resolve(root, dir)) && realpathSync(path.resolve(root, dir)) === root))) throw new Error("excludedDirectories must not exclude the entire project; use mode off explicitly")
        if (key === "scriptExtensions" && (item as string[]).some(ext => !/^\.[a-z0-9]+$/i.test(ext))) throw new Error("scriptExtensions entries must be extensions such as .ps1")
        if (key === "rules") {
          if (!item || typeof item !== "object" || Array.isArray(item)) throw new Error("rules must be an object")
          for (const [rule, level] of Object.entries(item)) if (!["MMVC001", "MMVC002", "MMVC003", "MMVC004", "MMVC005", "MMVC-REVIEW"].includes(rule) || !["error", "warn", "off"].includes(String(level))) throw new Error(`invalid rule/level ${rule}: ${String(level)}`)
        }
      }
      config = value
    } else if (explicit) throw new Error("explicit configuration file does not exist")
  } catch (error) { configError = `[mmvc-guard] Invalid configuration ${configPath}: ${String(error)}. Repair the JSON and restart Kilo. No settings from the invalid file were applied.` }
  const jsonSettings: Record<string, unknown> = { MMVC_GUARD_MODE: config.mode, MMVC_GUARD_ENABLED: config.enabled, MMVC_GUARD_SKILL_DIR: config.skillDir }
  const setting = (key: string) => process.env[key] ?? options[key] ?? jsonSettings[key]
  const mode = setting("MMVC_GUARD_MODE") ?? "enforce"
  if (!["enforce", "warn", "off"].includes(String(mode))) throw new Error("[mmvc-guard] Invalid MMVC_GUARD_MODE; use enforce, warn or off.")
  if (mode === "off") return {}
  let active = ["1", "true", true].includes(setting("MMVC_GUARD_ENABLED") as string | boolean)
  const manifest = path.join(root, "Packages", "manifest.json")
  if (existsSync(manifest)) {
    try { active ||= Object.hasOwn(JSON.parse(readFileSync(manifest, "utf8")).dependencies ?? {}, "com.evialdev.savableobservable") }
    catch { throw new Error(`[mmvc-guard] Cannot parse ${manifest}; repair manifest or explicitly set MMVC_GUARD_MODE=off.`) }
  }
  if (!active) return {}
  // Do not throw at plugin initialization: Kilo can isolate load errors and silently omit a guard.
  if (configError) return {
    "experimental.chat.system.transform": async (_, output) => { output.system.push(configError) },
    "experimental.session.compacting": async (_, output) => { output.context.push(configError) },
    "tool.execute.before": async input => { if (mutations.has(input.tool)) throw new Error(configError) },
  }

  const canonical = (file: string): string => {
    const absolute = path.resolve(root, file.replace(/[\\/]/g, path.sep))
    let ancestor = absolute
    const tail: string[] = []
    while (!existsSync(ancestor)) {
      const parent = path.dirname(ancestor)
      if (parent === ancestor) break
      tail.unshift(path.basename(ancestor)); ancestor = parent
    }
    return path.join(existsSync(ancestor) ? realpathSync(ancestor) : ancestor, ...tail)
  }
  const tempRoots = [
    tmpdir(), process.env.TEMP, process.env.TMP,
    process.env.LOCALAPPDATA ? path.join(process.env.LOCALAPPDATA, "Temp") : undefined,
    ...(process.platform === "win32" ? [path.join(homedir(), "AppData", "Local", "Temp")] : ["/tmp", "/var/tmp"]),
    process.env.XDG_RUNTIME_DIR,
  ].filter((p): p is string => typeof p === "string" && !!p.trim()).map(canonical)
  // Filter out any temp root that contains the project root (e.g. test runner temp dirs under os tmpdir)
  const isUnder = (parent: string, file: string) => { const rel = path.relative(parent, file); return rel !== "" && rel !== ".." && !rel.startsWith(`..${path.sep}`) && !path.isAbsolute(rel) }
  const inside = (file: string) => isUnder(root, file) || path.resolve(file) === root
  const isOsTemp = (file: string) => config.excludeOsTemp !== false && tempRoots.filter(tr => !isUnder(tr, root) && tr !== root).some(tr => isUnder(tr, file) || tr === file)
  const excludedRoots = (config.excludedDirectories ?? [".kilo/temp"]).map(canonical)
  const scratch = (file: string) => excludedRoots.some(dir => isUnder(dir, file) || dir === file) || isOsTemp(file)
  const csharp = (file: string) => inside(file) && !scratch(file) && /\.cs$/i.test(file)
  const cached = (file: string) => inside(file) && /^Library[\\/]PackageCache[\\/]com\.evialdev\.savableobservable(?:@|[\\/])/i.test(path.relative(root, file))
  const excluded = (file: string) => /^(Library|Temp|obj)([\\/]|$)/i.test(path.relative(root, file))
  const candidates = [
    ...(typeof setting("MMVC_GUARD_SKILL_DIR") === "string" ? [path.resolve(root, String(setting("MMVC_GUARD_SKILL_DIR")))] : []),
    path.join(root, ".kilo", "skills", "mmvc-unity"),
    path.join(homedir(), ".skills", "mmvc-unity"),
  ]
  // Explicit configuration is authoritative, including a missing or malformed directory.
  const skillDir = typeof setting("MMVC_GUARD_SKILL_DIR") === "string" ? candidates[0] : candidates.find(p => existsSync(path.join(p, "SKILL.md")))
  const skillPath = skillDir ? canonical(path.join(skillDir, "SKILL.md")) : undefined
  let skill = ""
  let skillProblem = ""
  try {
    if (!skillPath) throw new Error("not found")
    skill = readFileSync(skillPath, "utf8")
    const frontmatter = /^---\r?\n([\s\S]*?)\r?\n---\r?\n([\s\S]+)$/.exec(skill)
    if (!frontmatter || !/^name:\s*mmvc-unity\s*$/m.test(frontmatter[1]) || !/^description:\s*\S.+$/m.test(frontmatter[1]) || !frontmatter[2].trim()) throw new Error("malformed frontmatter or empty body")
  } catch (error) { skillProblem = `[mmvc-guard] Missing/malformed mmvc-unity skill (${String(error)}). Install SKILL.md and references in one of: ${candidates.join(", ")}. Or set MMVC_GUARD_SKILL_DIR to the skill directory.` }
  const loaded = new Set<string>()
  const warnings = new Map<string, string[]>()
  const text = (file: string) => existsSync(file) ? readFileSync(file, "utf8") : ""
  const reject = (message: string, notes: string[]) => {
    if (mode === "enforce") throw new Error(message)
    notes.push(message)
  }
  const levelFor = (rule: string, fallback: Level = "error"): Level => config.rules?.[rule] ?? fallback
  const scriptExtensions = new Set((config.scriptExtensions ?? [".ps1", ".psm1", ".psd1", ".sh", ".bash", ".zsh", ".fish", ".cmd", ".bat", ".py", ".pyw", ".js", ".mjs", ".cjs", ".ts", ".mts", ".cts", ".tsx", ".jsx", ".lua", ".rb", ".pl", ".ipynb", ".csx"]).map(ext => ext.toLowerCase()))
  const commandRoots = (config.commandDirectories ?? [".kilo/command", ".kilo/commands"]).map(canonical)
  const temporaryGuidance = `[mmvc-guard] Put temporary support files, Unity CLI command wrappers and one-off scripts outside the project in standard OS temp folders (e.g. %LOCALAPPDATA%\\Temp, /tmp, /var/tmp, or ${path.join(tmpdir(), "kilo")}). Configured excluded directories: ${excludedRoots.join(", ") || "none"}. These exact subtrees are NOT monitored (including C#); similarly named siblings such as .kilo/temp-other/ are not exempt. OS temp exclusions: ${config.excludeOsTemp !== false ? "enabled (a temp ancestor never exempts the project itself)" : "disabled"}. Never use exemptions for production source. Prefer direct Unity CLI commands when no script is needed. Settings: ${configPath}; restart Kilo after operator-approved changes. Skill gate: ${config.requireSkill !== false ? "enabled" : "disabled"}; rule overrides: ${JSON.stringify(config.rules ?? {})}.`
  const inspectCreation = (file: string, notes: string[]) => {
    if (!inside(file) || scratch(file) || isOsTemp(file) || excluded(file) || existsSync(file)) return
    const script = scriptExtensions.has(path.extname(file).toLowerCase())
    const command = commandRoots.some(dir => isUnder(dir, file))
    const level = levelFor("MMVC005")
    if ((script || command) && level !== "off") {
      const message = `[mmvc-guard] MMVC005 ${file}: creation of a standalone script or command in the monitored project. ${temporaryGuidance} If this is intentional permanent tooling, obtain explicit operator approval to change configuration or use MMVC_GUARD_MODE=warn; do not silently disable the guard.`
      if (level === "warn") notes.push(message); else reject(message, notes)
    }
  }
  const inspectCreations = (tool: string, args: Record<string, any>, notes: string[]) => {
    if (tool === "write" && typeof args.filePath === "string") inspectCreation(canonical(args.filePath), notes)
    if (!["apply_patch", "patch"].includes(tool)) return
    const patch = args.patchText ?? args.patch ?? args.input
    if (typeof patch !== "string") return
    // Inspect all destinations before C# reconstruction, including non-C# Add File and moves.
    for (const match of patch.matchAll(/^\*\*\* (?:Add File|Move to): (.+)\r?$/gm)) inspectCreation(canonical(match[1].trimEnd()), notes)
  }
  const replace = (source: string, edit: Record<string, unknown>) => {
    if (typeof edit.oldString !== "string" || typeof edit.newString !== "string" || !edit.oldString) throw new Error("edit requires nonempty oldString and newString")
    const count = source.split(edit.oldString).length - 1
    if (!count || (count !== 1 && edit.replaceAll !== true)) throw new Error("edit text is missing or ambiguous; provide exact unique oldString or replaceAll")
    return edit.replaceAll === true ? source.split(edit.oldString).join(edit.newString) : source.replace(edit.oldString, () => String(edit.newString))
  }
  const reconstruct = (tool: string, args: Record<string, any>): Change[] => {
    if (tool === "write" || tool === "edit" || tool === "multiedit") {
      if (typeof args.filePath !== "string") throw new Error(`${tool} requires filePath`)
      const file = canonical(args.filePath)
      if (!csharp(file)) return []
      const before = text(file)
      if (tool === "write") {
        if (typeof args.content !== "string") throw new Error("write requires content")
        return [{ from: file, to: file, before, after: args.content }]
      }
      if (!existsSync(file)) throw new Error("edit target does not exist; use write or Add File")
      const edits = tool === "multiedit" ? args.edits : [args]
      if (!Array.isArray(edits) || !edits.length) throw new Error("multiedit requires edits")
      return [{ from: file, to: file, before, after: edits.reduce((s, e) => replace(s, e), before) }]
    }
    const patch = args.patchText ?? args.patch ?? args.input
    if (typeof patch !== "string") throw new Error("patch requires patchText")
    const lines = patch.replace(/\r\n/g, "\n").split("\n")
    if (lines[0] !== "*** Begin Patch" || !lines.includes("*** End Patch")) throw new Error("expected Begin Patch/End Patch envelope")
    const changes: Change[] = []
    const touched = new Set<string>()
    let i = 1
    while (i < lines.length && lines[i] !== "*** End Patch") {
      const header = /^\*\*\* (Add|Update|Delete) File: (.+)$/.exec(lines[i++])
      if (!header) throw new Error("unrecognized patch file header")
      const from = canonical(header[2])
      let to = from
      if (lines[i]?.startsWith("*** Move to: ")) to = canonical(lines[i++].slice(13))
      if (touched.has(from) || (from !== to && touched.has(to))) throw new Error("repeated patch paths are ambiguous; use one operation per path")
      touched.add(from); touched.add(to)
      const start = i
      while (i < lines.length && !/^\*\*\* (?:Add|Update|Delete) File: |^\*\*\* End Patch$/.test(lines[i])) i++
      const body = lines.slice(start, i)
      if (!csharp(from) && !csharp(to)) continue
      const before = text(from)
      if (header[1] === "Add") {
        if (existsSync(to) || body.some(line => !line.startsWith("+"))) throw new Error("Add File requires a nonexistent target and + lines")
        changes.push({ from, to, before: "", after: body.map(line => line.slice(1)).join("\n") + "\n" }); continue
      }
      if (!existsSync(from)) throw new Error("patch target does not exist")
      if (header[1] === "Delete") {
        if (body.length) throw new Error("Delete File must not have a body")
        changes.push({ from, to, before, after: "" }); continue
      }
      if (from !== to && existsSync(to)) throw new Error("rename destination already exists")
      const original = before.replace(/\r\n/g, "\n").split("\n")
      if (original.at(-1) === "") original.pop()
      let result: string[] = [], cursor = 0, b = 0
      while (b < body.length) {
        if (!/^@@(?: |$)/.test(body[b])) throw new Error("update hunks require @@ markers and exact context")
        const anchor = body[b++].slice(2).trim()
        if (anchor) {
          const locations = original.map((line, n) => line === anchor && n >= cursor ? n : -1).filter(n => n >= 0)
          if (locations.length !== 1) throw new Error("patch anchor is ambiguous or missing")
          result.push(...original.slice(cursor, locations[0] + 1)); cursor = locations[0] + 1
        }
        const old: string[] = [], next: string[] = []
        while (b < body.length && !body[b].startsWith("@@")) {
          const line = body[b++]
          if (line === "*** End of File") continue
          if (!/^[ +\-]/.test(line)) throw new Error("unrecognized patch hunk line")
          if (line[0] !== "+") old.push(line.slice(1))
          if (line[0] !== "-") next.push(line.slice(1))
        }
        const positions: number[] = []
        if (!old.length) throw new Error("context-free insertion is ambiguous; provide context")
        for (let n = cursor; n <= original.length - old.length; n++) if (old.every((line, j) => original[n + j] === line)) positions.push(n)
        if (positions.length !== 1) throw new Error("patch context is missing or ambiguous; use exact unique context")
        result.push(...original.slice(cursor, positions[0]), ...next); cursor = positions[0] + old.length
      }
      if (!body.length) throw new Error("empty update is ambiguous")
      result.push(...original.slice(cursor))
      changes.push({ from, to, before, after: result.join("\n") + "\n" })
    }
    if (lines.slice(i + 1).some(line => line.trim())) throw new Error("unexpected content after End Patch")
    return changes
  }

  return {
    "experimental.chat.system.transform": async (_, output) => { if (!output.system.some(text => text.startsWith(guidance))) output.system.push(guidance + `\n${temporaryGuidance}` + (skillProblem ? `\n${skillProblem}` : "")) },
    "experimental.session.compacting": async (_, output) => { output.context.push(guidance + `\n${temporaryGuidance}`) },
    "tool.execute.after": async (input, output) => {
      const metadata = output.metadata ?? {}
      const success = !metadata.error && metadata.success !== false && metadata.status !== "error" && !metadata.truncated && typeof output.output === "string" && !!output.output.trim()
      if (!skillProblem && success) {
        const body = skill.replace(/\r\n/g, "\n").replace(/^---\n[\s\S]*?\n---\n/, "").trim()
        if (input.tool === "skill" && input.args?.name === "mmvc-unity" && output.output.replace(/\r\n/g, "\n").includes(body)) loaded.add(input.sessionID)
        const readPath = input.args?.filePath ?? input.args?.path
        if (input.tool === "read" && typeof readPath === "string" && canonical(readPath) === skillPath) {
          // Read adds line-number prefixes. Require every source line in order, never just a title or preview.
          const content = output.output.split(/\r?\n/).map(line => line.replace(/^\d+: ?/, "")).join("\n")
          if (content.includes(skill.replace(/\r\n/g, "\n").trim())) loaded.add(input.sessionID)
        }
      }
      const notes = warnings.get(input.callID)
      if (notes) { output.output += `\n\n${notes.join("\n")}`; warnings.delete(input.callID) }
    },
    "tool.execute.before": async (input, output) => {
      if (!mutations.has(input.tool)) return
      const notes: string[] = []
      inspectCreations(input.tool, output.args ?? {}, notes)
      let changes: Change[]
      try { changes = reconstruct(input.tool, output.args ?? {}) }
      catch (error) {
        // Unknown recognized mutations cannot silently bypass the gate. Non-C# explicit targets are inert.
        const explicit = output.args?.filePath
        if (typeof explicit === "string" && !csharp(canonical(explicit))) { if (notes.length) warnings.set(input.callID, notes); return }
        const patch = output.args?.patchText ?? output.args?.patch ?? output.args?.input
        if (typeof patch === "string" && !/\.cs\b/i.test(patch)) { if (notes.length) warnings.set(input.callID, notes); return }
        reject(`[mmvc-guard] Cannot reconstruct mutation: ${String(error)}. Use write {filePath, content}, exact edit/multiedit, or a supported exact-context apply_patch.`, notes)
        if (notes.length) warnings.set(input.callID, notes)
        return
      }
      const applicable = changes.filter(change => [change.from, change.to].some(file => csharp(file) && !excluded(file)))
      if (config.protectPackageCache !== false && changes.some(change => [change.from, change.to].some(cached))) reject("[mmvc-guard] Cached framework package source is read-only. Fix application code or the authorized embedded/local source, never Library/PackageCache.", notes)
      if (config.requireSkill !== false && applicable.length && !loaded.has(input.sessionID)) reject(skillProblem || `[mmvc-guard] C# mutation blocked: first successfully load skill { name: "mmvc-unity" } in this session. Direct-read fallback: read the complete ${skillPath}. Failed, empty or truncated reads do not count; children must load independently.`, notes)
      for (const change of applicable) {
        const baseline = new Map<string, number>()
        // A move entering application scope is a new file, even if its source already violated rules.
        for (const diagnostic of scan(csharp(change.from) && !excluded(change.from) ? change.before : "")) if (diagnostic.severity === "error") baseline.set(diagnostic.identity, (baseline.get(diagnostic.identity) ?? 0) + 1)
        const introduced: string[] = []
        for (const diagnostic of scan(csharp(change.to) && !excluded(change.to) ? change.after : "")) {
          const level = levelFor(diagnostic.rule, diagnostic.severity === "warning" ? "warn" : "error")
          if (level === "off") continue
          const message = `[mmvc-guard] ${diagnostic.rule} ${change.to}:${diagnostic.line}: ${diagnostic.reason} Replacement: ${diagnostic.replacement}`
          if (level === "warn" || diagnostic.severity === "warning") { notes.push(message); continue }
          const count = baseline.get(diagnostic.identity) ?? 0
          if (count) { baseline.set(diagnostic.identity, count - 1); notes.push(`Existing violation (not blocked): ${message}`) }
          else introduced.push(message)
        }
        if (introduced.length) reject(introduced.join("\n"), notes)
      }
      if (notes.length) warnings.set(input.callID, notes.slice(0, 12))
    },
  }
}

export default { id: "mmvc-guard", server: MMVCGuard } satisfies PluginModule
