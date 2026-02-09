import crypto from "node:crypto";
import { Connection, PublicKey } from "@solana/web3.js";

const NAME_PROGRAM_ID = new PublicKey("namesLPneVptA9Z5rqUDD9tMTWEJwofgaYwp8cawRkX");
const ROOT_DOMAIN_ACCOUNT = new PublicKey("58PwtjSDuFHuUkYjH9BYnnQKHfwo9reZhC2zMJv9JPkx");
const REVERSE_LOOKUP_CLASS = new PublicKey("33m47vH6Eav6jr5Ry86XjhRft2jRBLDnDgPSHoquXi2Z");

const connection = new Connection("https://api.mainnet-beta.solana.com", "confirmed");

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

const getDomainKeySimple = (domain) => getNameAccountKey(hashName(domain), undefined, ROOT_DOMAIN_ACCOUNT);

const getDomainKeySyncLikeSns = (domain) => {
  if (domain.endsWith(".sol")) {
    domain = domain.slice(0, -4);
  }

  const labels = domain.split(".");
  if (labels.length === 1) {
    return getDomainKeySimple(labels[0]);
  }

  if (labels.length === 2) {
    const parent = getDomainKeySimple(labels[1]);
    const subLabel = `${String.fromCharCode(0)}${labels[0]}`;
    return getNameAccountKey(hashName(subLabel), undefined, parent);
  }

  throw new Error(`Unsupported domain format for quick probe: ${domain}`);
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

const readOwner = (accountData) => new PublicKey(accountData.subarray(32, 64));

const run = async () => {
  const reverseAccounts = await connection.getProgramAccounts(NAME_PROGRAM_ID, {
    filters: [{ memcmp: { offset: 64, bytes: REVERSE_LOOKUP_CLASS.toBase58() } }],
  });

  let firstSolName = null;
  let skrDotMatches = 0;

  for (const entry of reverseAccounts) {
    if (entry.account.data.length <= 96) continue;
    const name = decodeReverse(entry.account.data.subarray(96));
    if (!name) continue;

    const lower = name.toLowerCase();
    if (lower.endsWith(".skr")) {
      skrDotMatches += 1;
    }

    if (!firstSolName && lower.endsWith(".sol") && !lower.includes("\n") && lower.length < 64) {
      firstSolName = name;
    }
  }

  console.log("reverse_accounts_count:", reverseAccounts.length);
  console.log("skr_dot_matches:", skrDotMatches);
  console.log("sample_sol_name:", firstSolName ?? "<none>");

  if (!firstSolName) {
    return;
  }

  const sampleSolDomainKey = getDomainKeySyncLikeSns(firstSolName);
  const sampleSolDomainAccount = await connection.getAccountInfo(sampleSolDomainKey);

  if (!sampleSolDomainAccount) {
    console.log("sample_sol_domain_account_missing:", sampleSolDomainKey.toBase58());
    return;
  }

  const sampleOwner = readOwner(sampleSolDomainAccount.data);
  console.log("sample_owner_from_sol:", sampleOwner.toBase58());

  const solParent = getDomainKeySimple("sol");
  const skrParent = getDomainKeySimple("skr");

  const solOwned = await connection.getProgramAccounts(NAME_PROGRAM_ID, {
    filters: [
      { memcmp: { offset: 0, bytes: solParent.toBase58() } },
      { memcmp: { offset: 32, bytes: sampleOwner.toBase58() } },
    ],
    dataSlice: { offset: 0, length: 0 },
  });

  const skrOwned = await connection.getProgramAccounts(NAME_PROGRAM_ID, {
    filters: [
      { memcmp: { offset: 0, bytes: skrParent.toBase58() } },
      { memcmp: { offset: 32, bytes: sampleOwner.toBase58() } },
    ],
    dataSlice: { offset: 0, length: 0 },
  });

  console.log("sample_owner_sol_domain_count:", solOwned.length);
  console.log("sample_owner_skr_domain_count:", skrOwned.length);
};

run().catch((error) => {
  console.error(error);
  process.exit(1);
});
