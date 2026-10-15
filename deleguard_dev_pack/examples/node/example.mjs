import crypto from 'node:crypto';

const payload = {
  agent: { id: 'invoice-agent', environment: 'production' },
  actor: { id: 'usr_481', type: 'human' },
  action: 'invoice.cancel',
  resource: {
    type: 'invoice', id: 'FAC-8392',
    attributes: { amount: 35000000, currency: 'COP', status: 'issued' }
  },
  context: { reason: 'duplicate invoice' }
};

const r = await fetch('https://api.deleguard.example/v1/authorize', {
  method: 'POST',
  headers: {
    'authorization': `Bearer ${process.env.DELEGUARD_TOKEN}`,
    'content-type': 'application/json',
    'idempotency-key': crypto.randomUUID()
  },
  body: JSON.stringify(payload)
});
if (!r.ok) throw new Error(`Deleguard ${r.status}`);
const d = await r.json();
if (d.decision === 'ALLOW') {
  // call existing tool/API
}
