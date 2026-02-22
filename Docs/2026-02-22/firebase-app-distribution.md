# Firebase App Distribution Runbook (2026-02-22)

Project
- Firebase Project ID: `lootgoblins-e35c5`
- Firebase Project Number: `79747038743`
- Android App ID: `1:79747038743:android:0d6f23474fd36bf357e652`

Tester Group
- Group alias: `friends`
- Group display name: `Friends`

Commands Used

1. Verify login and project list
```powershell
firebase login:list
firebase projects:list
```

2. Verify groups
```powershell
firebase appdistribution:group:list --project lootgoblins-e35c5
```

3. Add tester to reusable group
```powershell
firebase appdistribution:testers:add naardeark@gmail.com --group-alias friends --project lootgoblins-e35c5
```

4. Upload APK (without auto-distribution email)
```powershell
firebase appdistribution:distribute "Builds/V2/lootgoblins.apk" `
  --app 1:79747038743:android:0d6f23474fd36bf357e652 `
  --project lootgoblins-e35c5 `
  --release-notes "V2 dev build uploaded 2026-02-22"
```

Notes
- The upload command above intentionally omits `--groups` and `--testers` to avoid triggering distribution notifications.
- Testers still need access (for example, membership in `friends`) to open the shared tester release link.
- The direct binary download URL from CLI output expires (typically 1 hour).

Latest V2 Release Links
- Console release page:
  - https://console.firebase.google.com/project/lootgoblins-e35c5/appdistribution/app/android:com.Kimo.Lootgoblins/releases/00tq3eoap815g?utm_source=firebase-tools
- Tester share link:
  - https://appdistribution.firebase.google.com/testerapps/1:79747038743:android:0d6f23474fd36bf357e652/releases/00tq3eoap815g?utm_source=firebase-tools

Optional Next Command (if you want to notify testers automatically)
```powershell
firebase appdistribution:distribute "Builds/V2/lootgoblins.apk" `
  --app 1:79747038743:android:0d6f23474fd36bf357e652 `
  --project lootgoblins-e35c5 `
  --groups friends `
  --release-notes "V2 dev build"
```
