import crypto from "node:crypto";
import { Connection, PublicKey } from "@solana/web3.js";

const NAME_PROGRAM_ID = new PublicKey("namesLPneVptA9Z5rqUDD9tMTWEJwofgaYwp8cawRkX");
const ROOT_DOMAIN_ACCOUNT = new PublicKey("58PwtjSDuFHuUkYjH9BYnnQKHfwo9reZhC2zMJv9JPkx");
const REVERSE_LOOKUP_CLASS = new PublicKey("33m47vH6Eav6jr5Ry86XjhRft2jRBLDnDgPSHoquXi2Z");
const MAINNET_RPC = "https://api.mainnet-beta.solana.com";

const hashName = (name) => {
  const input = Buffer.from(`SPL Name Service${name}`, "utf8");
  return Buffer.from(crypto.createHash("sha256").update(input).digest());
};

const getNameAccountKey = (hashedName, nameClass, parentName) => {
  const seeds = [hashedName];
  seeds.push(nameClass ? nameClass.toBuffer() : Buffer.alloc(32));
  seeds.push(parentName ? parentName.toBuffer() : Buffer.alloc(32));
  const [pubkey] = PublicKey.findProgramAddressSync(seeds, NAME_PROGRAM_ID);
  return pubkey;
};

const getDomainKey = (domain) => {
  const hashed = hashName(domain);
  return getNameAccountKey(hashed, undefined, ROOT_DOMAIN_ACCOUNT);
};

const getReverseKeyFromDomainKey = (domainKey) => {
  const hashed = hashName(domainKey.toBase58());
  return getNameAccountKey(hashed, REVERSE_LOOKUP_CLASS, undefined);
};

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

const readOwner = (accountData) => {
  if (!accountData || accountData.length < 64) return null;
  return new PublicKey(accountData.subarray(32, 64));
};

const main = async () => {
  const connection = new Connection(MAINNET_RPC, "confirmed");

  const skrTldKey = getDomainKey("skr");
  const skrTldAccount = await connection.getAccountInfo(skrTldKey);

  console.log("RPC:", MAINNET_RPC);
  console.log("skr_tld_key:", skrTldKey.toBase58());
  console.log("skr_tld_exists:", Boolean(skrTldAccount));

  if (!skrTldAccount) {
    console.log("No onchain .skr TLD account found via SNS derivation.");
    return;
  }

  const skrDomainAccounts = await connection.getProgramAccounts(NAME_PROGRAM_ID, {
    filters: [{ memcmp: { offset: 0, bytes: skrTldKey.toBase58() } }],
  });

  console.log("skr_domains_count:", skrDomainAccounts.length);

  if (skrDomainAccounts.length === 0) {
    console.log(".skr TLD exists but no subdomains found.");
    return;
  }

  const firstDomain = skrDomainAccounts[0];
  const firstOwner = readOwner(firstDomain.account.data);
  console.log("sample_domain_key:", firstDomain.pubkey.toBase58());
  console.log("sample_owner:", firstOwner?.toBase58() ?? "<none>");

  const reverseKey = getReverseKeyFromDomainKey(firstDomain.pubkey);
  const reverseAccount = await connection.getAccountInfo(reverseKey);
  const reverseName = reverseAccount ? decodeReverse(reverseAccount.data.subarray(96)) : null;
  console.log("sample_reverse_key:", reverseKey.toBase58());
  console.log("sample_reverse_name:", reverseName ?? "<none>");

  if (firstOwner) {
    const ownerFiltered = await connection.getProgramAccounts(NAME_PROGRAM_ID, {
      filters: [
        { memcmp: { offset: 0, bytes: skrTldKey.toBase58() } },
        { memcmp: { offset: 32, bytes: firstOwner.toBase58() } },
      ],
    });

    console.log("owner_filtered_count:", ownerFiltered.length);

    const resolvedNames = [];
    for (const entry of ownerFiltered.slice(0, 5)) {
      const key = getReverseKeyFromDomainKey(entry.pubkey);
      const acc = await connection.getAccountInfo(key);
      const name = acc ? decodeReverse(acc.data.subarray(96)) : null;
      resolvedNames.push({ domainKey: entry.pubkey.toBase58(), reverse: name ?? null });
    }

    console.log("owner_filtered_resolved:", JSON.stringify(resolvedNames, null, 2));
  }

  const repoSessionWallet = new PublicKey("2eoK8KdoAJ9hgBjoUbQY6SQypJmNYeEnyxFvkQaWXELP");
  const repoSessionMatches = await connection.getProgramAccounts(NAME_PROGRAM_ID, {
    filters: [
      { memcmp: { offset: 0, bytes: skrTldKey.toBase58() } },
      { memcmp: { offset: 32, bytes: repoSessionWallet.toBase58() } },
    ],
    dataSlice: { offset: 0, length: 0 },
  });
  console.log("repo_session_wallet_matches:", repoSessionMatches.length);
};

main().catch((error) => {
  console.error("probe_error:", error);
  process.exit(1);
});
