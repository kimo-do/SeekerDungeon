import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import type { Chaindepth } from "../target/types/chaindepth";
import { SystemProgram } from "@solana/web3.js";
import BN from "bn.js";

const DIRECTION_EAST = 2;
const WALL_RUBBLE = 1;
const TOKEN_PROGRAM_ID = new anchor.web3.PublicKey(
  "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA",
);
const ASSOCIATED_TOKEN_PROGRAM_ID = new anchor.web3.PublicKey(
  "ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL",
);

const ensureError = function (thrownObject: unknown): Error {
  if (thrownObject instanceof Error) {
    return thrownObject;
  }
  return new Error(`Non-Error thrown: ${String(thrownObject)}`);
};

const deriveGlobalPda = function (
  programId: anchor.web3.PublicKey,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("global")],
    programId,
  )[0];
};

const derivePlayerPda = function (
  programId: anchor.web3.PublicKey,
  player: anchor.web3.PublicKey,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("player"), player.toBuffer()],
    programId,
  )[0];
};

const deriveRoomPda = function (
  programId: anchor.web3.PublicKey,
  seasonSeed: anchor.BN,
  x: number,
  y: number,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [
      Buffer.from("room"),
      seasonSeed.toArrayLike(Buffer, "le", 8),
      Buffer.from([x & 0xff]),
      Buffer.from([y & 0xff]),
    ],
    programId,
  )[0];
};

const deriveEscrowPda = function (
  programId: anchor.web3.PublicKey,
  room: anchor.web3.PublicKey,
  direction: number,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("escrow"), room.toBuffer(), Buffer.from([direction])],
    programId,
  )[0];
};

const deriveHelperStakePda = function (
  programId: anchor.web3.PublicKey,
  room: anchor.web3.PublicKey,
  direction: number,
  player: anchor.web3.PublicKey,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("stake"), room.toBuffer(), Buffer.from([direction]), player.toBuffer()],
    programId,
  )[0];
};

const deriveAta = function (
  mint: anchor.web3.PublicKey,
  owner: anchor.web3.PublicKey,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [owner.toBuffer(), TOKEN_PROGRAM_ID.toBuffer(), mint.toBuffer()],
    ASSOCIATED_TOKEN_PROGRAM_ID,
  )[0];
};

const adjacentEast = function (x: number, y: number): [number, number] {
  return [x + 1, y];
};

async function main(): Promise<void> {
  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);

  const program = anchor.workspace.Chaindepth as Program<Chaindepth>;
  const walletPubkey = provider.wallet.publicKey;
  const globalPda = deriveGlobalPda(program.programId);

  const globalAccount = await program.account.globalAccount.fetch(globalPda);
  const playerPda = derivePlayerPda(program.programId, walletPubkey);

  console.log("=== Devnet Door Smoke Test ===");
  console.log("Wallet:", walletPubkey.toBase58());
  console.log("Program:", program.programId.toBase58());
  console.log("Global PDA:", globalPda.toBase58());
  console.log("Season seed:", globalAccount.seasonSeed.toString());

  try {
    await program.methods
      .initPlayer()
      .accountsPartial({
        player: walletPubkey,
        global: globalPda,
        playerAccount: playerPda,
        systemProgram: SystemProgram.programId,
      })
      .rpc();
    console.log("init_player: created");
  } catch (thrownObject: unknown) {
    const error = ensureError(thrownObject);
    if (error.message.includes("already in use")) {
      console.log("init_player: already exists");
    } else {
      throw error;
    }
  }

  const playerAccount = await program.account.playerAccount.fetch(playerPda);
  const roomPda = deriveRoomPda(
    program.programId,
    globalAccount.seasonSeed,
    playerAccount.currentRoomX,
    playerAccount.currentRoomY,
  );
  const roomAccount = await program.account.roomAccount.fetch(roomPda);

  if (roomAccount.walls[DIRECTION_EAST] !== WALL_RUBBLE) {
    throw new Error(
      `East wall is not rubble at (${playerAccount.currentRoomX}, ${playerAccount.currentRoomY}). State=${roomAccount.walls[DIRECTION_EAST]}`,
    );
  }

  const direction = DIRECTION_EAST;
  const escrowPda = deriveEscrowPda(program.programId, roomPda, direction);
  const helperStakePda = deriveHelperStakePda(
    program.programId,
    roomPda,
    direction,
    walletPubkey,
  );
  const playerTokenAccount = deriveAta(globalAccount.skrMint, walletPubkey);

  const connection = provider.connection;
  let latestRoom = roomAccount;
  let helperStakeExists = (await connection.getAccountInfo(helperStakePda)) !== null;

  if (!helperStakeExists && !latestRoom.jobCompleted[direction]) {
    await program.methods
      .joinJob(direction)
      .accountsPartial({
        player: walletPubkey,
        global: globalPda,
        playerAccount: playerPda,
        room: roomPda,
        escrow: escrowPda,
        helperStake: helperStakePda,
        playerTokenAccount,
        skrMint: globalAccount.skrMint,
        tokenProgram: TOKEN_PROGRAM_ID,
        systemProgram: SystemProgram.programId,
      })
      .rpc();
    console.log("join_job: ok");
    helperStakeExists = true;
  } else {
    console.log("join_job: skipped (already joined or completed)");
  }

  latestRoom = await program.account.roomAccount.fetch(roomPda);
  if (!latestRoom.jobCompleted[direction]) {
    await program.methods
      .boostJob(direction, new BN(10_000_000))
      .accountsPartial({
        player: walletPubkey,
        global: globalPda,
        room: roomPda,
        prizePool: globalAccount.prizePool,
        playerTokenAccount,
        tokenProgram: TOKEN_PROGRAM_ID,
      })
      .rpc();
    console.log("boost_job: ok");
  } else {
    console.log("boost_job: skipped (already completed)");
  }

  const [adjX, adjY] = adjacentEast(playerAccount.currentRoomX, playerAccount.currentRoomY);
  const adjacentRoomPda = deriveRoomPda(
    program.programId,
    globalAccount.seasonSeed,
    adjX,
    adjY,
  );

  latestRoom = await program.account.roomAccount.fetch(roomPda);
  if (!latestRoom.jobCompleted[direction]) {
    await program.methods
      .completeJob(direction)
      .accountsPartial({
        player: walletPubkey,
        global: globalPda,
        playerAccount: playerPda,
        room: roomPda,
        helperStake: helperStakePda,
        adjacentRoom: adjacentRoomPda,
        escrow: escrowPda,
        prizePool: globalAccount.prizePool,
        tokenProgram: TOKEN_PROGRAM_ID,
        systemProgram: SystemProgram.programId,
      })
      .rpc();
    console.log("complete_job: ok");
  } else {
    console.log("complete_job: skipped (already completed)");
  }

  latestRoom = await program.account.roomAccount.fetch(roomPda);
  if (latestRoom.jobCompleted[direction] && helperStakeExists) {
    await program.methods
      .claimJobReward(direction)
      .accountsPartial({
        player: walletPubkey,
        global: globalPda,
        playerAccount: playerPda,
        room: roomPda,
        escrow: escrowPda,
        helperStake: helperStakePda,
        playerTokenAccount,
        tokenProgram: TOKEN_PROGRAM_ID,
      })
      .rpc();
    console.log("claim_job_reward: ok");
  } else {
    console.log("claim_job_reward: skipped (not claimable)");
  }

  console.log("=== Smoke Test Passed ===");
}

main().catch((thrownObject: unknown) => {
  const error = ensureError(thrownObject);
  console.error("Smoke test failed:", error.message);
  process.exit(1);
});
