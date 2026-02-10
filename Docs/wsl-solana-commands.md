# Solana Program WSL Commands

Use this guide from repo root: `e:\Github2\SeekerDungeon`.

## Preferred path (always try first)

Run all `solana-program` commands through the wrapper:

```powershell
solana-program/scripts/wsl/run.sh <your command here>
```

Common examples:

```powershell
solana-program/scripts/wsl/build.sh
solana-program/scripts/wsl/run.sh npm install
solana-program/scripts/wsl/run.sh npm test
solana-program/scripts/wsl/run.sh npm run smoke-session-join-job
solana-program/scripts/wsl/run.sh anchor test
```

## If wrapper gives no output or odd failures

Some environments have broken WSL shell startup files (`~/.bashrc`) or CRLF shell scripts.
Use this robust fallback that skips shell profiles:

```powershell
wsl -d Ubuntu -- /bin/bash --noprofile --norc -c "cd /mnt/e/Github2/SeekerDungeon/solana-program && <your command here>"
```

Example:

```powershell
wsl -d Ubuntu -- /bin/bash --noprofile --norc -c "cd /mnt/e/Github2/SeekerDungeon/solana-program && npm run check-state"
```

## Anchor env vars pattern (safe copy/paste)

```powershell
wsl -d Ubuntu -- /bin/bash --noprofile --norc -c "cd /mnt/e/Github2/SeekerDungeon/solana-program && export ANCHOR_PROVIDER_URL=https://api.devnet.solana.com && export ANCHOR_WALLET=devnet-wallet.json && npm run smoke-session-join-job"
```

## Top up a wallet using `devnet-wallet.json`

Use these when your connected wallet needs devnet funds.

### 1) Check dev wallet SOL balance

```powershell
solana-program/scripts/wsl/run.sh "solana balance --url devnet -k devnet-wallet.json"
```

### 2) Send devnet SOL from dev wallet to target wallet

Replace `<TARGET_WALLET>` and amount:

```powershell
solana-program/scripts/wsl/run.sh "solana transfer <TARGET_WALLET> 0.5 --allow-unfunded-recipient --url devnet -k devnet-wallet.json"
```

### 3) Optional: airdrop SOL to dev wallet

```powershell
solana-program/scripts/wsl/run.sh "solana airdrop 2 --url devnet -k devnet-wallet.json"
```

### 4) Send test SKR (primary path)

```powershell
solana-program/scripts/wsl/run.sh "npm run mint-tokens <TARGET_WALLET> 25"
```

### 5) Send test SKR (fallback when mint script fails with `Expected a Address.`)

Use `spl-token.exe` from WSL with no profile loading:
Replace `<WINDOWS_USER>` with your actual Windows username.

```powershell
wsl -d Ubuntu -- /bin/bash --noprofile --norc -c 'cd /mnt/e/Github2/SeekerDungeon/solana-program && "/mnt/c/Users/<WINDOWS_USER>/.local/share/solana/install/active_release/bin/spl-token.exe" create-account Dkpjmf6mUxxLyw9HmbdkBKhVf7zjGZZ6jNjruhjYpkiN --owner <TARGET_WALLET> --fee-payer devnet-wallet.json -u devnet || true && "/mnt/c/Users/<WINDOWS_USER>/.local/share/solana/install/active_release/bin/spl-token.exe" mint Dkpjmf6mUxxLyw9HmbdkBKhVf7zjGZZ6jNjruhjYpkiN 25 --recipient-owner <TARGET_WALLET> --mint-authority devnet-wallet.json --fee-payer devnet-wallet.json -u devnet'
```

Check token accounts/balances:

```powershell
wsl -d Ubuntu -- /bin/bash --noprofile --norc -c '"/mnt/c/Users/<WINDOWS_USER>/.local/share/solana/install/active_release/bin/spl-token.exe" accounts --owner <TARGET_WALLET> -u devnet'
```

### 6) Full clean-slate helper (reset profile/player + fund SOL + mint SKR)

This calls the admin-only onchain reset and then funds test assets in one command:

```powershell
solana-program/scripts/wsl/run.sh "export ANCHOR_PROVIDER_URL=https://api.devnet.solana.com && export ANCHOR_WALLET=devnet-wallet.json && npm run reset-player-and-fund <TARGET_WALLET> 0.5 25"
```

### 7) Reset only (no SOL, no SKR funding)

Use this to test low/no-balance flows:

```powershell
solana-program/scripts/wsl/run.sh "export ANCHOR_PROVIDER_URL=https://api.devnet.solana.com && export ANCHOR_WALLET=devnet-wallet.json && npm run reset-player-only <TARGET_WALLET>"
```

## Quick troubleshooting checks

Check WSL npm:

```powershell
wsl -d Ubuntu -- /bin/bash --noprofile --norc -c "which npm && npm -v"
```

If wrapper scripts show `$'\r': command not found`, normalize line endings:

```powershell
wsl -d Ubuntu -- sed -i 's/\r$//' /mnt/e/Github2/SeekerDungeon/solana-program/scripts/wsl/*.sh
```

## Conventions

- Prefer `npm` (never `yarn`).
- Run from repo root unless command requires otherwise.
- Always capture exact command + full error output when debugging.
- Never share or commit private key contents from `devnet-wallet.json`.
