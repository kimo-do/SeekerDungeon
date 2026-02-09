import crypto from "node:crypto";
import { Connection, PublicKey } from "@solana/web3.js";

const RPC = "https://api.mainnet-beta.solana.com";
const connection = new Connection(RPC, "confirmed");

const NAME_PROGRAM_ID = new PublicKey("namesLPneVptA9Z5rqUDD9tMTWEJwofgaYwp8cawRkX");
const ROOT_DOMAIN_ACCOUNT = new PublicKey("58PwtjSDuFHuUkYjH9BYnnQKHfwo9reZhC2zMJv9JPkx");
const REVERSE_LOOKUP_CLASS = new PublicKey("33m47vH6Eav6jr5Ry86XjhRft2jRBLDnDgPSHoquXi2Z");

const targetWallet = new PublicKey("CbBMc9dnTg1vBAWijVqi6chZ1JYvtMwdkn1hpZyCBW6s");
const targetName = "asynkimo.skr";

const hashName = (name) => {
  const input = Buffer.from(`SPL Name Service${name}`, "utf8");
  return Buffer.from(crypto.createHash("sha256").update(input).digest());
};

const getNameAccountKey = (hashedName, nameClass, parentName) => {
  const seeds = [hashedName];
  seeds.push(nameClass ? nameClass.toBuffer() : Buffer.alloc(32));
  seeds.push(parentName ? parentName.toBuffer() : Buffer.alloc(32));
  return PublicKey.findProgramAddressSync(seeds, NAME_PROGRAM_ID)[0];
};

const getRootDomainKey = (label) => getNameAccountKey(hashName(label), undefined, ROOT_DOMAIN_ACCOUNT);

const getSubdomainKey = (label, parent, mode) => {
  let left;
  if (mode === "nul") {
    left = `\0${label}`;
  } else if (mode === "v1") {
    left = `${String.fromCharCode(1)}${label}`;
  } else {
    left = `${String.fromCharCode(2)}${label}`;
  }
  return getNameAccountKey(hashName(left), undefined, parent);
};

const getReverseKeyFromDomainKey = (domainKey) => {
  return getNameAccountKey(hashName(domainKey.toBase58()), REVERSE_LOOKUP_CLASS, undefined);
};

const decodeReverse = (data) => {
  if (!data || data.length < 4) return null;
  const len = data.readUInt32LE(0);
  if (len <= 0 || len > data.length - 4) return null;
  return data.subarray(4, 4 + len).toString().replace(/^\0/, "");
};

const registryHeader = (data) => {
  if (!data || data.length < 96) return null;
  return {
    parent: new PublicKey(data.subarray(0, 32)).toBase58(),
    owner: new PublicKey(data.subarray(32, 64)).toBase58(),
    klass: new PublicKey(data.subarray(64, 96)).toBase58(),
  };
};

const probeSpecificName = async () => {
  const [left, tld] = targetName.split(".");
  const tldKey = getRootDomainKey(tld);

  const candidates = [
    { mode: "nul", key: getSubdomainKey(left, tldKey, "nul") },
    { mode: "v1", key: getSubdomainKey(left, tldKey, "v1") },
    { mode: "v2", key: getSubdomainKey(left, tldKey, "v2") },
    { mode: "root_full", key: getRootDomainKey(targetName) },
  ];

  console.log("rpc:", RPC);
  console.log("target_wallet:", targetWallet.toBase58());
  console.log("target_name:", targetName);
  console.log("skr_tld_key:", tldKey.toBase58());

  for (const candidate of candidates) {
    const account = await connection.getAccountInfo(candidate.key);
    console.log(`candidate_${candidate.mode}_key:`, candidate.key.toBase58());
    console.log(`candidate_${candidate.mode}_exists:`, Boolean(account));
    if (account) {
      const header = registryHeader(account.data);
      console.log(`candidate_${candidate.mode}_owner:`, header?.owner ?? "<unknown>");
      console.log(`candidate_${candidate.mode}_parent:`, header?.parent ?? "<unknown>");
      const reverseKey = getReverseKeyFromDomainKey(candidate.key);
      const reverseAccount = await connection.getAccountInfo(reverseKey);
      const reverse = reverseAccount ? decodeReverse(reverseAccount.data.subarray(96)) : null;
      console.log(`candidate_${candidate.mode}_reverse:`, reverse ?? "<none>");
    }
  }

  const parentMatches = await connection.getProgramAccounts(NAME_PROGRAM_ID, {
    filters: [{ memcmp: { offset: 0, bytes: tldKey.toBase58() } }],
    dataSlice: { offset: 0, length: 0 },
  });
  console.log("skr_parent_matches:", parentMatches.length);

  const ownerMatches = await connection.getProgramAccounts(NAME_PROGRAM_ID, {
    filters: [{ memcmp: { offset: 32, bytes: targetWallet.toBase58() } }],
  });
  console.log("target_wallet_name_accounts:", ownerMatches.length);

  const ownerRows = [];
  for (const entry of ownerMatches.slice(0, 20)) {
    const header = registryHeader(entry.account.data);
    const reverseKey = getReverseKeyFromDomainKey(entry.pubkey);
    const reverseAccount = await connection.getAccountInfo(reverseKey);
    const reverse = reverseAccount ? decodeReverse(reverseAccount.data.subarray(96)) : null;
    ownerRows.push({
      nameAccount: entry.pubkey.toBase58(),
      parent: header?.parent ?? null,
      owner: header?.owner ?? null,
      class: header?.klass ?? null,
      reverse,
    });
  }
  console.log("target_wallet_name_accounts_sample:", JSON.stringify(ownerRows, null, 2));

  const ownerSkrMatches = await connection.getProgramAccounts(NAME_PROGRAM_ID, {
    filters: [
      { memcmp: { offset: 0, bytes: tldKey.toBase58() } },
      { memcmp: { offset: 32, bytes: targetWallet.toBase58() } },
    ],
    dataSlice: { offset: 0, length: 0 },
  });
  console.log("target_wallet_skr_name_accounts:", ownerSkrMatches.length);
};

probeSpecificName().catch((error) => {
  console.error("probe_error", error);
  process.exit(1);
});
