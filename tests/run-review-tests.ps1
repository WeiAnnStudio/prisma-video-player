$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
. (Join-Path $PSScriptRoot 'prepare-test-bin.ps1')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath 'src' -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler /nologo /target:exe /main:ReviewTests /platform:x64 /utf8output /out:tests\bin\ReviewTests.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll @sources tests\ReviewTests.cs
if ($LASTEXITCODE -ne 0) { throw 'Error al compilar las pruebas de revisión' }
& '.\tests\bin\ReviewTests.exe' $projectRoot 2>&1 | Tee-Object -FilePath 'tests\revision.txt'
if ($LASTEXITCODE -ne 0) { throw 'Han fallado las pruebas de revisión' }
