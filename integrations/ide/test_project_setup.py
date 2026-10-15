import argparse
import base64
import contextlib
import importlib.util
import io
import json
import os
import sqlite3
import subprocess
import sys
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("setup", Path(__file__).with_name("project_setup.py"))
s = importlib.util.module_from_spec(spec)
spec.loader.exec_module(s)
PACKAGE = Path(__file__).parent


class ProjectSetupTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name) / "project"
        self.root.mkdir()
        self.args = argparse.Namespace(root=str(self.root), url="https://guard.example", project="demo",
                                       agent="agent", environment="develop", adapter="cursor", mcp_url=None,
                                       plan=False, replace_managed=False, migrate_profile=None, keychain_ready=False)

    def tearDown(self):
        self.tmp.cleanup()

    def config(self):
        return s.configuration(self.args)

    def installed(self):
        config = self.config()
        files = s.plan_files(config, PACKAGE)
        s.commit_files(self.root, files, ".deleguard/" + self.args.adapter)
        return config

    def test_identity_isolates_every_scope_dimension(self):
        config = self.config()
        for key in ("root", "url", "project", "agent", "adapter", "environment"):
            with self.subTest(key=key):
                other = dict(config)
                other[key] += "-other"
                self.assertNotEqual(config["credential_id"], s.identity(other))

    def test_plan_has_no_filesystem_or_credential_side_effect(self):
        self.args.plan = True
        class Forbidden:
            def __getattr__(self, name):
                raise AssertionError("Preview accessed credential or API")
        with contextlib.redirect_stdout(io.StringIO()) as out:
            s.install(self.args, Forbidden())
        result = json.loads(out.getvalue())
        self.assertFalse(result["changes_applied"])
        self.assertEqual([], list(self.root.iterdir()))
        self.assertTrue(result["credential_id"].startswith("ide-"))

    def test_all_adapters_have_project_local_hooks_skill_agent_and_mcp(self):
        for adapter in s.ADAPTERS:
            with self.subTest(adapter=adapter):
                self.args.adapter = adapter
                self.args.mcp_url = "https://guard.example/mcp/guard"
                config = self.config()
                files = s.plan_files(config, PACKAGE)
                self.assertIn(s.HOOK_FILES[adapter], files)
                self.assertIn(s.MCP_FILES[adapter], files)
                self.assertTrue(any("/skills/" in name for name in files))
                self.assertTrue(any("/agents/" in name for name in files))
                hook = json.loads(files[s.HOOK_FILES[adapter]])
                if os.name == "nt" and adapter != "cursor":
                    self.assertIn("-EncodedCommand", json.dumps(hook))
                else:
                    self.assertIn("--config", json.dumps(hook))
                self.assertNotIn("--profile", json.dumps(hook))
                self.assertNotIn("Authorization", files[s.MCP_FILES[adapter]].decode())

    def test_reinstall_is_idempotent_and_preserves_other_hooks(self):
        hooks_path = self.root / s.HOOK_FILES["cursor"]
        hooks_path.parent.mkdir()
        other = {"command": "my-own-hook", "timeout": 5}
        hooks_path.write_text(json.dumps({"version": 1, "hooks": {"preToolUse": [other]}}))
        config = self.installed()
        files = s.plan_files(config, PACKAGE)
        self.assertEqual([], s.commit_files(self.root, files, ".deleguard/cursor"))
        self.assertIn(other, s.read_json(hooks_path)["hooks"]["preToolUse"])
        self.assertEqual(2, len(s.read_json(hooks_path)["hooks"]["preToolUse"]))
        s.integrity(config)

    @unittest.skipUnless(os.name == "nt", "Windows shell contract")
    def test_windows_hook_quotes_metacharacters_and_preserves_stdin(self):
        dangerous_root = self.root / "project&name %PATH% 'quoted'"
        dangerous_root.mkdir()
        self.args.root = str(dangerous_root)
        config = self.config()
        runtime = dangerous_root / ".deleguard/cursor/deleguard_ide.py"
        runtime.parent.mkdir(parents=True)
        runtime.write_text("import sys,json; print(json.dumps({'args':sys.argv[1:], 'input':sys.stdin.buffer.read().decode('utf-8-sig')}))")
        command = s.hook_definition(config)["hooks"]["preToolUse"][0]["command"]
        self.assertTrue(command.startswith("& {"))
        self.assertIn("'" + sys.executable.replace("'", "''") + "'", command)
        payload = self.root / "synthetic-event.json"
        payload.write_text('{"tool_name":"Read"}', encoding="utf-8")
        wrapped = "$OutputEncoding=[System.Text.Encoding]::UTF8; Get-Content -LiteralPath '" + str(payload).replace("'", "''") + "' -Raw | & { $input | " + command + " }"
        result = subprocess.run([s.windows_powershell(), "-NoProfile", "-NonInteractive", "-Command", wrapped], capture_output=True, text=True, timeout=10)
        self.assertEqual(0, result.returncode, result.stderr)
        data = json.loads(result.stdout)
        self.assertEqual({"tool_name": "Read"}, json.loads(data["input"]))
        self.assertEqual(["hook", "--config", str(runtime.with_name("config.json"))], data["args"])

    def test_migration_preserves_pending_and_uncertain_journal(self):
        legacy = Path(self.tmp.name) / "legacy"
        legacy.mkdir()
        with sqlite3.connect(legacy / "old.sqlite") as db:
            db.execute("create table operations(id text,state text)")
            db.executemany("insert into operations values(?,?)", [("op1", "pending"), ("op2", "armed"), ("op3", "unknown")])
        db.close()
        class Guard:
            state_root = staticmethod(lambda: legacy)
        files = s.migration_journal(self.config(), "old", Guard)
        data = files[".deleguard/cursor/state.sqlite"]
        db = sqlite3.connect(":memory:")
        try:
            db.deserialize(data)
            self.assertEqual([("op1", "pending"), ("op2", "armed"), ("op3", "unknown")], db.execute("select * from operations").fetchall())
        finally:
            db.close()

    def test_customized_skill_requires_confirmation(self):
        config = self.installed()
        skill = self.root / ".cursor/skills/deleguard-project/SKILL.md"
        skill.write_text("custom rules")
        with self.assertRaises(ValueError):
            s.plan_files(config, PACKAGE)
        self.assertIn(str(skill.relative_to(self.root)).replace("\\", "/"), s.plan_files(config, PACKAGE, replace=True))

    def test_hook_tampering_is_reported_and_not_silently_repaired(self):
        config = self.installed()
        hook = self.root / s.HOOK_FILES["cursor"]
        hook.write_text('{"hooks":{}}')
        with self.assertRaises(ValueError):
            s.integrity(config)
        with self.assertRaises(ValueError):
            s.plan_files(config, PACKAGE)

    def test_modified_runtime_fails_integrity(self):
        config = self.installed()
        (self.root / ".deleguard/cursor/deleguard_ide.py").write_text("tampered")
        with self.assertRaises(ValueError):
            s.integrity(config)

    def test_changed_endpoint_is_detected_and_preserves_unrelated_mcp(self):
        self.args.mcp_url = "https://guard.example/mcp/guard"
        path = self.root / s.MCP_FILES["cursor"]
        path.parent.mkdir()
        other = {"url": "https://other.example/mcp"}
        path.write_text(json.dumps({"mcpServers": {"other": other}}))
        config = self.installed()
        mcp = s.read_json(path)
        self.assertEqual(other, mcp["mcpServers"]["other"])
        _, name, _ = s.mcp_definition(config)
        mcp["mcpServers"][name]["url"] = "https://tampered.example/mcp"
        path.write_text(json.dumps(mcp))
        with self.assertRaises(ValueError):
            s.integrity(config)
        with self.assertRaises(ValueError):
            s.plan_files(config, PACKAGE)

    def test_same_directory_cannot_silently_rebind_another_project(self):
        self.installed()
        self.args.project = "other-project"
        with self.assertRaises(ValueError):
            s.plan_files(self.config(), PACKAGE, replace=True)

    def test_mcp_rejects_http_secrets_queries_fragments_and_control_chars(self):
        for url in ("http://localhost/mcp/guard", "https://user:secret@host/mcp", "https://host/mcp?token=x", "https://host/mcp#x", "https://host/\nmcp"):
            with self.subTest(url=url), self.assertRaises(ValueError):
                s.validate_url(url, mcp=True)

    def test_api_allows_loopback_only_for_http(self):
        self.assertEqual("http://localhost:8082", s.validate_url("http://localhost:8082/"))
        with self.assertRaises(ValueError):
            s.validate_url("http://example.test")

    def test_linked_paths_rejected(self):
        outside = Path(self.tmp.name) / "outside"
        outside.mkdir()
        try:
            (self.root / ".deleguard").symlink_to(outside, target_is_directory=True)
        except OSError:
            self.skipTest("OS symlink privilege unavailable")
        with self.assertRaises(ValueError):
            s.plan_files(self.config(), PACKAGE)
        self.assertFalse(list(outside.iterdir()))

    def test_hard_link_target_rejected(self):
        source = Path(self.tmp.name) / "outside.json"
        source.write_text("{}")
        (self.root / ".cursor").mkdir()
        os.link(source, self.root / ".cursor/hooks.json")
        with self.assertRaises(ValueError):
            s.plan_files(self.config(), PACKAGE)

    def test_moved_configuration_cannot_use_old_identity(self):
        self.installed()
        correct = self.root / ".deleguard/cursor/config.json"
        self.assertEqual("demo", s.load_config(correct)["project"])
        wrong = self.root / "copied.json"
        wrong.write_bytes(correct.read_bytes())
        with self.assertRaises(ValueError):
            s.load_config(wrong)

    def test_atomic_failure_rolls_back_written_files(self):
        (self.root / "first").write_bytes(b"old")
        original_write = s.atomic_write
        def fail(path, data):
            if path.name == "second":
                raise OSError("simulated disk failure")
            original_write(path, data)
        with patch.object(s, "atomic_write", side_effect=fail), self.assertRaises(OSError):
            s.commit_files(self.root, {"first": b"new", "second": b"new"}, ".deleguard/cursor")
        self.assertEqual(b"old", (self.root / "first").read_bytes())
        self.assertFalse((self.root / "second").exists())

    def test_diagnostic_does_not_authorize_consume_or_execute(self):
        config = self.installed()
        class Api:
            def __init__(self, config, token): pass
            def call(self, route):
                self_test.assertEqual("/v1/ide/config/demo", route)
                return {"id": "demo"}
        class Guard:
            credential = staticmethod(lambda _: "dgk_test")
            safe_failure_reason = staticmethod(lambda _: "safe error")
        Guard.Api = Api
        self_test = self
        report = s.doctor(config, Guard)
        self.assertNotEqual("protected", report["status"])
        self.assertTrue(any(c["id"] == "policy" and c["status"] == "unverified" for c in report["checks"]))
        self.assertFalse((self.root / ".deleguard/cursor/state.sqlite").exists())

    def test_install_refuses_invalid_credentials_before_writes(self):
        class Guard:
            @staticmethod
            def Api(config, token):
                raise ValueError("invalid credential")
        with patch.object(s.getpass, "getpass", return_value="invalid"), self.assertRaises(ValueError):
            s.install(self.args, Guard)
        self.assertEqual([], list(self.root.iterdir()))

    def test_reconnect_replaces_only_valid_key_and_preserves_journal(self):
        store = {}
        scope = dict(organization_id="org-a", application_id="app-a", agent_id="agent-a")
        class Api:
            def __init__(self, config, token): pass
            def call(self, route): return dict(scope)
        class Guard:
            @staticmethod
            def credential(key, value=None):
                if value is not None:
                    store[key] = value
                return store[key]
        Guard.Api = Api
        with patch.object(s.getpass, "getpass", return_value="dgk_original"), contextlib.redirect_stdout(io.StringIO()):
            s.install(self.args, Guard)
        journal = self.root / ".deleguard/cursor/state.sqlite"
        journal.write_bytes(b"unchanged journal fixture")
        self.args.reconnect = True
        with patch.object(s.getpass, "getpass", return_value="dgk_replacement"), contextlib.redirect_stdout(io.StringIO()):
            s.install(self.args, Guard)
        self.assertEqual("dgk_replacement", store[self.config()["credential_id"]])
        self.assertEqual(b"unchanged journal fixture", journal.read_bytes())
        scope["organization_id"] = "other-org"
        with patch.object(s.getpass, "getpass", return_value="dgk_wrong_tenant"), self.assertRaises(ValueError):
            s.install(self.args, Guard)
        self.assertEqual("dgk_replacement", store[self.config()["credential_id"]])
        self.assertEqual(b"unchanged journal fixture", journal.read_bytes())


if __name__ == "__main__":
    unittest.main()
