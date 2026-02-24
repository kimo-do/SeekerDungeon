# Mainnet Wallet + Deploy Workflow

This repo now includes VS Code tasks for mainnet operations.

## Important Security Rules

- Keep your mainnet keypair outside this repo.
- Never commit wallet JSON files.
- Never paste private key contents into chat, logs, or docs.
- Use a dedicated deploy wallet (not your personal main wallet).

## 1) Create a Secure Mainnet Wallet

From WSL Ubuntu:

```bash
mkdir -p ~/.config/solana
chmod 700 ~/.config/solana
solana-keygen new --outfile ~/.config/solana/mainnet-deployer.json
chmod 600 ~/.config/solana/mainnet-deployer.json
solana address -k ~/.config/solana/mainnet-deployer.json
```

Backup the seed phrase offline (paper/metal), stored separately from this machine.

## 2) Optional Vanity Address (`loot...`)

You can control the **prefix**, not an arbitrary custom full address.

Example (prefix starts with `loot`):

```bash
solana-keygen grind --starts-with loot:1 --ignore-case --outfile ~/.config/solana/loot-mainnet.json
chmod 600 ~/.config/solana/loot-mainnet.json
```

Notes:

- Vanity grinding can take a long time depending on prefix length.
- Never use online vanity generators that ask for your private key.

## 3) Use VS Code Tasks (Ctrl+Shift+P -> Tasks: Run Task)

New tasks added:

- `Solana: Mainnet Preflight`
- `Anchor: Deploy to Mainnet`
- `Solana: Force Reset Season (Mainnet)`
- `Solana: Reclaim SPL Token Rent (Mainnet)`
- `Solana: Close Program Buffers (Mainnet)`

Each task prompts for:

- `mainnetWalletPath` (WSL path), for example:
  - `~/.config/solana/mainnet-deployer.json`
  - `~/.config/solana/loot-mainnet.json`

## 4) Recommended Deploy Order

1. Run `Anchor: Build (WSL)`
2. Run `Generate: Unity C# Client` (if IDL changed)
3. Run `Solana: Mainnet Preflight`
4. Run `Anchor: Deploy to Mainnet`
5. Run `Solana: Mainnet Preflight` again to verify

## 5) Rent Recovery Tasks

- `Solana: Reclaim SPL Token Rent (Mainnet)`:
  - Cleans up auxiliary SPL token accounts and reclaims their rent.
- `Solana: Close Program Buffers (Mainnet)`:
  - Reclaims SOL from leftover deploy buffers.

## 6) What Is Not Included as a Task (On Purpose)

Closing the program itself is intentionally not a one-click task. It is destructive and should be done manually with explicit review.
