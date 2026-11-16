import { mkdtempSync, mkdirSync, copyFileSync, writeFileSync, readFileSync, rmSync } from "node:fs"
import { tmpdir } from "node:os"
import path from "node:path"
import { spawn } from "node:child_process"
import { fileURLToPath } from "node:url"

const binary = process.env.KILO_SMOKE_BINARY
const attax = process.env.KILO_SMOKE_ATTAX_GUARD
if (!binary || !attax) throw new Error("Set KILO_SMOKE_BINARY and KILO_SMOKE_ATTAX_GUARD to installed client/guard paths.")
const root = mkdtempSync(path.join(tmpdir(), "mmvc-client-smoke-"))
try {
  const plugins = path.join(root, ".kilo/plugin")
  mkdirSync(plugins, { recursive: true }); mkdirSync(path.join(root, "Packages"))
  writeFileSync(path.join(root, "Packages/manifest.json"), JSON.stringify({ dependencies: { "com.evialdev.savableobservable": "file:../framework" } }))
  const skillDir = path.join(root, ".kilo/skills/mmvc-unity")
  mkdirSync(skillDir, { recursive: true })
  const skill = "---\nname: mmvc-unity\ndescription: Disposable smoke skill\n---\n# MMVC Unity\nFollow the observable architecture.\n"
  writeFileSync(path.join(skillDir, "SKILL.md"), skill)
  copyFileSync(fileURLToPath(new URL("../mmvc-guard.ts", import.meta.url)), path.join(plugins, "mmvc-guard.ts"))
  copyFileSync(fileURLToPath(new URL("../mmvc-guard.example.json", import.meta.url)), path.join(root, ".kilo/mmvc-guard.json"))
  copyFileSync(attax, path.join(plugins, "attax-guard.ts"))
  writeFileSync(path.join(plugins, "smoke-probe.ts"), `
import mmvc from "./mmvc-guard.ts"
import attax from "./attax-guard.ts"
import { writeFileSync, readFileSync } from "node:fs"
import { join } from "node:path"
export default { id: "mmvc-smoke-probe", server: async (input) => {
  const m = await mmvc.server(input), a = await attax.server(input)
  const result = { ids: [mmvc.id, attax.id], guidance: false, blocked: false, allowed: false, violation: false, coexistence: false }
  const system = { system: ["original"] }
  await m["experimental.chat.system.transform"]({model:{}}, system)
  result.guidance = system.system.length === 2 && system.system[1].includes("Logic -> Model")
  const call = {tool:"write",sessionID:"fixture",callID:"smoke"}, args = {filePath:"Smoke.cs", content:"class Smoke {}"}
  try { await m["tool.execute.before"](call,{args}) } catch(e) { result.blocked = String(e).includes("skill") }
  const skill = readFileSync(join(input.directory,".kilo/skills/mmvc-unity/SKILL.md"),"utf8")
  await m["tool.execute.after"]({tool:"skill",sessionID:"fixture",callID:"load",args:{name:"mmvc-unity"}},{title:"",output:skill,metadata:{}})
  await m["tool.execute.before"](call,{args}); result.allowed = true
  try { await m["tool.execute.before"](call,{args:{...args,content:"using SavableObservable; class M : BaseObservableDataModel, ISharedSingleton {}"}}) } catch(e) { result.violation = String(e).includes("MMVC001") }
  await a["tool.execute.before"]({tool:"execute",sessionID:"fixture",callID:"context"},{args:{code:"tools.zengram.brain_query({}); tools.CodeGraphContext.find_code({});"}})
  await a["tool.execute.before"](call,{args}); result.coexistence = true
  writeFileSync(join(input.directory,"smoke-result.json"),JSON.stringify(result))
  return {}
}}
`)
  const child = spawn(binary, ["serve", "--port", "0", "--print-logs"], { cwd: root, env: { ...process.env, MMVC_GUARD_MODE: "enforce", ATTAX_GUARD_MODE: "enforce" } })
  try {
    const address = await new Promise<string>((resolve, reject) => {
      let output = ""
      const deadline = setTimeout(() => reject(new Error("Kilo server readiness timed out")), 60000)
      child.once("error", reject)
      child.once("exit", code => { clearTimeout(deadline); reject(new Error(`Kilo exited before readiness: ${code}`)) })
      child.stderr.on("data", data => console.error(String(data)))
      child.stdout.on("data", data => {
        output += String(data)
        const match = /https?:\/\/127\.0\.0\.1:\d+/.exec(output)
        if (match) { clearTimeout(deadline); resolve(match[0]) }
      })
    })
    const response = await fetch(`${address}/config?directory=${encodeURIComponent(root)}`, { signal: AbortSignal.timeout(60000) })
    if (!response.ok) throw new Error(`Kilo fixture session creation: ${response.status} ${await response.text()}`)
    await response.text()
  } finally { child.kill(); await new Promise<void>(resolve => child.once("exit", () => resolve())) }
  const evidence = JSON.parse(readFileSync(path.join(root, "smoke-result.json"), "utf8"))
  console.log(JSON.stringify(evidence))
  if (Object.entries(evidence).some(([key, value]) => key !== "ids" && value !== true)) throw new Error("Client-discovery hook probe failed")
  console.log("PASS: real-client discovery and in-runtime mocked hook probe. No model-driven tools or existing session reload verified.")
} finally { rmSync(root, { recursive: true, force: true }) }
