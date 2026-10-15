"""No automatic POST retries. Persist operation identity in the application's job record."""
import json
import time
import urllib.request

class UncertainOperation(RuntimeError):
    pass

class Client:
    def __init__(self, base_url, api_key):
        self.base_url, self.api_key = base_url.rstrip("/"), api_key

    def _request(self, path, body=None, key=None):
        headers = {"Authorization": "Bearer " + self.api_key, "Content-Type": "application/json"}
        if key:
            headers["Idempotency-Key"] = key
        request = urllib.request.Request(self.base_url + path, headers=headers,
            data=None if body is None else json.dumps(body).encode("utf-8"))
        with urllib.request.urlopen(request, timeout=30) as response:
            data = response.read()
            return json.loads(data) if data else None

    def authorize(self, request):
        operation = request["operation"]
        return self._request("/v1/authorize", request, "sdk_" + operation["id"])

    def wait(self, decision, seconds=900):
        deadline = time.monotonic() + seconds
        while True:
            decision = self._request("/v1/decisions/" + decision["decision_id"])
            if decision["decision"] != "REQUIRE_APPROVAL":
                return decision
            if time.monotonic() >= deadline:
                raise TimeoutError("Approval is still pending; preserve operation identity.")
            time.sleep(min(2, max(0, deadline - time.monotonic())))

    def execute(self, request, execute, current_snapshot, approval_timeout=900):
        """current_snapshot must re-read trusted business facts, not reuse stale model arguments."""
        decision = self.wait(self.authorize(request), approval_timeout)
        if decision["decision"] != "ALLOW" or not decision.get("permit"):
            raise PermissionError(decision.get("reason", "No live permission"))
        snapshot = current_snapshot()
        if snapshot != decision["resource_snapshot_hash"]:
            raise PermissionError("Resource changed; request a new authorization")
        operation = request["operation"]
        claim = self._request("/v1/permits/" + decision["permit_id"] + "/consume", {
            "operation_id": operation["id"], "operation_hash": operation["hash"], "resource_snapshot_hash": snapshot})
        if not claim["acquired"]:
            raise UncertainOperation("Already consumed; inspect " + claim["execution_id"])
        receipt = {"decision_id": decision["decision_id"], "permit_id": decision["permit_id"],
            "execution_id": claim["execution_id"], "resource_snapshot_hash": snapshot}
        try:
            value = execute()
        except BaseException:
            try:
                self._request("/v1/executions", {**receipt, "status": "UNKNOWN"})
            except Exception:
                pass  # The durable consumed record already says UNKNOWN.
            raise
        try:
            self._request("/v1/executions", {**receipt, "status": "SUCCESS"})
        except Exception as error:
            raise UncertainOperation("Executed, but receipt delivery is uncertain: " + claim["execution_id"]) from error
        return value
