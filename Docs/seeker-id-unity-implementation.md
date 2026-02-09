# Seeker ID (`.skr`) Resolution in Unity (Repo Handoff)

Last verified: 2026-02-09 (mainnet)

## Goal
Use Seeker ID (for example `asynkimo.skr`) as the default in-game display name after wallet connect, with safe fallbacks.

## What We Verified Onchain
For wallet `CbBMc9dnTg1vBAWijVqi6chZ1JYvtMwdkn1hpZyCBW6s`:

- `asynkimo.skr` exists and maps to that wallet.
- Registration tx exists: `4hBMbPfqyrK8yYbehqcS5PYKYmaMAoz1JuYc8KhepmJFeUtYHdCKUe338WHmwRDRzUe8LFPq9exHZwSxDga52c6y`
- Tx logs include: `Buying domain asynkimo.skr`
- Tx includes program `TLDHkysf5pCnKsVA4gXpNvmy7psXLPEu4LAdDJthT9S`
- Tx also invokes:
  - `ALTNSZ46uaAUU7XUV6awvdorLGqAsPwa9shm7h4uP2FK`
  - `VALiD2QJKbrPVHAMRUQ3JnURxXjE3KvX663gWuYFGmY`

Important: classic SNS reverse-lookup (`namesLP...`) did not return usable `.skr` mappings in our direct tests. `.skr` resolution appears to be TLDH/ALTNS-specific, not a simple SNS owner scan.

## Practical Architecture (Recommended)
Do not make gameplay dependent on `.skr` resolution.

1. Connect wallet normally (MWA via `LGWalletSessionManager`).
2. Immediately set temporary name to short wallet (`CbBM...BW6s`).
3. Resolve `.skr` asynchronously.
4. If resolution succeeds, replace display name with `.skr`.
5. Cache result locally and refresh opportunistically.

This gives best UX without introducing login fragility.

## Unity Integration Points In This Repo
- Wallet lifecycle: `Assets/Scripts/Solana/LGWalletSessionManager.cs`
  - Use `OnWalletConnectionChanged`
  - Read `ConnectedWalletPublicKey`
- Loading/connect flow: `Assets/Scripts/Solana/LGLoadingController.cs`
- Profile/menu name usage:
  - `Assets/Scripts/Solana/LGMainMenuCharacterManager.cs`
  - `Assets/Scripts/Solana/LGMainMenuCharacterUI.cs`
  - `Assets/Scripts/Solana/LGDomainModels.cs` (short wallet fallback behavior)

## Resolver Design
Create a dedicated component/service, for example:

- `SeekerIdentityManager` (Unity-facing orchestrator)
- `ISeekerIdResolver` (provider interface)
- Providers:
  - `BackendResolver` (recommended primary)
  - `DirectOnchainResolver` (optional advanced fallback)

### Why backend-first
Unity clients should avoid large onchain scans and fragile binary decoding logic for unknown account layouts. Backend can cache, rate-limit, and evolve decoder logic independently.

### Backend contract (suggested)
- Request: `GET /seeker-id/resolve?wallet=<pubkey>`
- Response:
  - `found: boolean`
  - `seekerId: string | null` (for example `asynkimo.skr`)
  - `source: "tldh" | "indexer" | "cache"`
  - `updatedAtUnix: number`

## Verification Tiers (Use In Order)
Tier 1: Name resolution
- Resolve wallet -> `.skr`.
- If missing, keep short wallet fallback.

Tier 2: Device/identity confidence (optional for gated rewards)
- Check Seeker-related proofs (for example SGT ownership policy your game defines).
- Keep this separate from display name logic.

Tier 3: Signed session/auth (optional)
- SIWS/message signing when you need high-assurance access control.

## Caching Policy
- Key by wallet address.
- Cache TTL: 10-30 minutes in-memory.
- Persist last known `.skr` locally (PlayerPrefs or local save) to reduce cold-start flicker.
- Never block scene load on cache miss.

## UI Rules
- Initial label: short wallet.
- On resolve success: switch to `.skr`.
- If player already set a custom onchain display name, keep custom name as highest priority.

Suggested priority:
1. User custom profile display name
2. Resolved `.skr`
3. Short wallet

## Error Handling
- Handle resolver timeout as non-fatal.
- Handle RPC/indexer 429 and network errors with exponential backoff.
- Log diagnostic source and timing, but do not surface noisy errors to user.

## Notes On External Claims
The long-form reference doc is directionally useful (MWA flow, identity tiers), but parts are inconsistent with live behavior. Treat these as guidance, not authority:

- `.skr` resolution was not reliably obtainable through plain SNS reverse lookup in our tests.
- TLDH/ALTNS program path is currently the reliable onchain signal.

## Minimal Implementation Sequence
1. Add `SeekerIdentityManager` and wire it to `LGWalletSessionManager.OnWalletConnectionChanged`.
2. Implement backend resolver call with timeout (for example 2-3 seconds soft timeout).
3. Update menu/HUD name binding to consume `ResolvedSeekerId`.
4. Add cache and fallback behavior.
5. Add debug toggle and logs for resolver source/latency.

## Optional Future Work
- Add server-side signature verification when resolver is used for reward eligibility.
- Add analytics: resolution success rate, latency, fallback rate.
- If TLDH publishes stable IDL/spec, add deterministic direct-onchain resolver and compare against backend.

