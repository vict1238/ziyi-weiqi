param(
    [string]$SourceCache = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'GoDesk\downloads'),
    [string]$Destination = (Join-Path $PSScriptRoot 'release\梓懿围棋-v1.5.0\gpu')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$cacheRoot = [IO.Path]::GetFullPath($SourceCache)
$destinationRoot = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $cacheRoot,$destinationRoot -Force | Out-Null

$engineName = 'katago-v1.18.1-opencl-windows-x64.zip'
$engineUrl = 'https://github.com/lightvector/KataGo/releases/download/v1.18.1/katago-v1.18.1-opencl-windows-x64.zip'
$engineSha = '1710DB1903AB921AA6837A9599C8474F8A59F057650217C5D9BC125EE393A9FF'
$models = @(
    [ordered]@{
        id = 'kata1-tf3-b11c768-s11003M-d5973M-7gres'
        file = 'models/kata1-tf3-b11c768-s11003M-d5973M-7gres.bin.gz'
        bytes = 262039869
        sha256 = '93BDB63A3BFAE4A70DB0CB5265287495ECFC10B1BA1CC6814FEEBA1CDF055871'
        uploadedAtUtc = '2026-09-25T02:04:37Z'
        date = '2026-09-25'
        dateKind = 'official-upload'
        recommended = $true
    },
    [ordered]@{
        id = 'kata1-b18c384nbt-s9996604416-d4316597426'
        file = 'models/kata1-b18c384nbt-s9996604416-d4316597426.bin.gz'
        bytes = 97898094
        sha256 = '9D7A6AFED8FF5B74894727E156F04F0CD36060A24824892008FBB6E0CBA51F1D'
        uploadedAtUtc = '2024-05-26T12:47:48Z'
        date = '2024-05-26'
        dateKind = 'official-upload'
        recommended = $false
    }
)

function Assert-PinnedFile([string]$Path, [long]$Bytes, [string]$Sha256) {
    if ((Get-Item -LiteralPath $Path).Length -ne $Bytes) { throw "文件长度校验失败：$Path" }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Sha256) { throw "SHA-256 校验失败：$Path" }
}

function Get-PinnedAsset([string]$Name, [string]$Url, [long]$Bytes, [string]$Sha256) {
    $cachedPath = Join-Path $cacheRoot $Name
    if (Test-Path -LiteralPath $cachedPath) {
        Assert-PinnedFile $cachedPath $Bytes $Sha256
        Write-Host "使用已校验缓存：$Name"
        return $cachedPath
    }
    $temporary = Join-Path $cacheRoot ($Name + '.' + [Guid]::NewGuid().ToString('N') + '.partial')
    try {
        Write-Host "下载：$Name"
        Invoke-WebRequest -Uri $Url -OutFile $temporary -UseBasicParsing -TimeoutSec 1800
        Assert-PinnedFile $temporary $Bytes $Sha256
        Move-Item -LiteralPath $temporary -Destination $cachedPath
        return $cachedPath
    }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}

$noticeRoot = Join-Path $PSScriptRoot 'gpu-licenses'
foreach ($notice in @('README.md','KataGo-LICENSE.txt','KataGo-CONTRIBUTORS.txt','CLBlast-LICENSE.txt','half-LICENSE.txt','tclap-COPYING.txt','bzip2-COPYING.txt','Model-LICENSE.html','Model-LICENSE.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $noticeRoot $notice))) { throw "缺少随源码提供的 GPU 许可文件：$notice" }
}

$archive = Get-PinnedAsset $engineName $engineUrl 6004137 $engineSha
$modelSources = @{}
foreach ($model in $models) {
    $modelName = $model.id + '.bin.gz'
    $modelUrl = 'https://media.katagotraining.org/uploaded/networks/models/kata1/' + $modelName
    $model['url'] = $modelUrl
    $modelSources[$model.id] = Get-PinnedAsset $modelName $modelUrl $model.bytes $model.sha256
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$staging = Join-Path $cacheRoot ('gpu-stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
$stagingPrefix = [IO.Path]::GetFullPath($staging).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
try {
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($entry in $zip.Entries) {
            $target = [IO.Path]::GetFullPath((Join-Path $staging $entry.FullName))
            if (-not $target.StartsWith($stagingPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '压缩包含越界路径。' }
            if ($entry.FullName.EndsWith('/')) { New-Item -ItemType Directory -Path $target -Force | Out-Null; continue }
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
            $inputStream = $entry.Open()
            try {
                $outputStream = [IO.File]::Create($target)
                try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose() }
            }
            finally { $inputStream.Dispose() }
        }
    }
    finally { $zip.Dispose() }
    if (-not (Test-Path -LiteralPath (Join-Path $staging 'katago.exe'))) { throw '官方压缩包中未找到 katago.exe。' }
    # Keep the entire upstream runtime, including every DLL and configuration example.
    Get-ChildItem -LiteralPath $staging -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $destinationRoot -Recurse -Force }
    New-Item -ItemType Directory -Path (Join-Path $destinationRoot 'models') -Force | Out-Null
    foreach ($model in $models) {
        $modelOutput = Join-Path $destinationRoot $model.file
        Copy-Item -LiteralPath $modelSources[$model.id] -Destination $modelOutput -Force
        Assert-PinnedFile $modelOutput $model.bytes $model.sha256
    }
    $licenseOutput = Join-Path $destinationRoot 'licenses'
    New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
    Get-ChildItem -LiteralPath $noticeRoot -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $licenseOutput -Force }
    Copy-Item -LiteralPath (Join-Path $noticeRoot 'README.md') -Destination (Join-Path $destinationRoot 'THIRD-PARTY.txt') -Force
    $metadata = [ordered]@{
        backend = 'OpenCL'
        engineVersion = '1.18.1'
        engineArchiveSha256 = $engineSha
        engineUrl = $engineUrl
        modelName = $models[0].id
        modelFile = $models[0].file
        modelSha256 = $models[0].sha256
        modelUrl = $models[0].url
        networkDate = $models[0].date
        networkDateKind = 'official-upload'
        strongestCheckedAt = '2026-10-20'
        source = 'https://katagotraining.org/networks/'
        models = $models
    }
    [IO.File]::WriteAllText((Join-Path $destinationRoot 'runtime.json'), ($metadata | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
}
finally {
    # Only remove the unique extraction directory created inside the chosen cache.
    $resolvedStaging = [IO.Path]::GetFullPath($staging)
    $cachePrefix = $cacheRoot.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedStaging.StartsWith($cachePrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedStaging) -notmatch '^gpu-stage-[0-9a-f]{32}$') { throw '临时目录边界检查失败。' }
    if (Test-Path -LiteralPath $resolvedStaging) { Remove-Item -LiteralPath $resolvedStaging -Recurse -Force }
}

Write-Host 'GPU 套件已准备完成。保持 gpu 文件夹与梓懿围棋.exe 相邻；首次 OpenCL 调优可能需要几分钟。'
[PSCustomObject]@{ Destination = $destinationRoot; Backend = 'OpenCL'; EngineVersion = '1.18.1'; DefaultModel = $models[0].id; Models = $models.Count }
