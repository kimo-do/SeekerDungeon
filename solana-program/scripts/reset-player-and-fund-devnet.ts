/**
 * Admin test helper:
 * 1) Reset target player's PlayerAccount + PlayerProfile PDAs.
 * 2) Fund target wallet with devnet SOL from admin wallet.
 * 3) Mint fake SKR to target wallet.
 *
 * Usage:
 *   npm run reset-player-and-fund <wallet_address> [sol_amount] [skr_amount]
 *
 * Example:
 *   npm run reset-player-and-fund CbBMc9dnTg1vBAWijVqi6chZ1JYvtMwdkn1hpZyCBW6s 0.5 25
 *
 * Environment:
 *   ANCHOR_PROVIDER_URL=https://api.devnet.solana.com
 *   ANCHOR_WALLET=devnet-wallet.json
 */

import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import type { Chaindepth } from "../target/types/chaindepth";
import { connect } from "solana-kite";
import { address } from "@solana/kit";
import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";
import { execFileSync } from "child_process";
import { LAMPORTS_PER_SOL, SKR_MULTIPLIER } from "./constants";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

interface DevnetConfig {
  skrMint: string;
}

const parseAmountOrDefault = (value: string | undefined, fallback: number): number => {
  if (!value) {
    return fallback;
  }

  const parsed = Number.parseFloat(value);
  if (!Number.isFinite(parsed) || parsed < 0) {
    return fallback;
  }

  return parsed;
};

async function main(): Promise<void> {
  const args = process.argv.slice(2);
  if (args.length < 1) {
    console.log("Usage: npm run reset-player-and-fund <wallet_address> [sol_amount] [skr_amount]");
    process.exit(1);
  }

  const targetWalletAddress = address(args[0]);
  const solAmount = parseAmountOrDefault(args[1], 0.5);
  const skrAmount = parseAmountOrDefault(args[2], 25);
  const solLamports = BigInt(Math.floor(solAmount * Number(LAMPORTS_PER_SOL)));
  const skrBaseUnits = BigInt(Math.floor(skrAmount * SKR_MULTIPLIER));

  const configPath = path.join(__dirname, "..", "devnet-config.json");
  if (!fs.existsSync(configPath)) {
    throw new Error("devnet-config.json not found. Run init-devnet first.");
  }
  const config: DevnetConfig = JSON.parse(fs.readFileSync(configPath, "utf-8"));

  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);

  const program = anchor.workspace.Chaindepth as Program<Chaindepth>;
  const targetPubkey = new anchor.web3.PublicKey(targetWalletAddress);

  const [globalPda] = anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("global")],
    program.programId
  );
  const [playerPda] = anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("player"), targetPubkey.toBuffer()],
    program.programId
  );
  const [profilePda] = anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("profile"), targetPubkey.toBuffer()],
    program.programId
  );

  console.log("=== Reset Player And Fund (Devnet) ===");
  console.log("Program:", program.programId.toBase58());
  console.log("Admin:", provider.wallet.publicKey.toBase58());
  console.log("Target wallet:", targetWalletAddress);
  console.log("SOL top-up:", solAmount);
  console.log("SKR mint:", skrAmount);

  const playerInfo = await provider.connection.getAccountInfo(playerPda, "confirmed");
  const profileInfo = await provider.connection.getAccountInfo(profilePda, "confirmed");

  if (playerInfo && profileInfo) {
    const resetSignature = await program.methods
      .resetPlayerForTesting()
      .accountsPartial({
        authority: provider.wallet.publicKey,
        player: targetPubkey,
        global: globalPda,
        playerAccount: playerPda,
        profile: profilePda,
      })
      .rpc();
    console.log("Reset player signature:", resetSignature);
  } else {
    console.log("Reset skipped: player/profile account missing for target wallet.");
  }

  const topUpTx = new anchor.web3.Transaction().add(
    anchor.web3.SystemProgram.transfer({
      fromPubkey: provider.wallet.publicKey,
      toPubkey: targetPubkey,
      lamports: solLamports,
    })
  );
  const topUpSignature = await provider.sendAndConfirm(topUpTx, [], { commitment: "confirmed" });
  console.log("SOL transfer signature:", topUpSignature);

  const connection = connect("devnet");
  const admin = await connection.loadWalletFromFile();
  try {
    await connection.mintTokens({
      mint: address(config.skrMint),
      mintAuthority: admin,
      destination: targetWalletAddress,
      amount: skrBaseUnits,
    });
    console.log("Minted SKR via Kite:", skrAmount);
  } catch (thrownObject: unknown) {
    const message = thrownObject instanceof Error ? thrownObject.message : String(thrownObject);
    if (!message.includes("Expected a Address.")) {
      throw thrownObject instanceof Error ? thrownObject : new Error(message);
    }

    console.log("Kite mint fallback triggered: using spl-token CLI path.");
    const mintAddress = config.skrMint;
    const mintAmountText = skrAmount.toString();
    const splTokenArgsCreate = [
      "create-account",
      mintAddress,
      "--owner",
      targetWalletAddress,
      "--fee-payer",
      "devnet-wallet.json",
      "-u",
      "devnet",
    ];
    try {
      execFileSync("spl-token", splTokenArgsCreate, { stdio: "inherit" });
    } catch {
      // Safe to ignore create-account failures when ATA already exists.
    }

    const splTokenArgsMint = [
      "mint",
      mintAddress,
      mintAmountText,
      "--recipient-owner",
      targetWalletAddress,
      "--mint-authority",
      "devnet-wallet.json",
      "--fee-payer",
      "devnet-wallet.json",
      "-u",
      "devnet",
    ];
    execFileSync("spl-token", splTokenArgsMint, { stdio: "inherit" });
    console.log("Minted SKR via spl-token fallback:", skrAmount);
  }

  console.log("Done.");
}

main().catch((thrownObject: unknown) => {
  const error =
    thrownObject instanceof Error
      ? thrownObject
      : new Error(`Non-Error thrown: ${String(thrownObject)}`);
  console.error("Error:", error.message);
  process.exit(1);
});
