import { Connection, PublicKey } from "@solana/web3.js";
import type { AppConfig } from "./config.js";
import { WalletLookupCache, type CacheEntry } from "./cache.js";

const BUYING_DOMAIN_REGEX = /Buying domain\s+([a-z0-9][a-z0-9-]*\.skr)\b/i;
const ANY_SKR_REGEX = /([a-z0-9][a-z0-9-]*\.skr)\b/i;

type ResolveResult = {
  found: boolean;
  seekerId: string | null;
  source: "cache" | "enhanced_history" | "rpc_scan";
  updatedAtUnix: number;
};

const extractSkrFromText = (text: string): string | null => {
  const buyingMatch = BUYING_DOMAIN_REGEX.exec(text);
  if (buyingMatch?.[1]) {
    return buyingMatch[1].toLowerCase();
  }

  const genericMatch = ANY_SKR_REGEX.exec(text);
  if (genericMatch?.[1]) {
    return genericMatch[1].toLowerCase();
  }

  return null;
};

const isLikelyBase58Pubkey = (wallet: string): boolean => {
  try {
    // Throws if invalid base58 or wrong length.
    // eslint-disable-next-line no-new
    new PublicKey(wallet);
    return true;
  } catch {
    return false;
  }
};

const normalizeWallet = (wallet: string): string => wallet.trim();

type HeliusTx = {
  description?: string;
  instructions?: Array<{ data?: string; programId?: string }>;
  events?: unknown;
};

const fetchHeliusTransactions = async (
  wallet: string,
  config: AppConfig,
  pageLimit: number,
  beforeSignature?: string
): Promise<Array<HeliusTx>> => {
  const url = new URL(`https://api-mainnet.helius-rpc.com/v0/addresses/${encodeURIComponent(wallet)}/transactions`);
  url.searchParams.set("api-key", config.heliusApiKey);
  url.searchParams.set("limit", String(Math.max(1, Math.min(100, pageLimit))));
  url.searchParams.set("sort-order", "desc");
  if (beforeSignature) {
    url.searchParams.set("before-signature", beforeSignature);
  }

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), config.requestTimeoutMs);

  try {
    const response = await fetch(url, { signal: controller.signal });
    if (!response.ok) {
      return [];
    }

    const json = (await response.json()) as unknown;
    if (!Array.isArray(json)) {
      return [];
    }

    return json as Array<HeliusTx>;
  } finally {
    clearTimeout(timeout);
  }
};

const tryResolveFromHelius = async (
  wallet: string,
  config: AppConfig,
  log: (message: string) => void
): Promise<string | null> => {
  let scanned = 0;
  let beforeSignature: string | undefined;
  let pageCount = 0;

  while (scanned < config.maxTransactionsPerLookup) {
    pageCount += 1;
    const remaining = config.maxTransactionsPerLookup - scanned;
    const page = await fetchHeliusTransactions(wallet, config, remaining, beforeSignature);
    if (page.length === 0) {
      return null;
    }

    for (const tx of page) {
      const serialized = JSON.stringify(tx);
      const seekerId = extractSkrFromText(serialized);
      if (seekerId) {
        return seekerId;
      }

      scanned += 1;
      if (scanned >= config.maxTransactionsPerLookup) {
        break;
      }
    }

    const tail = page[page.length - 1] as { signature?: string };
    beforeSignature = tail.signature;
    if (!beforeSignature) {
      break;
    }

    if (config.logDebug) {
      log(`helius_page_scanned wallet=${wallet} page=${pageCount} scanned=${scanned}`);
    }
  }

  return null;
};

const tryResolveFromRpcScan = async (
  wallet: string,
  connection: Connection,
  config: AppConfig,
  log: (message: string) => void
): Promise<string | null> => {
  let signatures;
  try {
    signatures = await connection.getSignaturesForAddress(new PublicKey(wallet), {
      limit: config.maxSignatureScan
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (config.logDebug) {
      log(`rpc_signature_scan_failed wallet=${wallet} message=${message}`);
    }
    return null;
  }

  const orderedSignatures = signatures
    .map((entry) => entry.signature)
    .filter((entry): entry is string => Boolean(entry));
  const maxToProbe = Math.max(1, Math.min(config.maxTransactionsPerLookup, orderedSignatures.length));
  const recentCount = Math.min(Math.ceil(maxToProbe / 2), orderedSignatures.length);
  const oldestCount = Math.min(maxToProbe - recentCount, orderedSignatures.length - recentCount);
  const probeOrder = [
    ...orderedSignatures.slice(0, recentCount),
    ...orderedSignatures.slice(orderedSignatures.length - oldestCount)
  ];
  const seen = new Set<string>();
  const candidateSignatures = probeOrder.filter((signature) => {
    if (seen.has(signature)) {
      return false;
    }
    seen.add(signature);
    return true;
  });

  let processed = 0;
  for (const signature of candidateSignatures) {
    let tx;
    try {
      tx = await connection.getTransaction(signature, {
        maxSupportedTransactionVersion: 0,
        commitment: "confirmed"
      });
    } catch {
      continue;
    }
    if (!tx) {
      continue;
    }

    const logMessages = tx.meta?.logMessages ?? [];
    for (const logLine of logMessages) {
      const seekerId = extractSkrFromText(logLine);
      if (seekerId) {
        return seekerId;
      }
    }

    processed += 1;
    if (processed >= maxToProbe) {
      break;
    }
  }

  if (config.logDebug) {
    log(`rpc_scan_complete wallet=${wallet} scanned=${processed} signatures=${signatures.length}`);
  }
  return null;
};

export class SeekerIdService {
  private readonly cache = new WalletLookupCache();
  private readonly inFlight = new Map<string, Promise<ResolveResult>>();
  private readonly connection: Connection;

  public constructor(private readonly config: AppConfig, private readonly log: (message: string) => void) {
    this.connection = new Connection(config.mainnetRpcUrl, {
      commitment: "confirmed",
      disableRetryOnRateLimit: true
    });
  }

  public async resolve(walletInput: string): Promise<ResolveResult> {
    const wallet = normalizeWallet(walletInput);
    if (!isLikelyBase58Pubkey(wallet)) {
      throw new Error("Invalid wallet parameter.");
    }

    const nowUnix = Math.floor(Date.now() / 1000);
    const cached = this.cache.get(wallet, nowUnix);
    if (cached) {
      return {
        found: cached.found,
        seekerId: cached.seekerId,
        source: "cache",
        updatedAtUnix: cached.updatedAtUnix
      };
    }

    const existing = this.inFlight.get(wallet);
    if (existing) {
      return existing;
    }

    const promise = this.resolveUncached(wallet, nowUnix)
      .finally(() => {
        this.inFlight.delete(wallet);
      });
    this.inFlight.set(wallet, promise);
    return promise;
  }

  private async resolveUncached(wallet: string, nowUnix: number): Promise<ResolveResult> {
    let heliusResult: string | null = null;
    try {
      heliusResult = await tryResolveFromHelius(wallet, this.config, this.log);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      if (this.config.logDebug) {
        this.log(`helius_resolve_failed wallet=${wallet} message=${message}`);
      }
    }
    if (heliusResult) {
      this.cache.set(wallet, true, heliusResult, "enhanced_history", this.config.cacheTtlSeconds, nowUnix);
      return {
        found: true,
        seekerId: heliusResult,
        source: "enhanced_history",
        updatedAtUnix: nowUnix
      };
    }

    let rpcResult: string | null = null;
    try {
      rpcResult = await tryResolveFromRpcScan(wallet, this.connection, this.config, this.log);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      if (this.config.logDebug) {
        this.log(`rpc_resolve_failed wallet=${wallet} message=${message}`);
      }
    }
    if (rpcResult) {
      this.cache.set(wallet, true, rpcResult, "rpc_scan", this.config.cacheTtlSeconds, nowUnix);
      return {
        found: true,
        seekerId: rpcResult,
        source: "rpc_scan",
        updatedAtUnix: nowUnix
      };
    }

    this.cache.set(wallet, false, null, "rpc_scan", this.config.negativeCacheTtlSeconds, nowUnix);
    return {
      found: false,
      seekerId: null,
      source: "rpc_scan",
      updatedAtUnix: nowUnix
    };
  }
}

export type { ResolveResult, CacheEntry };
