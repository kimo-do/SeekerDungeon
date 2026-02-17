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
  "source": "tldparser",
  "updatedAtUnix": 1739220000
}
```

## Local Run

1. Copy `.env.example` to `.env` and fill values.
2. Install dependencies:
   - `npm install`
3. Start dev server:
   - `npm run dev`

## Environment Variables

- `PORT` (default `3000`)
- `MAINNET_RPC_URL` (optional override; defaults to `https://api.mainnet-beta.solana.com`)
- `HELIUS_API_KEY` (optional; enables legacy Helius history fallback when `tldparser` lookup fails)
- `CACHE_TTL_SECONDS` (default `86400`)
- `NEGATIVE_CACHE_TTL_SECONDS` (default `21600`)
- `MAX_SIGNATURE_SCAN` (default `120`, bounded fallback scan)
- `MAX_TRANSACTIONS_PER_LOOKUP` (default `120`, hard cap per request)
- `REQUEST_TIMEOUT_MS` (default `5000`)
- `LOG_DEBUG` (`true` or `false`)

## Railway Deployment

1. Create a new Railway service from this repo.
2. Set service root directory to `seeker-id-api`.
3. Add the environment variables listed above.
4. No manual start command is needed:
   - Railway Railpack detects Node from `package.json` and uses `npm run start`.
   - `Procfile` is included as an explicit fallback (`web: npm run start`).
5. Use the generated public URL in Unity config:
   - `https://<your-service>.up.railway.app/seeker-id/resolve?wallet={address}`

## Notes

- `source=cache` means the result came from in-memory cache.
- `source=tldparser` means the result came from `@onsol/tldparser` reverse lookup (primary path).
- Backend does not block gameplay; Unity should still keep short-wallet fallback.
- Legacy fallback path (`enhanced_history`/`rpc_scan`) remains for resilience if primary lookup fails.
