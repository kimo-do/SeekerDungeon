const parseIntOrDefault = (value: string | undefined, fallback: number): number => {
  if (!value) {
    return fallback;
  }

  const parsed = Number.parseInt(value, 10);
  if (!Number.isFinite(parsed) || parsed <= 0) {
    return fallback;
  }

  return parsed;
};

export type AppConfig = {
  port: number;
  heliusApiKey: string;
  mainnetRpcUrl: string;
  cacheTtlSeconds: number;
  negativeCacheTtlSeconds: number;
  maxSignatureScan: number;
  maxTransactionsPerLookup: number;
  requestTimeoutMs: number;
  logDebug: boolean;
};

export const loadConfig = (): AppConfig => {
  const heliusApiKey = (process.env.HELIUS_API_KEY ?? "").trim();
  if (!heliusApiKey) {
    throw new Error("Missing HELIUS_API_KEY environment variable.");
  }

  const defaultHeliusMainnetRpcUrl = `https://mainnet.helius-rpc.com/?api-key=${encodeURIComponent(heliusApiKey)}`;
  const mainnetRpcUrl = (process.env.MAINNET_RPC_URL ?? defaultHeliusMainnetRpcUrl).trim();
  const cacheTtlSeconds = parseIntOrDefault(process.env.CACHE_TTL_SECONDS, 86400);
  const negativeCacheTtlSeconds = parseIntOrDefault(process.env.NEGATIVE_CACHE_TTL_SECONDS, 21600);
  const maxSignatureScan = parseIntOrDefault(process.env.MAX_SIGNATURE_SCAN, 120);
  const maxTransactionsPerLookup = parseIntOrDefault(process.env.MAX_TRANSACTIONS_PER_LOOKUP, 120);
  const requestTimeoutMs = parseIntOrDefault(process.env.REQUEST_TIMEOUT_MS, 5000);
  const port = parseIntOrDefault(process.env.PORT, 3000);
  const logDebug = (process.env.LOG_DEBUG ?? "false").trim().toLowerCase() === "true";

  return {
    port,
    heliusApiKey,
    mainnetRpcUrl,
    cacheTtlSeconds,
    negativeCacheTtlSeconds,
    maxSignatureScan,
    maxTransactionsPerLookup,
    requestTimeoutMs,
    logDebug
  };
};
