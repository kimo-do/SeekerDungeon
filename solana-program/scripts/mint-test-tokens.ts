/**
 * Mint test SKR tokens to a wallet
 *
 * Usage: npm run mint-tokens <wallet_address> [amount]
 * Example: npm run mint-tokens 7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU 100
 */

import { connect } from "solana-kite";
import { address, type Address } from "@solana/kit";
import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";
import dotenv from "dotenv";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
import { SKR_MULTIPLIER, DEFAULT_TEST_MINT_AMOUNT } from "./constants";

dotenv.config();

interface DevnetConfig {
  skrMint: string;
  programId: string;
  globalPda: string;
  prizePoolPda: string;
  network: string;
  rpcUrl: string;
}

async function main(): Promise<void> {
  const args = process.argv.slice(2);

  if (args.length < 1) {
    console.log("Usage: npm run mint-tokens <wallet_address> [amount]");
    console.log("Example: npm run mint-tokens 7xKXtg... 100");
    process.exit(1);
  }

  const targetWalletAddress = address(args[0]);
  const amountInput = args[1] ? parseFloat(args[1]) : DEFAULT_TEST_MINT_AMOUNT / SKR_MULTIPLIER;
  const amount = BigInt(Math.floor(amountInput * SKR_MULTIPLIER));

  // Load config
  const configPath = path.join(__dirname, "..", "devnet-config.json");
  if (!fs.existsSync(configPath)) {
    console.error("Error: devnet-config.json not found. Run init-devnet first.");
    process.exit(1);
  }

  const config: DevnetConfig = JSON.parse(fs.readFileSync(configPath, "utf-8"));
  const skrMintAddress = address(config.skrMint);

  // Setup Kite connection
  const connection = connect("devnet");
  const admin = await connection.loadWalletFromFile();

  console.log("=== Mint Test SKR Tokens ===");
  console.log("Target wallet:", targetWalletAddress);
  console.log("Amount:", Number(amount) / SKR_MULTIPLIER, "SKR");
  console.log("SKR Mint:", skrMintAddress);

  // Mint tokens using Kite
  await connection.mintTokens({
    mint: skrMintAddress,
    mintAuthority: admin,
    destination: targetWalletAddress,
    amount: amount,
  });

  console.log("✅ Minted", Number(amount) / SKR_MULTIPLIER, "SKR to", targetWalletAddress);
}

main().catch((error: unknown) => {
  const message = error instanceof Error ? error.message : String(error);
  console.error("Error:", message);
  process.exit(1);
});
