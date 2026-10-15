param([string]$OutputZip = "")
$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'Download-Papers.ps1')
python (Join-Path $PSScriptRoot 'verify_pdfs.py')
if ($LASTEXITCODE -ne 0) { throw 'PDF verification failed' }
if ([string]::IsNullOrWhiteSpace($OutputZip)) {
    $OutputZip=Join-Path (Split-Path $Root -Parent) 'Deleguard_Dev_Starter_With_Papers.zip'
}
if (Test-Path $OutputZip) { Remove-Item $OutputZip -Force }
Compress-Archive -Path (Join-Path $Root '*') -DestinationPath $OutputZip -CompressionLevel Optimal
Write-Host "Created: $OutputZip"
