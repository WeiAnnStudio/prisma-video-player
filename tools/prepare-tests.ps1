$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$vendorPath = Join-Path $projectRoot 'vendor'
New-Item -ItemType Directory -Force -Path $vendorPath | Out-Null
$archivePath = Join-Path $vendorPath 'ffmpeg-tests.zip'
$download = 'https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip'
$expectedHash = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
if (-not (Test-Path -LiteralPath $archivePath)) { Invoke-WebRequest -Uri $download -OutFile $archivePath }
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Huella incorrecta de FFmpeg.' }
tar -xf $archivePath -C $vendorPath
if ($LASTEXITCODE -ne 0) { throw 'No se pudo extraer FFmpeg.' }
python (Join-Path $projectRoot 'tests\generate-fixtures.py')
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron generar los vídeos de prueba.' }
