# Devnet Smoke Wallet

- Purpose: direct script-based contract smoke tests before Unity testing.
- Keypair file: `test-wallets/devnet-smoke-wallet.json` (gitignored)
- Public key: `E2xzqHcjQ78kKzZ2bFE41csGFswn6YgRG8hRKFQj2RvH`

## Current Funding Snapshot

- SOL: ~1 SOL
- SKR: 25

## Run Smoke Test

```bash
export ANCHOR_WALLET=test-wallets/devnet-smoke-wallet.json
export ANCHOR_PROVIDER_URL=https://api.devnet.solana.com
npm run smoke-door
```
