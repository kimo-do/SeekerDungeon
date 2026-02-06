import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import { Chaindepth } from "../target/types/chaindepth";
import {
  createMint,
  createAssociatedTokenAccount,
  mintTo,
  getAssociatedTokenAddress,
  TOKEN_PROGRAM_ID,
} from "@solana/spl-token";
import { expect } from "chai";

describe("chaindepth", () => {
  // Configure the client to use the local cluster
  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);

  const program = anchor.workspace.Chaindepth as Program<Chaindepth>;
  const admin = provider.wallet;

  // Test accounts
  let skrMint: anchor.web3.PublicKey;
  let adminTokenAccount: anchor.web3.PublicKey;
  let globalPda: anchor.web3.PublicKey;
  let globalBump: number;
  let prizePoolPda: anchor.web3.PublicKey;

  // Test player
  const player = anchor.web3.Keypair.generate();
  let playerTokenAccount: anchor.web3.PublicKey;
  let playerPda: anchor.web3.PublicKey;

  // Constants
  const START_X = 5;
  const START_Y = 5;
  const DIRECTION_NORTH = 0;
  const DIRECTION_SOUTH = 1;
  const DIRECTION_EAST = 2;
  const DIRECTION_WEST = 3;

  before(async () => {
    // Create mock SKR token mint
    skrMint = await createMint(
      provider.connection,
      (admin as any).payer,
      admin.publicKey,
      null,
      9 // 9 decimals like SKR
    );

    // Create admin token account and mint some tokens
    adminTokenAccount = await createAssociatedTokenAccount(
      provider.connection,
      (admin as any).payer,
      skrMint,
      admin.publicKey
    );

    // Mint 1000 SKR to admin for prize pool funding
    await mintTo(
      provider.connection,
      (admin as any).payer,
      skrMint,
      adminTokenAccount,
      admin.publicKey,
      1000 * 10 ** 9 // 1000 SKR
    );

    // Create player token account and mint tokens
    playerTokenAccount = await createAssociatedTokenAccount(
      provider.connection,
      (admin as any).payer,
      skrMint,
      player.publicKey
    );

    await mintTo(
      provider.connection,
      (admin as any).payer,
      skrMint,
      playerTokenAccount,
      admin.publicKey,
      100 * 10 ** 9 // 100 SKR for testing
    );

    // Derive global PDA
    [globalPda, globalBump] = anchor.web3.PublicKey.findProgramAddressSync(
      [Buffer.from("global")],
      program.programId
    );

    // Derive prize pool PDA
    [prizePoolPda] = anchor.web3.PublicKey.findProgramAddressSync(
      [Buffer.from("prize_pool"), globalPda.toBuffer()],
      program.programId
    );

    // Derive player PDA
    [playerPda] = anchor.web3.PublicKey.findProgramAddressSync(
      [Buffer.from("player"), player.publicKey.toBuffer()],
      program.programId
    );
  });

  describe("init_global", () => {
    it("initializes global state and starting room", async () => {
      const initialPrizePool = new anchor.BN(10 * 10 ** 9); // 10 SKR

      // Generate season seed from current slot
      const slot = await provider.connection.getSlot();
      const seasonSeed = new anchor.BN(slot);
      
      // Derive start room PDA using the season seed
      const [startRoomPda] = anchor.web3.PublicKey.findProgramAddressSync(
        [
          Buffer.from("room"),
          seasonSeed.toArrayLike(Buffer, "le", 8),
          Buffer.from([START_X]),
          Buffer.from([START_Y]),
        ],
        program.programId
      );

      try {
        await program.methods
          .initGlobal(initialPrizePool, seasonSeed)
          .accounts({
            admin: admin.publicKey,
            global: globalPda,
            skrMint: skrMint,
            prizePool: prizePoolPda,
            adminTokenAccount: adminTokenAccount,
            startRoom: startRoomPda,
            tokenProgram: TOKEN_PROGRAM_ID,
            systemProgram: anchor.web3.SystemProgram.programId,
          })
          .rpc();
          
        // Verify global state
        const globalAccount = await program.account.globalAccount.fetch(globalPda);
        expect(globalAccount.seasonSeed.toString()).to.equal(seasonSeed.toString());
        expect(globalAccount.depth).to.equal(0);
        expect(globalAccount.admin.toBase58()).to.equal(admin.publicKey.toBase58());
        
        console.log("Global initialized with season seed:", globalAccount.seasonSeed.toString());
      } catch (e: any) {
        if (e.message?.includes("already in use")) {
          console.log("Global already initialized (expected in repeated test runs)");
        } else {
          throw e;
        }
      }
    });
  });

  describe("player operations", () => {
    it("player can be initialized at spawn point", async () => {
      // This would test init_player if we added it as a separate instruction
      // For now, move_player handles init_if_needed
    });
  });

  describe("job operations", () => {
    it("validates direction parameter", async () => {
      // Test that invalid directions are rejected
      const invalidDirection = 5;
      
      // This would fail with InvalidDirection error
      // Actual test would require proper account setup
    });
  });
});

// Helper functions for deriving PDAs
export function deriveGlobalPda(programId: anchor.web3.PublicKey): [anchor.web3.PublicKey, number] {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("global")],
    programId
  );
}

export function derivePlayerPda(
  programId: anchor.web3.PublicKey,
  playerPubkey: anchor.web3.PublicKey
): [anchor.web3.PublicKey, number] {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("player"), playerPubkey.toBuffer()],
    programId
  );
}

export function deriveRoomPda(
  programId: anchor.web3.PublicKey,
  seasonSeed: anchor.BN,
  x: number,
  y: number
): [anchor.web3.PublicKey, number] {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [
      Buffer.from("room"),
      seasonSeed.toArrayLike(Buffer, "le", 8),
      Buffer.from([x]),
      Buffer.from([y]),
    ],
    programId
  );
}

export function deriveEscrowPda(
  programId: anchor.web3.PublicKey,
  roomPda: anchor.web3.PublicKey,
  direction: number
): [anchor.web3.PublicKey, number] {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("escrow"), roomPda.toBuffer(), Buffer.from([direction])],
    programId
  );
}

export function derivePrizePoolPda(
  programId: anchor.web3.PublicKey,
  globalPda: anchor.web3.PublicKey
): [anchor.web3.PublicKey, number] {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("prize_pool"), globalPda.toBuffer()],
    programId
  );
}
