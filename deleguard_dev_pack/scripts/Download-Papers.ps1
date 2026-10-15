param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\papers')
)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ManifestPath = Join-Path $Root 'research\papers_manifest.json'
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$papers = Get-Content -Raw -Encoding UTF8 $ManifestPath | ConvertFrom-Json
$headers = @{ 'User-Agent' = 'DeleguardResearchPack/1.0 (+research download)' }

foreach ($p in $papers) {
    $out = Join-Path $Destination $p.filename
    if (Test-Path $out) {
        $bytes = [System.IO.File]::ReadAllBytes($out)
        if ($bytes.Length -gt 4 -and [System.Text.Encoding]::ASCII.GetString($bytes,0,5) -eq '%PDF-') {
            Write-Host "SKIP $($p.arxiv_id) already exists"
            continue
        }
    }

    Write-Host "DOWNLOAD $($p.arxiv_id) -> $($p.filename)"
    $ok = $false
    for ($attempt=1; $attempt -le 4 -and -not $ok; $attempt++) {
        try {
            Invoke-WebRequest -Uri $p.pdf_url -OutFile $out -Headers $headers -MaximumRedirection 5
            $bytes = [System.IO.File]::ReadAllBytes($out)
            if ($bytes.Length -lt 5 -or [System.Text.Encoding]::ASCII.GetString($bytes,0,5) -ne '%PDF-') {
                throw 'Downloaded file is not a PDF'
            }
            $ok = $true
        } catch {
            Write-Warning "Attempt $attempt failed: $($_.Exception.Message)"
            if ($attempt -eq 4) { throw }
            Start-Sleep -Seconds ([Math]::Pow(2,$attempt))
        }
    }
}

Write-Host "Downloaded $($papers.Count) paper PDFs. Run verify_pdfs.py next."
