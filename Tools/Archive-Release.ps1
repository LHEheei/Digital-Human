param(
    [Parameter(Mandatory = $true)]
    [string]$ApkPath,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Label,

    [string]$BuildLog,

    [string]$Notes = ""
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$resolvedApk = (Resolve-Path -LiteralPath $ApkPath).Path
if (-not (Test-Path -LiteralPath $resolvedApk -PathType Leaf)) {
    throw "APK 不存在：$ApkPath"
}

$safeVersion = $Version -replace '[^0-9A-Za-z._-]', '-'
$safeLabel = $Label -replace '[^0-9A-Za-z._-]', '-'
$date = Get-Date -Format 'yyyyMMdd'
$archiveName = "$safeVersion-$safeLabel-$date"
$archivePath = Join-Path $projectRoot "Releases\\$archiveName"
if (Test-Path -LiteralPath $archivePath) {
    throw "归档目录已存在，为避免覆盖已停止：$archivePath"
}

New-Item -ItemType Directory -Path $archivePath | Out-Null
$apkName = Split-Path -Leaf $resolvedApk
$archivedApk = Join-Path $archivePath $apkName
Copy-Item -LiteralPath $resolvedApk -Destination $archivedApk

if (-not [string]::IsNullOrWhiteSpace($BuildLog)) {
    $resolvedLog = (Resolve-Path -LiteralPath $BuildLog).Path
    Copy-Item -LiteralPath $resolvedLog -Destination (Join-Path $archivePath (Split-Path -Leaf $resolvedLog))
}

$apk = Get-Item -LiteralPath $archivedApk
$sha256 = (Get-FileHash -LiteralPath $archivedApk -Algorithm SHA256).Hash
$readme = @"
# $archiveName

- 归档时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
- 原始 APK：`$resolvedApk`
- 文件大小：$($apk.Length) bytes
- SHA-256：`$sha256`

## 演示说明

$Notes
"@
Set-Content -LiteralPath (Join-Path $archivePath 'README.md') -Value $readme -Encoding utf8

Write-Host "已归档：$archivePath"
