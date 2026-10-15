# Paper PDFs

The verified corpus is listed in `../research/papers_manifest.json` and `PAPER_INDEX.md`.

This environment was able to verify/read the arXiv PDFs but its binary downloader rejected arXiv materialization, so the ZIP contains reproducible download scripts rather than pretending the PDF binaries were embedded.

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File ..\scripts\Download-Papers.ps1
```

Cross-platform:

```bash
python ../scripts/download_papers.py
```

Then:

```bash
python ../scripts/verify_pdfs.py
```
