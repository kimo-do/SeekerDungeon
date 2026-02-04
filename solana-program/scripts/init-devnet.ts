/**
 * ChainDepth Devnet Initialization Script
 * 
 * This script:
 * 1. Creates a mock SKR token on devnet
 * 2. Initializes the global game state
 * 3. Funds the prize pool
 * 
 * Prerequisites:
 * - Solana CLI configured for devnet
 * - Wallet with SOL balance (run: solana airdrop 5)
 * - Anchor CLI installed
 * 
 * Usage: npx ts-node scripts/init-devnet.ts
 */

import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import { Chaindepth } from "../target/types/chaindepth";
import {
  createMint,
  createAssociatedTokenAccount,
  mintTo,
  getAssociatedTokenAddress,
  TOKEN_PROGRAM_ID,
  ASSOCIATED_TOKEN_PROGRAM_ID,
} from "@solana/spl-token";
import * as fs from "fs";
import * as path from "path";
import dotenv from "dotenv";

dotenv.config();

async function main() {
  console.log("=== ChainDepth Devnet Initialization ===\n");

  // Setup provider
  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);

  const program = anchor.workspace.Chaindepth as Program<Chaindepth>;
  const admin = provider.wallet;

  console.log("Program ID:", program.programId.toBase58());
  console.log("Admin wallet:", admin.publicKey.toBase58());

  // Check admin balance
  const balance = await provider.connection.getBalance(admin.publicKey);
  console.log("Admin SOL balance:", balance / anchor.web3.LAMPORTS_PER_SOL, "SOL");

  if (balance < 0.5 * anchor.web3.LAMPORTS_PER_SOL) {
    console.log("\n⚠️  Low SOL balance! Requesting airdrop...");
    const sig = await provider.connection.requestAirdrop(
      admin.publicKey,
      2 * anchor.web3.LAMPORTS_PER_SOL
    );
    await provider.connection.confirmTransaction(sig);
    console.log("Airdrop complete!");
  }

  // Step 1: Create mock SKR token
  console.log("\n--- Step 1: Creating mock SKR token ---");
  
  let skrMint: anchor.web3.PublicKey;
  const existingMint = process.env.SKR_MINT;
  
  if (existingMint) {
    skrMint = new anchor.web3.PublicKey(existingMint);
    console.log("Using existing SKR mint:", skrMint.toBase58());
  } else {
    skrMint = await createMint(
      provider.connection,
      (admin as any).payer,
      admin.publicKey,
      admin.publicKey, // Freeze authority (optional)
      9 // 9 decimals like real SKR
    );
    console.log("Created new SKR mint:", skrMint.toBase58());
    console.log("\n⚠️  Add this to your .env file:");
    console.log(`SKR_MINT=${skrMint.toBase58()}`);
  }

  // Step 2: Create admin token account and mint tokens
  console.log("\n--- Step 2: Setting up admin token account ---");
  
  const adminAta = await getAssociatedTokenAddress(skrMint, admin.publicKey);
  
  try {
    await createAssociatedTokenAccount(
      provider.connection,
      (admin as any).payer,
      skrMint,
      admin.publicKey
    );
    console.log("Created admin ATA:", adminAta.toBase58());
  } catch (e) {
    console.log("Admin ATA already exists:", adminAta.toBase58());
  }

  // Mint tokens for prize pool funding
  const mintAmount = 1000 * 10 ** 9; // 1000 SKR
  console.log("Minting", mintAmount / 10 ** 9, "SKR to admin...");
  
  await mintTo(
    provider.connection,
    (admin as any).payer,
    skrMint,
    adminAta,
    admin.publicKey,
    mintAmount
  );
  console.log("Minting complete!");

  // Step 3: Derive PDAs
  console.log("\n--- Step 3: Deriving PDAs ---");
  
  const [globalPda, globalBump] = anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("global")],
    program.programId
  );
  console.log("Global PDA:", globalPda.toBase58());

  const [prizePoolPda] = anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("prize_pool"), globalPda.toBuffer()],
    program.programId
  );
  console.log("Prize Pool PDA:", prizePoolPda.toBase58());

  // Generate season seed from current slot
  const slot = await provider.connection.getSlot();
  const seasonSeed = new anchor.BN(slot);
  console.log("Season seed (slot):", slot);

  const START_X = 5;
  const START_Y = 5;
  
  const [startRoomPda] = anchor.web3.PublicKey.findProgramAddressSync(
    [
      Buffer.from("room"),
      seasonSeed.toArrayLike(Buffer, "le", 8),
      Buffer.from([START_X]),
      Buffer.from([START_Y]),
    ],
    program.programId
  );
  console.log("Start Room PDA:", startRoomPda.toBase58());

  // Step 4: Initialize global state
  console.log("\n--- Step 4: Initializing global state ---");
  
  const initialPrizePool = new anchor.BN(100 * 10 ** 9); // 100 SKR
  
  try {
    const tx = await program.methods
      .initGlobal(initialPrizePool, seasonSeed)
      .accountsPartial({
        admin: admin.publicKey,
        global: globalPda,
        skrMint: skrMint,
        prizePool: prizePoolPda,
        adminTokenAccount: adminAta,
        startRoom: startRoomPda,
        tokenProgram: TOKEN_PROGRAM_ID,
        systemProgram: anchor.web3.SystemProgram.programId,
      })
      .rpc();

    console.log("✅ Global state initialized!");
    console.log("Transaction:", tx);
    
    // Fetch and display global state
    const globalAccount = await program.account.globalAccount.fetch(globalPda);
    console.log("\nGlobal State:");
    console.log("  Season Seed:", globalAccount.seasonSeed.toString());
    console.log("  Depth:", globalAccount.depth);
    console.log("  SKR Mint:", globalAccount.skrMint.toBase58());
    console.log("  Prize Pool:", globalAccount.prizePool.toBase58());
    console.log("  Admin:", globalAccount.admin.toBase58());
    console.log("  End Slot:", globalAccount.endSlot.toString());
    
  } catch (e: any) {
    if (e.message?.includes("already in use")) {
      console.log("ℹ️  Global state already initialized");
      
      // Fetch existing state
      const globalAccount = await program.account.globalAccount.fetch(globalPda);
      console.log("\nExisting Global State:");
      console.log("  Season Seed:", globalAccount.seasonSeed.toString());
      console.log("  Depth:", globalAccount.depth);
    } else {
      console.error("Error initializing:", e.message);
      throw e;
    }
  }

  // Step 5: Output configuration summary
  console.log("\n=== Configuration Summary ===");
  console.log("\nAdd these to your Unity project:");
  console.log(`PROGRAM_ID=${program.programId.toBase58()}`);
  console.log(`SKR_MINT=${skrMint.toBase58()}`);
  console.log(`GLOBAL_PDA=${globalPda.toBase58()}`);
  console.log(`RPC_URL=https://api.devnet.solana.com`);

  // Save to config file
  const config = {
    programId: program.programId.toBase58(),
    skrMint: skrMint.toBase58(),
    globalPda: globalPda.toBase58(),
    prizePoolPda: prizePoolPda.toBase58(),
    network: "devnet",
    rpcUrl: "https://api.devnet.solana.com",
    startRoom: { x: START_X, y: START_Y },
  };

  const configPath = path.join(__dirname, "..", "devnet-config.json");
  fs.writeFileSync(configPath, JSON.stringify(config, null, 2));
  console.log(`\n✅ Config saved to: ${configPath}`);

  console.log("\n=== Initialization Complete ===");
}

main().catch((e) => {
  console.error("Fatal error:", e);
  process.exit(1);
});
