export type CacheEntry = {
  found: boolean;
  seekerId: string | null;
  source: "cache" | "enhanced_history" | "rpc_scan";
  updatedAtUnix: number;
  expiresAtUnix: number;
};

export class WalletLookupCache {
  private readonly store = new Map<string, CacheEntry>();

  public get(wallet: string, nowUnix: number): CacheEntry | null {
    const entry = this.store.get(wallet);
    if (!entry) {
      return null;
    }

    if (entry.expiresAtUnix <= nowUnix) {
      this.store.delete(wallet);
      return null;
    }

    return entry;
  }

  public set(
    wallet: string,
    found: boolean,
    seekerId: string | null,
    source: "enhanced_history" | "rpc_scan",
    ttlSeconds: number,
    nowUnix: number
  ): CacheEntry {
    const entry: CacheEntry = {
      found,
      seekerId,
      source,
      updatedAtUnix: nowUnix,
      expiresAtUnix: nowUnix + ttlSeconds
    };
    this.store.set(wallet, entry);
    return entry;
  }
}
