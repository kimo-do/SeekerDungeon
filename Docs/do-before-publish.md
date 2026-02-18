# Do Before Publish

## 1) Switch from test SKR to real SKR
- Create/open: `Assets/Resources/Solana/LGSolanaDeploymentConfig.asset`
- Set `Solana Network` to `Mainnet` (this now flips Unity RPC usage to mainnet across managers).
- Set `Skr Mint Mode` to `MainnetReal`.
- Real SKR mint already configured in code:
  - `SKRbvo6Gf7GondiT3BbTfuRDPqLWei4j2Qy2NPGZhW3`

## 2) Move all gameplay RPC + program config to mainnet
- In `LGSolanaDeploymentConfig.asset`, confirm mainnet RPC fields are set:
  - `mainnetRpcUrl`
  - `mainnetFallbackRpcUrl`
- Ensure program IDs/global config are mainnet values (not devnet).

## 3) Seeker ID lookup config (mainnet)
- Keep Seeker ID lookup on mainnet.
- Preferred: use local secret asset (not committed) for Helius/API keys:
  - `Assets/Resources/LocalSecrets/LocalSeekerIdentityConfig.asset`
- Set:
  - mainnet RPC URL
  - optional fallback mainnet RPC URLs
  - enhanced history URL template(s), if used

## 4) Secret safety
- Do not put API keys in scene/prefab serialized fields.
- Keep keys only in local ignored assets under:
  - `Assets/Resources/LocalSecrets/`
- Rotate any key that was ever pasted/shared publicly.

## 5) Final verification pass
- Test on physical Seeker device build:
  - wallet connect
  - SOL/SKR balances visible
  - session status becomes ready
  - SKR name resolution (falls back gracefully)
  - create character + enter dungeon
- Confirm no devnet-only labels/endpoints remain.

## 6) Legacy account compatibility gate
- If a wallet has old/incompatible onchain player account data, block gameplay until user action.
- Show a clear blocking message:
  - account is from an older version
  - reset is required to continue
- Require explicit user confirmation for reset (do not auto-reset silently).
- Ensure reset path is available in-app before release (for devnet/testing environments).
- Version bump knobs:
  - Onchain account version constant: `solana-program/programs/chaindepth/src/state/player.rs` (`PlayerAccount::CURRENT_DATA_VERSION`)
  - Unity required version gate: `Assets/Scripts/Solana/LGConfig.cs` (`REQUIRED_PLAYER_ACCOUNT_DATA_VERSION`)
  - For a breaking change, bump both and deploy.
