$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location -LiteralPath $repoRoot
if ($env:DELEGUARD_TEST_POSTGRES) { throw 'Unset DELEGUARD_TEST_POSTGRES; acceptance must use disposable databases.' }
foreach ($suite in @('UnitTests', 'SecurityTests', 'IntegrationTests')) {
    dotnet test "tests/Deleguard.$suite/Deleguard.$suite.csproj" --artifacts-path artifacts/v1-acceptance -p:WarningsAsErrors=NU1903%3BNU1904 --logger "trx;LogFileName=v1-$suite.trx" --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "$suite failed with exit code $LASTEXITCODE" }
}
dotnet build sdks/dotnet/Deleguard.Client.csproj --artifacts-path artifacts/v1-acceptance --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }
python -m unittest discover -s integrations/ide -p 'test_*.py'
if ($LASTEXITCODE -ne 0) { throw 'IDE adapter contracts failed.' }
python -m py_compile sdks/python/deleguard_client.py
if ($LASTEXITCODE -ne 0) { throw 'Python client failed.' }
node --experimental-strip-types -e "import('./sdks/typescript/client.ts').then(m => { if (!m.Client) process.exit(1); })"
if ($LASTEXITCODE -ne 0) { throw 'TypeScript client failed.' }
