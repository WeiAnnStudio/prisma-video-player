$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
. (Join-Path $PSScriptRoot 'prepare-test-bin.ps1')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /utf8output /out:tests\bin\EngineTests.exe /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll src\Hardening.cs src\MpvEngine.cs src\Settings.cs tests\EngineTests.cs
if ($LASTEXITCODE -ne 0) { throw 'Error al compilar las pruebas' }
& '.\tests\bin\EngineTests.exe' $projectRoot 2>&1 | Tee-Object -FilePath 'tests\resultado.txt'
if ($LASTEXITCODE -ne 0) { throw 'Han fallado las pruebas' }
