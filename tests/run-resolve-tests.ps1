$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
# Runs the Resolve marker script against a simulated timeline with Resolve's own Lua (fuscript).
$fuscript = Join-Path $env:ProgramFiles 'Blackmagic Design\DaVinci Resolve\fuscript.exe'
if (-not (Test-Path -LiteralPath $fuscript)) { throw 'No se encuentra fuscript.exe de DaVinci Resolve.' }
& $fuscript 'tests\ResolveScriptTests.lua' 2>&1 | Tee-Object -FilePath 'tests\resolve-lua.txt'
if ($LASTEXITCODE -ne 0) { throw 'Han fallado las pruebas del script de Resolve' }
