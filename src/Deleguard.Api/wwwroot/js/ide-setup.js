(function () {
  "use strict";
  const builder = document.getElementById("ide-install-builder");
  if (!builder) return;
  const el = id => document.getElementById("install-" + id);
  el("url").value = window.location.origin;
  const platform = navigator.userAgentData?.platform || navigator.platform || "";
  if (/win/i.test(platform)) el("os").value = "windows";
  else if (/mac/i.test(platform)) el("os").value = "macos";
  else if (/linux/i.test(platform)) el("os").value = "linux";
  // Values are rendered as text only. Quote every argument for the selected shell.
  const quote = (value, windows) => "'" + value.replace(/'/g, windows ? "''" : "'\"'\"'") + "'";
  function update() {
    const os = el("os").value, windows = os === "windows", linux = os === "linux";
    const preview = document.getElementById("install-preview"); if (preview) preview.open = os === "macos";
    const project = el("project").selectedOptions[0];
    const adapter = el("adapter").value;
    const root = el("root").value.trim(), urlText = el("url").value.trim();
    const mcpText = el("mcp-url").value.trim();
    el("root").placeholder = windows ? window.dgText("js.ide.path.windows") : (os === "macos" ? window.dgText("js.ide.path.macos") : window.dgText("js.ide.path.linux"));
    el("profile").textContent = window.dgText("js.ide.profile", adapter);
    el("os-help").textContent = windows
      ? window.dgText("js.ide.windows")
      : linux
        ? window.dgText("js.ide.linux")
        : window.dgText("js.ide.macos");
    el("commands").hidden = true;
    el("command").textContent = "";
    el("doctor").textContent = "";
    el("plan").textContent = "";
    for (const field of ["project", "root", "url", "mcp-url"]) el(field).removeAttribute("aria-invalid");
    let error = "";
    let invalidField = "url";
    let url;
    try { url = new URL(urlText); } catch { error = window.dgText("js.ide.url"); }
    if (url && (url.username || url.password || url.search || url.hash || url.pathname !== "/" ||
        !(url.protocol === "https:" || (url.protocol === "http:" && ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname)))))
      error = window.dgText("js.ide.baseurl");
    if (mcpText) {
      let mcp;
      try { mcp = new URL(mcpText); } catch { /* Handled by the same accessible message below. */ }
      if (!mcp || mcp.protocol !== "https:" || mcp.username || mcp.password || mcp.search || mcp.hash || /[\x00-\x1f\x7f]/.test(mcpText)) {
        error = window.dgText("js.ide.mcpurl");
        invalidField = "mcp-url";
      }
    }
    if (!project.value) { error = window.dgText("js.ide.project"); invalidField = "project"; }
    else if (!root || /[\x00-\x1f\x7f]/.test(root) || (windows ? !/^[a-zA-Z]:[\\/]/.test(root) : !root.startsWith("/"))) {
      error = windows ? window.dgText("js.ide.root.windows") : window.dgText("js.ide.root.unix");
      invalidField = "root";
    }
    else if (/[\x00-\x1f\x7f]/.test(urlText)) { error = window.dgText("js.ide.urlcontrol"); invalidField = "url"; }
    if (error) { el(invalidField).setAttribute("aria-invalid", "true"); el("status").textContent = error; return; }
    const python = windows ? "python" : "python3";
    const args = ["--root", root, "--url", url.origin, "--project", project.value,
      "--agent", project.dataset.agent, "--environment", project.dataset.environment, "--adapter", adapter];
    if (mcpText) args.push("--mcp-url", mcpText);
    const command = python + " ./deleguard_ide.py install " + args.map(v => quote(v, windows)).join(" ");
    el("plan").textContent = command + " --plan";
    el("command").textContent = command + (os === "macos" ? " --keychain-ready" : "") + (el("reconnect")?.checked ? " --reconnect" : "");
    const separator = windows ? "\\" : "/";
    const localDir = root.replace(windows ? /[\\/]+$/ : /\/+$/, "") + separator + ".deleguard" + separator + adapter;
    el("doctor").textContent = python + " " + quote(localDir + separator + "deleguard_ide.py", windows) + " doctor --config " + quote(localDir + separator + "config.json", windows);
    el("hook").textContent = window.dgText("js.ide.hook", ({cursor: ".cursor/hooks.json", vscode: ".github/hooks/deleguard.json", claude: ".claude/settings.json"})[adapter]) + window.dgText(mcpText ? "js.ide.mcppending" : "js.ide.nomcp");
    el("status").textContent = window.dgText("js.ide.ready");
    el("commands").hidden = false;
  }
  builder.addEventListener("input", update);
  builder.addEventListener("change", update);
  update();
})();
