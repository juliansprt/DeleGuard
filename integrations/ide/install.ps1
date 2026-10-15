# Run with Python 3.11+ installed. Arguments are passed as individual tokens.
& python (Join-Path $PSScriptRoot 'deleguard_ide.py') install @args
exit $LASTEXITCODE
