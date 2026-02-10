<#
.SYNOPSIS
    Build and deploy the Android APK to a connected device.

.DESCRIPTION
    Invokes Unity in batch mode to build (or patch) a development Android APK
    and run it on the connected device. After launch, optionally captures
    session-focused logs.

.PARAMETER Mode
    "build"       Full Build and Run (default)
    "patch"       Patch and Run (incremental, much faster after first build)
    "buildonly"   Build APK without deploying to device

.PARAMETER Release
    If set, builds a non-development build.

.PARAMETER BuildPath
    Override the APK output path.
    Default: Builds/Android/SeekerDungeon.apk

.PARAMETER Logs
    If set, clears logcat and captures session logs after the app launches.

.PARAMETER UnityPath
    Override the Unity editor executable path. Auto-detected by default.

.EXAMPLE
    # Full build and run (dev), then capture logs
    .\scripts\android\build-and-run.ps1 -Logs

.EXAMPLE
    # Patch and run (fast incremental)
    .\scripts\android\build-and-run.ps1 -Mode patch -Logs

.EXAMPLE
    # Build only, no deploy
    .\scripts\android\build-and-run.ps1 -Mode buildonly
#>

param(
    [ValidateSet("build", "patch", "buildonly")]
    [string]$Mode = "build",

    [switch]$Release,
    [string]$BuildPath = "",
    [switch]$Logs,
    [string]$UnityPath = ""
)

$ErrorActionPreference = "Stop"

# --- Fix sandbox env vars (Cursor IDE redirects cache dirs to very long paths) ---
if ($env:GRADLE_USER_HOME -match "cursor-sandbox") {
    $env:GRADLE_USER_HOME = "$env:USERPROFILE\.gradle"
    Write-Host "Fixed GRADLE_USER_HOME (sandbox override removed)"
}

# --- Resolve paths ---
$projectRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
$adb = if (Test-Path "E:\platform-tools\adb.exe") { "E:\platform-tools\adb.exe" } else { "adb" }
$package = "com.Kimoworks.Lootgoblins"

# --- Find Unity ---
if ($UnityPath -and (Test-Path $UnityPath)) {
    $unity = $UnityPath
} else {
    # Read ProjectVersion.txt to find the exact version
    $versionFile = Join-Path $projectRoot "ProjectSettings\ProjectVersion.txt"
    $versionLine = (Get-Content $versionFile | Select-String "m_EditorVersion:").ToString()
    $editorVersion = ($versionLine -split ":\s*")[1].Trim()
    $hubPath = "C:\Program Files\Unity\Hub\Editor\$editorVersion\Editor\Unity.exe"

    if (Test-Path $hubPath) {
        $unity = $hubPath
    } else {
        Write-Error "Unity $editorVersion not found at: $hubPath`nUse -UnityPath to specify the location."
        exit 1
    }
}

Write-Host "Unity:   $unity"
Write-Host "Project: $projectRoot"
Write-Host "Mode:    $Mode"
Write-Host "Release: $Release"
Write-Host ""

# --- Determine the executeMethod ---
switch ($Mode) {
    "build"     { $method = "SeekerDungeon.Editor.AndroidBuilder.BuildAndRun" }
    "patch"     { $method = "SeekerDungeon.Editor.AndroidBuilder.PatchAndRun" }
    "buildonly" { $method = "SeekerDungeon.Editor.AndroidBuilder.BuildOnly" }
}

# --- Build Unity args ---
$unityArgs = @(
    "-batchmode"
    "-quit"
    "-projectPath", $projectRoot
    "-executeMethod", $method
    "-logFile", "-"              # stream build log to stdout
    "-buildTarget", "Android"
)

if ($Release) {
    $unityArgs += "-release"
}

if ($BuildPath) {
    $unityArgs += "-buildPath", $BuildPath
}

# --- Run Unity build ---
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "Starting Unity build..."
Write-Host "$unity $($unityArgs -join ' ')"
Write-Host "---"

$process = Start-Process -FilePath $unity -ArgumentList $unityArgs -NoNewWindow -Wait -PassThru
$stopwatch.Stop()

Write-Host "---"
Write-Host "Build exited with code $($process.ExitCode) in $([math]::Round($stopwatch.Elapsed.TotalSeconds, 1))s"

if ($process.ExitCode -ne 0) {
    Write-Error "Build failed (exit code $($process.ExitCode))."
    exit $process.ExitCode
}

Write-Host "Build succeeded."

# --- Capture logs if requested ---
if ($Logs -and $Mode -ne "buildonly") {
    Write-Host ""
    Write-Host "Clearing logcat and waiting for app to start..."
    & $adb logcat -c 2>$null
    Start-Sleep -Seconds 8

    $appPid = (& $adb shell pidof $package 2>$null).Trim()
    if ($appPid) {
        Write-Host "App running (PID: $appPid). Capturing session logs for 20s..."
        Start-Sleep -Seconds 20
        & $adb logcat -d --pid=$appPid 2>$null |
            Select-String "WalletSession|session-attempt|multi-signer|Session started|Session ready|Session unavail|RPC send|assembled|fund|Bundling|begin_session|ix\(s\)|MainMenuCharacter|PrepareGameplay" |
            Select-Object -Last 100
    } else {
        Write-Host "App not detected. You can capture logs manually later."
    }
}

Write-Host ""
Write-Host "Done."
