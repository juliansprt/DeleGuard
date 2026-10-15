#!/usr/bin/env python3
"""DeleGuard IDE hooks. Python 3.11+, standard library; no model in the decision path."""
import argparse
import contextlib
import getpass
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import sqlite3
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

ACTIONS = {
    "Read": "filesystem.read", "ReadFile": "filesystem.read", "read": "filesystem.read", "read_file": "filesystem.read", "read_text_file": "filesystem.read",
    "Write": "filesystem.write", "WriteFile": "filesystem.write", "write": "filesystem.write", "write_file": "filesystem.write", "create_file": "filesystem.create", "CreateFile": "filesystem.create",
    "Edit": "filesystem.write", "edit_file": "filesystem.write", "StrReplace": "filesystem.write", "replace_string_in_file": "filesystem.write", "search_replace": "filesystem.write",
    "Delete": "filesystem.delete", "delete_file": "filesystem.delete", "DeleteFile": "filesystem.delete",
    "Move": "filesystem.move", "move_file": "filesystem.move", "MoveFile": "filesystem.move",
    "Glob": "filesystem.list", "LS": "filesystem.list", "list_dir": "filesystem.list", "file_search": "filesystem.list",
    "Grep": "filesystem.search", "grep_search": "filesystem.search",
    "Bash": "process.execute", "PowerShell": "process.execute", "Shell": "process.execute", "run_in_terminal": "process.execute", "runTerminalCommand": "process.execute",
    "createFile": "filesystem.create", "deleteFile": "filesystem.delete", "readFile": "filesystem.read", "editFiles": "filesystem.write",
}
AFTER = {"PostToolUse", "postToolUse", "PostToolUseFailure", "postToolUseFailure"}
BEFORE = {"PreToolUse", "preToolUse"}

class AdapterError(ValueError):
    """Stable diagnostics, never raw tool inputs or credential-provider stderr."""
    def __init__(self, code, message):
        self.code = code
        super().__init__(message)

def valid_agent_key(value):
    return isinstance(value, str) and bool(re.fullmatch(r"dgk_[A-Za-z0-9_-]+", value))

def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True)

def digest(value):
    return hashlib.sha256(canonical(value).encode()).hexdigest()

def state_root():
    if getattr(sys, "frozen", False):
        raise AdapterError("project_scope_required", "Use a project-local portable connection.")
    base = Path(os.environ.get("LOCALAPPDATA", str(Path.home() / ".local/share"))) if os.name == "nt" else Path.home() / ".local/share"
    root = base / "deleguard-ide"
    root.mkdir(parents=True, exist_ok=True, mode=0o700)
    if os.name != "nt": root.chmod(0o700)
    return root

def credential(profile, value=None):
    """Legacy storage only. Frozen portable builds never access global credential stores."""
    if getattr(sys, "frozen", False):
        raise AdapterError("project_scope_required", "Migrate this project to its local vault.")
    if not isinstance(profile, str) or not re.fullmatch(r"[A-Za-z0-9_-]{1,160}", profile):
        raise AdapterError("credential_scope_invalid", "El identificador local de la credencial no es válido. Vuelve a conectar este proyecto.")
    if value is not None and not valid_agent_key(value):
        raise AdapterError("credential_invalid", "Introduce una clave de agente dgk_ válida; no una clave administrativa o una URL.")
    if os.name == "nt":
        secret = state_root() / (profile + ".dpapi")
        if value is not None:
            out = windows_secret(value, protect=True)
            secret.write_text(out, encoding="utf-8")
            return
        try: encrypted = secret.read_text(encoding="utf-8-sig").strip()
        except FileNotFoundError:
            raise AdapterError("credential_missing", "No hay una clave guardada para este proyecto. Ejecuta la conexión de IDE Control.") from None
        return windows_secret(encrypted, protect=False)
    if sys.platform == "darwin":
        # Keychain utility's interactive password prompt avoids secrets in command arguments.
        if value is not None:
            raise ValueError("Store the agent key using Keychain Access: generic password, service deleguard-ide, account " + profile + ". Then run install with --keychain-ready.")
        result = credential_command(["security", "find-generic-password", "-s", "deleguard-ide", "-a", profile, "-w"])
        if not valid_agent_key(result): raise AdapterError("credential_invalid", "La credencial guardada no es una clave de agente válida. Vuelve a conectar este proyecto.")
        return result
    if value is not None:
        credential_command(["secret-tool", "store", "--label=DeleGuard IDE", "service", "deleguard-ide", "profile", profile], value)
        return
    result = credential_command(["secret-tool", "lookup", "service", "deleguard-ide", "profile", profile])
    if not valid_agent_key(result): raise AdapterError("credential_missing", "No se encontró una clave válida para este proyecto en el almacén del sistema. Vuelve a conectarlo.")
    return result

def credential_command(command, value=None):
    try:
        result = subprocess.run(command, input=value, text=True, encoding="utf-8", capture_output=True,
                                check=True, timeout=5,
                                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0) if os.name == "nt" else 0)
    except (OSError, subprocess.SubprocessError, UnicodeError):
        raise AdapterError("credential_unavailable", "No se pudo abrir el almacén de claves del sistema. Revisa el usuario de Windows o desbloquea el llavero y ejecuta el diagnóstico.") from None
    return result.stdout.strip()

def windows_secret(value, protect):
    """Current-user DPAPI, including the legacy PowerShell UTF-16LE hex format.

    Direct OS calls avoid a PowerShell module/autoload failure being mistaken for
    an invalid saved key. No secret is passed to another process or to its argv.
    """
    if not isinstance(value, str) or not value or len(value) > 65536:
        raise AdapterError("credential_invalid", "El formato de la credencial guardada no es válido. Vuelve a conectar este proyecto.")
    if protect and not valid_agent_key(value):
        raise AdapterError("credential_invalid", "Introduce una clave de agente dgk_ válida.")
    try:
        if protect: return dpapi(value.encode("utf-16-le"), protect=True).hex()
        if not re.fullmatch(r"(?:[0-9a-fA-F]{2})+", value): raise ValueError("Invalid encrypted format")
        result = dpapi(bytes.fromhex(value), protect=False).decode("utf-16-le")
    except AdapterError:
        raise
    except (ValueError, UnicodeError):
        raise AdapterError("credential_invalid", "El formato de la credencial guardada no es válido. Vuelve a conectar este proyecto.") from None
    if not valid_agent_key(result):
        raise AdapterError("credential_invalid", "La credencial descifrada no es una clave de agente válida. Vuelve a conectar este proyecto.")
    return result

def dpapi(value, protect):
    """Windows DPAPI with UI forbidden, current-user scope, and no fallback."""
    import ctypes
    from ctypes import wintypes
    class Blob(ctypes.Structure):
        _fields_ = [("size", wintypes.DWORD), ("data", ctypes.POINTER(ctypes.c_ubyte))]
    try:
        # Restrict DLL lookup to the operating-system directory, not the workspace.
        crypt = ctypes.WinDLL("crypt32.dll", use_last_error=True, winmode=0x00000800)
        kernel = ctypes.WinDLL("kernel32.dll", use_last_error=True, winmode=0x00000800)
        operation = crypt.CryptProtectData if protect else crypt.CryptUnprotectData
        operation.argtypes = [ctypes.POINTER(Blob), ctypes.c_void_p, ctypes.POINTER(Blob), ctypes.c_void_p,
                              ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(Blob)]
        operation.restype = wintypes.BOOL
        kernel.LocalFree.argtypes = [ctypes.c_void_p]
        kernel.LocalFree.restype = ctypes.c_void_p
        buffer = ctypes.create_string_buffer(value)
        source = Blob(len(value), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_ubyte)))
        output = Blob()
        try:
            if not operation(ctypes.byref(source), None, None, None, None, 0x1, ctypes.byref(output)):
                raise OSError("DPAPI failed")
            return ctypes.string_at(output.data, output.size)
        finally:
            ctypes.memset(buffer, 0, len(buffer))
            if output.data:
                ctypes.memset(output.data, 0, output.size)
                kernel.LocalFree(output.data)
    except (OSError, AttributeError):
        raise AdapterError("credential_unavailable", "Windows no pudo abrir la clave protegida. Usa el mismo usuario que la guardó o vuelve a conectar este proyecto.") from None

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        raise ValueError("API redirects are not allowed")

class Api:
    def __init__(self, config, token):
        self.config, self.token = config, token
        url = urllib.parse.urlsplit(config["url"])
        if url.scheme != "https" and not (url.scheme == "http" and url.hostname in ("127.0.0.1", "localhost", "::1")):
            raise ValueError("Use HTTPS or a loopback development API")
        if url.username or url.password or url.query or url.fragment: raise ValueError("Invalid API URL")
        if not valid_agent_key(token): raise AdapterError("credential_invalid", "La conexión requiere una clave de agente dgk_ válida. Vuelve a conectar este proyecto.")
        self.opener = urllib.request.build_opener(NoRedirect())
    def call(self, route, body=None, idem=None):
        headers = {"Authorization": "Bearer " + self.token, "Content-Type": "application/json"}
        if idem: headers["Idempotency-Key"] = idem
        request = urllib.request.Request(self.config["url"].rstrip("/") + route, data=None if body is None else canonical(body).encode(), headers=headers)
        with self.opener.open(request, timeout=4) as response:
            raw = response.read(1024 * 1024 + 1)
            if len(raw) > 1024 * 1024: raise AdapterError("api_response_invalid", "La API devolvió una respuesta demasiado grande. No se autorizó la operación.")
            result = json.loads(raw) if raw else {}
            if not isinstance(result, dict): raise AdapterError("api_response_invalid", "La API devolvió una respuesta no válida. No se autorizó la operación.")
            return result

class BridgeApi:
    """The native project credential host performs authenticated requests; it never returns the key."""
    def __init__(self, config):
        self.config = config

    def call(self, route, body=None, idem=None):
        import project_setup
        import io
        root = Path(self.config["root"])
        base = ".deleguard/" + self.config["adapter"] + "/"
        executable = project_setup.local_path(root, base + "runtime/" + ("Deleguard.Desktop.exe" if os.name == "nt" else "Deleguard.Desktop"))
        config_path = project_setup.local_path(root, base + "config.json")
        if not executable.is_file():
            raise AdapterError("runtime_missing", "The project runtime is missing. Reconnect this project in the visual installer.")
        result = subprocess.run([str(executable), "--bridge", str(config_path)],
            input=json.dumps(dict(Route=route, Body=body, IdempotencyKey=idem)), capture_output=True, text=True,
            encoding="utf-8", timeout=16, cwd=str(config_path.parent),
            **({"creationflags": subprocess.CREATE_NO_WINDOW} if os.name == "nt" else {}))
        try:
            reply = json.loads(result.stdout)
        except (ValueError, TypeError):
            raise AdapterError("session_locked", "Open this project's visual installer and unlock its connection.") from None
        if reply.get("Error"):
            raise AdapterError("session_locked", "The project connection is locked or unavailable. Open its visual installer to reconnect or unlock it.")
        status = reply.get("Status", 0)
        if not 200 <= status < 300:
            raise urllib.error.HTTPError(self.config["url"] + route, status, "Request rejected", {}, io.BytesIO(json.dumps(reply.get("Body") or {}).encode()))
        return reply.get("Body") or {}

def project_api(config):
    if config.get("schema_version") == 2 and config.get("credential_storage") == "project-vault":
        return BridgeApi(config)
    return Api(config, credential(config["credential_id"]))

class State:
    def __init__(self, path):
        self.path = Path(path)
        self.db = sqlite3.connect(path, timeout=12)
        self.db.row_factory = sqlite3.Row
        self.db.execute("pragma journal_mode=WAL")
        self.db.execute("create table if not exists operations (id text primary key, session text, fingerprint text, call_id text, snapshot text, decision text, state text, created real, evidence text)")
        self.db.execute("create unique index if not exists call_ids on operations(session,call_id) where call_id != ''")
        self.db.commit()
    def recover_results(self):
        pending = self.path.parent / "pending-results"
        if not pending.exists(): return
        import project_setup
        for path in list(pending.glob("*.json"))[:100]:
            # Never follow custom links inside the installation.
            project_setup.local_path(self.path.parent, str(path.relative_to(self.path.parent)))
            try: item = json.loads(path.read_text(encoding="utf-8"))
            except FileNotFoundError: continue  # Another hook recovered the same durable receipt.
            resolved = False
            with self.locked():
                row = self.db.execute("select * from operations where session=? and call_id=?", (item["session"], item["call_id"])).fetchone()
                if row and row["state"] == "armed":
                    payload = {"decision_id": json.loads(row["decision"])["decision_id"], "execution_id": json.loads(row["decision"]).get("execution", {}).get("execution_id"), "status": item["status"], "resource_snapshot_hash": row["snapshot"], "metadata": {"source": "ide-hook", "operation_id": row["id"], "session_id": item["session"], "evidence_kind": "client_reported"}}
                    self.db.execute("update operations set state='completed',evidence=? where id=?", (canonical(payload),row["id"]))
                    resolved = True
                elif row and row["state"] not in ("pending", "unknown"):
                    resolved = True
            if resolved: path.unlink(missing_ok=True)
    @contextlib.contextmanager
    def locked(self):
        self.db.execute("begin immediate")
        try:
            yield
            self.db.commit()
        except BaseException:
            self.db.commit()  # retain pending operation identity after transport failure
            raise
    def flush(self, api):
        self.recover_results()
        for row in self.db.execute("select id,evidence from operations where evidence is not null and state != 'evidence_rejected'").fetchall():
            try: api.call("/v1/executions", json.loads(row["evidence"]))
            except urllib.error.HTTPError as error:
                if error.code in (400, 403, 404, 409):
                    self.db.execute("update operations set state='evidence_rejected' where id=?", (row["id"],))
                continue
            except Exception: continue
            self.db.execute("update operations set evidence=null where id=?", (row["id"],))
        self.db.commit()

def safe_path(root, raw, cwd=None):
    if not isinstance(raw, str) or not raw or "\x00" in raw: raise ValueError("Missing or invalid path")
    if raw.startswith(("\\\\", "//")) or any(ord(c) < 32 for c in raw): raise ValueError("Network and control-character paths are not supported")
    if ".." in raw.replace("\\", "/").split("/"): raise ValueError("Parent traversal is not supported")
    if any(part not in ("", ".", "..") and part.endswith((".", " ")) for part in raw.replace("\\", "/").split("/")): raise ValueError("Ambiguous path suffix")
    root = Path(root).resolve(strict=True)
    candidate = Path(raw)
    if not candidate.is_absolute(): candidate = Path(cwd or root) / candidate
    candidate = Path(os.path.abspath(candidate))
    try: relative = candidate.relative_to(root)
    except ValueError: raise ValueError("Path outside registered project")
    current = root
    for part in relative.parts:
        if part in (".", "..") or ":" in part or part.endswith((".", " ")): raise ValueError("Ambiguous path")
        if os.name == "nt" and re.fullmatch(r"(?i)(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?", part): raise ValueError("Device paths are not supported")
        current = current / part
        if current.is_symlink() or (hasattr(current, "is_junction") and current.is_junction()): raise ValueError("Links and junctions are not supported")
        # Reject reparse points even on Python versions without Path.is_junction.
        if current.exists() and getattr(current.lstat(), "st_file_attributes", 0) & 0x400: raise ValueError("Reparse point")
    if candidate.resolve(strict=False) != candidate: raise ValueError("Path alias")
    stat = candidate.stat() if candidate.exists() else None
    if stat and getattr(stat, "st_nlink", 1) > 1 and candidate.is_file(): raise ValueError("Hard-linked files are not supported")
    stamp = {"exists": stat is not None}
    if stat:
        stamp.update(size=stat.st_size, modified_ns=stat.st_mtime_ns, inode=stat.st_ino, device=stat.st_dev)
        if candidate.is_file():
            if stat.st_size > 256 * 1024 * 1024: raise ValueError("File too large for a bound snapshot")
            sha = hashlib.sha256()
            with candidate.open("rb") as source:
                for chunk in iter(lambda: source.read(1024 * 1024), b""): sha.update(chunk)
            stamp["sha256"] = sha.hexdigest()
    return relative.as_posix() if relative.parts else ".", stamp

def normalize(event, config):
    if not isinstance(event, dict): raise AdapterError("hook_input_invalid", "El IDE no envió un evento válido. Actualiza la integración de este proyecto.")
    name = event.get("tool_name", "")
    if not isinstance(name, str) or not name or len(name) > 500: raise ValueError("Tool is not mapped; no execution permission")
    args = event["tool_input"] if "tool_input" in event else event.get("arguments", {})
    if isinstance(args, str):
        try: args = json.loads(args)
        except json.JSONDecodeError: raise ValueError("Tool arguments must be an object") from None
    if not isinstance(args, dict): raise ValueError("Tool arguments must be an object")
    hook = event.get("hook_event_name", "")
    if hook in ("beforeReadFile", "beforeTabFileRead"):
        name, args = "Read", {"file_path": event.get("file_path")}
    if hook == "beforeShellExecution": name, args = "Shell", {"command": event.get("command"), "cwd": event.get("cwd")}
    if hook == "beforeMCPExecution": name, args = "mcp__" + str(event.get("tool_name", "unknown")), event.get("tool_input", {})
    action = ACTIONS.get(name)
    if name.startswith(("mcp__", "MCP:")): action = "mcp.call"
    if not action: raise ValueError("Tool is not mapped; no execution permission")
    cwd = event.get("cwd") or config["root"]
    if not isinstance(cwd, str): raise ValueError("Missing or invalid path")
    # An absolute tool path must not hide a hook running for a different project.
    safe_path(config["root"], cwd)
    # IDEs do not agree on the field name for a single file. Accept only explicit,
    # single-path aliases; unknown shapes still fail closed.
    if action in ("process.execute", "mcp.call"):
        raw = single_path(args, ("working_directory", "cwd")) or cwd
    else:
        raw = single_path(args, ("file_path", "filePath", "path", "file", "target_file", "targetFile", "relative_path", "relativePath", "directory"), include_files=True)
    if action in ("filesystem.search", "filesystem.list") and not raw: raw = cwd
    path, stamp = safe_path(config["root"], raw, cwd)
    if action == "filesystem.write" and not stamp["exists"]: action = "filesystem.create"
    destination = None
    if action == "filesystem.move":
        destination, target_stamp = safe_path(config["root"], single_path(args, ("destination", "destination_path")), cwd)
        stamp["destination"] = target_stamp
    # Hash the complete arguments locally. Content, command text and outputs never enter the request.
    operation_hash = digest({"tool_name": name, "action": action, "arguments": args, "path": path, "destination": destination, "snapshot": stamp})
    attrs = {"project_id": config["project"], "path": path, "operation_hash": operation_hash, "tool_name": name}
    if not action.startswith("filesystem."): attrs["operation_kind"] = action
    if destination is not None: attrs["destination_path"] = destination
    return action, attrs, operation_hash

def single_path(args, fields, include_files=False):
    """Do not guess which of two conflicting paths the actual IDE will touch."""
    candidates = [args[field] for field in fields if field in args]
    if include_files and "files" in args:
        files = args["files"]
        if not isinstance(files, list) or len(files) != 1:
            raise AdapterError("batch_unsupported", "Divide esta operación en una llamada por archivo. La integración no puede autorizar un lote como si fuera un solo archivo.")
        candidates.extend(files)
    if any(not isinstance(path, str) or not path for path in candidates): raise ValueError("Missing or invalid path")
    if len(set(candidates)) > 1:
        raise AdapterError("path_ambiguous", "La herramienta envió varias rutas distintas. Usa una operación con una sola ruta explícita.")
    return candidates[0] if candidates else None

def response(adapter, allow, reason):
    if adapter == "cursor": return {"permission": "allow" if allow else "deny", "user_message": reason, "agent_message": reason}
    return {"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "allow" if allow else "deny", "permissionDecisionReason": reason}}

def capabilities(adapter):
    # Self-reports do not attest that the host actually loaded or enforced a hook.
    return {"mapped_file_tools": "observed", "terminal_invocation": "observed", "terminal_file_effects": "uncovered", "mcp_via_hook": "observed", "indexing": "uncovered", "other_extensions": "uncovered", "post_tool_result": "observed", "unmapped_tools": "uncovered", "broker_isolation": "uncovered"}

def safe_failure_reason(error, language="es"):
    """Give the developer a next step without disclosing paths, arguments, tokens or server responses."""
    if language == "en":
        code=error.code if isinstance(error,AdapterError) else "connection_failed"
        messages={
            "session_locked":"Open the visual installer for this project and unlock or reconnect its connection.",
            "runtime_missing":"The project runtime is missing. Reconnect this folder with the visual installer.",
            "project_scope_changed":"This key belongs to another organization, application or agent. Reconnect the correct project.",
            "session_missing":"The IDE did not send a valid session identifier. Update it and reconnect this project.",
            "tool_call_missing":"The tool call has no stable identifier. Update the IDE and use its general pre-tool hook.",
            "tool_call_reused":"The IDE reused an operation identifier or the resource changed. Request a new operation.",
            "batch_unsupported":"Split this request into one operation per file.",
            "path_ambiguous":"The tool supplied conflicting paths. Use one explicit file path.",
            "cursor_transport_unverified":"This Cursor version requires hook transport verification. Update the integration before retrying.",
        }
        if isinstance(error,urllib.error.HTTPError):
            return "The key or project was rejected. Check this project's server and agent in IDE Control; the operation remains blocked."
        if isinstance(error,(urllib.error.URLError,TimeoutError,ConnectionError)):
            return "DeleGuard is unreachable. Check the server URL and connection; the operation remains blocked."
        return "["+code+"] "+messages.get(code,"The local integration needs attention. Open the visual installer, check this folder's connection and review its files before retrying. The operation remains blocked.")
    if isinstance(error, AdapterError): return "[" + error.code + "] " + str(error)
    text = str(error).lower()
    if isinstance(error, urllib.error.HTTPError):
        if error.code in (401, 403): return "La clave del agente no puede usar este proyecto. Vuelve a conectar el IDE con el agente correcto."
        if error.code == 404: return "El proyecto o el agente ya no están registrados. Abre IDE Control y revisa la conexión."
        if error.code == 409: return "La configuración o la regla cambió mientras se comprobaba la operación. Vuelve a intentarlo desde IDE Control."
        return "DeleGuard devolvió un error de configuración (HTTP " + str(error.code) + "). Revisa el estado del proyecto en IDE Control."
    if isinstance(error, (urllib.error.URLError, TimeoutError, ConnectionError)):
        return "[api_unreachable] No se pudo contactar con DeleGuard. Revisa la URL, la conexión y el estado de la API. La operación sigue bloqueada."
    if "managed" in text or "manifest" in text or "hooks missing" in text:
        return "[installation_changed] Faltan archivos o cambió la integración. Ejecuta install --plan, revisa tus personalizaciones y usa --replace-managed solo si quieres repararlas."
    if "legacy" in text:
        return "[migration_required] La instalación anterior necesita una migración explícita. Usa install --migrate-profile con el perfil y el Python originales; no se cambió otro proyecto."
    if "configuration moved" in text or "another connection" in text or "tenant/application changed" in text:
        return "[project_mismatch] Esta carpeta o credencial pertenece a otra conexión. Revisa proyecto, agente y servidor; no reutilices la configuración de otra carpeta."
    if "case setting" in text:
        return "[filesystem_case_mismatch] El ajuste de mayúsculas del proyecto no coincide con este disco. Registra el proyecto con la semántica correcta antes de conectar."
    if "invalid api url" in text or "use https" in text or "base api url" in text:
        return "[connection_url_invalid] Revisa la URL base de DeleGuard y la URL HTTPS del MCP. No incluyas claves, parámetros ni credenciales en las direcciones."
    if isinstance(error, json.JSONDecodeError):
        return "[configuration_json_invalid] Un archivo de configuración no contiene JSON válido. Revisa su sintaxis; no se sobrescribió."
    if "tool is not mapped" in text or "missing or invalid path" in text or "tool arguments must be an object" in text:
        return "Esta herramienta o su ruta no están reconocidas. Usa la herramienta de lectura de archivos del IDE y vuelve a ejecutar el diagnóstico."
    if "outside registered project" in text or "links and junctions" in text or "path alias" in text or "reparse point" in text or "parent traversal" in text:
        return "La ruta no pertenece al proyecto protegido o usa un enlace. Abre la carpeta correcta en el IDE."
    if "missing execution permit" in text:
        return "DeleGuard autorizó la operación pero no devolvió un permiso de ejecución. Revisa el registro en IDE Control y solicita de nuevo la operación exacta."
    if "tool call id was reused" in text or "resource changed while waiting" in text:
        return "La operación cambió de argumentos o de archivo mientras se autorizaba. Solicita una operación nueva; no reutilices la llamada anterior."
    if "hook input too large" in text:
        return "El evento del hook es demasiado grande. Divide la operación; no se autorizó."
    if isinstance(error, FileNotFoundError):
        return "[local_file_missing] Falta una ruta o un componente de la integración local. Ejecuta el diagnóstico; no intentes una vía alternativa para la operación bloqueada."
    if "invalid_agent" in text or "unauthorized" in text or "401" in text:
        return "La conexión necesita una clave válida del agente. Ejecuta el diagnóstico de IDE Control."
    return "El adaptador local necesita atención (" + type(error).__name__ + "). Ejecuta el diagnóstico de IDE Control antes de reintentar."

def blocked_decision_message(config, decision):
    base = config["url"].rstrip("/")
    record = base + "/audit/decisions/" + urllib.parse.quote(str(decision.get("decision_id", "")), safe="") if decision.get("decision_id") else base + "/audit/activity"
    if config.get("language") == "en":
        if decision.get("decision") == "REQUIRE_APPROVAL" and decision.get("approval_id"):
            approval=base+"/approvals/"+urllib.parse.quote(str(decision["approval_id"]),safe="")
            return "DeleGuard: REQUIRE_APPROVAL. Review this exact operation at "+approval+" and retry it after approval. Do not use another tool to bypass the pause. Record: "+record
        reason="No published policy applies to this agent and action. Check publication and assignments." if decision.get("reason")=="No applicable policy." else "The policy did not authorize this operation."
        return "DeleGuard: DENY. "+reason+" No approval is waiting. Review the configuration and request a new operation. Do not bypass this block. Record: "+record
    if decision.get("decision") == "REQUIRE_APPROVAL" and decision.get("approval_id"):
        approval = base + "/approvals/" + urllib.parse.quote(str(decision["approval_id"]), safe="")
        return "DeleGuard: REQUIRE_APPROVAL. Pendiente de aprobación: " + approval + ". Después de aprobar en la consola, solicita de nuevo esta operación exacta. No se ejecutará automáticamente. Registro: " + record
    # Only emit known diagnostics: raw reasons may include policy-controlled sensitive values.
    reason = "La política denegó esta operación."
    if decision.get("reason") == "No applicable policy.":
        reason = "No hay una política publicada aplicable a este agente y acción. Revisa la publicación y asignación en IDE Control. High clasifica el riesgo; no concede permisos."
    elif decision.get("decision") != "DENY":
        reason = "La respuesta no contiene una autorización o aprobación válida. Revisa la integración."
    return "DeleGuard: DENY. " + reason + " No hay una aprobación pendiente que puedas aceptar. Tras corregir la configuración, solicita una nueva operación desde el IDE. No uses otra herramienta para eludir este bloqueo. Registro: " + record


def run_hook(event, config, api, state):
    if not isinstance(event, dict): raise AdapterError("hook_input_invalid", "El IDE no envió un evento válido. Actualiza la integración de este proyecto.")
    session = event.get("session_id") or event.get("conversation_id") or ""
    if not isinstance(session, str) or not session or len(session) > 120:
        raise AdapterError("session_missing", "El IDE no envió una identidad de sesión válida. Actualiza el IDE y vuelve a instalar la integración en este proyecto.")
    call_id = event.get("tool_use_id") or event.get("toolUseId") or ""
    if not isinstance(call_id, str) or len(call_id) > 200:
        raise AdapterError("tool_call_invalid", "El identificador de la llamada no es válido. No se puede vincular un permiso a esta operación.")
    hook = event.get("hook_event_name", "")
    state.recover_results()
    if hook in AFTER:
        if not call_id: return {}
        with state.locked():
            row = state.db.execute("select * from operations where session=? and call_id=?", (session, call_id)).fetchone()
            if row and row["state"] == "armed":
                failed = "Failure" in hook or "failure" in hook.lower()
                payload = {"decision_id": json.loads(row["decision"])["decision_id"], "execution_id": json.loads(row["decision"]).get("execution", {}).get("execution_id"), "status": "FAILED" if failed else "SUCCESS", "resource_snapshot_hash": row["snapshot"], "metadata": {"source": "ide-hook", "operation_id": row["id"], "session_id": session, "evidence_kind": "client_reported"}}
                state.db.execute("update operations set state='completed',evidence=? where id=?", (canonical(payload), row["id"]))
        state.flush(api)
        return {}
    if hook not in BEFORE: raise AdapterError("hook_event_unsupported", "Se necesita el hook general preToolUse/PreToolUse. Los hooks antiguos no identifican cada operación de forma segura.")
    if not call_id: raise AdapterError("tool_call_missing", "El IDE no envió un identificador estable de la operación. Actualiza el IDE y usa preToolUse/PreToolUse; no se creó un permiso.")
    action, attrs, fingerprint = normalize(event, config)
    # Always fetch current project config; never authorize offline from cached approval.
    project = api.call("/v1/ide/config/" + urllib.parse.quote(config["project"], safe=""))
    if config.get("server_scope") and config["server_scope"] != {key: project.get(key) for key in ("organization_id", "application_id", "agent_id")}:
        raise AdapterError("project_scope_changed", "La credencial pertenece a otra organización, aplicación o agente. Reconecta este proyecto; no se solicitó ninguna operación.")
    if project.get("share_diff") and isinstance(event.get("tool_input"), dict):
        patch = event["tool_input"].get("diff")
        if isinstance(patch, str) and len(patch) <= 16000: attrs["proposed_diff"] = patch
    api.call("/v1/ide/sessions", {"id": session, "project_id": config["project"], "adapter": config["adapter"], "version": str(event.get("cursor_version") or event.get("version") or "unverified"), "platform": platform.system(), "capabilities": capabilities(config["adapter"])})
    with state.locked():
        row = state.db.execute("select * from operations where session=? and call_id=?", (session, call_id)).fetchone()
        if row and row["fingerprint"] != fingerprint:
            # Cursor 3.17 reuses tool_use_id across tools in one generation. A finished
            # row must not consume the unique (session, call_id) slot or a later Write
            # raises before authorize. Pending/armed stay bound to the original permit.
            if row["state"] in ("denied", "completed", "unknown", "evidence_rejected"):
                retired = call_id + ":retired:" + row["id"][:8]
                state.db.execute("update operations set call_id=? where id=?", (retired, row["id"]))
                row = None
            else:
                raise AdapterError("tool_call_reused", "El IDE reutilizó el identificador de una operación distinta o el archivo cambió. Solicita una operación nueva.")
        if not row:
            # Only unresolved approvals/authorizations can resume. Older adapters stored
            # DENY as pending: retain that audit history, but never reuse it for a new call.
            candidates = state.db.execute("select * from operations where session=? and fingerprint=? and state='pending' order by created desc", (session, fingerprint)).fetchall()
            for candidate in candidates:
                prior = json.loads(candidate["decision"]) if candidate["decision"] else None
                if prior and prior.get("decision") == "DENY":
                    state.db.execute("update operations set state='denied' where id=?", (candidate["id"],))
                    continue
                row = candidate
                break
            if row:
                state.db.execute("update operations set call_id=? where id=?", (call_id, row["id"]))
        saved = json.loads(row["decision"]) if row and row["decision"] else None
        if row and (row["state"] == "denied" or row["state"] == "pending" and saved and saved.get("decision") == "DENY"):
            state.db.execute("update operations set state='denied' where id=?", (row["id"],))
            return response(config["adapter"], False, blocked_decision_message(config, saved or {}))
        if row and row["state"] != "pending":
            return response(config["adapter"], False, ("La operación ya se liberó o terminó. No se ejecutará de nuevo. Si no llegó el resultado, revisa Actividad antes de iniciar otra operación." if config.get("language")=="es" else "Operation already released or completed. It will not be executed again. If no result arrived, inspect Activity before starting a new operation."))
        operation = row["id"] if row else uuid.uuid4().hex
        attrs["operation_id"] = operation
        snapshot = digest(attrs)
        if not row:
            state.db.execute("insert into operations values(?,?,?,?,?,?,?,?,?)", (operation, session, fingerprint, call_id, snapshot, None, "pending", time.time(), None))
        if saved:
            decision = api.call("/v1/decisions/" + saved["decision_id"])
        else:
            kind = "filesystem" if action.startswith("filesystem.") else "process" if action == "process.execute" else "mcp"
            body = {"operation": {"id": operation, "hash": attrs["operation_hash"]}, "agent": {"id": config["agent"], "environment": config["environment"]}, "action": action, "resource": {"type": kind, "id": config["project"] + ":" + attrs["path"], "attributes": attrs, "snapshot_hash": snapshot}, "context": {"source": "ide-hook", "session_id": session, "adapter": config["adapter"]}}
            decision = api.call("/v1/authorize", body, "ide_" + operation)
        state.db.execute("update operations set decision=? where id=?", (canonical(decision), operation))
        if decision.get("decision") != "ALLOW":
            if decision.get("decision") != "REQUIRE_APPROVAL" or not decision.get("approval_id"):
                state.db.execute("update operations set state='denied' where id=?", (operation,))
            return response(config["adapter"], False, blocked_decision_message(config, decision))
        if not decision.get("permit"): raise ValueError("Missing execution permit")
        # Re-read file metadata immediately before consume; authorization is bound to the original operation.
        if normalize(event, config)[2] != fingerprint: raise ValueError("Resource changed while waiting; request a new operation")
        consumed = api.call("/v1/ide/permits/consume", {"decision_id": decision["decision_id"], "operation_id": operation, "snapshot_hash": snapshot})
        if not consumed.get("acquired"):
            state.db.execute("update operations set state='unknown' where id=?", (operation,))
            return response(config["adapter"], False, ("El permiso ya se consumió. El resultado es incierto; se bloqueó la repetición automática." if config.get("language")=="es" else "Permit was already consumed. Execution outcome is uncertain; automatic replay is blocked."))
        if not isinstance(consumed.get("execution_id"), str) or not consumed["execution_id"]:
            state.db.execute("update operations set state='unknown' where id=?", (operation,))
            return response(config["adapter"], False, "Execution receipt missing after consumption; automatic replay is blocked.")
        decision["execution"] = {"execution_id": consumed["execution_id"]}
        state.db.execute("update operations set state='armed', decision=? where id=?", (canonical(decision), operation))
    return response(config["adapter"], True, ("DeleGuard autorizó esta operación exacta. El hook del IDE reportará el resultado." if config.get("language")=="es" else "DeleGuard authorized this exact operation. Result will be reported by the IDE hook."))

def install(args):
    import project_setup
    project_setup.install(args, sys.modules[__name__])

def queue_completion(event, config):
    """Durable minimal receipt before waiting for SQLite; never store tool arguments/output."""
    import project_setup
    session=event.get("session_id") or event.get("conversation_id") or ""
    call_id=event.get("tool_use_id") or event.get("toolUseId") or ""
    if not isinstance(session,str) or not 0<len(session)<=120 or not isinstance(call_id,str) or not 0<len(call_id)<=200:
        return
    base=".deleguard/"+config["adapter"]+"/pending-results/"
    target=project_setup.local_path(Path(config["root"]),base+digest([session,call_id])+".json")
    target.parent.mkdir(mode=0o700,exist_ok=True)
    temporary=project_setup.local_path(Path(config["root"]),base+uuid.uuid4().hex+".tmp")
    status="FAILED" if "failure" in event["hook_event_name"].lower() else "SUCCESS"
    try:
        with os.fdopen(os.open(temporary,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600),"w",encoding="utf-8") as stream:
            json.dump(dict(session=session,call_id=call_id,status=status),stream);stream.flush();os.fsync(stream.fileno())
        os.replace(temporary,target)
    finally:temporary.unlink(missing_ok=True)

def main():
    parser = argparse.ArgumentParser()
    subs = parser.add_subparsers(dest="verb", required=True)
    setup = subs.add_parser("install")
    for option in ("url", "project", "agent"): setup.add_argument("--" + option, required=True)
    setup.add_argument("--adapter", required=True, choices=("cursor", "vscode", "claude"))
    setup.add_argument("--root", default=str(Path.cwd()))
    setup.add_argument("--profile", help="Legacy label, not used for new installation identity")
    setup.add_argument("--environment", default="develop")
    setup.add_argument("--keychain-ready", action="store_true")
    setup.add_argument("--plan", action="store_true", help="Preview without API calls, credentials or file changes")
    setup.add_argument("--mcp-url", help="HTTPS Guard URL; authentication remains in the IDE")
    setup.add_argument("--replace-managed", action="store_true", help="Confirm replacing customized managed files after review")
    setup.add_argument("--migrate-profile", help="Migrate matching legacy hooks without changing other projects")
    setup.add_argument("--reconnect", action="store_true", help="Prompt for a replacement agent key, preserving project identity and operation journal")
    for name in ("hook", "doctor", "flush", "pending"):
        sub = subs.add_parser(name)
        source = sub.add_mutually_exclusive_group(required=True)
        source.add_argument("--profile", help="Legacy installation only")
        source.add_argument("--config", help="Absolute or relative project-local config.json path")
    args = parser.parse_args()
    if args.verb == "install": install(args); return
    event = {}
    config = {}
    import contextlib
    installation = contextlib.ExitStack()
    state = None
    try:
        if args.verb == "hook":
            raw = sys.stdin.buffer.read(4 * 1024 * 1024 + 1)
            if len(raw) > 4 * 1024 * 1024: raise ValueError("Hook input too large")
            event = json.loads(raw.decode("utf-8-sig"))
            if not isinstance(event, dict): raise ValueError("Invalid hook payload")
        if args.config:
            import project_setup
            config = project_setup.load_config(args.config)
            if config.get("schema_version") == 2:
                from portable_setup import installation_lock
                installation.enter_context(installation_lock(Path(config["root"]), shared=True))
                config = project_setup.load_config(args.config)
            if args.verb == "doctor":
                report = project_setup.doctor(config, sys.modules[__name__])
                print(json.dumps(report, ensure_ascii=True, indent=2))
                if report["status"] == "configuration_incomplete": sys.exit(2)
                return
            project_setup.integrity(config)
            if args.verb == "hook" and config.get("cursor_windows_transport") == "3.17-utf8-file":
                if not re.fullmatch(r"3\.17\.\d+", str(event.get("cursor_version", ""))):
                    raise AdapterError("cursor_transport_unverified", "Esta versión de Cursor necesita validar su transporte de hooks. Actualiza la integración; no se autorizó la operación.")
            secret_id = config["credential_id"]
            journal = project_setup.local_path(Path(config["root"]), ".deleguard/" + config["adapter"] + "/state.sqlite")
        else:
            if not args.profile or not args.profile.replace("-", "").replace("_", "").isalnum(): raise ValueError("Invalid profile name")
            config = json.loads((state_root() / (args.profile + ".json")).read_text())
            secret_id = args.profile
            journal = state_root() / (args.profile + ".sqlite")
        api = project_api(config) if args.config else Api(config, credential(secret_id))
        if args.verb == "doctor":
            api.call("/v1/ide/config/" + urllib.parse.quote(config["project"], safe=""))
            print(json.dumps({"api": "connected", "adapter": config["adapter"], "status": "legacy_partial_coverage", "next_step": "Migrate this project with install --migrate-profile. Connectivity does not certify hook coverage."}, indent=2)); return
        if config.get("schema_version") == 2 and event.get("hook_event_name") in AFTER:
            queue_completion(event, config)
        state = State(journal)
        if args.verb == "pending":
            for row in state.db.execute("select id,decision from operations where state='pending' and decision is not null"):
                saved = json.loads(row["decision"])
                current = api.call("/v1/decisions/" + saved["decision_id"])
                next_step = ("Permission available: request the same operation again. This diagnostic does not execute tools." if config.get("language")=="en" else "Permiso disponible: solicita de nuevo la operación exacta. Este diagnóstico no ejecuta herramientas.") if current.get("decision") == "ALLOW" else blocked_decision_message(config, current)
                print(canonical({"operation_id": row["id"], "decision_id": saved["decision_id"], "decision": current.get("decision"), "next_step": next_step}))
            return
        if args.verb == "flush": state.flush(api); return
        print(canonical(run_hook(event, config, api, state)))
    except Exception as error:
        # Never return exception text: transport errors and tool arguments can contain secrets.
        if args.verb != "hook":
            print(canonical({"status": "configuration_incomplete", "message": safe_failure_reason(error,config.get("language","en") if config.get("schema_version")==2 or getattr(sys,"frozen",False) else "es")}))
            sys.exit(2)
        if event.get("hook_event_name") in AFTER:
            print("{}"); return
        if config.get("adapter"):
            print(canonical(response(config["adapter"], False, safe_failure_reason(error,config.get("language","en") if config.get("schema_version")==2 else "es"))))
            return  # JSON decision is consumed consistently across supported IDEs.
        print(safe_failure_reason(error,"en" if getattr(sys,"frozen",False) else "es"), file=sys.stderr)
        sys.exit(2)  # Missing configuration: never return a successful empty hook response.
    finally:
        if state is not None:
            state.db.close()
        installation.close()

if __name__ == "__main__":
    try: main()
    except Exception as error:
        print(safe_failure_reason(error,"en" if getattr(sys,"frozen",False) else "es"), file=sys.stderr)
        sys.exit(2)
