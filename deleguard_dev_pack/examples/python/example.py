import os, uuid, requests

payload = {
    "agent": {"id": "invoice-agent", "environment": "production"},
    "actor": {"id": "usr_481", "type": "human"},
    "action": "invoice.cancel",
    "resource": {
        "type": "invoice",
        "id": "FAC-8392",
        "attributes": {"amount": 35000000, "currency": "COP", "status": "issued"}
    },
    "context": {"reason": "duplicate invoice"}
}

r = requests.post(
    "https://api.deleguard.example/v1/authorize",
    json=payload,
    headers={
        "Authorization": f"Bearer {os.environ['DELEGUARD_TOKEN']}",
        "Idempotency-Key": uuid.uuid4().hex,
    },
    timeout=5,
)
r.raise_for_status()
decision = r.json()
if decision["decision"] == "ALLOW":
    pass  # execute existing business action
