import test from "node:test"
import assert from "node:assert/strict"
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync, rmSync } from "node:fs"
import { tmpdir } from "node:os"
import path from "node:path"
import type { PluginInput } from "@kilocode/plugin"
import guard from "../mmvc-guard.ts"

const skill = "---\nname: mmvc-unity\ndescription: Framework instructions\n---\n\n# MMVC Unity\n\nUse observable models.\n"
const bad = "using SavableObservable; class M : BaseObservableDataModel, ISharedSingleton {}\n"
let call = 0
async function fixture(options: Record<string, unknown> = {}, active = true, config?: unknown) {
  const root = mkdtempSync(path.join(tmpdir(), "mmvc-guard-"))
  mkdirSync(path.join(root, "Packages"))
  writeFileSync(path.join(root, "Packages/manifest.json"), JSON.stringify({ dependencies: active ? { "com.evialdev.savableobservable": "file:../local" } : {} }))
  const skillDir = path.join(root, ".kilo/skills/mmvc-unity")
  mkdirSync(skillDir, { recursive: true }); writeFileSync(path.join(skillDir, "SKILL.md"), skill)
  if (config !== undefined) writeFileSync(path.join(root, ".kilo/mmvc-guard.json"), typeof config === "string" ? config : JSON.stringify(config))
  const hooks = await guard.server({ directory: root, worktree: root } as PluginInput, options)
  const before = async (tool: string, args: unknown, sessionID = "parent") => hooks["tool.execute.before"]?.({ tool, sessionID, callID: String(++call) }, { args })
  const after = async (tool: string, args: unknown, output: string, metadata = {}, sessionID = "parent") => hooks["tool.execute.after"]?.({ tool, args, sessionID, callID: String(call) }, { title: "", output, metadata })
  const load = async (id = "parent") => after("skill", { name: "mmvc-unity" }, `<skill_content name="mmvc-unity">\n${skill}\n</skill_content>`, {}, id)
  return { root, skillDir, hooks, before, after, load, close: () => rmSync(root, { recursive: true, force: true }) }
}

test("inactive and off projects expose no guard hooks", async () => {
  for (const [options, active] of [[{}, false], [{ MMVC_GUARD_MODE: "off" }, true]] as const) {
    const f = await fixture(options, active); try { assert.deepEqual(f.hooks, {}) } finally { f.close() }
  }
})
test("new write requires successful load and never creates file on rejection", async () => {
  const f = await fixture(); try {
    const args = { filePath: "New.cs", content: "class New {}" }
    await assert.rejects(f.before("write", args), /skill/)
    assert.equal(existsSync(path.join(f.root, "New.cs")), false)
    await f.load(); await f.before("write", args)
    await assert.rejects(f.before("write", { ...args, content: bad }), /MMVC001/)
    assert.equal(existsSync(path.join(f.root, "New.cs")), false)
  } finally { f.close() }
})
test("failed, empty, truncated and partial skill reads cannot satisfy gate", async () => {
  const f = await fixture(); try {
    for (const [output, metadata] of [[skill, { error: "failure" }], ["", {}], [skill, { truncated: true }], ["# MMVC Unity", {}], [skill, { success: false }]] as const) {
      await f.after("skill", { name: "mmvc-unity" }, output, metadata)
      await assert.rejects(f.before("write", { filePath: "A.cs", content: "" }), /skill/)
    }
    await f.after("read", { filePath: path.join(f.skillDir, "SKILL.md") }, skill.split("\n").map((s, i) => `${i + 1}: ${s}`).join("\n"))
    await f.before("write", { filePath: "A.cs", content: "" })
  } finally { f.close() }
})
test("session gate is independent for children and restarted plugin state", async () => {
  const f = await fixture(); try {
    await f.load(); await assert.rejects(f.before("write", { filePath: "A.cs", content: "" }, "child"), /skill/)
    await f.load("child"); await f.before("write", { filePath: "A.cs", content: "" }, "child")
    const fresh = await guard.server({ directory: f.root, worktree: f.root } as PluginInput)
    await assert.rejects(fresh["tool.execute.before"]!({ tool: "write", sessionID: "parent", callID: "restart" }, { args: { filePath: "A.cs", content: "" } }), /skill/)
  } finally { f.close() }
})
test("system and compaction append guidance without replacing original context", async () => {
  const f = await fixture(); try {
    const output = { system: ["original"] }
    await f.hooks["experimental.chat.system.transform"]!({ model: {} as never }, output)
    await f.hooks["experimental.chat.system.transform"]!({ model: {} as never }, output)
    assert.equal(output.system.length, 2); assert.match(output.system[1], /two consecutive Play/)
    const compact = { context: ["original"], prompt: "existing" }
    await f.hooks["experimental.session.compacting"]!({ sessionID: "parent" }, compact)
    assert.equal(compact.prompt, "existing"); assert.equal(compact.context.length, 2)
  } finally { f.close() }
})
test("missing and malformed configured skills fail with actionable paths", async () => {
  for (const content of [undefined, "bad"]) {
    const f = await fixture(); try {
      const location = path.join(f.root, "configured")
      if (content) { mkdirSync(location); writeFileSync(path.join(location, "SKILL.md"), content) }
      const hooks = await guard.server({ directory: f.root, worktree: f.root } as PluginInput, { MMVC_GUARD_SKILL_DIR: location })
      await assert.rejects(hooks["tool.execute.before"]!({ tool: "write", sessionID: "s", callID: "a" }, { args: { filePath: "A.cs", content: "" } }), /Missing\/malformed.*MMVC_GUARD_SKILL_DIR/)
    } finally { f.close() }
  }
})
test("warn mode permits mutations and appends bounded diagnostics", async () => {
  const f = await fixture({ MMVC_GUARD_MODE: "warn" }); try {
    await f.before("write", { filePath: "A.cs", content: bad })
    const output = { title: "", output: "written", metadata: {} }
    await f.hooks["tool.execute.after"]!({ tool: "write", sessionID: "parent", callID: String(call), args: {} }, output)
    assert.match(output.output, /skill/); assert.match(output.output, /MMVC001/)
  } finally { f.close() }
})
test("external and generated paths are ignored; framework cache is protected", async () => {
  const f = await fixture(); try {
    for (const filePath of ["../External.cs", "Temp/A.cs", "obj/A.cs", "Library/A.cs", "Assets/readme.txt"]) await f.before("write", { filePath, content: bad })
    await assert.rejects(f.before("write", { filePath: "Library/PackageCache/com.evialdev.savableobservable@123/Runtime/A.cs", content: "" }), /read-only/)
  } finally { f.close() }
})
test("baseline violations survive line shifts; new identity or occurrence is blocked", async () => {
  const f = await fixture(); try {
    await f.load(); writeFileSync(path.join(f.root, "M.cs"), bad)
    await f.before("write", { filePath: "M.cs", content: "\n\n" + bad })
    await assert.rejects(f.before("write", { filePath: "M.cs", content: bad + "class Other : BaseObservableDataModel, ISharedSingleton {}" }), /MMVC001/)
    await assert.rejects(f.before("write", { filePath: "M.cs", content: "using SavableObservable; class M : BaseObservableDataModel {} class X { void F() { Services.Get<M>(); } }" }), /MMVC002/)
  } finally { f.close() }
})
test("edit and multiedit reconstruct exact content with CRLF and Windows separators", async () => {
  const f = await fixture(); try {
    await f.load(); mkdirSync(path.join(f.root, "Assets")); writeFileSync(path.join(f.root, "Assets/M.cs"), "using SavableObservable;\r\nclass M : BaseObservableDataModel {}\r\n")
    await assert.rejects(f.before("edit", { filePath: "Assets\\M.cs", oldString: "BaseObservableDataModel", newString: "BaseObservableDataModel, ISharedSingleton" }), /MMVC001/)
    await f.before("multiedit", { filePath: "Assets/M.cs", edits: [{ oldString: "class M", newString: "class Model" }, { oldString: "BaseObservableDataModel", newString: "BaseObservableDataModel" }] })
    await assert.rejects(f.before("edit", { filePath: "Assets/M.cs", oldString: "missing", newString: "" }), /Cannot reconstruct/)
    assert.match(readFileSync(path.join(f.root, "Assets/M.cs"), "utf8"), /class M /)
  } finally { f.close() }
})
test("patch new files have empty baseline and mixed multi-file operations reject before mutation", async () => {
  const f = await fixture(); try {
    const patch = (content: string) => `*** Begin Patch\n*** Add File: New.cs\n+${content}\n*** End Patch`
    await assert.rejects(f.before("apply_patch", { patchText: patch("class New {}") }), /skill/)
    await f.load(); await f.before("apply_patch", { patchText: patch("class New {}") })
    await assert.rejects(f.before("patch", { patch: patch(bad.trim()) }), /MMVC001/)
    writeFileSync(path.join(f.root, "Existing.cs"), "class Existing {}\n")
    await assert.rejects(f.before("apply_patch", { patchText: `*** Begin Patch\n*** Update File: Existing.cs\n@@\n-class Existing {}\n+class Changed {}\n*** Add File: Bad.cs\n+${bad.trim()}\n*** End Patch` }), /MMVC001/)
    assert.equal(readFileSync(path.join(f.root, "Existing.cs"), "utf8"), "class Existing {}\n")
    assert.equal(existsSync(path.join(f.root, "Bad.cs")), false)
  } finally { f.close() }
})
test("patch deletions require skill; renames entering scope count as additions", async () => {
  const f = await fixture(); try {
    writeFileSync(path.join(f.root, "M.cs"), bad)
    await assert.rejects(f.before("apply_patch", { patchText: "*** Begin Patch\n*** Delete File: M.cs\n*** End Patch" }), /skill/)
    await f.load(); await f.before("apply_patch", { patchText: "*** Begin Patch\n*** Delete File: M.cs\n*** End Patch" })
    writeFileSync(path.join(f.root, "source.txt"), bad)
    await assert.rejects(f.before("apply_patch", { patchText: `*** Begin Patch\n*** Update File: source.txt\n*** Move to: Incoming.cs\n@@\n-${bad.trim()}\n+${bad.trim()}\n*** End Patch` }), /MMVC001/)
    await f.before("apply_patch", { patchText: `*** Begin Patch\n*** Update File: M.cs\n*** Move to: Renamed.cs\n@@\n-${bad.trim()}\n+${bad.trim()}\n*** End Patch` })
    assert.equal(existsSync(path.join(f.root, "Incoming.cs")), false)
  } finally { f.close() }
})
test("malformed and ambiguous in-scope mutations fail closed", async () => {
  const f = await fixture(); try {
    await f.load()
    for (const [tool, args] of [["write", { filePath: "A.cs" }], ["edit", { filePath: "A.cs", oldString: "", newString: "x" }], ["apply_patch", { patchText: "*** Update File: A.cs\n???" }], ["multiedit", { filePath: "A.cs", edits: [] }]] as const) await assert.rejects(f.before(tool, args), /Cannot reconstruct/)
  } finally { f.close() }
})

test("all four canonical scanner rules have positive and negative fixtures", async () => {
  const f = await fixture(); try {
    await f.load()
    const prelude = "using SavableObservable; class M : BaseObservableDataModel {} class P : BaseObservablePresenter<M> {} "
    for (const [content, rule] of [
      ["using SavableObservable; class P : BaseObservablePresenter<M>, ISharedSingleton {}", "MMVC001"],
      [prelude + "class C { void F() { Services.Get<M>(); Services.Get<P>(); } }", "MMVC002"],
      [prelude + "class L : BaseLogic<M,P> { private P view; }", "MMVC003"],
      [prelude + "class L : BaseLogic<M,P> { void F() { new P(); } }", "MMVC003"],
      [prelude + "class L : BaseLogic<M,P> { void F() { GetComponent<P>(); } }", "MMVC003"],
      ["using SavableObservable; class P : BaseObservablePresenter<M> { protected override void InitializePresenter() { Setup(); } }", "MMVC004"],
      ["class P : SavableObservable.BaseObservablePresenter<M>, SavableObservable.ISharedSingleton {}", "MMVC001"],
    ]) await assert.rejects(f.before("write", { filePath: "Rule.cs", content }), new RegExp(rule))
    for (const content of [
      prelude + "class L : BaseLogic<M,P> {}",
      prelude + "class EditorTool { P view; void F() { GetComponent<P>(); } }",
      "using SavableObservable; class PlainService : MonoBehaviour, ISharedSingleton {}",
      "using SavableObservable; class M : BaseObservableDataModel {} class StateService : MonoBehaviour, ISharedSingleton { M GetModel() => null; }",
      "class PretendPresenter {} class L : BaseLogic<M,PretendPresenter> { PretendPresenter view; }",
      "using SavableObservable; class P : CustomExternalPresenter, ISharedSingleton {}",
      "using SavableObservable; class P : BaseObservablePresenter<M> { protected override void InitializePresenter() { base.InitializePresenter(); Setup(); } }",
      "using SavableObservable; namespace A { class P : BaseObservablePresenter<M> {} } namespace B { class P {} } class L : BaseLogic<M,P> { P view; }",
    ]) await f.before("write", { filePath: "Rule.cs", content })
  } finally { f.close() }
})

test("literal/comment forms do not fabricate violations, including nested interpolation", async () => {
  const f = await fixture(); try {
    await f.load()
    const content = `using SavableObservable;
class Fine {
// class Bad : BaseObservableDataModel, ISharedSingleton {}
/* Services.Get<M>(); */
string a = "class Bad : BaseObservableDataModel, ISharedSingleton {}";
string b = @"class Bad : BaseObservableDataModel, ISharedSingleton {}";
string c = $"{Call("class Bad : BaseObservableDataModel, ISharedSingleton {}")}";
string d = """class Bad : BaseObservableDataModel, ISharedSingleton {}""";
string e = $$"""{{Call("class Bad : BaseObservableDataModel, ISharedSingleton {}")}}""";
char ch = '\\'';
}`
    await f.before("write", { filePath: "Literal.cs", content })
    await assert.rejects(f.before("write", { filePath: "Literal.cs", content: content + bad }), /MMVC001/)
  } finally { f.close() }
})

test("adding another occurrence in the same member cannot hide behind baseline", async () => {
  const f = await fixture(); try {
    await f.load()
    const prefix = "using SavableObservable; class M : BaseObservableDataModel {} class C { void F() { "
    writeFileSync(path.join(f.root, "Count.cs"), prefix + "Services.Get<M>(); } }")
    await assert.rejects(f.before("write", { filePath: "Count.cs", content: prefix + "Services.Get<M>(); Services.Get<M>(); } }" }), /MMVC002/)
  } finally { f.close() }
})

test("skill body without frontmatter and read preview metadata are accepted only with complete content", async () => {
  const f = await fixture(); try {
    await f.after("skill", { name: "mmvc-unity" }, "# MMVC Unity\n\nUse observable models.\n")
    await f.before("write", { filePath: "Body.cs", content: "class Fine {}" })
    await f.after("read", { path: path.join(f.skillDir, "SKILL.md") }, skill, { preview: "---" }, "reader")
    await f.before("write", { filePath: "Body.cs", content: "class Fine {}" }, "reader")
  } finally { f.close() }
})

test("explicit opt-in activates unusual layouts and nested Unity directory wins over worktree", async () => {
  const f = await fixture({}, false); try {
    const hooks = await guard.server({ directory: f.root, worktree: path.dirname(f.root) } as PluginInput, { MMVC_GUARD_ENABLED: "true" })
    await assert.rejects(hooks["tool.execute.before"]!({ tool: "write", sessionID: "s", callID: "x" }, { args: { filePath: "Nested.cs", content: "" } }), /skill/)
  } finally { f.close() }
})

test("late or conditional presenter base calls warn rather than hard-block", async () => {
  const f = await fixture(); try {
    await f.load()
    await f.before("write", { filePath: "P.cs", content: "using SavableObservable; class P : BaseObservablePresenter<M> { protected override void InitializePresenter() { if (ready) { base.InitializePresenter(); } } }" })
    const output = { title: "", output: "written", metadata: {} }
    await f.hooks["tool.execute.after"]!({ tool: "write", sessionID: "parent", callID: String(call), args: {} }, output)
    assert.match(output.output, /MMVC-REVIEW/)
    assert.match(output.output, /conditional\/late/)
  } finally { f.close() }
})

test("new project scripts and commands redirect to explicit temporary locations", async () => {
  const f = await fixture(); try {
    await f.load()
    for (const filePath of ["helper.ps1", "scripts/helper.py", "tools/unity.ts", "helper.csx", ".kilo/command/unity.md", ".kilo/temp-other/helper.js"]) {
      await assert.rejects(f.before("write", { filePath, content: "unity run" }), /MMVC005[\s\S]*outside the project[\s\S]*NOT monitored/)
      assert.equal(existsSync(path.join(f.root, filePath)), false)
    }
    await f.before("write", { filePath: "Assets/New.cs", content: "class New {}" })
    await f.before("write", { filePath: "Packages/local/New.cs", content: "class New {}" })
    await f.before("write", { filePath: "notes.txt", content: "ordinary source material" })
  } finally { f.close() }
})

test("scratch subtree and OS temp locations are unmonitored for CSharp and scripts without skill loading", async () => {
  const f = await fixture(); try {
    const osTempFile = path.join(tmpdir(), "kilo-test-helper.ps1")
    const appDataTempFile = path.join(process.env.LOCALAPPDATA || tmpdir(), "Temp", "kilo-sub", "tool.py")
    const linuxTmpFile = "/tmp/kilo-test-helper.sh"
    for (const filePath of [".kilo/temp/helper.ps1", ".kilo\\temp\\nested\\Bad.cs", "../outside-helper.py", osTempFile, appDataTempFile, linuxTmpFile]) {
      await f.before("write", { filePath, content: bad })
    }
    await f.before("apply_patch", { patchText: `*** Begin Patch\n*** Add File: .kilo/temp/Bad.cs\n+${bad.trim()}\n*** Add File: .kilo/temp/helper.py\n+print('helper')\n*** End Patch` })
    await assert.rejects(f.before("write", { filePath: ".kilo/temp/../../helper.ps1", content: "" }), /MMVC005/)
    const system = { system: [] as string[] }
    await f.hooks["experimental.chat.system.transform"]!({ model: {} as never }, system)
    assert.match(system.system[0], /\.kilo.temp/)
    assert.match(system.system[0], /NOT monitored/)
  } finally { f.close() }
})

test("new script patches and moves reject whole operations while existing scripts stay editable", async () => {
  const f = await fixture(); try {
    writeFileSync(path.join(f.root, "existing.py"), "print('old')\n")
    await f.before("write", { filePath: "existing.py", content: "print('new')" })
    await f.before("edit", { filePath: "existing.py", oldString: "old", newString: "new" })
    const patchText = "*** Begin Patch\n*** Update File: existing.py\n@@\n-print('old')\n+print('changed')\n*** Add File: helper.ps1\n+unity run\n*** End Patch"
    await assert.rejects(f.before("apply_patch", { patchText }), /MMVC005/)
    assert.equal(readFileSync(path.join(f.root, "existing.py"), "utf8"), "print('old')\n")
    await assert.rejects(f.before("patch", { patch: "*** Begin Patch\n*** Update File: existing.py\n*** Move to: scripts/moved.py\n@@\n-print('old')\n+print('old')\n*** End Patch" }), /MMVC005/)
  } finally { f.close() }
})

test("warn mode reports helper creation without silently disabling checks", async () => {
  const f = await fixture({ MMVC_GUARD_MODE: "warn" }); try {
    await f.before("write", { filePath: "helper.ps1", content: "unity run" })
    const output = { title: "", output: "written", metadata: {} }
    await f.hooks["tool.execute.after"]!({ tool: "write", sessionID: "parent", callID: String(call), args: {} }, output)
    assert.match(output.output, /MMVC005/)
    assert.match(output.output, /\.kilo.temp/)
  } finally { f.close() }
})

test("JSON config controls individual rules and skill gate independently", async () => {
  const f = await fixture({}, true, { requireSkill: false, rules: { MMVC001: "off", MMVC005: "warn" } }); try {
    await f.before("write", { filePath: "Model.cs", content: bad })
    await f.before("write", { filePath: "helper.ps1", content: "" })
    const output = { title: "", output: "written", metadata: {} }
    await f.hooks["tool.execute.after"]!({ tool: "write", sessionID: "parent", callID: String(call), args: {} }, output)
    assert.match(output.output, /MMVC005/)
    await assert.rejects(f.before("write", { filePath: "P.cs", content: "using SavableObservable; class P : BaseObservablePresenter<M> { protected override void InitializePresenter() {} }" }), /MMVC004/)
  } finally { f.close() }
})

test("JSON lists replace defaults and directories use subtree boundaries", async () => {
  const f = await fixture({}, true, { excludedDirectories: ["scratch"], scriptExtensions: [".custom"], commandDirectories: ["commands"] }); try {
    await f.before("write", { filePath: "scratch/Bad.cs", content: bad })
    await assert.rejects(f.before("write", { filePath: "scratch-other/Bad.cs", content: bad }), /skill/)
    await assert.rejects(f.before("write", { filePath: ".kilo/temp/Bad.cs", content: bad }), /skill/)
    await f.before("write", { filePath: "helper.ps1", content: "" })
    await assert.rejects(f.before("write", { filePath: "helper.custom", content: "" }), /MMVC005/)
    await assert.rejects(f.before("write", { filePath: "commands/run.md", content: "" }), /MMVC005/)
  } finally { f.close() }
})

test("malformed configs retain blocking hooks instead of disappearing on load", async () => {
  for (const config of ["{", { unknown: true }, { rules: { MMVC999: "off" } }, { rules: { MMVC001: "ignore" } }, { requireSkill: "false" }, { scriptExtensions: ["*"] }, { excludedDirectories: ["."] }]) {
    const f = await fixture({}, true, config); try { await assert.rejects(f.before("write", { filePath: "Anything.txt", content: "" }), /Invalid configuration/) } finally { f.close() }
  }
})

test("plugin options override JSON and explicit config path is honored", async () => {
  const f = await fixture({ MMVC_GUARD_MODE: "enforce" }, true, { mode: "off" }); try {
    await assert.rejects(f.before("write", { filePath: "A.cs", content: "" }), /skill/)
    writeFileSync(path.join(f.root, "custom.json"), JSON.stringify({ mode: "off" }))
    const hooks = await guard.server({ directory: f.root, worktree: f.root } as PluginInput, { MMVC_GUARD_CONFIG: "custom.json" })
    assert.deepEqual(hooks, {})
    const missing = await guard.server({ directory: f.root, worktree: f.root } as PluginInput, { MMVC_GUARD_CONFIG: "missing.json" })
    await assert.rejects(missing["tool.execute.before"]!({ tool: "write", sessionID: "s", callID: "missing" }, { args: {} }), /does not exist/)
  } finally { f.close() }
})
