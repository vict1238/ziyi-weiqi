param([switch]$Checks)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows 的 .NET Framework 4.8 开发编译器。' }
$releaseDir = Join-Path $projectRoot 'release/梓懿围棋-v1.4.0'
$qaDir = Join-Path $projectRoot 'qa'
New-Item -ItemType Directory -Path $releaseDir,$qaDir -Force | Out-Null
$sourcePaths = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName })
$common = @('/nologo','/utf8output','/codepage:65001','/platform:x64','/optimize+',
    '/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/r:System.Numerics.dll',
    ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')))
$icon = Join-Path $projectRoot 'app.ico'
if (Test-Path -LiteralPath $icon) { $common += '/win32icon:' + $icon }
$assetRoot = Join-Path $projectRoot 'engine-assets'
$requiredAssets = @('katago.exe','g170e-b10c128-s1141046784-d204142634.bin.gz')
foreach ($asset in $requiredAssets) { if (-not (Test-Path -LiteralPath (Join-Path $assetRoot $asset))) { throw "缺少引擎资源：$asset" } }
foreach ($asset in (Get-ChildItem -LiteralPath $assetRoot -File | Sort-Object Name)) {
    $common += '/resource:' + $asset.FullName + ',engine.' + $asset.Name
}
foreach ($license in (Get-ChildItem -LiteralPath (Join-Path $assetRoot 'licenses') -File | Sort-Object Name)) {
    $common += '/resource:' + $license.FullName + ',notices.' + $license.Name
}
$common += '/resource:' + (Join-Path $projectRoot 'THIRD-PARTY.md') + ',notices.THIRD-PARTY.txt'
$common += '/resource:' + (Join-Path $projectRoot 'LICENSE.txt') + ',notices.GoDesk-LICENSE.txt'
$common += '/resource:' + (Join-Path $projectRoot 'assets/stone.wav') + ',audio.stone.wav'
$exe = Join-Path $releaseDir '梓懿围棋.exe'
& $compiler @common '/target:winexe' ('/out:' + $exe) @sourcePaths
if ($LASTEXITCODE -ne 0) { throw '桌面程序编译失败。' }
Copy-Item -LiteralPath (Join-Path $projectRoot '使用说明.txt') -Destination $releaseDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE.txt') -Destination $releaseDir -Force
$notice = Join-Path $projectRoot 'THIRD-PARTY.md'
if (Test-Path -LiteralPath $notice) { Copy-Item -LiteralPath $notice -Destination (Join-Path $releaseDir '第三方许可.txt') -Force }
$licenses = Join-Path $assetRoot 'licenses'
if (Test-Path -LiteralPath $licenses) {
    $licenseOutput = Join-Path $releaseDir 'licenses'
    New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
    Get-ChildItem -LiteralPath $licenses -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $licenseOutput -Force }
}
if ($Checks) {
    $checksExe = Join-Path $qaDir 'GoDesk.Checks.exe'
    & $compiler @common '/target:exe' ('/out:' + $checksExe) @sourcePaths
    if ($LASTEXITCODE -ne 0) { throw '验证程序编译失败。' }
    $rulesReport = Join-Path $qaDir 'rules-test.txt'
    $ruleProcess = Start-Process -FilePath $checksExe -ArgumentList @('--self-test',('"'+$rulesReport+'"')) -WindowStyle Hidden -PassThru -Wait
    Get-Content -LiteralPath $rulesReport
    if ($ruleProcess.ExitCode -ne 0) { throw '围棋规则或棋谱检查失败。' }
    $engineReport = Join-Path $qaDir 'engine-test.txt'
    $engineProcess = Start-Process -FilePath $checksExe -ArgumentList @('--engine-test',('"'+$engineReport+'"')) -WindowStyle Hidden -PassThru -Wait
    Get-Content -LiteralPath $engineReport
    if ($engineProcess.ExitCode -ne 0) { throw '真实 KataGo 引擎检查失败。' }
}
Get-Item -LiteralPath $exe | Select-Object FullName,Length,LastWriteTime
Get-FileHash -LiteralPath $exe -Algorithm SHA256 | Select-Object Algorithm,Hash,Path
