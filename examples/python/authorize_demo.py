"""Vertical-slice client for Deleguard running at localhost:8080."""
import hashlib
import json
import os
import uuid
import urllib.request

BASE = os.environ.get("DELEGUARD_URL", "http://localhost:8080")
TOKEN = os.environ.get("DELEGUARD_TOKEN", "dgk_demo_invoice_agent_local_dev_only")
CONSOLE = os.environ.get("DELEGUARD_CONSOLE", "dgc_demo_console_local_dev_only")


def authorize(resource_id, status, amount, reported=False):
    body = {
        "agent": {"id": "invoice-agent", "environment": "production"},
        "actor": {"id": "usr_481", "type": "human"},
        "action": "invoice.cancel",
        "resource": {
            "type": "invoice",
            "id": resource_id,
            "attributes": {
                "amount": amount,
                "currency": "COP",
                "status": status,
                "reportedToTaxAuthority": reported,
            },
        },
        "context": {"reason": "demo"},
    }
    # Decision-only demo. This script never executes or consumes the action.
    operation_id = uuid.uuid4().hex
    body["operation"] = {"id": operation_id, "hash": hashlib.sha256(
        json.dumps(body, sort_keys=True, separators=(",", ":")).encode()).hexdigest()}
    req = urllib.request.Request(
        f"{BASE}/v1/authorize",
        data=json.dumps(body).encode(),
        headers={
            "Authorization": f"Bearer {TOKEN}",
            "Content-Type": "application/json",
            "Idempotency-Key": operation_id,
        },
        method="POST",
    )
    with urllib.request.urlopen(req) as resp:
        return json.loads(resp.read().decode())


def main():
    print("draft ALLOW", authorize("FAC-DRAFT", "draft", 5_000_000))
    print("high-value", authorize("FAC-8392", "issued", 35_000_000))
    print("tax DENY", authorize("FAC-TAX", "issued", 1_000, True))


if __name__ == "__main__":
    main()
