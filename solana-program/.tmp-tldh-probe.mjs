import { Connection, PublicKey } from "@solana/web3.js";

const RPC = "https://api.mainnet-beta.solana.com";
const connection = new Connection(RPC, "confirmed");

const tldhProgram = new PublicKey("TLDHkysf5pCnKsVA4gXpNvmy7psXLPEu4LAdDJthT9S");
const wallet = new PublicKey("CbBMc9dnTg1vBAWijVqi6chZ1JYvtMwdkn1hpZyCBW6s");
const walletBytes = wallet.toBuffer();
const targetName = "asynkimo.skr";
const targetNameBytes = Buffer.from(targetName, "utf8");

const shortHex = (buffer) => buffer.subarray(0, 16).toString("hex");

const scanProgramAccounts = async () => {
  const accounts = await connection.getProgramAccounts(tldhProgram, {
    // no filters first; we need unknown layout discovery
  });

  console.log("rpc:", RPC);
  console.log("program:", tldhProgram.toBase58());
  console.log("program_accounts_count:", accounts.length);

  const sizeHistogram = new Map();
  let walletHits = 0;
  let nameHits = 0;
  const sampleWalletHits = [];
  const sampleNameHits = [];

  for (const entry of accounts) {
    const data = entry.account.data;
    const size = data.length;
    sizeHistogram.set(size, (sizeHistogram.get(size) ?? 0) + 1);

    if (data.includes(walletBytes)) {
      walletHits += 1;
      if (sampleWalletHits.length < 20) {
        sampleWalletHits.push({
          account: entry.pubkey.toBase58(),
          size,
          dataPrefixHex: shortHex(data),
        });
      }
    }

    if (data.includes(targetNameBytes)) {
      nameHits += 1;
      if (sampleNameHits.length < 20) {
        sampleNameHits.push({
          account: entry.pubkey.toBase58(),
          size,
          dataPrefixHex: shortHex(data),
        });
      }
    }
  }

  const sortedSizes = Array.from(sizeHistogram.entries())
    .sort((a, b) => b[1] - a[1])
    .slice(0, 20)
    .map(([size, count]) => ({ size, count }));

  console.log("program_account_size_histogram_top20:", JSON.stringify(sortedSizes, null, 2));
  console.log("wallet_byte_hits:", walletHits);
  console.log("wallet_byte_hit_samples:", JSON.stringify(sampleWalletHits, null, 2));
  console.log("name_string_hits:", nameHits);
  console.log("name_string_hit_samples:", JSON.stringify(sampleNameHits, null, 2));

  return { accounts, sampleWalletHits, sampleNameHits };
};

const scanWalletTransactions = async () => {
  const signatures = await connection.getSignaturesForAddress(wallet, { limit: 200 });
  console.log("wallet_signature_sample_count:", signatures.length);

  let touchedProgram = 0;
  const touchingSignatures = [];

  for (const sig of signatures) {
    const tx = await connection.getTransaction(sig.signature, {
      maxSupportedTransactionVersion: 0,
      commitment: "confirmed",
    });
    if (!tx) continue;

    const keys = tx.transaction.message.getAccountKeys();
    const staticKeys = keys.staticAccountKeys.map((k) => k.toBase58());
    if (staticKeys.includes(tldhProgram.toBase58())) {
      touchedProgram += 1;
      if (touchingSignatures.length < 20) {
        touchingSignatures.push({
          signature: sig.signature,
          slot: sig.slot,
          blockTime: sig.blockTime,
        });
      }
    }
  }

  console.log("wallet_tx_touching_tldh_count:", touchedProgram);
  console.log("wallet_tx_touching_tldh_samples:", JSON.stringify(touchingSignatures, null, 2));
};

const run = async () => {
  await scanProgramAccounts();
  await scanWalletTransactions();
};

run().catch((error) => {
  console.error("probe_error:", error);
  process.exit(1);
});
