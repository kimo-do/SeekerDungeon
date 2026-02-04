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
 * Usage: npm run init-devnet
 *
 * Note: This script uses @coral-xyz/anchor for program calls until
 * Codama client is generated. Run `npm run codama` after building
 * to generate the type-safe client.
 */

import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import type { Chaindepth } from "../target/types/chaindepth";
import { connect } from "solana-kite";
import { address, lamports, type Address, type KeyPairSigner } from "@solana/kit";
import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";
import dotenv from "dotenv";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
import {
  SKR_DECIMALS,
  SKR_MULTIPLIER,
  MIN_SOL_BALANCE_LAMPORTS,
  AIRDROP_AMOUNT_LAMPORTS,
  START_X,
  START_Y,
  INITIAL_PRIZE_POOL_AMOUNT,
  DEFAULT_MINT_AMOUNT,
  GLOBAL_SEED,
  PRIZE_POOL_SEED,
  ROOM_SEED,
  DEVNET_RPC_URL,
} from "./constants";

dotenv.config();

async function main(): Promise<void> {
  console.log("=== ChainDepth Devnet Initialization ===\n");

  // Setup Anchor provider (still needed for program calls)
  const anchorProvider = anchor.AnchorProvider.env();
  anchor.setProvider(anchorProvider);

  const program = anchor.workspace.Chaindepth as Program<Chaindepth>;
  const adminPublicKey = anchorProvider.wallet.publicKey;

  console.log("Program ID:", program.programId.toBase58());
  console.log("Admin wallet:", adminPublicKey.toBase58());

  // Setup Kite connection for token operations
  const connection = connect("devnet");

  // Load wallet from Solana CLI config for Kite operations
  const admin = await connection.loadWalletFromFile();
  console.log("Kite wallet loaded:", admin.address);

  // Check admin balance using Kite
  const balance = await connection.getLamportBalance(admin.address);
  console.log("Admin SOL balance:", Number(balance) / Number(1_000_000_000n), "SOL");

  if (balance < MIN_SOL_BALANCE_LAMPORTS) {
    console.log("\n⚠️  Low SOL balance! Requesting airdrop...");
    await connection.airdropIfRequired(
      admin.address,
      lamports(AIRDROP_AMOUNT_LAMPORTS),
      lamports(MIN_SOL_BALANCE_LAMPORTS)
    );
    console.log("Airdrop complete!");
  }

  // Step 1: Create mock SKR token using Kite
  console.log("\n--- Step 1: Creating mock SKR token ---");

  let skrMintAddress: Address;
  const existingMint = process.env.SKR_MINT;

  if (existingMint) {
    skrMintAddress = address(existingMint);
    console.log("Using existing SKR mint:", skrMintAddress);
  } else {
    // Create classic SPL token (not Token Extensions) for compatibility
    skrMintAddress = await connection.createTokenMint({
      mintAuthority: admin,
      decimals: SKR_DECIMALS,
      useTokenExtensions: false,
    });
    console.log("Created new SKR mint:", skrMintAddress);
    console.log("\n⚠️  Add this to your .env file:");
    console.log(`SKR_MINT=${skrMintAddress}`);
  }

  // Step 2: Create admin token account and mint tokens using Kite
  console.log("\n--- Step 2: Setting up admin token account ---");

  const adminAta = await connection.getTokenAccountAddress(
    admin.address,
    skrMintAddress
  );
  console.log("Admin ATA:", adminAta);

  // Mint tokens for prize pool funding
  const mintAmountTokens = BigInt(DEFAULT_MINT_AMOUNT);
  console.log("Minting", Number(mintAmountTokens) / SKR_MULTIPLIER, "SKR to admin...");

  await connection.mintTokens({
    mint: skrMintAddress,
    mintAuthority: admin,
    destination: admin.address,
    amount: mintAmountTokens,
  });
  console.log("Minting complete!");

  // Step 3: Derive PDAs (using Kite)
  console.log("\n--- Step 3: Deriving PDAs ---");

  const programAddress = address(program.programId.toBase58());

  const { pda: globalPda } = await connection.getPDAAndBump(programAddress, [
    GLOBAL_SEED,
  ]);
  console.log("Global PDA:", globalPda);

  const { pda: prizePoolPda } = await connection.getPDAAndBump(programAddress, [
    PRIZE_POOL_SEED,
    globalPda,
  ]);
  console.log("Prize Pool PDA:", prizePoolPda);

  // Generate season seed from current slot
  const slot = await connection.getCurrentSlot();
  const seasonSeed = BigInt(slot);
  console.log("Season seed (slot):", slot);

  const { pda: startRoomPda } = await connection.getPDAAndBump(programAddress, [
    ROOM_SEED,
    seasonSeed,
    START_X,
    START_Y,
  ]);
  console.log("Start Room PDA:", startRoomPda);

  // Step 4: Initialize global state (using Anchor until Codama client exists)
  console.log("\n--- Step 4: Initializing global state ---");

  const initialPrizePool = new anchor.BN(INITIAL_PRIZE_POOL_AMOUNT);
  const seasonSeedBN = new anchor.BN(slot);

  // Convert Kite addresses back to Anchor PublicKeys for the program call
  const skrMintPubkey = new anchor.web3.PublicKey(skrMintAddress);
  const globalPdaPubkey = new anchor.web3.PublicKey(globalPda);
  const prizePoolPdaPubkey = new anchor.web3.PublicKey(prizePoolPda);
  const startRoomPdaPubkey = new anchor.web3.PublicKey(startRoomPda);
  const adminAtaPubkey = new anchor.web3.PublicKey(adminAta);

  try {
    const tx = await program.methods
      .initGlobal(initialPrizePool, seasonSeedBN)
      .accountsPartial({
        admin: adminPublicKey,
        global: globalPdaPubkey,
        skrMint: skrMintPubkey,
        prizePool: prizePoolPdaPubkey,
        adminTokenAccount: adminAtaPubkey,
        startRoom: startRoomPdaPubkey,
        tokenProgram: anchor.utils.token.TOKEN_PROGRAM_ID,
        systemProgram: anchor.web3.SystemProgram.programId,
      })
      .rpc();

    console.log("✅ Global state initialized!");
    console.log("Transaction:", tx);

    // Fetch and display global state
    const globalAccount = await program.account.globalAccount.fetch(globalPdaPubkey);
    console.log("\nGlobal State:");
    console.log("  Season Seed:", globalAccount.seasonSeed.toString());
    console.log("  Depth:", globalAccount.depth);
    console.log("  SKR Mint:", globalAccount.skrMint.toBase58());
    console.log("  Prize Pool:", globalAccount.prizePool.toBase58());
    console.log("  Admin:", globalAccount.admin.toBase58());
    console.log("  End Slot:", globalAccount.endSlot.toString());
  } catch (error: unknown) {
    const errorMessage = error instanceof Error ? error.message : String(error);
    if (errorMessage.includes("already in use")) {
      console.log("ℹ️  Global state already initialized");

      // Fetch existing state
      const globalAccount = await program.account.globalAccount.fetch(globalPdaPubkey);
      console.log("\nExisting Global State:");
      console.log("  Season Seed:", globalAccount.seasonSeed.toString());
      console.log("  Depth:", globalAccount.depth);
    } else {
      console.error("Error initializing:", errorMessage);
      throw error;
    }
  }

  // Step 5: Output configuration summary
  console.log("\n=== Configuration Summary ===");
  console.log("\nAdd these to your Unity project:");
  console.log(`PROGRAM_ID=${program.programId.toBase58()}`);
  console.log(`SKR_MINT=${skrMintAddress}`);
  console.log(`GLOBAL_PDA=${globalPda}`);
  console.log(`RPC_URL=${DEVNET_RPC_URL}`);

  // Save to config file
  const config = {
    programId: program.programId.toBase58(),
    skrMint: skrMintAddress,
    globalPda: globalPda,
    prizePoolPda: prizePoolPda,
    network: "devnet",
    rpcUrl: DEVNET_RPC_URL,
    startRoom: { x: START_X, y: START_Y },
  };

  const configPath = path.join(__dirname, "..", "devnet-config.json");
  fs.writeFileSync(configPath, JSON.stringify(config, null, 2));
  console.log(`\n✅ Config saved to: ${configPath}`);

  console.log("\n=== Initialization Complete ===");
}

main().catch((error: unknown) => {
  const message = error instanceof Error ? error.message : String(error);
  console.error("Fatal error:", message);
  process.exit(1);
});
