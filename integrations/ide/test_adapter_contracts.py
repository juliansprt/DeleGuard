"""Host contract and credential regressions; no real agent key or API required."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("adapter_contract_guard", Path(__file__).with_name("deleguard_ide.py"))
g = importlib.util.module_from_spec(spec)
spec.loader.exec_module(g)


class ContractTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        (self.root / "code.txt").write_text("local fixture", encoding="utf-8")
        self.config = {"root": str(self.root), "project": "project-a", "adapter": "cursor"}

    def tearDown(self):
        self.tmp.cleanup()

    def event(self, name="Read", args=None, **extra):
        return {"hook_event_name": "preToolUse", "conversation_id": "session-1", "tool_use_id": "tool-1",
                "tool_name": name, "tool_input": {"file_path": "code.txt"} if args is None else args,
                "cwd": str(self.root), **extra}

    def test_supported_single_file_contracts(self):
        cases = [("Read", {"path": "code.txt"}), ("ReadFile", {"file_path": "code.txt"}),
                 ("read_file", {"filePath": "code.txt"}), ("readFile", {"filePath": "code.txt"})]
        for tool, args in cases:
            with self.subTest(tool=tool):
                action, attrs, _ = g.normalize(self.event(tool, args), self.config)
                self.assertEqual("filesystem.read", action)
                self.assertEqual("code.txt", attrs["path"])

    def test_create_in_project_root_maps_to_create(self):
        for tool in ("Write", "WriteFile", "create_file", "createFile"):
            with self.subTest(tool=tool):
                action, attrs, _ = g.normalize(self.event(tool, {"file_path": "new.txt", "content": "PRIVATE"}), self.config)
                self.assertEqual("filesystem.create", action)
                self.assertNotIn("PRIVATE", json.dumps(attrs))

    def test_string_tool_input_is_parsed_for_write(self):
        event = self.event("Write", {"path": "new.txt", "contents": "PRIVATE"})
        event["tool_input"] = json.dumps(event["tool_input"])
        action, attrs, _ = g.normalize(event, self.config)
        self.assertEqual("filesystem.create", action)
        self.assertEqual("new.txt", attrs["path"])
        self.assertNotIn("PRIVATE", json.dumps(attrs))

    def test_missing_permit_and_string_args_have_safe_next_step(self):
        self.assertIn("permiso de ejecución", g.safe_failure_reason(ValueError("Missing execution permit")))
        self.assertIn("no están reconocidas", g.safe_failure_reason(ValueError("Tool arguments must be an object")))
        reused = g.safe_failure_reason(g.AdapterError("tool_call_reused", "El IDE reutilizó el identificador de una operación distinta o el archivo cambió. Solicita una operación nueva."))
        self.assertIn("[tool_call_reused]", reused)
        self.assertNotIn("prueba.txt", reused)

    def test_conflicting_paths_are_not_guessed(self):
        for args in ({"path": "code.txt", "file_path": "other.txt"},
                     {"files": ["code.txt"], "filePath": "other.txt"},
                     {"files": ["code.txt", "other.txt"]},
                     {"file_path": "code.txt", "path": None}):
            with self.subTest(args=args), self.assertRaises(ValueError):
                g.normalize(self.event("Edit", args), self.config)

    def test_move_destination_aliases_cannot_disagree(self):
        with self.assertRaises(g.AdapterError):
            g.normalize(self.event("Move", {"path": "code.txt", "destination": "a.txt", "destination_path": "b.txt"}), self.config)

    def test_external_cwd_cannot_borrow_project_absolute_file(self):
        with self.assertRaises(ValueError):
            g.normalize(self.event("Read", {"file_path": str(self.root / "code.txt")}, cwd=str(self.root.parent)), self.config)

    def test_non_object_arguments_fail_closed(self):
        for args in (None, [], 0, "[]"):
            event = self.event()
            event["tool_input"] = args
            with self.subTest(args=args), self.assertRaises(ValueError):
                g.normalize(event, self.config)

    def test_process_and_mcp_send_operation_kind_without_arguments(self):
        for tool, action in (("Shell", "process.execute"), ("PowerShell", "process.execute"),
                             ("MCP:remote_tool", "mcp.call"), ("mcp__server__tool", "mcp.call")):
            with self.subTest(tool=tool):
                mapped, attrs, fingerprint = g.normalize(self.event(tool, {"command": "PRIVATE"}), self.config)
                self.assertEqual(action, mapped)
                self.assertEqual(action, attrs["operation_kind"])
                self.assertNotIn("PRIVATE", json.dumps(attrs))
                self.assertEqual(64, len(fingerprint))

    def test_missing_identity_and_legacy_hooks_fail_before_api(self):
        class NoApi:
            def call(self, *args, **kwargs): raise AssertionError("must not call API")
        with tempfile.TemporaryDirectory() as state_dir:
            state = g.State(Path(state_dir) / "state.sqlite")
            try:
                events = [self.event(tool_use_id=""), self.event(conversation_id=""),
                          self.event(tool_use_id={"untrusted": True}), self.event(hook_event_name="beforeReadFile")]
                for event in events:
                    with self.subTest(event=event), self.assertRaises(g.AdapterError):
                        g.run_hook(event, self.config, NoApi(), state)
            finally: state.db.close()

    def test_each_adapter_deny_contract(self):
        self.assertEqual("deny", g.response("cursor", False, "blocked")["permission"])
        for adapter in ("claude", "vscode"):
            output = g.response(adapter, False, "blocked")["hookSpecificOutput"]
            self.assertEqual("PreToolUse", output["hookEventName"])
            self.assertEqual("deny", output["permissionDecision"])

    def test_hook_coverage_never_claims_attested_enforcement(self):
        for adapter in ("cursor", "claude", "vscode"):
            values = set(g.capabilities(adapter).values())
            self.assertLessEqual(values, {"uncovered", "observed"})
            self.assertEqual("uncovered", g.capabilities(adapter)["broker_isolation"])

    @unittest.skipUnless(os.name == "nt", "Windows device paths")
    def test_windows_device_paths_do_not_get_opened(self):
        for path in ("NUL", "CON.txt", "COM1", "folder/LPT9.txt"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                g.safe_path(self.root, path)


class CredentialTests(unittest.TestCase):
    def test_profile_names_cannot_escape_storage(self):
        for profile in ("../other", "C:\\secret", "", "one/two"):
            with self.subTest(profile=profile), self.assertRaises(g.AdapterError): g.credential(profile)

    def test_invalid_key_never_overwrites_existing_credential(self):
        for value in ("", "dgk_", "admin_key", "dgk_test\nsecond", " dgk_test"):
            with self.subTest(value=value), patch.object(g, "state_root") as root, self.assertRaises(g.AdapterError):
                g.credential("profile", value)
            root.assert_not_called()

    def test_provider_failure_never_leaks_stderr_or_secret(self):
        error = subprocess.CalledProcessError(1, ["provider"], stderr="PRIVATE credential")
        with patch.object(g.subprocess, "run", side_effect=error), self.assertRaises(g.AdapterError) as raised:
            g.credential_command(["provider"], "PRIVATE")
        reason = g.safe_failure_reason(raised.exception)
        self.assertIn("credential_unavailable", reason)
        self.assertNotIn("PRIVATE", reason)

    def test_dpapi_preserves_legacy_utf16_hex_format_without_subprocess(self):
        with patch.object(g, "dpapi", return_value="dgk_test".encode("utf-16-le")) as dpapi, patch.object(g.subprocess, "run") as run:
            self.assertEqual("dgk_test", g.windows_secret("0100", protect=False))
        dpapi.assert_called_once_with(bytes.fromhex("0100"), protect=False)
        run.assert_not_called()
        with patch.object(g, "dpapi", return_value=bytes.fromhex("0100")) as dpapi:
            self.assertEqual("0100", g.windows_secret("dgk_test", protect=True))
        dpapi.assert_called_once_with("dgk_test".encode("utf-16-le"), protect=True)

    def test_empty_success_is_not_accepted_as_a_credential(self):
        with patch.object(g, "dpapi", return_value=b""), self.assertRaises(g.AdapterError):
            g.windows_secret("0100", protect=False)

    def test_dpapi_failure_is_not_misreported_as_invalid_saved_key(self):
        error = g.AdapterError("credential_unavailable", "Windows cannot unlock this credential")
        with patch.object(g, "dpapi", side_effect=error), self.assertRaises(g.AdapterError) as raised:
            g.windows_secret("0100", protect=False)
        self.assertEqual("credential_unavailable", raised.exception.code)

    @unittest.skipUnless(os.name == "nt", "Windows DPAPI roundtrip")
    def test_windows_dpapi_roundtrip_without_writing_a_secret_file(self):
        # A synthetic non-production value; it is sent only over stdin.
        encrypted = g.windows_secret("dgk_contract_test_only", protect=True)
        self.assertNotIn("dgk_contract_test_only", encrypted)
        self.assertEqual("dgk_contract_test_only", g.windows_secret(encrypted, protect=False))


if __name__ == "__main__": unittest.main()
