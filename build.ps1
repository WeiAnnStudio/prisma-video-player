param([string]$OutputDirectory = 'Prisma')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Se necesita .NET Framework 4.8, incluido en Windows 10/11.' }
if (-not (Test-Path -LiteralPath 'vendor\libmpv-2.dll')) { & (Join-Path $PSScriptRoot 'tools\prepare-engine.ps1') }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$sources = @(Get-ChildItem -LiteralPath 'src' -Filter '*.cs' | ForEach-Object { $_.FullName })
$compilerArgs = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', ('/out:' + (Join-Path $OutputDirectory 'Prisma.exe')), '/win32manifest:src\app.manifest', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Xml.dll', '/reference:System.Xml.Linq.dll')
if (Test-Path -LiteralPath 'Prisma\Prisma.ico') { $compilerArgs += '/win32icon:Prisma\Prisma.ico' }
& $compiler @compilerArgs @sources
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar Prisma.' }
Copy-Item -LiteralPath 'vendor\libmpv-2.dll' -Destination (Join-Path $OutputDirectory 'libmpv-2.dll') -Force
if ($OutputDirectory -ne 'Prisma' -and (Test-Path -LiteralPath 'Prisma\Prisma.ico')) { Copy-Item -LiteralPath 'Prisma\Prisma.ico' -Destination $OutputDirectory -Force }
Write-Output ('Prisma está listo: ' + (Join-Path $OutputDirectory 'Prisma.exe'))
