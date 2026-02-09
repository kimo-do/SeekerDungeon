import { Connection, PublicKey } from "@solana/web3.js";

const connection = new Connection("https://api.mainnet-beta.solana.com", "confirmed");
const signature = "4hBMbPfqyrK8yYbehqcS5PYKYmaMAoz1JuYc8KhepmJFeUtYHdCKUe338WHmwRDRzUe8LFPq9exHZwSxDga52c6y";
const wallet = new PublicKey("CbBMc9dnTg1vBAWijVqi6chZ1JYvtMwdkn1hpZyCBW6s").toBase58();
const tldh = "TLDHkysf5pCnKsVA4gXpNvmy7psXLPEu4LAdDJthT9S";
const altns = "ALTNSZ46uaAUU7XUV6awvdorLGqAsPwa9shm7h4uP2FK";

const tx = await connection.getTransaction(signature, { maxSupportedTransactionVersion: 0, commitment: "confirmed" });
if (!tx) {
  console.log("tx_missing");
  process.exit(0);
}

const keys = tx.transaction.message.getAccountKeys().staticAccountKeys.map((k) => k.toBase58());
console.log("signature:", signature);
console.log("slot:", tx.slot);
console.log("blockTime:", tx.blockTime);
console.log("contains_wallet:", keys.includes(wallet));
console.log("contains_tldh_program:", keys.includes(tldh));
console.log("contains_altns_program:", keys.includes(altns));
console.log("account_keys:", JSON.stringify(keys, null, 2));
console.log("log_messages:", JSON.stringify(tx.meta?.logMessages ?? [], null, 2));
