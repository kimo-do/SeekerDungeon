# AGENTS.md

Project defaults for this repo.

## Scope
- Applies to the whole repository.
- Keep changes focused; avoid broad refactors unless requested.

## Repo Layout
- `solana-program/` = Anchor program + TS scripts/tests.
- `Assets/` = Unity C# game/client code.

## Collaboration Context
- The human developer is strong in Unity/C# and new to Solana/Anchor/Rust.
- Treat blockchain architecture/constraints as the AI's responsibility.
- Explain Solana constraints simply when they affect requested gameplay behavior.

## Solana Workflow
- For `solana-program`, run commands via:
  - `solana-program/scripts/wsl/run.sh`
  - `solana-program/scripts/wsl/build.sh`
- Prefer `npm` (never `yarn`).
- Before finishing Solana changes, run:
  - `npm test`
- If session auth logic changed, also run:
  - `npm run smoke-session-join-job`
- Use `solana-program/AI_HANDOFF.md` as the operational reference.

## Unity Workflow
- If program accounts/instructions/events change:
  1. Build program to refresh IDL.
  2. Regenerate Unity client from IDL (`Assets/Scripts/Solana/Generated/LGClient.cs`).
  3. Update domain wrappers/mappers in `Assets/Scripts/Solana/LGDomainModels.cs` when needed.
- Keep game-facing code using DomainModels/wrappers instead of raw generated account structs where practical.

## Safety
- Never print or commit private key contents.
- Do not edit wallet JSON files unless explicitly asked.

## Communication
- Keep updates short and actionable.
- If blocked, report exact command/error and next step.
