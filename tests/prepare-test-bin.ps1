$projectRoot = Split-Path -Parent $PSScriptRoot
$testBin = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $testBin | Out-Null
$testEngine = Join-Path $testBin 'libmpv-2.dll'
if (-not (Test-Path -LiteralPath $testEngine)) {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'vendor\libmpv-2.dll') -Destination $testEngine
}
