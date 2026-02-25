import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import type { Chaindepth } from "../target/types/chaindepth";
import {
  createAssociatedTokenAccountInstruction,
  getAssociatedTokenAddressSync,
  mintTo,
} from "@solana/spl-token";
import { LAMPORTS_PER_SOL, SystemProgram, Transaction } from "@solana/web3.js";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { SKR_DECIMALS } from "./constants";

type DevnetConfig = {
  programId: string;
  skrMint: string;
  globalPda: string;
  prizePoolPda: string;
  seasonSeed?: string;
  network?: string;
  rpcUrl?: string;
  signupFaucet?: string;
};

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const GLOBAL_SEED = "global";
const DEFAULT_FAUCET_TOKENS = 1_000_000_000n;
const MIN_ADMIN_SOL_LAMPORTS = 1n * BigInt(LAMPORTS_PER_SOL);
const ADMIN_SOL_TOP_UP_LAMPORTS = 2n * BigInt(LAMPORTS_PER_SOL);

const parseTokensArg = function (): bigint {
  const argValue = process.argv[2];
  if (!argValue) {
    return DEFAULT_FAUCET_TOKENS;
  }

  const trimmed = argValue.trim();
  if (!/^\d+$/.test(trimmed)) {
    throw new Error("Token amount must be a whole number (for example: 1000000000).");
  }
  return BigInt(trimmed);
};

const deriveGlobalPda = function (
  programId: anchor.web3.PublicKey,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from(GLOBAL_SEED)],
    programId,
  )[0];
};

async function main(): Promise<void> {
  const faucetTokens = parseTokensArg();
  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);

  const program = anchor.workspace.Chaindepth as Program<Chaindepth>;
  const connection = provider.connection;
  const adminPubkey = provider.wallet.publicKey;
  const adminKeypair = (provider.wallet as any).payer as anchor.web3.Keypair;
  if (!adminKeypair) {
    throw new Error("Anchor wallet payer keypair is unavailable.");
  }

  const configPath = path.join(__dirname, "..", "devnet-config.json");
  const config: DevnetConfig = fs.existsSync(configPath)
    ? JSON.parse(fs.readFileSync(configPath, "utf8"))
    : {
        programId: program.programId.toBase58(),
        skrMint: "",
        globalPda: "",
        prizePoolPda: "",
      };

  const globalPda = deriveGlobalPda(program.programId);
  const globalAccount = await program.account.globalAccount.fetch(globalPda);
  const signupFaucet = getAssociatedTokenAddressSync(
    globalAccount.skrMint,
    globalPda,
    true,
  );

  console.log("=== Fund Signup Faucet (Devnet) ===");
  console.log("Admin:", adminPubkey.toBase58());
  console.log("Program:", program.programId.toBase58());
  console.log("Global PDA:", globalPda.toBase58());
  console.log("SKR Mint:", globalAccount.skrMint.toBase58());
  console.log("Signup Faucet ATA:", signupFaucet.toBase58());

  const adminSolBefore = await connection.getBalance(adminPubkey, "confirmed");
  console.log("Admin SOL before:", adminSolBefore / LAMPORTS_PER_SOL);
  if (BigInt(adminSolBefore) < MIN_ADMIN_SOL_LAMPORTS) {
    const airdropSignature = await connection.requestAirdrop(
      adminPubkey,
      Number(ADMIN_SOL_TOP_UP_LAMPORTS),
    );
    await connection.confirmTransaction(airdropSignature, "confirmed");
    console.log("Airdrop requested:", airdropSignature);
  }

  const faucetInfo = await connection.getAccountInfo(signupFaucet, "confirmed");
  if (!faucetInfo) {
    const createFaucetIx = createAssociatedTokenAccountInstruction(
      adminPubkey,
      signupFaucet,
      globalPda,
      globalAccount.skrMint,
    );
    const transaction = new Transaction().add(createFaucetIx);
    await provider.sendAndConfirm(transaction);
    console.log("Created signup faucet ATA.");
  } else {
    console.log("Signup faucet ATA already exists.");
  }

  const mintRawAmount =
    faucetTokens * 10n ** BigInt(SKR_DECIMALS);
  const mintSignature = await mintTo(
    connection,
    adminKeypair,
    globalAccount.skrMint,
    signupFaucet,
    adminPubkey,
    mintRawAmount,
  );
  console.log("Minted", faucetTokens.toString(), "SKR to signup faucet.");
  console.log("Mint signature:", mintSignature);

  const adminSolAfter = await connection.getBalance(adminPubkey, "confirmed");
  console.log("Admin SOL after:", adminSolAfter / LAMPORTS_PER_SOL);

  config.signupFaucet = signupFaucet.toBase58();
  config.skrMint = globalAccount.skrMint.toBase58();
  config.globalPda = globalPda.toBase58();
  fs.writeFileSync(configPath, JSON.stringify(config, null, 2));
  console.log("Updated devnet-config.json with signupFaucet.");
}

main().catch((thrownObject: unknown) => {
  const error =
    thrownObject instanceof Error
      ? thrownObject
      : new Error(`Non-Error thrown: ${String(thrownObject)}`);
  console.error("Error:", error.message);
  process.exit(1);
});
