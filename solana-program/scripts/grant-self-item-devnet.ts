/**
 * Dev helper: grant an inventory item to the signer wallet.
 *
 * Self-only by design (player == signer).
 *
 * Usage:
 *   npm run grant-self-item -- [item_id] [amount] [durability]
 *
 * Examples:
 *   npm run grant-self-item
 *   npm run grant-self-item -- 214 25
 *   npm run grant-self-item -- 214 50 0
 *
 * Defaults:
 *   item_id    = 214 (Skeleton Key / Bone Key)
 *   amount     = 10
 *   durability = 0
 */

import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import type { Chaindepth } from "../target/types/chaindepth";

const DEFAULT_ITEM_ID = 214;
const DEFAULT_AMOUNT = 10;
const DEFAULT_DURABILITY = 0;

function parseU16(value: string | undefined, fallback: number): number {
  if (!value) {
    return fallback;
  }

  const parsed = Number.parseInt(value, 10);
  if (!Number.isFinite(parsed) || parsed < 0 || parsed > 65535) {
    return fallback;
  }

  return parsed;
}

function parseU32(value: string | undefined, fallback: number): number {
  if (!value) {
    return fallback;
  }

  const parsed = Number.parseInt(value, 10);
  if (!Number.isFinite(parsed) || parsed <= 0 || parsed > 4294967295) {
    return fallback;
  }

  return parsed;
}

async function main(): Promise<void> {
  const args = process.argv.slice(2);
  const itemId = parseU16(args[0], DEFAULT_ITEM_ID);
  const amount = parseU32(args[1], DEFAULT_AMOUNT);
  const durability = parseU16(args[2], DEFAULT_DURABILITY);

  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);

  const program = anchor.workspace.Chaindepth as Program<Chaindepth>;
  const player = provider.wallet.publicKey;

  const [inventoryPda] = anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("inventory"), player.toBuffer()],
    program.programId
  );

  console.log("=== Grant Self Item (Devnet) ===");
  console.log("Program:", program.programId.toBase58());
  console.log("Player:", player.toBase58());
  console.log("Inventory PDA:", inventoryPda.toBase58());
  console.log("Item ID:", itemId);
  console.log("Amount:", amount);
  console.log("Durability:", durability);

  const signature = await program.methods
    .addInventoryItem(itemId, amount, durability)
    .accountsPartial({
      player,
      inventory: inventoryPda,
      systemProgram: anchor.web3.SystemProgram.programId,
    })
    .rpc();

  console.log("Grant signature:", signature);
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

