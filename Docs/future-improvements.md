# Future Improvements (Concise)

## High-value follow-ups
- Add a safety layer for `force_reset_season` in production:
  - multisig admin, timelock, or explicit confirmation flag/event trail.
- Add a small admin tooling surface:
  - one command/dashboard for season status, reset actions, and post-reset verification.
- Improve room presence subscription lifecycle:
  - explicitly unsubscribe old room listeners on room travel to avoid long-session buildup.
- Expand interaction UX:
  - lightweight tap feedback + pending tx state to reduce double taps and uncertainty.
- Add focused integration tests for room travel:
  - open-door move path, fade transition, and room occupant swap.

## Optional polish
- Reduce noisy recurring Anchor build warnings where practical.
- Add a short “ops runbook” for common devnet admin actions.
