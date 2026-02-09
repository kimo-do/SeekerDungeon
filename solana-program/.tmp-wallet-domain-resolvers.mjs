import { Connection, PublicKey } from "@solana/web3.js";
import {
  getFavoriteDomain,
  getDomainKeysWithReverses,
} from "../.tmp/package/dist/esm/index.js";

const connection = new Connection("https://api.mainnet-beta.solana.com", "confirmed");
const wallet = new PublicKey("CbBMc9dnTg1vBAWijVqi6chZ1JYvtMwdkn1hpZyCBW6s");

const run = async () => {
  console.log("wallet:", wallet.toBase58());

  try {
    const favorite = await getFavoriteDomain(connection, wallet);
    console.log("favorite_domain:", favorite);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.log("favorite_domain_error:", message);
  }

  try {
    const domains = await getDomainKeysWithReverses(connection, wallet);
    console.log("domain_keys_with_reverses_count:", domains.length);
    console.log(
      "domain_keys_with_reverses_sample:",
      JSON.stringify(domains.slice(0, 20).map((d) => ({ pubKey: d.pubKey.toBase58(), domain: d.domain })), null, 2)
    );
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.log("domain_keys_with_reverses_error:", message);
  }
};

run().catch((error) => {
  console.error(error);
  process.exit(1);
});
