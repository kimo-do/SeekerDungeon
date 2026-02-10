/**
 * Admin test helper:
 * Reset target player's PlayerAccount + PlayerProfile PDAs only.
 *
 * Usage:
 *   npm run reset-player-only <wallet_address>
 *
 * Example:
 *   npm run reset-player-only CbBMc9dnTg1vBAWijVqi6chZ1JYvtMwdkn1hpZyCBW6s
 *
 * Environment:
 *   ANCHOR_PROVIDER_URL=https://api.devnet.solana.com
 *   ANCHOR_WALLET=devnet-wallet.json
 */

import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import type { Chaindepth } from "../target/types/chaindepth";
import { address } from "@solana/kit";

async function main(): Promise<void> {
  const args = process.argv.slice(2);
  if (args.length < 1) {
    console.log("Usage: npm run reset-player-only <wallet_address>");
    process.exit(1);
  }

  const targetWalletAddress = address(args[0]);
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

  console.log("=== Reset Player Only (Devnet) ===");
  console.log("Program:", program.programId.toBase58());
  console.log("Admin:", provider.wallet.publicKey.toBase58());
  console.log("Target wallet:", targetWalletAddress);

  const playerInfo = await provider.connection.getAccountInfo(playerPda, "confirmed");
  const profileInfo = await provider.connection.getAccountInfo(profilePda, "confirmed");

  if (!playerInfo || !profileInfo) {
    console.log("Reset skipped: player/profile account missing for target wallet.");
    return;
  }

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
