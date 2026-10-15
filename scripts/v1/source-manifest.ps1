$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$extensions = @('.cs','.csproj','.props','.targets','.sql','.sln','.slnx','.json','.yaml','.yml','.md','.py','.ts','.tsx','.css','.cshtml','.ps1','.bicep','.toml','.sh','.html','.csv')
$files = foreach ($directory in @('src','tests','sdks','policies','integrations','deleguard_dev_pack/spec','docs','scripts','.github','infra')) {
    $path = Join-Path $repoRoot $directory
    if (Test-Path -LiteralPath $path) {
        Get-ChildItem -LiteralPath $path -Recurse -File | Where-Object {
            $extensions -contains $_.Extension -and $_.FullName -notmatch '[\\/](bin|obj|TestResults|node_modules|__pycache__|dpkeys)[\\/]' -and
            $_.Name -notmatch 'secret|credential|localsettings|appsettings\.Local'
        }
    }
}
$files = @($files) + @(Get-ChildItem -LiteralPath $repoRoot -File | Where-Object {
    $_.Name -in @('Deleguard.slnx', 'global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'NuGet.Config', 'README.md')
})
$entries = @($files | Sort-Object FullName | ForEach-Object {
    @{ path = [IO.Path]::GetRelativePath($repoRoot, $_.FullName).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$output = Join-Path $repoRoot 'artifacts/source-manifest.json'
New-Item -ItemType Directory -Path (Split-Path $output) -Force | Out-Null
$sourceText = ($entries | ForEach-Object { $_.path + ':' + $_.sha256 }) -join "`n"
$sourceRevision = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sourceText))).ToLowerInvariant()
@{ source_revision = $sourceRevision; created_at = [DateTimeOffset]::UtcNow.ToString('o'); files = $entries; count = $entries.Count } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $output -Encoding utf8
Write-Output "Source revision: $sourceRevision"
Get-FileHash -LiteralPath $output -Algorithm SHA256 | Select-Object Hash, Path
