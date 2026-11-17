$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$vendorPath = Join-Path $projectRoot 'vendor'
New-Item -ItemType Directory -Force -Path $vendorPath | Out-Null
$archivePath = Join-Path $vendorPath 'mpv-dev.7z'
$download = 'https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261111/mpv-dev-x86_64-20261111-git-bcde6b93cc.7z'
$expectedHash = '273c079b11ea5575822f972756f4c9a71149a5c10130353abeb8586738938452'
if (-not (Test-Path -LiteralPath $archivePath)) { Invoke-WebRequest -Uri $download -OutFile $archivePath }
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) { throw 'El motor descargado no coincide con la huella publicada. No se extraerá.' }
tar -xf $archivePath -C $vendorPath
if ($LASTEXITCODE -ne 0) { throw 'No se pudo extraer el motor.' }
