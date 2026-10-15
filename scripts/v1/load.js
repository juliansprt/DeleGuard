// Run only against a disposable validation environment. k6 is an external prerequisite.
import http from 'k6/http';
import { check } from 'k6';
import exec from 'k6/execution';
import { Trend, Rate } from 'k6/metrics';

const tenants = JSON.parse(__ENV.DG_LOAD_TENANTS_JSON || '[]');
if (__ENV.DG_LOAD_ACK !== 'validation-only' || tenants.length !== 5)
  throw new Error('Supply DG_LOAD_ACK=validation-only and exactly five distinct validation tenants.');
if (new Set(tenants.map(t => t.organization_id)).size !== 5 || tenants.some(t => !t.api_key || !t.request?.resource))
  throw new Error('Each tenant needs a distinct organization_id, api_key and known-ALLOW request fixture.');
const base = (__ENV.DG_LOAD_URL || '').replace(/\/$/, '');
if (!base) throw new Error('DG_LOAD_URL is required. Never use a customer environment.');

const serverMs = new Trend('authorization_server_ms', true);
const missingTiming = new Rate('missing_server_timing');
export const options = {
  scenarios: { authorization: { executor: 'constant-arrival-rate', rate: 50, timeUnit: '1s', duration: '15m', preAllocatedVUs: 100, maxVUs: 500 } },
  thresholds: {
    authorization_server_ms: ['p(95)<100'], missing_server_timing: ['rate==0'],
    http_req_failed: ['rate<0.01'], checks: ['rate==1'], dropped_iterations: ['count==0'],
  },
};

export default function () {
  const index = exec.scenario.iterationInTest;
  const tenant = tenants[index % tenants.length];
  const request = JSON.parse(JSON.stringify(tenant.request));
  request.operation = { id: `load-${exec.instance.currentTestRunDuration}-${__VU}-${index}`, hash: 'a'.repeat(64) };
  // Synthetic operations only authorize; this harness never consumes or executes a tool.
  const result = http.post(base + '/v1/authorize', JSON.stringify(request), {
    headers: { Authorization: `Bearer ${tenant.api_key}`, 'Content-Type': 'application/json', 'Idempotency-Key': request.operation.id },
    tags: { tenant: String(index % tenants.length), name: 'POST /v1/authorize' }, timeout: '10s', redirects: 0,
  });
  check(result, { 'authorization succeeds': r => r.status === 200 && r.json('decision') === 'ALLOW' });
  const timing = result.headers['Server-Timing']?.match(/(?:^|,)\s*authorization;dur=([\d.]+)/);
  missingTiming.add(!timing);
  if (timing) serverMs.add(Number(timing[1]));
}

export function handleSummary(data) {
  return { 'artifacts/load-summary.json': JSON.stringify({ recorded_at: new Date().toISOString(), ...data }, null, 2) };
}
