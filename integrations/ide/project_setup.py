"""Project-scoped installation. No external tool execution or policy mutation."""
import copy
import contextlib
import base64
import getpass
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import sqlite3
import subprocess
import sys
import tempfile
import urllib.parse

VERSION = "3.0.3"
ADAPTERS = ("cursor", "vscode", "claude")
HOOK_FILES = {"cursor": ".cursor/hooks.json", "vscode": ".github/hooks/deleguard.json", "claude": ".claude/settings.json"}
MCP_FILES = {"cursor": ".cursor/mcp.json", "vscode": ".vscode/mcp.json", "claude": ".mcp.json"}


def encoded(value):
    return (json.dumps(value, ensure_ascii=True, indent=2) + "\n").encode()


def sha(value):
    return hashlib.sha256(value).hexdigest()


def validate_url(raw, mcp=False):
    url = urllib.parse.urlsplit(raw)
    if (not url.hostname or url.username or url.password or url.query or url.fragment
            or any(c.isspace() or ord(c) < 32 for c in raw)):
        raise ValueError("Invalid API URL")
    if url.scheme != "https" and (mcp or url.scheme != "http" or url.hostname not in ("localhost", "127.0.0.1", "::1")):
        raise ValueError("Use HTTPS or a loopback development API")
    if not mcp and url.path not in ("", "/"):
        raise ValueError("Use the base API URL without a path")
    _ = url.port  # reject malformed ports
    return raw.rstrip("/")


def identity(config):
    scope = {key: config[key] for key in ("url", "root", "project", "agent", "environment", "adapter")}
    scope["root"] = os.path.normcase(scope["root"])
    if config.get("schema_version") == 2:
        scope["server_scope"] = config["server_scope"]
        return "ide-" + sha(json.dumps(scope, sort_keys=True, separators=(",", ":")).encode())[:40]
    return "ide-" + sha(json.dumps(scope, sort_keys=True).encode())[:40]


def local_path(root, relative):
    """Check each component before following it, including dangling links/reparse points."""
    part = Path(relative)
    if part.anchor or part.drive or part.root or ".." in part.parts:
        raise ValueError("Invalid managed path")
    current = Path(root)
    for name in part.parts:
        current /= name
        if current.is_symlink() or (hasattr(current, "is_junction") and current.is_junction()):
            raise ValueError("Managed path uses a link or junction")
        if current.exists() and getattr(current.lstat(), "st_file_attributes", 0) & 0x400:
            raise ValueError("Managed path uses a reparse point")
        if current.is_file() and current.stat().st_nlink > 1:
            raise ValueError("Managed file is hard linked")
    return current


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig")) if path.exists() else {}


def configuration(args):
    if args.adapter not in ADAPTERS:
        raise ValueError("Unknown adapter")
    root = Path(args.root).absolute()
    # Do not silently redirect an installation through a linked workspace.
    local_path(root.anchor, str(root.relative_to(root.anchor)))
    if not root.is_dir():
        raise ValueError("Project directory does not exist")
    root = root.resolve(strict=True)
    for field in (args.project, args.agent, args.environment):
        if not field or len(field) > 120 or any(ord(c) < 32 for c in field):
            raise ValueError("Invalid project identity")
    config = dict(schema_version=1, adapter_version=VERSION, root=str(root),
                  url=validate_url(args.url), project=args.project, agent=args.agent,
                  environment=args.environment, adapter=args.adapter)
    config["credential_id"] = identity(config)
    if os.name == "nt" and args.adapter == "cursor":
        config["cursor_windows_transport"] = "3.17-utf8-file"
    config["mcp_url"] = validate_url(args.mcp_url, mcp=True) if getattr(args, "mcp_url", None) else None
    return config


def hook_definition(config, python_executable=None):
    base = Path(config["root"]) / ".deleguard" / config["adapter"]
    pieces = [python_executable or sys.executable, str(base / "deleguard_ide.py"), "hook", "--config", str(base / "config.json")]
    portable = config.get("schema_version") == 2
    if portable:
        pieces = [str(base / "runtime/worker" / ("deleguard-adapter.exe" if os.name == "nt" else "deleguard-adapter")), "hook", "--config", str(base / "config.json")]
    if os.name == "nt" and config["adapter"] == "cursor":
        # Cursor already invokes PowerShell and pipes Get-Content -Raw into this
        # command. A second PowerShell process loses/re-encodes that pipeline.
        # Cursor 3.17 writes a UTF-8 file without BOM; Windows PS5 Get-Content
        # decodes it with the ANSI codepage. Reverse precisely that conversion,
        # failing closed if it is not reversible, then feed native Python UTF-8.
        invocation = "& " + " ".join("'" + p.replace("'", "''") + "'" for p in (pieces if portable else [pieces[0], "-X", "utf8", *pieces[1:]]))
        command = ("& { $ErrorActionPreference='Stop'; try { "
                   "$wire=[string]::Join([Environment]::NewLine,@($input)); "
                   "$utf8=[System.Text.UTF8Encoding]::new($false,$true); "
                   "if ($PSVersionTable.PSVersion.Major -le 5) { "
                   "$ansi=[System.Text.Encoding]::GetEncoding([System.Text.Encoding]::Default.CodePage,"
                   "[System.Text.EncoderFallback]::ExceptionFallback,[System.Text.DecoderFallback]::ExceptionFallback); "
                   "$wire=$utf8.GetString($ansi.GetBytes($wire)) }; "
                   "$OutputEncoding=$utf8; $wire | " + invocation + "; exit $LASTEXITCODE "
                   "} catch { [Console]::Out.WriteLine('{\"permission\":\"deny\",\"user_message\":\"DeleGuard: hook_input_encoding. No operation authorized.\"}'); exit 2 } }")
    elif os.name == "nt":
        # Hook hosts use a shell. CRT quoting is not shell quoting (&, %, |, etc.).
        # Encode only our fixed invocation, single-quote each literal, and never the event.
        script = "& " + " ".join("'" + p.replace("'", "''") + "'" for p in pieces) + "; exit $LASTEXITCODE"
        payload = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
        command = subprocess.list2cmdline([windows_powershell(), "-NoProfile", "-NonInteractive", "-EncodedCommand", payload])
    else:
        command = shlex.join(pieces)
    if config["adapter"] == "cursor":
        return {"version": 1, "hooks": {event: [{"command": command, "timeout": 20, **({"failClosed": True} if event == "preToolUse" else {})}]
                for event in ("preToolUse", "postToolUse", "postToolUseFailure")}}
    if config["adapter"] == "vscode":
        return {"hooks": {event: [{"type": "command", "command": command, "timeout": 20}]
                          for event in ("PreToolUse", "PostToolUse")}}
    return {"hooks": {event: [{"matcher": ".*", "hooks": [{"type": "command", "command": command, "timeout": 20}]}]
                      for event in ("PreToolUse", "PostToolUse", "PostToolUseFailure")}}


def windows_powershell():
    """Resolve the OS binary, never a workspace/PATH-provided powershell.exe."""
    import ctypes
    from ctypes import wintypes
    kernel = ctypes.WinDLL("kernel32.dll", use_last_error=True, winmode=0x00000800)
    kernel.GetSystemDirectoryW.argtypes = [wintypes.LPWSTR, wintypes.UINT]
    kernel.GetSystemDirectoryW.restype = wintypes.UINT
    buffer = ctypes.create_unicode_buffer(32768)
    length = kernel.GetSystemDirectoryW(buffer, len(buffer))
    if not length or length >= len(buffer):
        raise ValueError("Windows system launcher unavailable")
    path = Path(buffer.value) / "WindowsPowerShell/v1.0/powershell.exe"
    if not path.is_file():
        raise ValueError("Windows system launcher unavailable")
    return str(path)


def mcp_definition(config):
    key = "servers" if config["adapter"] == "vscode" else "mcpServers"
    entry = {"url": config["mcp_url"]}
    if config["adapter"] != "cursor":
        entry["type"] = "http"
    return key, "deleguard-" + config["credential_id"][4:16], entry


def merge_hooks(existing, desired, previous, replace=False):
    result = copy.deepcopy(existing)
    hooks = result.setdefault("hooks", {})
    if not isinstance(hooks, dict):
        raise ValueError("Invalid hooks configuration")
    for event, entries in desired["hooks"].items():
        current = hooks.setdefault(event, [])
        if not isinstance(current, list):
            raise ValueError("Invalid hook entries")
        old = previous.get("hooks", {}).get(event, [])
        if old and any(item not in current for item in old) and not replace:
            raise ValueError("Managed hooks changed; inspect and use --replace-managed to confirm repair")
        current[:] = [item for item in current if item not in old]
        for item in entries:
            if item not in current:
                current.append(item)
    if "version" in desired:
        if result.get("version", 1) != 1:
            raise ValueError("Unsupported Cursor hooks version")
        result["version"] = 1
    return result


def plan_files(config, package, replace=False, previous_hooks=None, portable_migration=None):
    root, adapter = Path(config["root"]), config["adapter"]
    base = ".deleguard/" + adapter + "/"
    manifest_path = local_path(root, base + "manifest.json")
    manifest = read_json(manifest_path)
    if manifest and manifest.get("credential_id") != config["credential_id"] and manifest != portable_migration:
        raise ValueError("This folder already belongs to another connection; do not overwrite its identity")
    files = {}
    managed = {}

    def own(relative, data):
        path = local_path(root, relative)
        if path.exists() and path.read_bytes() != data:
            old_hash = manifest.get("files", {}).get(relative)
            if not replace and (not old_hash or sha(path.read_bytes()) != old_hash):
                raise ValueError("Managed file was customized: " + relative + ". Inspect it before --replace-managed")
        files[relative] = data
        managed[relative] = sha(data)

    for name in ("deleguard_ide.py", "project_setup.py"):
        own(base + name, (package / name).read_bytes())
    skill = package / "assets/common/skills/deleguard-project/SKILL.md"
    agent_file = {"cursor": "deleguard-reviewer.md", "vscode": "deleguard-reviewer.agent.md", "claude": "deleguard-reviewer.md"}[adapter]
    dest = {"cursor": ".cursor", "vscode": ".github", "claude": ".claude"}[adapter]
    own(dest + "/skills/deleguard-project/SKILL.md", skill.read_bytes())
    own(dest + "/agents/" + agent_file, (package / "assets" / adapter / agent_file).read_bytes())
    # Keep the small templates beside the installer for deterministic upgrades/reinstalls.
    for source in sorted((package / "assets").rglob("*")):
        if source.is_file():
            own(base + source.relative_to(package).as_posix(), source.read_bytes())
    own(base + "config.json", encoded(config))
    hooks = hook_definition(config)
    hook_path = HOOK_FILES[adapter]
    current_hooks = read_json(local_path(root, hook_path))
    # Unmanaged legacy DeleGuard entries need explicit migration, not a second guard.
    prior = previous_hooks or manifest.get("hook_definition", {})
    if not prior and "deleguard_ide.py" in json.dumps(current_hooks) and all(
            item not in current_hooks.get("hooks", {}).get(event, []) for event, entries in hooks["hooks"].items() for item in entries):
        raise ValueError("Legacy DeleGuard hooks detected. Use --migrate-profile with the original profile")
    files[hook_path] = encoded(merge_hooks(current_hooks, hooks, prior, replace))
    mcp_manifest = None
    if config["mcp_url"]:
        key, name, entry = mcp_definition(config)
        mcp_path = MCP_FILES[adapter]
        mcp = read_json(local_path(root, mcp_path))
        servers = mcp.setdefault(key, {})
        old_entry = (manifest.get("mcp") or {}).get("entry")
        if name in servers and servers[name] not in (entry, old_entry) and not replace:
            raise ValueError("MCP connection was customized; confirm --replace-managed after review")
        servers[name] = entry
        files[mcp_path] = encoded(mcp)
        mcp_manifest = dict(path=mcp_path, key=key, name=name, entry=entry)
    elif manifest.get("mcp"):
        raise ValueError("Existing MCP connection: retain --mcp-url; removal requires explicit review")
    ignore = local_path(root, ".gitignore")
    old_ignore = ignore.read_text(encoding="utf-8") if ignore.exists() else ""
    if "/.deleguard/" not in old_ignore.splitlines():
        files[".gitignore"] = (old_ignore.rstrip("\n") + "\n\n# Machine-local DeleGuard identity and journal\n/.deleguard/\n").encode()
    files[base + "manifest.json"] = encoded(dict(version=VERSION, credential_id=config["credential_id"], files=managed,
                                                  hook_definition=hooks, mcp=mcp_manifest))
    return files


def atomic_write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    fd, name = tempfile.mkstemp(prefix=".deleguard-", dir=path.parent)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


def commit_files(root, files, base):
    originals = {name: local_path(root, name).read_bytes() if local_path(root, name).exists() else None for name in files}
    changed = [name for name, data in files.items() if data != originals[name]]
    # Backups never overwrite an older revision; contain configuration, not the OS secret.
    for name in changed:
        old = originals[name]
        if old is not None:
            backup = base + "/backups/" + sha(name.encode())[:16] + "-" + sha(old)[:16] + ".bak"
            atomic_write(local_path(root, backup), old)
    written = []
    try:
        # Hooks installed last: they cannot refer to a half-written runtime on ordinary errors.
        for name in sorted(changed, key=lambda n: (-1 if n == ".gitignore" else 1 if n in HOOK_FILES.values() else 0, n)):
            atomic_write(local_path(root, name), files[name])
            written.append(name)
    except BaseException:
        for name in reversed(written):
            path = local_path(root, name)
            if originals[name] is None:
                path.unlink()
            else:
                atomic_write(path, originals[name])
        raise
    return changed


def load_config(path):
    path = Path(path).absolute()
    config = read_json(path)
    if config.get("schema_version") not in (1, 2) or config.get("adapter") not in ADAPTERS:
        raise ValueError("Unsupported project configuration")
    expected = local_path(Path(config["root"]), ".deleguard/" + config["adapter"] + "/config.json")
    if path != expected or not Path(config["root"]).is_dir() or config.get("credential_id") != identity(config):
        raise ValueError("Project configuration moved or identity changed; reinstall for this folder")
    validate_url(config["url"])
    return config


def integrity(config):
    root = Path(config["root"])
    manifest = read_json(local_path(root, ".deleguard/" + config["adapter"] + "/manifest.json"))
    if manifest.get("credential_id") != config["credential_id"] or not manifest.get("files"):
        raise ValueError("Installation manifest is missing or mismatched")
    for name, expected in manifest["files"].items():
        path = local_path(root, name)
        if not path.is_file() or sha(path.read_bytes()) != expected:
            raise ValueError("Managed installation changed; inspect before repairing")
    current = read_json(local_path(root, HOOK_FILES[config["adapter"]]))
    for event, entries in manifest["hook_definition"]["hooks"].items():
        if any(entry not in current.get("hooks", {}).get(event, []) for entry in entries):
            raise ValueError("IDE hooks missing or modified")
    mcp = manifest.get("mcp")
    if mcp and read_json(local_path(root, mcp["path"])).get(mcp["key"], {}).get(mcp["name"]) != mcp["entry"]:
        raise ValueError("MCP endpoint changed; review connection before continuing")


def migrate(config, profile, guard):
    if not re.fullmatch(r"[A-Za-z0-9_-]+", profile):
        raise ValueError("Invalid legacy profile")
    old = read_json(guard.state_root() / (profile + ".json"))
    if any(old.get(key) != config[key] for key in ("url", "root", "project", "agent", "environment", "adapter")):
        raise ValueError("Legacy profile belongs to a different project or connection")
    pieces = [sys.executable, str(guard.state_root() / "deleguard_ide.py"), "hook", "--profile", profile]
    command = subprocess.list2cmdline(pieces) if os.name == "nt" else shlex.join(pieces)
    current = read_json(local_path(Path(config["root"]), HOOK_FILES[config["adapter"]]))
    # Exact old command only; do not remove unrelated user hooks or run any command.
    selected = {event: [entry for entry in entries if entry.get("command") == command or
                       any(h.get("command") == command for h in entry.get("hooks", []))]
                for event, entries in current.get("hooks", {}).items()}
    if not any(selected.values()):
        raise ValueError("Legacy hook command does not match this interpreter; migrate with its original Python")
    return {"hooks": selected}


def migration_journal(config, profile, guard):
    """Copy a coherent SQLite snapshot (including WAL), never reset operation identities."""
    source = guard.state_root() / (profile + ".sqlite")
    destination = ".deleguard/" + config["adapter"] + "/state.sqlite"
    target = local_path(Path(config["root"]), destination)
    if target.exists():
        raise ValueError("A project journal already exists; do not merge or overwrite operation history")
    if not source.exists():
        return {}
    # Validation of the exact legacy profile happens in migrate() before this read.
    with contextlib.closing(sqlite3.connect(source.as_uri() + "?mode=ro", uri=True, timeout=1)) as old:
        with contextlib.closing(sqlite3.connect(":memory:")) as snapshot:
            old.backup(snapshot)
            snapshot.execute("select id,state from operations limit 1")
            return {destination: snapshot.serialize()}


def install(args, guard):
    config = configuration(args)
    package = Path(__file__).parent
    previous = migrate(config, args.migrate_profile, guard) if getattr(args, "migrate_profile", None) else None
    files = plan_files(config, package, getattr(args, "replace_managed", False), previous)
    summary = dict(project=config["project"], adapter=config["adapter"], credential_id=config["credential_id"],
                   config=str(Path(config["root"]) / ".deleguard" / config["adapter"] / "config.json"),
                   files=list(files), coverage="partial", mcp="native_login_required" if config["mcp_url"] else "not_configured")
    if getattr(args, "plan", False):
        print(json.dumps(dict(summary, mode="preview", changes_applied=False), indent=2))
        return
    old_path = Path(summary["config"])
    old = read_json(old_path)
    reconnect = getattr(args, "reconnect", False)
    if args.keychain_ready or old and not reconnect:
        token = guard.credential(config["credential_id"])
    elif getattr(args, "migrate_profile", None):
        token = guard.credential(args.migrate_profile)
    else:
        token = getpass.getpass("Clave del agente de ESTE proyecto (dgk_, no se mostrara): ").strip()
    api = guard.Api(config, token)
    project = api.call("/v1/ide/config/" + urllib.parse.quote(config["project"], safe=""))
    scope = {key: project.get(key) for key in ("organization_id", "application_id", "agent_id")}
    if old.get("server_scope") and old["server_scope"] != scope:
        raise ValueError("Server tenant/application changed; reconnect explicitly with a different project identity")
    config["server_scope"] = scope
    # Probe semantics without creating a file: the selected existing directory is sufficient.
    root = Path(config["root"])
    alternate = root.with_name(root.name.swapcase())
    if alternate != root and root.name.swapcase() != root.name:
        insensitive = alternate.exists() and os.path.samefile(root, alternate)
        if project.get("case_sensitive") is not None and project["case_sensitive"] == insensitive:
            raise ValueError("Project path case setting does not match this filesystem")
    files = plan_files(config, package, getattr(args, "replace_managed", False), previous)
    if getattr(args, "migrate_profile", None):
        files.update(migration_journal(config, args.migrate_profile, guard))
    # Persist only a valid authenticated key, under this project-scoped OS-store account.
    if not args.keychain_ready and (not old or reconnect):
        guard.credential(config["credential_id"], token)
    if guard.credential(config["credential_id"]) != token:
        raise ValueError("OS credential round-trip failed; installation cancelled")
    changed = commit_files(root, files, ".deleguard/" + config["adapter"])
    print(json.dumps(dict(summary, mode="installed", changed=changed,
                         next_step="Reinicia el IDE, ejecuta doctor y la prueba guiada. No se certifica cobertura completa."), indent=2))


def doctor(config, guard):
    checks = []
    def check(name, fn, success):
        try:
            result = fn()
            checks.append(dict(id=name, status="ok", message=success))
            return result
        except Exception as error:
            checks.append(dict(id=name, status="error", message=guard.safe_failure_reason(error)))
            return None
    check("installation", lambda: integrity(config), "Archivos e integración del proyecto presentes y sin cambios.")
    api = check("credential", lambda: guard.project_api(config), "Credencial local disponible; no se ha mostrado.")
    project = check("project_connection", lambda: api.call("/v1/ide/config/" + urllib.parse.quote(config["project"], safe="")),
                    "API accesible y proyecto autorizado para esta credencial.") if api else None
    if project and config.get("server_scope") != {key: project.get(key) for key in ("organization_id", "application_id", "agent_id")}:
        checks.append(dict(id="server_scope", status="error", message="La identidad del proyecto remoto cambió. Revisa la conexión."))
    checks.extend([
        dict(id="policy", status="unverified", message="La conexión no demuestra que el perfil esté publicado. Compruébalo en IDE Control y completa la prueba guiada."),
        dict(id="runtime_coverage", status="partial", message="Solo llamadas interceptadas. Sin broker aislado verificado; indexación, otras extensiones y efectos internos de procesos no cubiertos."),
        dict(id="mcp", status="unverified" if config.get("mcp_url") else "not_configured", message="Inicia sesión desde el IDE y verifica las herramientas del Guard; configurar la URL no prueba OAuth ni cobertura." if config.get("mcp_url") else "MCP no conectado. Instala con --mcp-url HTTPS para añadir la conexión local al proyecto.")])
    if config["adapter"] == "claude":
        checks.append(dict(id="hook_timeout", status="partial", message="Claude Code puede continuar si un hook vence por tiempo; este paquete no garantiza bloqueo ante timeout."))
    return dict(adapter_version=VERSION, project=config["project"], adapter=config["adapter"],
                status="configuration_incomplete" if any(x["status"] == "error" for x in checks) else "partial_coverage", checks=checks)
