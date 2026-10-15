# Papers - download status

The research corpus in `research/papers_manifest.json` contains **verified public paper references** (arXiv IDs, titles, categories, abstract URLs and original PDF URLs).

## Important

The current artifact-building environment could verify the public paper pages but could **not materialize the original arXiv PDF binaries** into this package. To avoid redistributing reconstructed or incomplete copies, this starter ZIP intentionally does **not** contain fake/rebuilt PDFs.

To obtain the original public PDFs from arXiv on your machine, run from the package root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Full-Pack.ps1
```

This command:

1. Reads `research/papers_manifest.json`.
2. Downloads each original PDF URL into `papers/`.
3. Verifies that each file begins with a valid PDF signature.
4. Produces `Deleguard_Dev_Starter_With_Papers.zip` containing the documentation and downloaded originals.

Alternative Python flow:

```powershell
python .\scripts\download_papers.py
python .\scripts\verify_pdfs.py
```

## Source and rights

The scripts download the files directly from the public source URLs listed in the manifest. Paper copyrights and licenses remain with their respective authors/publishers. Review the license on each paper before redistribution outside your organization.
