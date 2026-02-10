param(
    [string]$Package = "com.Kimoworks.Lootgoblins",
    [switch]$Clear,
    [switch]$Live
)

$adb = "E:\platform-tools\adb.exe"
if (-not (Test-Path $adb)) {
    $adb = "adb"
}

$includePattern = "WalletSession|LGManager|MainMenuCharacter|LG-DIAG|begin_session|create_player_profile|custom program error|could not predict balance changes|Session restart failed|Wallet adapter send failed|Transaction failed"
$excludePattern = "ThreadPoolWorkQueue\.Dispatch Connection refused"

if ($Clear) {
    & $adb logcat -c
}

$pidRaw = & $adb shell pidof -s $Package
$appPid = ""
if ($null -ne $pidRaw) {
    $appPid = "$pidRaw".Trim()
}
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outPath = ".tmp/session-log-$timestamp.txt"

if ($Live) {
    if ([string]::IsNullOrWhiteSpace($appPid)) {
        & $adb logcat | rg -n -v $excludePattern | rg -n $includePattern
    }
    else {
        & $adb logcat --pid=$appPid | rg -n -v $excludePattern | rg -n $includePattern
    }
    exit 0
}

if ([string]::IsNullOrWhiteSpace($appPid)) {
    & $adb logcat -d | rg -n -v $excludePattern | rg -n $includePattern | Tee-Object -FilePath $outPath
}
else {
    & $adb logcat -d --pid=$appPid | rg -n -v $excludePattern | rg -n $includePattern | Tee-Object -FilePath $outPath
}

Write-Host "Saved filtered logs to $outPath"
