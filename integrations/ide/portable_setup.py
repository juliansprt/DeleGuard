"""Portable setup protocol. Writes only within the explicitly selected workspace."""
import contextlib
import hashlib
import json
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess
import sys
import urllib.parse

import project_setup as setup

class SetupError(ValueError):
    pass

def configuration(request):
    from types import SimpleNamespace
    profile = request.get("profile", {})
    if profile.get("schema_version") != 1:
        raise SetupError("invalid_profile")
    scope = {k: profile.get(k) for k in ("organization_id", "application_id", "agent_id")}
    if any(not isinstance(v, str) or not v or len(v) > 120 for v in scope.values()):
        raise SetupError("invalid_profile")
    config = setup.configuration(SimpleNamespace(root=request["root"], adapter=request.get("adapter", profile.get("adapter")),
        url=profile["server_url"], project=profile["project_id"], agent=profile["agent_external_id"],
        environment=profile["environment"], mcp_url=None))
    config.update(schema_version=2, credential_storage="project-vault", server_scope=scope,
                  language="es" if request.get("language", profile.get("language")) == "es" else "en")
    config["credential_id"] = setup.identity(config)
    return config

@contextlib.contextmanager
def installation_lock(root, shared=False):
    path = setup.local_path(root, ".deleguard/.setup.lock")
    path.parent.mkdir(mode=0o700, exist_ok=True)
    with open(path, "a+b") as lock:
        if path.stat().st_size == 0:
            lock.write(b"0"); lock.flush()
        lock.seek(0)
        try:
            if os.name == "nt":
                import msvcrt
                import ctypes
                from ctypes import wintypes
                class Overlapped(ctypes.Structure):
                    _fields_ = [("Internal", ctypes.c_size_t), ("InternalHigh", ctypes.c_size_t), ("Offset", wintypes.DWORD), ("OffsetHigh", wintypes.DWORD), ("hEvent", wintypes.HANDLE)]
                kernel = ctypes.WinDLL("kernel32.dll", use_last_error=True, winmode=0x00000800)
                kernel.LockFileEx.argtypes = [wintypes.HANDLE,wintypes.DWORD,wintypes.DWORD,wintypes.DWORD,wintypes.DWORD,ctypes.POINTER(Overlapped)]
                kernel.UnlockFileEx.argtypes = [wintypes.HANDLE,wintypes.DWORD,wintypes.DWORD,wintypes.DWORD,ctypes.POINTER(Overlapped)]
                overlapped = Overlapped()
                handle = msvcrt.get_osfhandle(lock.fileno())
                if not kernel.LockFileEx(handle,1 | (0 if shared else 2),0,1,0,ctypes.byref(overlapped)):
                    raise ctypes.WinError(ctypes.get_last_error())
            else:
                import fcntl
                fcntl.flock(lock.fileno(), (fcntl.LOCK_SH if shared else fcntl.LOCK_EX) | fcntl.LOCK_NB)
        except OSError:
            raise SetupError("installation_busy") from None
        try:
            yield
        finally:
            lock.seek(0)
            if os.name == "nt":
                kernel.UnlockFileEx(handle,0,1,0,ctypes.byref(overlapped))
            else:
                fcntl.flock(lock.fileno(), fcntl.LOCK_UN)

def reject_tracked_secrets(root):
    root = Path(root)
    if not any((p / ".git").exists() for p in (root, *root.parents)):
        return
    git = shutil.which("git")
    if not git or Path(git).resolve().is_relative_to(root.resolve()):
        raise SetupError("git_check_unavailable")
    env = {k: v for k, v in os.environ.items() if not k.startswith("GIT_")}
    result = subprocess.run([git, "--no-optional-locks", "-c", "core.fsmonitor=false", "-C", str(root), "ls-files", "-z", "--", ".deleguard"],
        capture_output=True, timeout=15, env=env, **({"creationflags": subprocess.CREATE_NO_WINDOW} if os.name == "nt" else {}))
    if result.returncode:
        raise SetupError("git_check_unavailable")
    if result.stdout:
        raise SetupError("tracked_credentials")

def source_package():
    return Path(__file__).parent

def validate_owned_manifest(config, manifest):
    base = ".deleguard/" + config["adapter"] + "/"
    dest = {"cursor": ".cursor", "vscode": ".github", "claude": ".claude"}[config["adapter"]]
    agent = {"cursor": "deleguard-reviewer.md", "vscode": "deleguard-reviewer.agent.md", "claude": "deleguard-reviewer.md"}[config["adapter"]]
    fixed = {base + f for f in ("config.json", "credential.vault", "deleguard_ide.py", "project_setup.py")}
    fixed.update((dest + "/skills/deleguard-project/SKILL.md", dest + "/agents/" + agent))
    fixed.update(base + path.relative_to(source_package()).as_posix() for path in (source_package() / "assets").rglob("*") if path.is_file())
    previous_hook = manifest.get("hook_definition")
    interpreter = None
    if config.get("schema_version") == 1:
        # Parse the historical launcher without executing it, then verify the ENTIRE
        # fixed hook template against this project and the original interpreter.
        import re, shlex, base64
        try:
            entry = next(iter(previous_hook["hooks"].values()))[0]
            command = (entry["hooks"][0] if "hooks" in entry else entry)["command"]
            if os.name == "nt":
                script = command if config["adapter"] == "cursor" else base64.b64decode(command.rsplit(" ",1)[1],validate=True).decode("utf-16-le")
                match = re.search(r"(?:\$wire \| |^)& '((?:[^']|'')*)'",script)
                interpreter = match.group(1).replace("''", "'") if match else None
            else:
                interpreter = shlex.split(command)[0]
            if not interpreter or not Path(interpreter).is_absolute():raise ValueError()
        except (ValueError,TypeError,KeyError,IndexError,StopIteration):
            raise SetupError("invalid_manifest") from None
    if manifest.get("credential_id") != config["credential_id"] or previous_hook != setup.hook_definition(config,interpreter):
        raise SetupError("invalid_manifest")
    for name in manifest.get("files", {}):
        setup.local_path(Path(config["root"]), name)
        if "\\" in name or ":" in name or name not in fixed and not name.startswith(base + "runtime/"):
            raise SetupError("invalid_manifest")
    expected = None
    if config.get("mcp_url"):
        key, name, entry = setup.mcp_definition(config)
        expected = dict(path=setup.MCP_FILES[config["adapter"]],key=key,name=name,entry=entry)
    if manifest.get("mcp") != expected:
        raise SetupError("invalid_manifest")

def make_plan(config, runtime, replace=False):
    root = Path(config["root"])
    base = ".deleguard/" + config["adapter"] + "/"
    previous = setup.read_json(setup.local_path(root, base + "config.json"))
    if previous:
        previous = setup.load_config(setup.local_path(root, base + "config.json"))
        validate_owned_manifest(previous, setup.read_json(setup.local_path(root, base + "manifest.json")))
        if any(previous.get(k) != config.get(k) for k in ("root", "url", "project", "agent", "environment", "adapter", "server_scope")):
            raise SetupError("account_mismatch")
    migration = bool(previous and previous.get("schema_version") == 1)
    if migration:
        # A migration is the same connection, never permission to change accounts.
        if any(previous.get(k) != config.get(k) for k in ("root", "url", "project", "agent", "environment", "adapter", "server_scope")):
            raise SetupError("account_mismatch")
        legacy_manifest = setup.read_json(setup.local_path(root, base + "manifest.json"))
        if legacy_manifest.get("credential_id") != previous.get("credential_id"):
            raise SetupError("invalid_manifest")
    else:
        legacy_manifest = None
    files = setup.plan_files(config, source_package(), replace, portable_migration=legacy_manifest)
    runtime = Path(runtime).resolve()
    if not runtime.is_dir() or not (runtime / ("Deleguard.Desktop.exe" if os.name == "nt" else "Deleguard.Desktop")).is_file():
        raise SetupError("runtime_missing")
    worker = runtime / "worker" / ("deleguard-adapter.exe" if os.name == "nt" else "deleguard-adapter")
    if not worker.is_file() or os.name != "nt" and not (runtime / ".deleguard-host").is_file():
        raise SetupError("runtime_missing")
    if runtime.is_relative_to(root / ".deleguard"):
        # Updating from the installed app is safe; source bytes are fully staged before writes.
        if runtime != (root / base / "runtime").resolve():
            raise SetupError("runtime_missing")
    manifest = json.loads(files[base + "manifest.json"])
    old = setup.read_json(setup.local_path(root, base + "manifest.json"))
    for path in sorted(runtime.rglob("*")):
        if not path.is_file():
            continue
        if not path.resolve().is_relative_to(runtime):
            raise SetupError("linked_path")
        relative = base + "runtime/" + path.relative_to(runtime).as_posix()
        target = setup.local_path(root, relative)
        value = path.read_bytes()
        if target.exists() and target.read_bytes() != value and setup.sha(target.read_bytes()) != old.get("files", {}).get(relative) and not replace:
            raise SetupError("customized_files")
        files[relative] = value
        manifest["files"][relative] = setup.sha(value)
    manifest["portable"] = True
    files[base + "manifest.json"] = setup.encoded(manifest)
    return files, migration

def validate_remote(config, token, guard):
    if token is not None and not guard.valid_agent_key(token):
        raise SetupError("invalid_key")
    if token is None:
        existing = setup.load_config(setup.local_path(Path(config["root"]), ".deleguard/" + config["adapter"] + "/config.json"))
        if existing.get("schema_version") != 2 or existing["credential_id"] != config["credential_id"]:
            raise SetupError("invalid_key")
        api = guard.project_api(existing)
    else:
        api = guard.Api(config, token)
    remote = api.call("/v1/ide/config/" + urllib.parse.quote(config["project"], safe=""))
    if any(remote.get(k) != v for k, v in config["server_scope"].items()) or any(
            remote.get(remote_key) != config[local_key] for remote_key, local_key in
            (("id", "project"), ("agent_external_id", "agent"), ("environment", "environment"))):
        raise SetupError("account_mismatch")
    insensitive = not filesystem_case_sensitive(Path(config["root"]))
    if remote.get("case_sensitive") is not None and remote["case_sensitive"] == insensitive:
        raise SetupError("filesystem_mismatch")
    return remote

def filesystem_case_sensitive(root):
    import uuid
    probe=setup.local_path(root,".deleguard/CaseProbe-"+uuid.uuid4().hex)
    try:
        with probe.open("xb") as stream:stream.write(b"")
        alias=probe.with_name(probe.name.lower())
        return not (alias.exists() and alias.samefile(probe))
    finally:
        probe.unlink(missing_ok=True)

def install(request, guard):
    config = configuration(request)
    root = Path(config["root"])
    reject_tracked_secrets(root)
    files, migration = make_plan(config, request["runtime"], request.get("replace", False))
    preview = dict(config=config, files=list(files), migration=migration, status="review", changes_applied=False)
    if request.get("operation") == "preview":
        return preview
    with installation_lock(root):
        # Recheck after obtaining the lock: previews are never authority to overwrite newer state.
        reject_tracked_secrets(root)
        files, migration = make_plan(config, request["runtime"], request.get("replace", False))
        if migration and not request.get("migrate"):
            raise SetupError("migration_required")
        validate_remote(config, request.get("key"), guard)
        vault = request.get("vault")
        if not isinstance(vault, str) or len(vault) > 8192:
            raise SetupError("invalid_vault")
        envelope = json.loads(vault)
        if envelope.get("Version") != 1 or envelope.get("Identity") != config["credential_id"]:
            raise SetupError("invalid_vault")
        base = ".deleguard/" + config["adapter"] + "/"
        files[base + "credential.vault"] = vault.encode()
        manifest = json.loads(files[base + "manifest.json"])
        manifest["files"][base + "credential.vault"] = setup.sha(files[base + "credential.vault"])
        files[base + "manifest.json"] = setup.encoded(manifest)
        # Preserve existing operation identities. Changing their account is rejected above.
        changed = setup.commit_files(root, files, base.rstrip("/"))
        if os.name != "nt":
            for executable in ("runtime/Deleguard.Desktop", "runtime/.deleguard-host", "runtime/worker/deleguard-adapter"):
                setup.local_path(root, base + executable).chmod(0o700)
        return dict(config=config, changed=changed, migration=migration, status="installed", changes_applied=True,
                    legacy_credential_cleanup="manual_review" if migration else "not_needed")

def disconnect(request):
    config = setup.load_config(request["config_path"])
    root = Path(config["root"])
    with installation_lock(root):
        config = setup.load_config(request["config_path"])
        base = ".deleguard/" + config["adapter"] + "/"
        manifest = setup.read_json(setup.local_path(root, base + "manifest.json"))
        validate_owned_manifest(config, manifest)
        setup.integrity(config)
        # Only entries recorded by our manifest are removed from shared editor files.
        hook_path = setup.HOOK_FILES[config["adapter"]]
        hooks = setup.read_json(setup.local_path(root, hook_path))
        for event, owned in manifest["hook_definition"]["hooks"].items():
            hooks["hooks"][event] = [entry for entry in hooks.get("hooks", {}).get(event, []) if entry not in owned]
        changes = {hook_path: setup.encoded(hooks)}
        mcp = manifest.get("mcp")
        if mcp:
            current = setup.read_json(setup.local_path(root, mcp["path"]))
            if current.get(mcp["key"], {}).get(mcp["name"]) != mcp["entry"]:
                raise SetupError("customized_files")
            del current[mcp["key"]][mcp["name"]]
            changes[mcp["path"]] = setup.encoded(current)
        # Hooks take the same lock. Snapshot SQLite including WAL before changing anything.
        import uuid
        archive = base + "history/" + config["credential_id"] + "/" + uuid.uuid4().hex
        journal = setup.local_path(root, base + "state.sqlite")
        if journal.exists():
            import deleguard_ide as guard
            state = guard.State(journal)
            try: state.recover_results()
            finally: state.db.close()
            target = setup.local_path(root, archive + "/state.sqlite")
            target.parent.mkdir(parents=True, mode=0o700, exist_ok=False)
            try:
                with contextlib.closing(sqlite3.connect(journal)) as source, contextlib.closing(sqlite3.connect(target)) as destination:
                    source.backup(destination)
                    if destination.execute("pragma quick_check").fetchone()[0] != "ok":
                        raise SetupError("history_conflict")
            except Exception:
                target.unlink(missing_ok=True)
                raise SetupError("history_conflict") from None
        setup.commit_files(root, changes, base.rstrip("/"))
        pending = setup.local_path(root, base + "pending-results")
        if pending.exists():
            for path in pending.iterdir():
                source = setup.local_path(root, base + "pending-results/" + path.name)
                target = setup.local_path(root, archive + "/pending-results/" + path.name)
                target.parent.mkdir(parents=True,mode=0o700,exist_ok=True)
                source.rename(target)
        for name in ("state.sqlite", "state.sqlite-wal", "state.sqlite-shm"):
            setup.local_path(root, base + name).unlink(missing_ok=True)
        for relative in manifest["files"]:
            path = setup.local_path(root, relative)
            # Executables in use may be locked on Windows. They are inert without hooks/config/vault.
            try: path.unlink(missing_ok=True)
            except PermissionError:
                if not relative.startswith(base + "runtime/"): raise
        setup.local_path(root, base + "manifest.json").unlink(missing_ok=True)
        setup.local_path(root, base + "session.json").unlink(missing_ok=True)
        return dict(status="disconnected", history=archive, key_revoked=False)

def main():
    import deleguard_ide as guard
    try:
        raw = sys.stdin.read(1_048_577)
        if len(raw) > 1_048_576:
            raise SetupError("invalid_request")
        request = json.loads(raw)
        if request.get("operation") in ("preview", "install"):
            result = install(request, guard)
        elif request.get("operation") == "disconnect":
            result = disconnect(request)
        elif request.get("operation") == "doctor":
            config = setup.load_config(request["config_path"])
            with installation_lock(Path(config["root"])):
                result = setup.doctor(setup.load_config(request["config_path"]), guard)
        else:
            raise SetupError("invalid_request")
        print(json.dumps(result, ensure_ascii=True))
    except Exception as error:
        code = str(error) if isinstance(error, SetupError) else "connection_failed"
        if isinstance(error, urllib.error.HTTPError):
            code = "invalid_key" if error.code in (401, 403) else "connection_failed"
        print(json.dumps(dict(status="error", error=code)))
        return 2
    return 0
