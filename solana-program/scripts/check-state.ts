/**
 * Check current game state on devnet
 * 
 * Usage: npx ts-node scripts/check-state.ts
 */

import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import { Chaindepth } from "../target/types/chaindepth";
import * as fs from "fs";
import * as path from "path";
import dotenv from "dotenv";

dotenv.config();

async function main() {
  // Setup provider
  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);

  const program = anchor.workspace.Chaindepth as Program<Chaindepth>;

  console.log("=== ChainDepth State Check ===\n");
  console.log("Program ID:", program.programId.toBase58());

  // Derive global PDA
  const [globalPda] = anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("global")],
    program.programId
  );

  try {
    const globalAccount = await program.account.globalAccount.fetch(globalPda);
    
    console.log("\n--- Global State ---");
    console.log("PDA:", globalPda.toBase58());
    console.log("Season Seed:", globalAccount.seasonSeed.toString());
    console.log("Current Depth:", globalAccount.depth);
    console.log("Jobs Completed:", globalAccount.jobsCompleted.toString());
    console.log("SKR Mint:", globalAccount.skrMint.toBase58());
    console.log("Prize Pool:", globalAccount.prizePool.toBase58());
    console.log("Admin:", globalAccount.admin.toBase58());
    
    const currentSlot = await provider.connection.getSlot();
    const endSlot = globalAccount.endSlot.toNumber();
    const slotsRemaining = endSlot - currentSlot;
    const secondsRemaining = slotsRemaining * 0.4; // ~400ms per slot
    
    console.log("\n--- Season Info ---");
    console.log("End Slot:", endSlot);
    console.log("Current Slot:", currentSlot);
    console.log("Slots Remaining:", slotsRemaining);
    console.log("Time Remaining:", formatDuration(secondsRemaining));

    // Check prize pool balance
    try {
      const prizePoolBalance = await provider.connection.getTokenAccountBalance(
        globalAccount.prizePool
      );
      console.log("\n--- Prize Pool ---");
      console.log("Balance:", prizePoolBalance.value.uiAmount, "SKR");
    } catch (e) {
      console.log("\n--- Prize Pool ---");
      console.log("Unable to fetch balance");
    }

    // Save updated config
    const config = {
      programId: program.programId.toBase58(),
      skrMint: globalAccount.skrMint.toBase58(),
      globalPda: globalPda.toBase58(),
      prizePoolPda: globalAccount.prizePool.toBase58(),
      seasonSeed: globalAccount.seasonSeed.toString(),
      network: "devnet",
      rpcUrl: "https://api.devnet.solana.com",
    };

    const configPath = path.join(__dirname, "..", "devnet-config.json");
    fs.writeFileSync(configPath, JSON.stringify(config, null, 2));
    console.log("\n✅ Config updated:", configPath);

  } catch (e: any) {
    if (e.message?.includes("Account does not exist")) {
      console.log("\n⚠️  Global state not initialized");
      console.log("Run: npx ts-node scripts/init-devnet.ts");
    } else {
      throw e;
    }
  }
}

function formatDuration(seconds: number): string {
  if (seconds < 0) return "Season ended";
  
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const mins = Math.floor((seconds % 3600) / 60);
  
  if (days > 0) return `${days}d ${hours}h ${mins}m`;
  if (hours > 0) return `${hours}h ${mins}m`;
  return `${mins}m`;
}

main().catch((e) => {
  console.error("Error:", e.message);
  process.exit(1);
});
