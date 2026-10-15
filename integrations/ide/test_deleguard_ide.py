import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("guard", Path(__file__).with_name("deleguard_ide.py"))
g = importlib.util.module_from_spec(spec)
spec.loader.exec_module(g)

class Api:
    def __init__(self): self.effect="REQUIRE_APPROVAL";self.used=False;self.calls=[];self.offline=False
    def call(self, route, body=None, idem=None):
        self.calls.append((route,body,idem))
        if self.offline: raise OSError("offline")
        if route.startswith("/v1/ide/config/"):return {"share_diff":False}
        if route=="/v1/ide/sessions":return {}
        if route=="/v1/ide/permits/consume":
            acquired=not self.used;self.used=True;return {"acquired":acquired,"already_consumed":not acquired,"execution_id":"exe_test"}
        if route=="/v1/executions":return {}
        return {"decision":self.effect,"decision_id":"dec_test","approval_id":"apr_test","permit":"permit" if self.effect=="ALLOW" else None}

class HookTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name);(self.root/"src").mkdir();(self.root/"src/a.txt").write_text("before")
        self.config={"root":str(self.root),"project":"ide-demo","adapter":"cursor","agent":"ide-agent","environment":"test","url":"https://example.invalid"}
        self.state=g.State(self.root/"local.sqlite");self.api=Api()
        self.event={"hook_event_name":"preToolUse","tool_name":"Read","tool_input":{"file_path":"src/a.txt"},"session_id":"s1","tool_use_id":"t1"}
    def tearDown(self):self.state.db.close();self.tmp.cleanup()
    def run_hook(self):return g.run_hook(self.event,self.config,self.api,self.state)
    def test_pending_then_approved_retry_consumes_once(self):
        self.assertEqual("deny",self.run_hook()["permission"])
        self.api.effect="ALLOW";self.event["tool_use_id"]="t2"
        self.assertEqual("allow",self.run_hook()["permission"])
        self.assertEqual("deny",self.run_hook()["permission"])
        self.assertEqual(1,len([c for c in self.api.calls if c[0]=="/v1/authorize"]))
    def test_deny_never_consumes(self):
        self.api.effect="DENY";self.assertEqual("deny",self.run_hook()["permission"])
        self.assertFalse(self.api.used)
        self.assertEqual("denied",self.state.db.execute("select state from operations").fetchone()[0])
    def test_deny_is_terminal_but_new_explicit_call_is_reauthorized(self):
        self.api.effect="DENY"
        result=self.run_hook()
        self.assertIn("No hay una aprobación pendiente",json.dumps(result,ensure_ascii=False))
        self.assertIn("/audit/decisions/dec_test",json.dumps(result))
        self.assertNotIn("/approvals/",json.dumps(result))
        self.api.effect="REQUIRE_APPROVAL"
        self.assertEqual("deny",self.run_hook()["permission"])
        self.assertEqual(1,len([c for c in self.api.calls if c[0]=="/v1/authorize"]))
        self.assertFalse(any(c[0].startswith("/v1/decisions/") for c in self.api.calls))
        self.event["tool_use_id"]="t2"
        result=self.run_hook()
        self.assertIn("/approvals/apr_test",json.dumps(result))
        self.assertEqual(2,len([c for c in self.api.calls if c[0]=="/v1/authorize"]))
        self.assertEqual(["denied","pending"],[r[0] for r in self.state.db.execute("select state from operations order by created")])
        self.assertFalse(self.api.used)
    def test_legacy_pending_deny_is_preserved_not_coalesced(self):
        self.api.effect="DENY";self.run_hook()
        self.state.db.execute("update operations set state='pending'");self.state.db.commit()
        self.api.effect="REQUIRE_APPROVAL";self.event["tool_use_id"]="t2"
        self.run_hook()
        self.assertEqual(2,len([c for c in self.api.calls if c[0]=="/v1/authorize"]))
        self.assertEqual("denied",self.state.db.execute("select state from operations where call_id='t1'").fetchone()[0])
    def test_missing_policy_message_has_safe_next_step(self):
        text=g.blocked_decision_message(self.config,{"decision":"DENY","reason":"No applicable policy.","decision_id":"dec_x"})
        self.assertIn("política publicada",text)
        self.assertIn("High clasifica",text)
        self.assertNotIn("Después de aprobar",text)
        self.assertNotIn("TOP_SECRET",g.blocked_decision_message(self.config,{"decision":"DENY","reason":"TOP_SECRET"}))
    def test_approval_without_id_or_unknown_effect_never_suggests_approval(self):
        for effect in ("REQUIRE_APPROVAL","unexpected",None):
            text=g.blocked_decision_message(self.config,{"decision":effect})
            self.assertIn("No hay una aprobación pendiente",text)
            self.assertNotIn("/approvals/",text)
    def test_changed_tenant_credential_never_posts(self):
        self.config["server_scope"]={"organization_id":"org-a","application_id":"app-a","agent_id":"agent-a"}
        with self.assertRaises(g.AdapterError):self.run_hook()
        self.assertEqual(["/v1/ide/config/ide-demo"],[c[0] for c in self.api.calls])
        self.assertFalse(self.api.used)
    def test_changed_content_cannot_reuse_tool_id(self):
        self.run_hook();(self.root/"src/a.txt").write_text("changed")
        self.api.effect="ALLOW"
        with self.assertRaises(g.AdapterError) as raised:self.run_hook()
        self.assertEqual("tool_call_reused", raised.exception.code)
    def test_host_reuses_call_id_after_completed_read_authorizes_write(self):
        self.api.effect="ALLOW"
        self.event["tool_use_id"]="call-1\nfc_0"
        self.assertEqual("allow",self.run_hook()["permission"])
        g.run_hook(dict(self.event,hook_event_name="postToolUseFailure"),self.config,self.api,self.state)
        self.api.used=False
        self.event.update(tool_name="Write",tool_input={"file_path":"src/prueba.txt","content":"archivo de prueba\n"})
        self.assertEqual("allow",self.run_hook()["permission"])
        actions=[c[1]["action"] for c in self.api.calls if c[0]=="/v1/authorize"]
        self.assertEqual(["filesystem.read","filesystem.create"],actions)
        rows=self.state.db.execute("select state,call_id from operations order by created").fetchall()
        self.assertEqual("completed",rows[0][0])
        self.assertIn(":retired:",rows[0][1])
        self.assertEqual(("armed","call-1\nfc_0"),(rows[1][0],rows[1][1]))
        g.run_hook(dict(self.event,hook_event_name="postToolUse"),self.config,self.api,self.state)
        self.assertEqual(["completed","completed"],[r[0] for r in self.state.db.execute("select state from operations order by created")])
    def test_denied_call_id_does_not_block_later_distinct_tool(self):
        self.api.effect="DENY";self.run_hook()
        self.api.effect="ALLOW"
        self.event.update(tool_name="Write",tool_input={"file_path":"src/nuevo.txt","content":"ok"})
        self.assertEqual("allow",self.run_hook()["permission"])
        self.assertEqual(2,len([c for c in self.api.calls if c[0]=="/v1/authorize"]))
    def test_restart_after_release_blocks_replay(self):
        self.api.effect="ALLOW";self.run_hook();self.state.db.close();self.state=g.State(self.root/"local.sqlite")
        self.assertEqual("deny",self.run_hook()["permission"])
    def test_evidence_only_after_release_and_no_content(self):
        self.api.effect="ALLOW";self.run_hook()
        event=dict(self.event,hook_event_name="postToolUse",tool_response={"content":"TOP_SECRET"})
        g.run_hook(event,self.config,self.api,self.state)
        evidence=[c[1] for c in self.api.calls if c[0]=="/v1/executions"]
        self.assertEqual(1,len(evidence));self.assertNotIn("TOP_SECRET",json.dumps(evidence))
        self.assertNotIn("before",json.dumps(self.api.calls))
    def test_post_denied_does_not_claim_execution(self):
        self.run_hook();g.run_hook(dict(self.event,hook_event_name="postToolUseFailure"),self.config,self.api,self.state)
        self.assertFalse(any(c[0]=="/v1/executions" for c in self.api.calls))
    def test_unsupported_tool_and_missing_call_fail_closed(self):
        self.event["tool_name"]="unknown"
        with self.assertRaises(ValueError):self.run_hook()
        self.event.pop("tool_use_id")
        with self.assertRaises(ValueError):self.run_hook()
    def test_paths_escape_network_aliases(self):
        for raw in ("../escape", "//server/share", "src/a.txt:stream", "src/../..", "src/a.txt "):
            with self.subTest(raw=raw),self.assertRaises(ValueError):g.safe_path(self.root,raw)
    def test_links_rejected(self):
        try:(self.root/"link").symlink_to(self.root/"src",target_is_directory=True)
        except OSError:self.skipTest("symlink privileges unavailable")
        with self.assertRaises(ValueError):g.safe_path(self.root,"link/a.txt")
    def test_move_binds_both_paths(self):
        self.event.update(tool_name="Move",tool_input={"path":"src/a.txt","destination":"src/b.txt"})
        action,attrs,_=g.normalize(self.event,self.config)
        self.assertEqual("filesystem.move",action);self.assertEqual("src/b.txt",attrs["destination_path"])
    def test_mcp_tool_identity_is_bound(self):
        self.event.update(tool_name="mcp__safe",tool_input={"x":1})
        first=g.normalize(self.event,self.config)[2]
        self.event["tool_name"]="mcp__dangerous"
        second=g.normalize(self.event,self.config)[2]
        self.assertNotEqual(first,second)
    def test_contracts(self):
        for adapter in ("claude","vscode"):
            result=g.response(adapter,False,"denied")
            self.assertEqual("deny",result["hookSpecificOutput"]["permissionDecision"])
    def test_offline_never_releases(self):
        self.api.offline=True
        with self.assertRaises(OSError):self.run_hook()
        self.assertFalse(self.api.used)
    def test_duplicate_server_consumption_is_unknown(self):
        self.api.effect="ALLOW";self.api.used=True
        self.assertEqual("deny",self.run_hook()["permission"])
        self.assertEqual("unknown",self.state.db.execute("select state from operations").fetchone()[0])
    def test_credential_transport(self):
        for url in ("http://remote.invalid","https://user:pass@example.invalid","https://example.invalid?token=x"):
            with self.assertRaises(ValueError):g.Api({"url":url},"dgk_test")

if __name__=="__main__":unittest.main()
