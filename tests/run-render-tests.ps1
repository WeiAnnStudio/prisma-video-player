$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
. (Join-Path $PSScriptRoot 'prepare-test-bin.ps1')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /utf8output /out:tests\bin\RenderTests.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll src\Hardening.cs src\MpvEngine.cs tests\RenderTests.cs
if ($LASTEXITCODE -ne 0) { throw 'No compila la prueba del renderizador' }
& '.\tests\bin\RenderTests.exe' $projectRoot 2>&1 | Tee-Object -FilePath 'tests\renderizado.txt'
if ($LASTEXITCODE -ne 0) { throw 'Fallo de renderizado' }
