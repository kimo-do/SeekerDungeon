# Seeker ID API

Small backend service for resolving wallet -> `.skr` display names with cache-first behavior.

## Endpoints

- `GET /healthz`
- `GET /seeker-id/resolve?wallet=<pubkey>`

Response shape:

```json
{
  "found": true,
  "seekerId": "asynkimo.skr",
  "source": "enhanced_history",
  "updatedAtUnix": 1739220000
}
```

## Local Run

1. Copy `.env.example` to `.env` and fill values.
2. Install dependencies:
   - `npm install`
3. Start dev server:
   - `npm run dev`

## Required Environment Variables

- `HELIUS_API_KEY`
- `PORT` (default `3000`)
- `MAINNET_RPC_URL` (optional override; defaults to Helius mainnet RPC built from `HELIUS_API_KEY`)
- `CACHE_TTL_SECONDS` (default `86400`)
- `NEGATIVE_CACHE_TTL_SECONDS` (default `21600`)
- `MAX_SIGNATURE_SCAN` (default `40`, bounded fallback scan)
- `MAX_TRANSACTIONS_PER_LOOKUP` (default `40`, hard cap per request)
- `REQUEST_TIMEOUT_MS` (default `5000`)
- `LOG_DEBUG` (`true` or `false`)

## Railway Deployment

1. Create a new Railway service from this repo.
2. Set service root directory to `seeker-id-api`.
3. Add the environment variables listed above.
4. Railway start command:
   - `npm run start`
5. Use the generated public URL in Unity config:
   - `https://<your-service>.up.railway.app/seeker-id/resolve?wallet={address}`

## Notes

- `source=cache` means the result came from in-memory cache.
- Backend does not block gameplay; Unity should still keep short-wallet fallback.
- RPC fallback disables automatic retry-on-rate-limit to avoid runaway call bursts.
