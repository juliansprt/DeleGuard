"""Frozen one-directory entry point. Never extracts a runtime to a global temp folder."""
import sys
sys.dont_write_bytecode = True
for stream in (sys.stdin, sys.stdout, sys.stderr):
    if stream is not None and hasattr(stream, "reconfigure"):
        stream.reconfigure(encoding="utf-8")
if len(sys.argv) > 1 and sys.argv[1] == "portable":
    import portable_setup
    raise SystemExit(portable_setup.main())
# The bundled adapter cannot invoke the legacy global installer or profile store.
if (len(sys.argv) != 4 or sys.argv[1] not in ("hook", "doctor", "flush", "pending")
        or sys.argv[2] != "--config"):
    import json
    print(json.dumps(dict(status="error", error="project_scope_required")))
    raise SystemExit(2)
import deleguard_ide
deleguard_ide.main()
