import { Connection, PublicKey } from "@solana/web3.js";

const NAME_PROGRAM_ID = new PublicKey("namesLPneVptA9Z5rqUDD9tMTWEJwofgaYwp8cawRkX");
const REVERSE_LOOKUP_CLASS = new PublicKey("33m47vH6Eav6jr5Ry86XjhRft2jRBLDnDgPSHoquXi2Z");
const connection = new Connection("https://api.mainnet-beta.solana.com", "confirmed");

const decodeReverse = (bytes) => {
  if (!bytes || bytes.length === 0) return null;
  if (bytes.length >= 4) {
    const len = bytes.readUInt32LE(0);
    if (len > 0 && len <= bytes.length - 4) {
      const text = bytes.subarray(4, 4 + len).toString("utf8").replace(/\0/g, "").trim();
      if (text) return text;
    }
  }
  const fallback = bytes.toString("utf8").replace(/\0/g, "").trim();
  return fallback || null;
};

const run = async () => {
  const reverseAccounts = await connection.getProgramAccounts(NAME_PROGRAM_ID, {
    filters: [{ memcmp: { offset: 64, bytes: REVERSE_LOOKUP_CLASS.toBase58() } }],
  });

  console.log("reverse_accounts_count:", reverseAccounts.length);

  const matches = [];
  for (const entry of reverseAccounts) {
    if (entry.account.data.length <= 96) continue;
    const name = decodeReverse(entry.account.data.subarray(96));
    if (!name) continue;
    if (name.toLowerCase().endsWith(".skr") || name.toLowerCase() === "skr") {
      matches.push({ reverseAccount: entry.pubkey.toBase58(), name });
      if (matches.length >= 20) break;
    }
  }

  console.log("skr_like_matches_found:", matches.length);
  console.log("skr_like_matches:", JSON.stringify(matches, null, 2));
};

run().catch((error) => {
  console.error(error);
  process.exit(1);
});
