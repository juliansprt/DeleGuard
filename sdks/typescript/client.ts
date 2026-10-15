export type Operation = { id: string; hash: string };
export type AuthorizationRequest = { operation: Operation; action: string; resource: Record<string, unknown>; agent?: Record<string, string>; context?: Record<string, unknown> };
export class UncertainOperation extends Error {}
export class Client {
  private baseUrl: string;
  private apiKey: string;
  constructor(baseUrl: string, apiKey: string) { this.baseUrl = baseUrl; this.apiKey = apiKey; }
  private async request(path: string, body?: unknown, key?: string): Promise<any> {
    const response = await fetch(this.baseUrl.replace(/\/$/, "") + path, {
      method: body === undefined ? "GET" : "POST", signal: AbortSignal.timeout(30000),
      headers: { Authorization: `Bearer ${this.apiKey}`, "Content-Type": "application/json", ...(key ? { "Idempotency-Key": key } : {}) },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (!response.ok) throw new Error(`DeleGuard rejected request: ${response.status}`);
    const text = await response.text(); return text ? JSON.parse(text) : undefined;
  }
  authorize(request: AuthorizationRequest) { return this.request("/v1/authorize", request, "sdk_" + request.operation.id); }
  async wait(decision: any, timeoutMs = 900000) {
    const deadline = Date.now() + timeoutMs;
    for (;;) {
      decision = await this.request("/v1/decisions/" + encodeURIComponent(decision.decision_id));
      if (decision.decision !== "REQUIRE_APPROVAL") return decision;
      if (Date.now() >= deadline) throw new Error("Approval pending; retain operation identity.");
      await new Promise(resolve => setTimeout(resolve, 2000));
    }
  }
  async execute<T>(request: AuthorizationRequest, run: () => Promise<T>, currentSnapshot: () => Promise<string>, timeoutMs = 900000): Promise<T> {
    const decision = await this.wait(await this.authorize(request), timeoutMs);
    if (decision.decision !== "ALLOW" || !decision.permit) throw new Error(decision.reason ?? "No live permit");
    const snapshot = await currentSnapshot();
    if (snapshot !== decision.resource_snapshot_hash) throw new Error("Resource changed; reauthorize.");
    const claim = await this.request(`/v1/permits/${encodeURIComponent(decision.permit_id)}/consume`, {
      operation_id: request.operation.id, operation_hash: request.operation.hash, resource_snapshot_hash: snapshot,
    });
    if (!claim.acquired) throw new UncertainOperation(`Already consumed: ${claim.execution_id}`);
    const receipt = { decision_id: decision.decision_id, permit_id: decision.permit_id, execution_id: claim.execution_id, resource_snapshot_hash: snapshot };
    let value: T;
    try { value = await run(); }
    catch (error) { try { await this.request("/v1/executions", { ...receipt, status: "UNKNOWN" }); } catch {} throw error; }
    try { await this.request("/v1/executions", { ...receipt, status: "SUCCESS" }); }
    catch { throw new UncertainOperation(`Executed; evidence delivery uncertain: ${claim.execution_id}`); }
    return value;
  }
}
