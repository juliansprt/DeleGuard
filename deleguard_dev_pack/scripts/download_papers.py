from pathlib import Path
import json, time, requests
ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'research/papers_manifest.json').read_text(encoding='utf-8'))
outdir=ROOT/'papers'; outdir.mkdir(exist_ok=True)
headers={'User-Agent':'DeleguardResearchPack/1.0 (+research download)'}
for p in manifest:
    out=outdir/p['filename']
    if out.exists() and out.read_bytes()[:5]==b'%PDF-':
        print('SKIP',p['arxiv_id']); continue
    print('DOWNLOAD',p['arxiv_id'])
    last=None
    for attempt in range(4):
        try:
            with requests.get(p['pdf_url'],headers=headers,timeout=90,stream=True,allow_redirects=True) as r:
                r.raise_for_status()
                with open(out,'wb') as f:
                    for chunk in r.iter_content(1024*1024):
                        if chunk: f.write(chunk)
            if out.read_bytes()[:5] != b'%PDF-':
                raise RuntimeError('not a PDF')
            last=None; break
        except Exception as e:
            last=e; time.sleep(2**attempt)
    if last: raise last
print('Done:',len(manifest),'papers')
