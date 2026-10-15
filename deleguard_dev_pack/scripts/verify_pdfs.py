from pathlib import Path
import json, hashlib
ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'research/papers_manifest.json').read_text(encoding='utf-8'))
missing=[]; invalid=[]
rows=[]
for p in manifest:
    f=ROOT/'papers'/p['filename']
    if not f.exists(): missing.append(p['arxiv_id']); continue
    data=f.read_bytes()
    if not data.startswith(b'%PDF-'): invalid.append(p['arxiv_id']); continue
    rows.append((p['arxiv_id'],len(data),hashlib.sha256(data).hexdigest()))
print(f'valid={len(rows)} missing={len(missing)} invalid={len(invalid)}')
for rid,size,sha in rows: print(f'{rid}\t{size}\t{sha}')
if missing: print('MISSING:',', '.join(missing))
if invalid: print('INVALID:',', '.join(invalid))
raise SystemExit(1 if missing or invalid else 0)
