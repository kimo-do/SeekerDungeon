import crypto from "node:crypto";
import { Connection, PublicKey } from "@solana/web3.js";

const NAME_PROGRAM_ID = new PublicKey("namesLPneVptA9Z5rqUDD9tMTWEJwofgaYwp8cawRkX");
const ROOT_DOMAIN_ACCOUNT = new PublicKey("58PwtjSDuFHuUkYjH9BYnnQKHfwo9reZhC2zMJv9JPkx");
const REVERSE_LOOKUP_CLASS = new PublicKey("33m47vH6Eav6jr5Ry86XjhRft2jRBLDnDgPSHoquXi2Z");

const hashName = (name) => Buffer.from(crypto.createHash("sha256").update(Buffer.from(`SPL Name Service${name}`, "utf8")).digest());
const getNameAccountKey = (hashedName, nameClass, parentName) => {
  const seeds = [hashedName, nameClass ? nameClass.toBuffer() : Buffer.alloc(32), parentName ? parentName.toBuffer() : Buffer.alloc(32)];
  return PublicKey.findProgramAddressSync(seeds, NAME_PROGRAM_ID)[0];
};
const decodeReverse = (bytes) => {
  if (!bytes || bytes.length < 4) return null;
  const len = bytes.readUInt32LE(0);
  if (len === 0 || len > bytes.length - 4) return null;
  return bytes.subarray(4, 4 + len).toString().replace(/^\0/, "");
};

const connection = new Connection("https://api.mainnet-beta.solana.com", "confirmed");
const solParent = getNameAccountKey(hashName("sol"), undefined, ROOT_DOMAIN_ACCOUNT);

const solAccounts = await connection.getProgramAccounts(NAME_PROGRAM_ID, {
  filters: [{ memcmp: { offset: 0, bytes: solParent.toBase58() } }],
  dataSlice: { offset: 32, length: 32 },
});

console.log("sol_parent:", solParent.toBase58());
console.log("sol_accounts_count:", solAccounts.length);

const first = solAccounts[0];
if (!first) process.exit(0);
const owner = new PublicKey(first.account.data);
console.log("sample_sol_owner:", owner.toBase58());

const ownerSolAccounts = await connection.getProgramAccounts(NAME_PROGRAM_ID, {
  filters: [
    { memcmp: { offset: 0, bytes: solParent.toBase58() } },
    { memcmp: { offset: 32, bytes: owner.toBase58() } },
  ],
  dataSlice: { offset: 0, length: 0 },
});

console.log("sample_sol_owner_domain_count:", ownerSolAccounts.length);

for (const account of ownerSolAccounts.slice(0, 3)) {
  const reverseKey = getNameAccountKey(hashName(account.pubkey.toBase58()), REVERSE_LOOKUP_CLASS, undefined);
  const reverseAccount = await connection.getAccountInfo(reverseKey);
  const reverseName = reverseAccount ? decodeReverse(reverseAccount.data.subarray(96)) : null;
  console.log("sample_sol_domain:", account.pubkey.toBase58(), "->", reverseName ?? "<no reverse>");
}
