# howtoadb

Quick commands to grab only relevant Android logs for this game.

## 0) Use the local adb path

```powershell
$adb = "E:\platform-tools\adb.exe"
```

Quick one-command option:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\android\capture-session-logs.ps1 -Clear
```

- Clears logcat first, filters out `Connection refused` spam, and saves a focused file to `.tmp/session-log-<timestamp>.txt`.

## 1) Confirm device + app package

```powershell
& $adb devices
& $adb shell "dumpsys window | grep -E 'mCurrentFocus|mFocusedApp'"
```

Expected package for this game:
- `com.Kimoworks.Lootgoblins`

## 2) Clean start (recommended before reproducing)

```powershell
& $adb logcat -c
```

Then reproduce the issue in-game (connect wallet, create character, etc.).

## 3) Get current app PID

```powershell
$pkg = "com.Kimoworks.Lootgoblins"
$appPid = (& $adb shell pidof -s $pkg).Trim()
$appPid
```

If empty, the app is not running yet.

## 4) Dump only this app's logs (fast)

```powershell
& $adb logcat -d --pid=$appPid
```

## 5) Dump only useful Solana/game lines

```powershell
& $adb logcat -d --pid=$appPid | rg -n "MainMenuCharacter|LGManager|WalletSession|SeekerIdentity|create_player_profile|init_player|SendTransaction|Simulation|Error|Exception"
```

## 5.1) Dump diagnostics-first lines (new)

```powershell
& $adb logcat -d --pid=$appPid | rg -n "LG-DIAG|Wallet adapter send failed|custom program error|could not predict balance changes|init_player precheck|init_player failed|CreateCharacterAsync"
```

`LG-DIAG` lines now classify root cause directly:
- `RPC transport failure: connection refused` -> endpoint/network issue
- `init_player account conflict at Instruction 2` -> stale start-room presence state
- `wallet simulation could not predict balance changes` -> on-chain simulation rejection, inspect custom program error line

## 5.2) Session-focused diagnostic dump

```powershell
& $adb logcat -d --pid=$appPid | rg -n "WalletSession|WalletAdapter|BeginGameplaySession|EnsureGameplaySession|session-attempt|SignAndSendTransaction|ConnectAsync|SendInstructions|SendTransaction|signingContext"
```

Key lines to look for:
- `BeginGameplaySessionAsync called` -> confirms session flow entered
- `WalletAdapter: calling SignAndSendTransaction` -> confirms wallet prompt triggered
- `WalletAdapter: result success=` -> shows what wallet returned
- `begin_session transaction failed` -> confirms where it stopped
- `Session auth disabled` -> sessions are disabled for this wallet mode

## 6) Hide known spam noise

```powershell
& $adb logcat -d --pid=$appPid `
  | rg -n -v "ThreadPoolWorkQueue.Dispatch Connection refused" `
  | rg -n "MainMenuCharacter|LGManager|WalletSession|SeekerIdentity|create_player_profile|init_player|SendTransaction|Simulation|Error|Exception"
```

## 7) Capture to file for sharing

```powershell
& $adb logcat -d --pid=$appPid > .tmp_app_pid_log.txt
& $adb logcat -d --pid=$appPid `
  | rg -n -v "ThreadPoolWorkQueue.Dispatch Connection refused" `
  | rg -n "MainMenuCharacter|LGManager|WalletSession|SeekerIdentity|create_player_profile|init_player|SendTransaction|Simulation|Error|Exception" `
  > .tmp_app_filtered_log.txt
```

## 8) Live tail while reproducing

```powershell
& $adb logcat --pid=$appPid | rg -n "MainMenuCharacter|LGManager|WalletSession|SeekerIdentity|create_player_profile|init_player|SendTransaction|Simulation|Error|Exception"
```

Or use the helper script:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\android\capture-session-logs.ps1 -Live
```

## 9) If PID filtering is empty, use package-wide grep

```powershell
& $adb logcat -d | rg -n "com.Kimoworks.Lootgoblins|MainMenuCharacter|LGManager|WalletSession|SeekerIdentity|SendTransaction|Simulation|Error|Exception"
```

