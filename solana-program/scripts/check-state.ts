/**
 * Check current game state on devnet
 *
 * Usage: npm run check-state
 *
 * Note: Uses @coral-xyz/anchor for fetching program accounts until
 * Codama client is generated. Run `npm run codama` after building
 * to generate the type-safe client.
 */

import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import type { Chaindepth } from "../target/types/chaindepth";
import { connect } from "solana-kite";
import { address, type Address } from "@solana/kit";
import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";
import dotenv from "dotenv";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
import {
  GLOBAL_SEED,
  SECONDS_PER_DAY,
  SECONDS_PER_HOUR,
  SECONDS_PER_MINUTE,
  SECONDS_PER_SLOT,
  DEVNET_RPC_URL,
} from "./constants";

dotenv.config();

function formatDuration(seconds: number): string {
  if (seconds < 0) return "Season ended";

  const days = Math.floor(seconds / SECONDS_PER_DAY);
  const hours = Math.floor((seconds % SECONDS_PER_DAY) / SECONDS_PER_HOUR);
  const mins = Math.floor((seconds % SECONDS_PER_HOUR) / SECONDS_PER_MINUTE);

  if (days > 0) return `${days}d ${hours}h ${mins}m`;
  if (hours > 0) return `${hours}h ${mins}m`;
  return `${mins}m`;
}

async function main(): Promise<void> {
  // Setup Anchor provider (needed for fetching program accounts)
  const anchorProvider = anchor.AnchorProvider.env();
  anchor.setProvider(anchorProvider);

  const program = anchor.workspace.Chaindepth as Program<Chaindepth>;

  // Setup Kite connection
  const connection = connect("devnet");

  console.log("=== ChainDepth State Check ===\n");
  console.log("Program ID:", program.programId.toBase58());

  // Derive global PDA using Kite
  const programAddress = address(program.programId.toBase58());
  const { pda: globalPda } = await connection.getPDAAndBump(programAddress, [
    GLOBAL_SEED,
  ]);
  const globalPdaPubkey = new anchor.web3.PublicKey(globalPda);

  try {
    // Fetch account using Anchor (until Codama client is available)
    const globalAccount = await program.account.globalAccount.fetch(globalPdaPubkey);

    console.log("\n--- Global State ---");
    console.log("PDA:", globalPda);
    console.log("Season Seed:", globalAccount.seasonSeed.toString());
    console.log("Current Depth:", globalAccount.depth);
    console.log("Jobs Completed:", globalAccount.jobsCompleted.toString());
    console.log("SKR Mint:", globalAccount.skrMint.toBase58());
    console.log("Prize Pool:", globalAccount.prizePool.toBase58());
    console.log("Admin:", globalAccount.admin.toBase58());

    // Get current slot using Kite
    const currentSlot = await connection.getCurrentSlot();
    const endSlot = globalAccount.endSlot.toNumber();
    const slotsRemaining = endSlot - Number(currentSlot);
    const secondsRemaining = slotsRemaining * SECONDS_PER_SLOT;

    console.log("\n--- Season Info ---");
    console.log("End Slot:", endSlot);
    console.log("Current Slot:", currentSlot);
    console.log("Slots Remaining:", slotsRemaining);
    console.log("Time Remaining:", formatDuration(secondsRemaining));

    // Check prize pool balance using Kite
    try {
      const prizePoolAddress = address(globalAccount.prizePool.toBase58());
      const prizePoolBalance = await connection.getTokenAccountBalance({
        tokenAccount: prizePoolAddress,
      });
      console.log("\n--- Prize Pool ---");
      console.log("Balance:", prizePoolBalance.uiAmount, "SKR");
    } catch {
      console.log("\n--- Prize Pool ---");
      console.log("Unable to fetch balance");
    }

    // Save updated config
    const config = {
      programId: program.programId.toBase58(),
      skrMint: globalAccount.skrMint.toBase58(),
      globalPda: globalPda,
      prizePoolPda: globalAccount.prizePool.toBase58(),
      seasonSeed: globalAccount.seasonSeed.toString(),
      network: "devnet",
      rpcUrl: DEVNET_RPC_URL,
    };

    const configPath = path.join(__dirname, "..", "devnet-config.json");
    fs.writeFileSync(configPath, JSON.stringify(config, null, 2));
    console.log("\n✅ Config updated:", configPath);
  } catch (error: unknown) {
    const errorMessage = error instanceof Error ? error.message : String(error);
    if (errorMessage.includes("Account does not exist")) {
      console.log("\n⚠️  Global state not initialized");
      console.log("Run: npm run init-devnet");
    } else {
      throw error;
    }
  }
}

main().catch((error: unknown) => {
  const message = error instanceof Error ? error.message : String(error);
  console.error("Error:", message);
  process.exit(1);
});
