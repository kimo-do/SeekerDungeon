# Next Session TODO (Auth + Real-time Scale)

Context:
- We added `PlayerProfile` (`skin_id`, `display_name`) and `RoomPresence` indexing.
- Onboarding flow now supports `create_player_profile(skin_id, display_name)` and grants starter bronze pickaxe once.
- Next bottlenecks are signature friction and high-frequency room updates.

## 1) Session Keys / Delegated Signing (High Priority)

Goal:
- Avoid Seed Vault prompt on every gameplay action.

Why:
- Current model needs wallet signature for each tx.
- For mobile gameplay this kills UX (too many confirmations).

Implement:
- Add a `SessionAuthority` PDA tying:
  - player wallet
  - session pubkey
  - expiry slot/timestamp
  - instruction allowlist
  - max token spend cap / scope
- Add `begin_session` instruction (wallet signs once).
- Update gameplay instructions to accept either:
  - wallet signer
  - valid session signer with policy checks.
- Add `end_session` / revoke support.

## 2) Real-time Room Occupant Sync via Presence Subscriptions (High Priority)

Goal:
- Spawn/update nearby players instantly with skin + weapon + activity.

Why:
- Polling is wasteful and laggy.
- We already have indexed `RoomPresence` designed exactly for this.

Implement:
- On room load:
  - fetch room presences for current room
  - call `StartRoomOccupantSubscriptions(roomX, roomY)`
- Maintain local `Dictionary<wallet, occupant>` cache in Unity.
- On presence update:
  - spawn/despawn/update character model
  - switch animation by activity:
    - idle
    - door job + direction
    - boss fight
- Add unsubscribe/cleanup when leaving a room.

## 3) Optional Follow-up

- Add `skin_id` + `display_name` fields to in-room nameplates.
- Add profile edit screen (rename + reskin) with cooldown or fee if needed.
