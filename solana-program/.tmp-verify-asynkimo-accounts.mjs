import { Connection, PublicKey } from "@solana/web3.js";

const connection = new Connection("https://api.mainnet-beta.solana.com", "confirmed");
const tldh = new PublicKey("TLDHkysf5pCnKsVA4gXpNvmy7psXLPEu4LAdDJthT9S");

const keys = {
  domainNA: new PublicKey("F3A8kuikEiu6k2399oSJ1PWfcJYDHqpwoQ2e8psSDNuF"),
  domainTLD: new PublicKey("4RKP4BEMu5sXBfXSH7xN2owtQrnAJvhhwtBBmj9JEYkA"),
  subDomainNA: new PublicKey("Ds4kzh3JWVj1Ky6jFRaspLVxmPu5jyFP8Gi9AreoMVtt"),
  subDomainTLD: new PublicKey("B4au2bKhWFzS4GBkcbTXcir6StQVXm6VERNYBY4xxxPp"),
};

const run = async () => {
  console.log("program:", tldh.toBase58());
  for (const [label, key] of Object.entries(keys)) {
    const account = await connection.getAccountInfo(key);
    console.log(`${label}_exists:`, Boolean(account));
    if (!account) continue;
    console.log(`${label}_owner:`, account.owner.toBase58());
    console.log(`${label}_size:`, account.data.length);
    console.log(`${label}_is_tldh_owner:`, account.owner.equals(tldh));

    const signatures = await connection.getSignaturesForAddress(key, { limit: 5 });
    console.log(`${label}_recent_signatures:`, JSON.stringify(signatures.map((s) => ({ signature: s.signature, slot: s.slot, blockTime: s.blockTime })), null, 2));
  }
};

run().catch((error) => {
  console.error(error);
  process.exit(1);
});
