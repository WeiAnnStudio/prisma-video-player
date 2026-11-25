param([string]$OutputDirectory = 'Prisma')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
# SHA-256 de libmpv-2.dll publicado en Prisma\licencias\PROCEDENCIA.txt. Solo se
# compila y se copia un motor idéntico al verificado.
$engineHash = '88504ee5560942a955405e99dfb5971368e4cf851321f36565bf2a667271db1b'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Se necesita .NET Framework 4.8, incluido en Windows 10/11.' }
if (-not (Test-Path -LiteralPath 'vendor\libmpv-2.dll')) { & (Join-Path $PSScriptRoot 'tools\prepare-engine.ps1') }
if ((Get-FileHash -LiteralPath 'vendor\libmpv-2.dll' -Algorithm SHA256).Hash -ne $engineHash) { throw 'vendor\libmpv-2.dll no coincide con la huella publicada. Borra vendor\libmpv-2.dll y vendor\mpv-dev.7z y vuelve a compilar.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
# Un Prisma abierto en segundo plano (por ejemplo uno que una conexión antigua de Claude o ChatGPT
# abrió sin ventana) bloquea Prisma.exe y la compilación falla. Se cierran solo esos;
# si hay una ventana de Prisma abierta, se pide cerrarla para no perder una nota a medio escribir.
$target = [IO.Path]::GetFullPath((Join-Path (Join-Path (Get-Location) $OutputDirectory) 'Prisma.exe'))
$running = @(Get-Process -Name 'Prisma' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and ([IO.Path]::GetFullPath($_.Path) -ieq $target) })
if (@($running | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero }).Count -gt 0) { throw ('Prisma está abierto. Ciérralo y vuelve a compilar (o usa -OutputDirectory tests\design).') }
if ($running.Count -gt 0) {
    Write-Output ('Cerrando ' + $running.Count + ' Prisma abierto en segundo plano...')
    $running | Stop-Process -Force
    $running | ForEach-Object { $_.WaitForExit(5000) | Out-Null }
}
$sources = @(Get-ChildItem -LiteralPath 'src' -Filter '*.cs' | ForEach-Object { $_.FullName })
$compilerArgs = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/highentropyva+', '/utf8output', ('/out:' + (Join-Path $OutputDirectory 'Prisma.exe')), '/win32manifest:src\app.manifest', '/resource:src\prisma_resolve.lua,Prisma.prisma_resolve.lua', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Xml.dll', '/reference:System.Xml.Linq.dll', '/reference:System.IO.Compression.dll')
if (Test-Path -LiteralPath 'Prisma\Prisma.ico') { $compilerArgs += '/win32icon:Prisma\Prisma.ico' }
& $compiler @compilerArgs @sources
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar Prisma.' }
Copy-Item -LiteralPath 'vendor\libmpv-2.dll' -Destination (Join-Path $OutputDirectory 'libmpv-2.dll') -Force
if ($OutputDirectory -ne 'Prisma' -and (Test-Path -LiteralPath 'Prisma\Prisma.ico')) { Copy-Item -LiteralPath 'Prisma\Prisma.ico' -Destination $OutputDirectory -Force }
Write-Output ('Prisma está listo: ' + (Join-Path $OutputDirectory 'Prisma.exe'))
