/**
 * Force reset season immediately (admin override).
 *
 * Usage:
 *   npm run force-reset-season
 *
 * Environment:
 *   ANCHOR_PROVIDER_URL=https://api.devnet.solana.com
 *   ANCHOR_WALLET=devnet-wallet.json
 */

import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import type { Chaindepth } from "../target/types/chaindepth";

async function main(): Promise<void> {
  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);

  const program = anchor.workspace.Chaindepth as Program<Chaindepth>;
  const [globalPda] = anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("global")],
    program.programId
  );

  const before = await program.account.globalAccount.fetch(globalPda);
  const slot = await provider.connection.getSlot("confirmed");

  console.log("=== Force Reset Season ===");
  console.log("Program:", program.programId.toBase58());
  console.log("Admin:", provider.wallet.publicKey.toBase58());
  console.log("Global PDA:", globalPda.toBase58());
  console.log("Current slot:", slot.toString());
  console.log("Before season seed:", before.seasonSeed.toString());
  console.log("Before end slot:", before.endSlot.toString());

  const signature = await program.methods
    .forceResetSeason()
    .accountsPartial({
      authority: provider.wallet.publicKey,
      global: globalPda,
    })
    .rpc();

  const after = await program.account.globalAccount.fetch(globalPda);
  console.log("Reset signature:", signature);
  console.log("After season seed:", after.seasonSeed.toString());
  console.log("After end slot:", after.endSlot.toString());
}

main().catch((thrownObject: unknown) => {
  const error =
    thrownObject instanceof Error
      ? thrownObject
      : new Error(`Non-Error thrown: ${String(thrownObject)}`);
  console.error("Error:", error.message);
  process.exit(1);
});
