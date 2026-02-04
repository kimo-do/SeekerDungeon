/**
 * Mint test SKR tokens to a wallet
 * 
 * Usage: npx ts-node scripts/mint-test-tokens.ts <wallet_address> [amount]
 * Example: npx ts-node scripts/mint-test-tokens.ts 7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU 100
 */

import * as anchor from "@coral-xyz/anchor";
import {
  createAssociatedTokenAccount,
  getAssociatedTokenAddress,
  mintTo,
} from "@solana/spl-token";
import * as fs from "fs";
import * as path from "path";
import dotenv from "dotenv";

dotenv.config();

async function main() {
  const args = process.argv.slice(2);
  
  if (args.length < 1) {
    console.log("Usage: npx ts-node scripts/mint-test-tokens.ts <wallet_address> [amount]");
    console.log("Example: npx ts-node scripts/mint-test-tokens.ts 7xKXtg... 100");
    process.exit(1);
  }

  const targetWallet = new anchor.web3.PublicKey(args[0]);
  const amount = parseFloat(args[1] || "10") * 10 ** 9; // Default 10 SKR

  // Load config
  const configPath = path.join(__dirname, "..", "devnet-config.json");
  if (!fs.existsSync(configPath)) {
    console.error("Error: devnet-config.json not found. Run init-devnet.ts first.");
    process.exit(1);
  }

  const config = JSON.parse(fs.readFileSync(configPath, "utf-8"));
  const skrMint = new anchor.web3.PublicKey(config.skrMint);

  // Setup provider
  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);
  const admin = provider.wallet;

  console.log("=== Mint Test SKR Tokens ===");
  console.log("Target wallet:", targetWallet.toBase58());
  console.log("Amount:", amount / 10 ** 9, "SKR");
  console.log("SKR Mint:", skrMint.toBase58());

  // Get or create target's ATA
  const targetAta = await getAssociatedTokenAddress(skrMint, targetWallet);
  
  try {
    await createAssociatedTokenAccount(
      provider.connection,
      (admin as any).payer,
      skrMint,
      targetWallet
    );
    console.log("Created ATA:", targetAta.toBase58());
  } catch (e) {
    console.log("ATA already exists:", targetAta.toBase58());
  }

  // Mint tokens
  await mintTo(
    provider.connection,
    (admin as any).payer,
    skrMint,
    targetAta,
    admin.publicKey,
    amount
  );

  console.log("✅ Minted", amount / 10 ** 9, "SKR to", targetWallet.toBase58());
}

main().catch((e) => {
  console.error("Error:", e.message);
  process.exit(1);
});
