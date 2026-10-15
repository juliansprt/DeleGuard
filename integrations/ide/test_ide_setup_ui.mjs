import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";

const source = fs.readFileSync(new URL("../../src/Deleguard.Api/wwwroot/js/ide-setup.js", import.meta.url), "utf8");

function render({ language = "en", platform = "Win32", root = "C:\\Projects\\store", mcp = "", url = "https://guard.example", projectId = "store", caseSensitive = "false", adapter = "cursor" } = {}) {
  const listeners = {};
  const elements = Object.fromEntries(["project", "adapter", "os", "root", "url", "mcp-url", "profile", "os-help", "status", "commands", "command", "doctor", "plan", "hook", "reconnect"].map(name => ["install-" + name, {
    value: "", textContent: "", hidden: false, attributes: {},
    setAttribute(key, value) { this.attributes[key] = value; },
    removeAttribute(key) { delete this.attributes[key]; }
  }]));
  elements["ide-install-builder"] = { addEventListener: (event, listener) => { listeners[event] = listener; } };
  const el = name => elements["install-" + name];
  el("project").selectedOptions = [{ value: projectId, dataset: { agent: "developer", environment: "development", caseSensitive } }];
  el("adapter").value = adapter;
  el("os").value = "windows";
  el("root").value = root;
  el("mcp-url").value = mcp;
  const catalog = JSON.parse(fs.readFileSync(new URL(`../../src/Deleguard.Api/Localization/UiStrings.${language}.json`, import.meta.url), "utf8"));
  elements["ui-messages"] = { textContent: JSON.stringify(Object.fromEntries(Object.entries(catalog).filter(([key]) => key.startsWith("js.")))) };
  const context = vm.createContext({ document: { getElementById: id => elements[id], addEventListener() {} }, navigator: { platform }, window: { location: { origin: url } }, URL });
  vm.runInContext(fs.readFileSync(new URL("../../src/Deleguard.Api/wwwroot/js/localization.js", import.meta.url), "utf8"), context);
  vm.runInContext(source, context);
  return { el, update: listeners.input };
}

test("installation and diagnosis refer to the selected project, never a shared profile", () => {
  const { el, update } = render();
  assert.equal(el("commands").hidden, false);
  assert.match(el("plan").textContent, / --plan$/);
  assert.match(el("command").textContent, /'--root' 'C:\\Projects\\store'/);
  assert.doesNotMatch(el("command").textContent, /--profile|--replace-managed/);
  assert.equal(el("doctor").textContent, "python 'C:\\Projects\\store\\.deleguard\\cursor\\deleguard_ide.py' doctor --config 'C:\\Projects\\store\\.deleguard\\cursor\\config.json'");
  el("root").value = "C:\\Projects\\other";
  update();
  assert.doesNotMatch(el("doctor").textContent, /\\store\\/);
  assert.match(el("doctor").textContent, /\\other\\.deleguard/);
});

test("quotes PowerShell apostrophes and does not interpolate project paths", () => {
  const { el } = render({ root: "C:\\Projects\\O'Brien $(whoami)" });
  assert.match(el("command").textContent, /'C:\\Projects\\O''Brien \$\(whoami\)'/);
});

test("detects macOS and shows the credential identifier workflow", () => {
  const { el } = render({ platform: "MacIntel", root: "/Users/me/project's folder", caseSensitive: "true" });
  assert.equal(el("os").value, "macos");
  assert.equal(el("commands").hidden, false);
  assert.match(el("command").textContent, / --keychain-ready$/);
  assert.doesNotMatch(el("plan").textContent, /--keychain-ready/);
  assert.match(el("os-help").textContent, /credential_id/);
  assert.match(el("command").textContent, /project'"'"'s folder/);
});

test("does not infer filesystem case sensitivity from the operating system", () => {
  const { el } = render({ platform: "Linux x86_64", root: "/work/store", caseSensitive: "false" });
  assert.equal(el("os").value, "linux");
  assert.equal(el("commands").hidden, false);
});

test("MCP is optional and requires an explicit HTTPS URL without secrets", () => {
  const plain = render();
  assert.doesNotMatch(plain.el("command").textContent, /--mcp-url/);
  const connected = render({ mcp: "https://guard.example/mcp/guard" });
  assert.match(connected.el("command").textContent, /'--mcp-url' 'https:\/\/guard.example\/mcp\/guard'/);
  assert.match(connected.el("hook").textContent, /require sign-in/);
  for (const mcp of ["http://localhost/mcp/guard", "https://user:secret@guard.example/mcp", "https://guard.example/mcp?token=secret", "https://guard.example/mcp#token", "https://guard.example/m\ncp"]) {
    const { el } = render({ mcp });
    assert.equal(el("commands").hidden, true, mcp);
    assert.equal(el("mcp-url").attributes["aria-invalid"], "true");
    assert.equal(el("command").textContent, "");
  }
});

test("errors mark the relevant input and clear stale commands", () => {
  const { el, update } = render();
  el("root").value = "relative/path";
  update();
  assert.equal(el("commands").hidden, true);
  assert.equal(el("root").attributes["aria-invalid"], "true");
  assert.equal(el("plan").textContent, "");
  assert.equal(el("doctor").textContent, "");
  el("root").value = "C:\\Projects\\valid";
  update();
  assert.equal(el("commands").hidden, false);
  assert.equal(el("root").attributes["aria-invalid"], undefined);
});

test("a missing project and non-local HTTP prevent installation commands", () => {
  const missing = render({ projectId: "" });
  assert.equal(missing.el("project").attributes["aria-invalid"], "true");
  assert.equal(missing.el("commands").hidden, true);
  const insecure = render({ url: "http://guard.example" });
  assert.equal(insecure.el("url").attributes["aria-invalid"], "true");
  assert.equal(insecure.el("commands").hidden, true);
});

test("switching language translates guidance without changing installation commands", () => {
  for (const options of [{}, { platform: "MacIntel", root: "/Users/me/project's folder" }, { mcp: "https://guard.example/mcp/guard" }]) {
    const en = render({ ...options, language: "en" });
    const es = render({ ...options, language: "es" });
    for (const name of ["command", "doctor", "plan"]) assert.equal(en.el(name).textContent, es.el(name).textContent);
    assert.notEqual(en.el("hook").textContent, es.el("hook").textContent);
    en.el("root").value = es.el("root").value = "relative/path";
    en.update(); es.update();
    assert.notEqual(en.el("status").textContent, es.el("status").textContent);
    assert.equal(es.el("command").textContent, "");
  }
});

test("reconnection asks for a replacement key without embedding it or resetting the project", () => {
  for (const language of ["en", "es"]) {
    const { el, update } = render({ language });
    const plan = el("plan").textContent;
    const doctor = el("doctor").textContent;
    assert.doesNotMatch(el("command").textContent, /--reconnect/);
    el("reconnect").checked = true;
    update();
    assert.match(el("command").textContent, / --reconnect$/);
    assert.doesNotMatch(el("command").textContent, /dgk_|--api-key|--replace-managed/);
    assert.equal(el("plan").textContent, plan);
    assert.equal(el("doctor").textContent, doctor);
    el("reconnect").checked = false;
    update();
    assert.doesNotMatch(el("command").textContent, /--reconnect/);
  }
});
