import json
import os
from pathlib import Path
import shutil
import sqlite3
from contextlib import closing
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import portable_setup as p
import project_setup as s

class Guard:
    @staticmethod
    def valid_agent_key(token):
        return isinstance(token, str) and token.startswith("dgk_")
    class Api:
        def __init__(self, config, token):
            self.config, self.token = config, token
        def call(self, route):
            scope = dict(self.config["server_scope"])
            if self.token == "dgk_other_account":
                scope["organization_id"] = "other-account"
            return dict(scope, id=self.config["project"],agent_external_id=self.config["agent"],environment=self.config["environment"],case_sensitive=os.path.normcase("A") != os.path.normcase("a"))

class PortableSetupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.base = Path(self.temp.name)
        self.root = self.base / "project a";self.root.mkdir()
        self.runtime = self.base / "package";self.runtime.mkdir()
        (self.runtime / ("Deleguard.Desktop.exe" if os.name == "nt" else "Deleguard.Desktop")).write_bytes(b"native-runtime")
        if os.name != "nt":(self.runtime / ".deleguard-host").write_bytes(b"managed-apphost")
        (self.runtime / "worker").mkdir()
        (self.runtime / "worker" / ("deleguard-adapter.exe" if os.name == "nt" else "deleguard-adapter")).write_bytes(b"adapter-runtime")
        self.profile = dict(schema_version=1,server_url="https://example.test",project_id="project-a",agent_id="agent-a",agent_external_id="assistant",
                            organization_id="org-a",application_id="app-a",environment="development",adapter="cursor")
    def tearDown(self): self.temp.cleanup()
    def request(self, operation="install", root=None, profile=None):
        request = dict(operation=operation,root=str(root or self.root),runtime=str(self.runtime),profile=profile or self.profile,adapter="cursor",key="dgk_project_key")
        request["vault"] = json.dumps(dict(Version=1,Identity=p.configuration(request)["credential_id"],Ciphertext="encrypted"))
        return request
    def test_frozen_runtime_cannot_access_legacy_global_state_or_credentials(self):
        import deleguard_ide as guard
        with patch.object(guard.sys,"frozen",True,create=True),patch.object(guard,"credential_command",side_effect=AssertionError("External store accessed")):
            for operation in (guard.state_root,lambda:guard.credential("legacy"),lambda:guard.credential("legacy","dgk_test")):
                with self.assertRaisesRegex(guard.AdapterError,"project|vault"):operation()
    def test_preview_has_no_project_or_global_writes(self):
        before = set(self.base.rglob("*"))
        result = p.install(self.request("preview"), Guard)
        self.assertFalse(result["changes_applied"])
        self.assertEqual(before, set(self.base.rglob("*")))
        self.assertEqual("project-vault", result["config"]["credential_storage"])
    def test_remote_project_agent_and_environment_must_match_before_saving_credentials(self):
        config=p.configuration(self.request())
        valid=dict(config["server_scope"],id=config["project"],agent_external_id=config["agent"],environment=config["environment"])
        for field in ("id","agent_external_id","environment"):
            with self.subTest(field=field),patch.object(Guard.Api,"call",return_value=dict(valid,**{field:"other"})):
                with self.assertRaisesRegex(p.SetupError,"account_mismatch"):p.install(self.request(),Guard)
                self.assertFalse((self.root/".deleguard/cursor/credential.vault").exists())
                self.assertFalse((self.root/".cursor/hooks.json").exists())
    def test_multiple_accounts_and_projects_are_independent(self):
        first = p.install(self.request(), Guard)
        other = self.base / "project b";other.mkdir()
        profile = dict(self.profile, organization_id="org-b",application_id="app-b",project_id="project-b",agent_id="agent-b")
        second = p.install(self.request(root=other,profile=profile), Guard)
        self.assertNotEqual(first["config"]["credential_id"],second["config"]["credential_id"])
        snapshot = {str(f.relative_to(other)):f.read_bytes() for f in other.rglob("*") if f.is_file()}
        p.install(self.request(),Guard)
        self.assertEqual(snapshot,{str(f.relative_to(other)):f.read_bytes() for f in other.rglob("*") if f.is_file()})
        self.assertFalse(any("dgk_project_key" in f.read_text(errors="ignore") for f in self.root.rglob("*") if f.is_file()))
    def test_repeated_install_keeps_journal_and_deduplicates_hooks(self):
        p.install(self.request(),Guard)
        journal=self.root/".deleguard/cursor/state.sqlite";journal.write_bytes(b"operation-history")
        hooks=json.loads((self.root/".cursor/hooks.json").read_text())
        hooks["hooks"]["preToolUse"].append(dict(command="unrelated-hook"))
        (self.root/".cursor/hooks.json").write_text(json.dumps(hooks))
        p.install(self.request(),Guard)
        current=json.loads((self.root/".cursor/hooks.json").read_text())
        self.assertEqual(2,len(current["hooks"]["preToolUse"]))
        self.assertEqual(b"operation-history",journal.read_bytes())
    def test_rejected_key_cannot_modify_existing_installation(self):
        p.install(self.request(),Guard)
        snapshot={str(f.relative_to(self.root)):f.read_bytes() for f in self.root.rglob("*") if f.is_file()}
        request=self.request();request["key"]="dgk_other_account"
        with self.assertRaisesRegex(p.SetupError,"account_mismatch"):p.install(request,Guard)
        self.assertEqual(snapshot,{str(f.relative_to(self.root)):f.read_bytes() for f in self.root.rglob("*") if f.is_file()})
    def test_cannot_rebind_an_installed_folder_to_another_account(self):
        p.install(self.request(),Guard)
        with self.assertRaises(ValueError):p.install(self.request(profile=dict(self.profile,organization_id="other")),Guard)
    def test_copied_configuration_is_rejected(self):
        p.install(self.request(),Guard)
        other=self.base/"copied";shutil.copytree(self.root,other)
        with self.assertRaises(ValueError):s.load_config(other/".deleguard/cursor/config.json")
    def test_customized_runtime_is_not_overwritten(self):
        p.install(self.request(),Guard)
        binary=self.root/".deleguard/cursor/runtime"/("Deleguard.Desktop.exe" if os.name=="nt" else "Deleguard.Desktop")
        binary.write_bytes(b"customized")
        with self.assertRaisesRegex(p.SetupError,"customized_files"):p.install(self.request(),Guard)
        self.assertEqual(b"customized",binary.read_bytes())
    def test_tracked_deleguard_files_block_credentials(self):
        if not shutil.which("git"):self.skipTest("git unavailable")
        subprocess.run(["git","init",str(self.root)],capture_output=True,check=True)
        folder=self.root/".deleguard";folder.mkdir();(folder/"tracked.txt").write_text("tracked")
        subprocess.run(["git","-C",str(self.root),"add",".deleguard/tracked.txt"],capture_output=True,check=True)
        with self.assertRaisesRegex(p.SetupError,"tracked_credentials"):p.install(self.request(),Guard)
        self.assertFalse((folder/"cursor/credential.vault").exists())
    def test_disconnect_removes_only_owned_hooks_and_keeps_other_project(self):
        result=p.install(self.request(),Guard)
        hooks_path=self.root/".cursor/hooks.json";hooks=json.loads(hooks_path.read_text());hooks["hooks"]["preToolUse"].append(dict(command="other-tool"));hooks_path.write_text(json.dumps(hooks))
        journal=self.root/".deleguard/cursor/state.sqlite"
        with sqlite3.connect(journal) as db:
            db.execute("create table history (value text)");db.execute("insert into history values ('preserved')")
        db.close()
        disconnected=p.disconnect(dict(config_path=str(self.root/".deleguard/cursor/config.json")))
        self.assertEqual([dict(command="other-tool")],json.loads(hooks_path.read_text())["hooks"]["preToolUse"])
        self.assertFalse((self.root/".deleguard/cursor/credential.vault").exists())
        with closing(sqlite3.connect(self.root/disconnected["history"]/"state.sqlite")) as db:
            self.assertEqual("preserved",db.execute("select value from history").fetchone()[0])
    def test_installation_lock_prevents_concurrent_changes(self):
        with p.installation_lock(self.root):
            with self.assertRaisesRegex(p.SetupError,"installation_busy"):
                with p.installation_lock(self.root):pass
    def test_parallel_hooks_share_lock_but_block_setup(self):
        with p.installation_lock(self.root,shared=True):
            with p.installation_lock(self.root,shared=True):
                with self.assertRaisesRegex(p.SetupError,"installation_busy"):
                    with p.installation_lock(self.root):pass
    def test_manifest_cannot_claim_foreign_files_or_hooks(self):
        p.install(self.request(),Guard)
        path=self.root/".deleguard/cursor/manifest.json";original=json.loads(path.read_text())
        for name in ("../outside.txt","/outside.txt","C:outside.txt",".cursor/custom.txt"):
            manifest=dict(original,files=dict(original["files"],**{name:"hash"}));path.write_text(json.dumps(manifest))
            with self.assertRaises(ValueError):p.disconnect(dict(config_path=str(self.root/".deleguard/cursor/config.json")))
            self.assertTrue((self.root/".deleguard/cursor/credential.vault").exists())
        manifest=json.loads(json.dumps(original));manifest["hook_definition"]["hooks"]["preToolUse"]=[dict(command="foreign-tool")];path.write_text(json.dumps(manifest))
        with self.assertRaisesRegex(p.SetupError,"invalid_manifest"):p.install(self.request(),Guard)
    def test_legacy_hooks_validate_using_original_python_not_frozen_launcher(self):
        request=self.request();config=p.configuration(request);config["schema_version"]=1;config.pop("credential_storage");config["credential_id"]=s.identity(config)
        files=s.plan_files(config,Path(s.__file__).parent)
        s.commit_files(self.root,files,".deleguard/cursor")
        with patch.object(s.sys,"executable",str(self.runtime/"worker/deleguard-adapter.exe")):
            result=p.install(self.request("preview"),Guard)
        self.assertTrue(result["migration"])
        self.assertFalse(result["changes_applied"])
    def test_after_event_is_retained_before_sqlite_is_available(self):
        import deleguard_ide as guard
        config=p.install(self.request(),Guard)["config"]
        journal=self.root/".deleguard/cursor/state.sqlite";state=guard.State(journal)
        try:
            state.db.execute("insert into operations values(?,?,?,?,?,?,?,?,?)",("operation","session","fingerprint","call","snapshot",json.dumps(dict(decision_id="decision")),"armed",1,None));state.db.commit()
            guard.queue_completion(dict(hook_event_name="postToolUse",session_id="session",tool_use_id="call",tool_output="sensitive output"),config)
            receipt=next((journal.parent/"pending-results").glob("*.json"))
            self.assertNotIn("sensitive",receipt.read_text())
            state.recover_results()
            saved=state.db.execute("select evidence,state from operations").fetchone()
            self.assertEqual("completed",saved["state"]);self.assertEqual("decision",json.loads(saved["evidence"])["decision_id"])
            self.assertFalse(receipt.exists())
        finally:state.db.close()
    def test_two_hooks_recover_the_same_receipt_without_spurious_failure(self):
        import deleguard_ide as guard
        import threading
        from concurrent.futures import ThreadPoolExecutor
        config=p.install(self.request(),Guard)["config"]
        journal=self.root/".deleguard/cursor/state.sqlite";state=guard.State(journal)
        state.db.execute("insert into operations values(?,?,?,?,?,?,?,?,?)",("operation","session","fingerprint","call","snapshot",json.dumps(dict(decision_id="decision")),"armed",1,None));state.db.commit();state.db.close()
        guard.queue_completion(dict(hook_event_name="postToolUse",session_id="session",tool_use_id="call"),config)
        barrier=threading.Barrier(2);original=Path.read_text
        def simultaneous_read(path,*args,**kwargs):
            value=original(path,*args,**kwargs)
            if path.parent.name=="pending-results":barrier.wait(timeout=10)
            return value
        def recover():
            local=guard.State(journal)
            try:local.recover_results()
            finally:local.db.close()
        with patch.object(Path,"read_text",simultaneous_read), ThreadPoolExecutor(max_workers=2) as pool:
            tasks=[pool.submit(recover) for _ in range(2)]
            for task in tasks:task.result(timeout=15)
        with closing(sqlite3.connect(journal)) as db:
            self.assertEqual(1,db.execute("select count(*) from operations where state='completed' and evidence is not null").fetchone()[0])

if __name__=="__main__":unittest.main()
