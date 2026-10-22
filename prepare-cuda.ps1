param(
    [string]$SourceCache = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'GoDesk\downloads'),
    [string]$Destination = (Join-Path $PSScriptRoot 'release\梓懿围棋-v1.5.0\cuda')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$cacheRoot = [IO.Path]::GetFullPath($SourceCache)
$destinationRoot = [IO.Path]::GetFullPath($Destination)
$noticeRoot = Join-Path $PSScriptRoot 'cuda-licenses'
$commonNoticeRoot = Join-Path $PSScriptRoot 'gpu-licenses'
$spec = Get-Content -LiteralPath (Join-Path $noticeRoot 'sources.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $cacheRoot,$destinationRoot -Force | Out-Null

function Assert-PinnedFile([string]$Path, [long]$Bytes, [string]$Sha256) {
    if ((Get-Item -LiteralPath $Path).Length -ne $Bytes) { throw "文件长度校验失败：$Path" }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Sha256) { throw "SHA-256 校验失败：$Path" }
}

function Get-PinnedAsset($Asset) {
    if ([IO.Path]::GetFileName($Asset.name) -ne $Asset.name) { throw '缓存名称含目录。' }
    $cachedPath = Join-Path $cacheRoot $Asset.name
    if (Test-Path -LiteralPath $cachedPath) {
        Assert-PinnedFile $cachedPath $Asset.size $Asset.sha256
        Write-Host "使用已校验缓存：$($Asset.name)"
        return $cachedPath
    }
    $temporary = Join-Path $cacheRoot ($Asset.name + '.' + [Guid]::NewGuid().ToString('N') + '.partial')
    try {
        Write-Host "下载：$($Asset.name)"
        Invoke-WebRequest -Uri $Asset.url -OutFile $temporary -UseBasicParsing -TimeoutSec 3600
        Assert-PinnedFile $temporary $Asset.size $Asset.sha256
        Move-Item -LiteralPath $temporary -Destination $cachedPath
        return $cachedPath
    }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}

foreach ($notice in @('README.md','DEPLOYMENT.md','NVIDIA-CUDA-12.1-EULA.txt','NVIDIA-cuDNN-wheel-License.txt','NVIDIA-cuDNN-9.8-EULA.html','NVIDIA-cuDNN-9.8-Acknowledgements.html','cudnn-frontend-LICENSE.txt','CUTLASS-LICENSE.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $noticeRoot $notice))) { throw "缺少随源码提供的 CUDA 许可文件：$notice" }
}
foreach ($notice in @('KataGo-LICENSE.txt','KataGo-CONTRIBUTORS.txt','KataGo-release-README.txt','Microsoft-VisualC-Runtime-License.html','OpenSSL-LICENSE.txt','libzip-LICENSE.txt','zlib-LICENSE.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $commonNoticeRoot $notice))) { throw "缺少通用运行库许可文件：$notice" }
}
# A re-run must not silently retain files excluded by this pinned deployment.
foreach ($excluded in $spec.excludedNvidiaFiles) {
    if (Test-Path -LiteralPath (Join-Path $destinationRoot $excluded)) { throw "目标目录含旧的额外运行库，请使用新的目标目录：$excluded" }
}

$archives = @{}
foreach ($source in $spec.sources) { $archives[$source.name] = Get-PinnedAsset $source }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$staging = Join-Path $cacheRoot ('cuda-stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
$stagingPrefix = [IO.Path]::GetFullPath($staging).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
try {
    $engine = @($spec.sources | Where-Object { $_.name -like 'katago-*.zip' })
    if ($engine.Count -ne 1) { throw 'CUDA 引擎来源数量不正确。' }
    $zip = [IO.Compression.ZipFile]::OpenRead($archives[$engine[0].name])
    try {
        foreach ($entry in $zip.Entries) {
            $target = [IO.Path]::GetFullPath((Join-Path $staging $entry.FullName))
            if (-not $target.StartsWith($stagingPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '引擎压缩包含越界路径。' }
            if ($entry.FullName.EndsWith('/')) { New-Item -ItemType Directory -Path $target -Force | Out-Null; continue }
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    }
    finally { $zip.Dispose() }
    if (-not (Test-Path -LiteralPath (Join-Path $staging 'katago.exe'))) { throw '官方 CUDA 压缩包中未找到 katago.exe。' }
    # Extract unmodified NVIDIA runtime DLLs only. Wheels are ZIP containers;
    # no Python imports, wheel installation or downloaded program execution occurs.
    foreach ($source in @($spec.sources | Where-Object { $_.name.EndsWith('.whl') })) {
        $zip = [IO.Compression.ZipFile]::OpenRead($archives[$source.name])
        try {
            foreach ($file in @($spec.runtimeFiles | Where-Object { $_.source -eq $source.name })) {
                if ([IO.Path]::GetFileName($file.file) -ne $file.file -or -not $file.file.EndsWith('.dll')) { throw 'DLL 名称不合法。' }
                $entry = $zip.GetEntry($file.entry)
                if ($null -eq $entry -or $entry.Length -ne $file.bytes) { throw "官方归档条目不匹配：$($file.entry)" }
                $target = Join-Path $staging $file.file
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
                Assert-PinnedFile $target $file.bytes $file.sha256
            }
        }
        finally { $zip.Dispose() }
    }
    # Use ordinal ordering; culture-based Sort-Object differs between PS 5 and 7.
    $runtimeNames = [string[]]@(Get-ChildItem -LiteralPath $staging -File | ForEach-Object { $_.Name })
    [Array]::Sort($runtimeNames, [StringComparer]::OrdinalIgnoreCase)
    $runtimeFiles = @($runtimeNames | ForEach-Object {
        $runtimeFile = Get-Item -LiteralPath (Join-Path $staging $_)
        [ordered]@{ file = $runtimeFile.Name; bytes = $runtimeFile.Length; sha256 = (Get-FileHash -LiteralPath $runtimeFile.FullName -Algorithm SHA256).Hash }
    })
    # Retain the complete upstream engine, including its configuration examples,
    # original README and every common dependency DLL supplied by KataGo.
    Get-ChildItem -LiteralPath $staging -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $destinationRoot -Recurse -Force }
    foreach ($file in $spec.runtimeFiles) { Assert-PinnedFile (Join-Path $destinationRoot $file.file) $file.bytes $file.sha256 }
    $licenseOutput = Join-Path $destinationRoot 'licenses'
    New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
    Get-ChildItem -LiteralPath $commonNoticeRoot -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $licenseOutput -Force }
    Get-ChildItem -LiteralPath $noticeRoot -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $licenseOutput -Force }
    Copy-Item -LiteralPath (Join-Path $noticeRoot 'README.md') -Destination (Join-Path $destinationRoot 'THIRD-PARTY.txt') -Force
    $metadata = [ordered]@{
        backend = 'CUDA'
        engineVersion = $spec.engineVersion
        cudaVersion = $spec.cudaVersion
        cudnnVersion = $spec.cudnnVersion
        engineArchiveSha256 = $engine[0].sha256
        engineUrl = $engine[0].url
        modelDirectory = '../gpu/models'
        checkedAt = $spec.checkedAt
        appLocalRuntime = $true
        globalInstallationRequired = $false
        sources = $spec.sources
        runtimeFiles = $runtimeFiles
        excludedNvidiaFiles = $spec.excludedNvidiaFiles
        dependencySelection = 'licenses/DEPLOYMENT.md'
        licenseNotice = 'licenses/README.md'
    }
    [IO.File]::WriteAllText((Join-Path $destinationRoot 'runtime.json'), ($metadata | ConvertTo-Json -Depth 9), (New-Object Text.UTF8Encoding($false)))
}
finally {
    # Only remove the unique extraction directory created inside the chosen cache.
    $resolvedStaging = [IO.Path]::GetFullPath($staging)
    $cachePrefix = $cacheRoot.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedStaging.StartsWith($cachePrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedStaging) -notmatch '^cuda-stage-[0-9a-f]{32}$') { throw '临时目录边界检查失败。' }
    if (Test-Path -LiteralPath $resolvedStaging) { Remove-Item -LiteralPath $resolvedStaging -Recurse -Force }
}
Write-Host 'CUDA 套件已准备完成。保持 cuda、gpu 文件夹与梓懿围棋.exe 相邻；无需安装 CUDA Toolkit。'
[PSCustomObject]@{ Destination = $destinationRoot; Backend = 'CUDA'; EngineVersion = $spec.engineVersion; CudaVersion = $spec.cudaVersion; CudnnVersion = $spec.cudnnVersion; NvidiaDlls = $spec.runtimeFiles.Count }
