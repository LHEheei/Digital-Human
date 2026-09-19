[CmdletBinding()]
param(
    [switch]$SkipUnity,
    [switch]$SkipEmotionService,
    [switch]$SkipAndroidBuild,
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.4.6f1\Editor\Unity.exe",
    [string]$ServiceUrl = "http://127.0.0.1:8000"
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$serviceRoot = Join-Path $projectRoot 'EmotionService'
$python = Join-Path $serviceRoot '.venv\Scripts\python.exe'
$validationDirectory = Join-Path $projectRoot 'Builds\Validation'
$buildLog = Join-Path $validationDirectory 'android-validation-build.log'

function Write-Step([string]$Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Require-File([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description not found: $Path"
    }
}

function Assert-UnityIsReady {
    if (Get-Process -Name Unity -ErrorAction SilentlyContinue) {
        throw 'Unity Editor is currently running. Close it before the unattended validation so it does not compete for the project database.'
    }

    $lockPath = Join-Path $projectRoot 'Temp\UnityLockfile'
    if (Test-Path -LiteralPath $lockPath) {
        try {
            $lockProbe = [IO.File]::Open($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
            $lockProbe.Dispose()
        }
        catch { throw "Unity still holds the project lock: $lockPath" }
    }

}

function Invoke-Unity([string[]]$Arguments, [string]$Description) {
    Write-Step $Description
    $process = Start-Process -FilePath $UnityPath -ArgumentList $Arguments -WorkingDirectory $projectRoot -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Unity exited with code $($process.ExitCode). Check the validation log."
    }
}

New-Item -ItemType Directory -Force -Path $validationDirectory | Out-Null
if (-not $SkipEmotionService) {
Require-File $python 'EmotionService virtual-environment Python'

Write-Step 'Running EmotionService contract tests'
Push-Location $serviceRoot
try {
    & $python -m unittest discover -s tests -v
    if ($LASTEXITCODE -ne 0) { throw "Python tests failed with code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

Write-Step 'Checking the local emotion service health endpoint'
try {
    $health = Invoke-RestMethod -Uri "$($ServiceUrl.TrimEnd('/'))/health" -TimeoutSec 10
}
catch {
    throw "The local EmotionService is unavailable at $ServiceUrl. Start it first, then rerun this script. $($_.Exception.Message)"
}

if ($health.status -ne 'ok') {
    throw "EmotionService is reachable but not ready (status: $($health.status)). Wait for model warm-up, then rerun."
}
Write-Host "Service ready: face=$($health.models.faceLandmarkerLoaded), voice=$($health.models.voiceModelLoaded)" -ForegroundColor Green

}
else { Write-Host 'LAN emotion service validation skipped (on-device workflow).' }

if ($SkipUnity) {
    Write-Host "`nRequested service checks completed. Unity checks were skipped." -ForegroundColor Green
    exit 0
}

Require-File $UnityPath 'Unity executable'
Assert-UnityIsReady

foreach ($testPlatform in @('EditMode', 'PlayMode')) {
    Assert-UnityIsReady
    $testResults = Join-Path $validationDirectory "$($testPlatform.ToLower())-validation-results.xml"
    $testLog = Join-Path $validationDirectory "$($testPlatform.ToLower())-validation.log"
    Remove-Item -LiteralPath $testResults -Force -ErrorAction SilentlyContinue
    Invoke-Unity @(
        '-batchmode',
        '-projectPath', $projectRoot,
        '-runTests',
        '-testPlatform', $testPlatform,
        '-testResults', $testResults,
        '-logFile', $testLog
    ) "Running Unity $testPlatform tests"
    Require-File $testResults "$testPlatform result file"
    [xml]$xml = Get-Content -LiteralPath $testResults
    $suite = $xml.SelectSingleNode('//test-run')
    if ($null -eq $suite -or $suite.result -ne 'Passed' -or [int]$suite.failed -ne 0 -or [int]$suite.total -eq 0) {
        throw "Unity $testPlatform tests did not pass. Read $testResults and $testLog."
    }
    Write-Host "Unity $testPlatform passed: $($suite.passed)/$($suite.total) tests." -ForegroundColor Green
}

if ($SkipAndroidBuild) {
    Write-Host "`nRequested service checks and Unity EditMode/PlayMode tests passed. Android build was skipped." -ForegroundColor Green
    exit 0
}

Assert-UnityIsReady
Invoke-Unity @(
    '-batchmode',
    '-quit',
    '-projectPath', $projectRoot,
    '-executeMethod', 'SrtpAvatarEditor.DemoSceneBuilder.BuildAndroidDevelopmentApk',
    '-logFile', $buildLog
) 'Building the Android development APK'

if (-not (Select-String -LiteralPath $buildLog -Pattern 'Build Finished, Result: Success\.' -Quiet)) {
    throw "Unity exited normally, but the Android success marker was not found. Read $buildLog."
}

Write-Host "`nRequested service checks, Unity EditMode/PlayMode tests, and Android build passed." -ForegroundColor Green
Write-Host "Artifacts: $validationDirectory"
