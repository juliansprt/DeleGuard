"""Reproduce Cursor 3.17.19's Windows hook wrapper without a real tool or credential."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import argparse

import project_setup as setup


@unittest.skipUnless(os.name == "nt", "Cursor Windows transport")
class CursorWindowsLauncherTests(unittest.TestCase):
    def test_cursor_powershell_pipeline_returns_json(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / ".deleguard/cursor/deleguard_ide.py"
            runtime.parent.mkdir(parents=True)
            # Native child reads the transport; no API, real workspace, or tool call.
            runtime.write_text("import json,sys; raw=sys.stdin.read(); print(json.dumps({'received':raw, 'permission':'deny'}))", encoding="utf-8")
            payload = root / "cursor-payload.json"
            event = {"hook_event_name": "preToolUse", "tool_name": "UnmappedDiagnostic", "tool_input": {"label": "prueba ñ"}}
            payload.write_text(json.dumps(event, ensure_ascii=False), encoding="utf-8")
            command = setup.hook_definition({"root": str(root), "adapter": "cursor"})["hooks"]["preToolUse"][0]["command"]
            # Taken from installed Cursor's executeHookDirect/mht: file -> UTF8 PS pipeline.
            invoke = "& " + command if command.startswith(('"', "'")) else command
            wrapped = "$OutputEncoding = [System.Text.Encoding]::UTF8; Get-Content -LiteralPath '" + str(payload).replace("'", "''") + "' -Raw | & { $input | " + invoke + " }"
            result = subprocess.run([setup.windows_powershell(), "-NoProfile", "-NonInteractive", "-Command", wrapped],
                                    capture_output=True, timeout=15)
            self.assertEqual(0, result.returncode, result.stderr.decode(errors="replace"))
            self.assertTrue(result.stdout.strip(), "Cursor's pipeline returned no output")
            response = json.loads(result.stdout.decode("utf-8-sig"))
            self.assertEqual("deny", response["permission"])
            self.assertTrue(response["received"], result.stderr.decode(errors="replace"))
            self.assertTrue(response["received"].lstrip("\ufeff").lstrip().startswith("{"), repr(response["received"]) + result.stderr.decode(errors="replace"))
            self.assertEqual(event, json.loads(response["received"].lstrip("\ufeff")))

    def test_real_runtime_accepts_utf8_bom_and_emits_credential_diagnostic(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "project"
            root.mkdir()
            args = argparse.Namespace(root=str(root), url="https://guard.invalid", project="fixture", agent="fixture", environment="test", adapter="cursor", mcp_url=None)
            config = setup.configuration(args)
            files = setup.plan_files(config, Path(__file__).parent)
            setup.commit_files(root, files, ".deleguard/cursor")
            env = dict(os.environ, LOCALAPPDATA=str(Path(directory) / "isolated-credentials"))
            event = {"hook_event_name": "preToolUse", "cursor_version": "3.17.19", "tool_name": "UnmappedDiagnostic", "tool_input": {"label": "ñ 漢字"}}
            runtime = root / ".deleguard/cursor/deleguard_ide.py"
            command = [sys.executable, "-X", "utf8", str(runtime), "hook", "--config", str(runtime.with_name("config.json"))]
            for version, expected in (("3.17.19", "credential_missing"), ("3.18.0", "cursor_transport_unverified")):
                with self.subTest(version=version):
                    event["cursor_version"] = version
                    result = subprocess.run(command, input=b"\xef\xbb\xbf" + json.dumps(event, ensure_ascii=False).encode("utf-8"), env=env, capture_output=True, timeout=10)
                    self.assertEqual(0, result.returncode, result.stderr)
                    response = json.loads(result.stdout)
                    self.assertEqual("deny", response["permission"])
                    self.assertIn(expected, response["user_message"])
            # Invalid UTF-8 is refused before credential/API work, never replacement-decoded.
            result = subprocess.run(command, input=b'{"bad":"\xff"}', env=env, capture_output=True, timeout=10)
            self.assertEqual(2, result.returncode)
            self.assertEqual(b"", result.stdout)


if __name__ == "__main__":
    unittest.main()
