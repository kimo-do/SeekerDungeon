import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import type { Chaindepth } from "../target/types/chaindepth";
import { Keypair, SystemProgram, Transaction } from "@solana/web3.js";
import BN from "bn.js";

const TOKEN_PROGRAM_ID = new anchor.web3.PublicKey(
  "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA",
);
const ASSOCIATED_TOKEN_PROGRAM_ID = new anchor.web3.PublicKey(
  "ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL",
);

const WALL_RUBBLE = 1;
const ROOM_ACTIVITY_DOOR_JOB = 1;
const STAKE_AMOUNT = 10_000_000;
const SESSION_JOIN_JOB_BIT = 1 << 7;
const SESSION_FUNDING_LAMPORTS = 20_000_000;
const SESSION_DURATION_SLOTS = 5_000;
const SESSION_DURATION_SECONDS = 3_600;
const START_ROOM_X = 5;
const START_ROOM_Y = 5;

type ActiveJob = {
  roomX: number;
  roomY: number;
  direction: number;
};

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

const deriveProfilePda = function (
  programId: anchor.web3.PublicKey,
  player: anchor.web3.PublicKey,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("profile"), player.toBuffer()],
    programId,
  )[0];
};

const deriveRoomPresencePda = function (
  programId: anchor.web3.PublicKey,
  seasonSeed: anchor.BN,
  x: number,
  y: number,
  player: anchor.web3.PublicKey,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [
      Buffer.from("presence"),
      seasonSeed.toArrayLike(Buffer, "le", 8),
      Buffer.from([x & 0xff]),
      Buffer.from([y & 0xff]),
      player.toBuffer(),
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

const deriveSessionAuthorityPda = function (
  programId: anchor.web3.PublicKey,
  player: anchor.web3.PublicKey,
  sessionKey: anchor.web3.PublicKey,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("session"), player.toBuffer(), sessionKey.toBuffer()],
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

async function main(): Promise<void> {
  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);

  const program = anchor.workspace.Chaindepth as Program<Chaindepth>;
  const walletPubkey = provider.wallet.publicKey;
  const globalPda = deriveGlobalPda(program.programId);

  const globalAccount = await program.account.globalAccount.fetch(globalPda);
  const playerPda = derivePlayerPda(program.programId, walletPubkey);
  const profilePda = deriveProfilePda(program.programId, walletPubkey);
  const startRoomPresencePda = deriveRoomPresencePda(
    program.programId,
    globalAccount.seasonSeed,
    START_ROOM_X,
    START_ROOM_Y,
    walletPubkey,
  );

  console.log("=== Devnet Session Join Job Smoke Test ===");
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
        profile: profilePda,
        roomPresence: startRoomPresencePda,
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

  const activeJobs = playerAccount.activeJobs as Array<ActiveJob>;
  let selectedDirection: number | null = null;

  for (let direction = 0; direction < 4; direction += 1) {
    if (roomAccount.walls[direction] !== WALL_RUBBLE) {
      continue;
    }
    if (roomAccount.jobCompleted[direction]) {
      continue;
    }
    const alreadyActive = activeJobs.some((job) => {
      return (
        job.roomX === playerAccount.currentRoomX &&
        job.roomY === playerAccount.currentRoomY &&
        job.direction === direction
      );
    });
    if (alreadyActive) {
      continue;
    }
    selectedDirection = direction;
    break;
  }

  if (selectedDirection === null) {
    throw new Error(
      `No joinable rubble direction in current room (${playerAccount.currentRoomX}, ${playerAccount.currentRoomY}).`,
    );
  }

  const direction = selectedDirection;
  const roomPresencePda = deriveRoomPresencePda(
    program.programId,
    globalAccount.seasonSeed,
    playerAccount.currentRoomX,
    playerAccount.currentRoomY,
    walletPubkey,
  );
  const escrowPda = deriveEscrowPda(program.programId, roomPda, direction);
  const helperStakePda = deriveHelperStakePda(
    program.programId,
    roomPda,
    direction,
    walletPubkey,
  );
  const playerTokenAccount = deriveAta(globalAccount.skrMint, walletPubkey);

  const sessionKeypair = Keypair.generate();
  const sessionAuthorityPda = deriveSessionAuthorityPda(
    program.programId,
    walletPubkey,
    sessionKeypair.publicKey,
  );

  const connection = provider.connection;
  const sessionBalance = await connection.getBalance(sessionKeypair.publicKey);
  if (sessionBalance < SESSION_FUNDING_LAMPORTS) {
    const fundTransaction = new Transaction().add(
      SystemProgram.transfer({
        fromPubkey: walletPubkey,
        toPubkey: sessionKeypair.publicKey,
        lamports: SESSION_FUNDING_LAMPORTS,
      }),
    );
    await provider.sendAndConfirm(fundTransaction);
    console.log("fund_session_key: ok");
  }

  const currentSlot = await connection.getSlot("confirmed");
  const currentTimestamp = await connection.getBlockTime(currentSlot);
  if (currentTimestamp === null) {
    throw new Error("Failed to fetch current block time from devnet.");
  }

  await program.methods
    .beginSession(
      new BN(currentSlot + SESSION_DURATION_SLOTS),
      new BN(currentTimestamp + SESSION_DURATION_SECONDS),
      new BN(SESSION_JOIN_JOB_BIT),
      new BN(STAKE_AMOUNT),
    )
    .accountsPartial({
      player: walletPubkey,
      sessionKey: sessionKeypair.publicKey,
      playerAccount: playerPda,
      global: globalPda,
      playerTokenAccount,
      sessionAuthority: sessionAuthorityPda,
      tokenProgram: TOKEN_PROGRAM_ID,
      systemProgram: SystemProgram.programId,
    })
    .signers([sessionKeypair])
    .rpc();
  console.log("begin_session: ok");

  await program.methods
    .joinJobWithSession(direction)
    .accounts({
      authority: sessionKeypair.publicKey,
      player: walletPubkey,
      global: globalPda,
      playerAccount: playerPda,
      room: roomPda,
      roomPresence: roomPresencePda,
      escrow: escrowPda,
      helperStake: helperStakePda,
      playerTokenAccount,
      skrMint: globalAccount.skrMint,
      sessionAuthority: sessionAuthorityPda,
      tokenProgram: TOKEN_PROGRAM_ID,
      systemProgram: SystemProgram.programId,
    })
    .signers([sessionKeypair])
    .rpc();
  console.log("join_job_with_session: ok");

  const roomPresenceAccount = await program.account.roomPresence.fetch(roomPresencePda);
  if (roomPresenceAccount.activity !== ROOM_ACTIVITY_DOOR_JOB) {
    throw new Error(
      `Room presence activity expected ${ROOM_ACTIVITY_DOOR_JOB}, got ${roomPresenceAccount.activity}.`,
    );
  }
  if (roomPresenceAccount.activityDirection !== direction) {
    throw new Error(
      `Room presence direction expected ${direction}, got ${roomPresenceAccount.activityDirection}.`,
    );
  }
  console.log("room_presence: door job activity synced");

  const sessionAuthorityAccount = await program.account.sessionAuthority.fetch(
    sessionAuthorityPda,
  );
  if (sessionAuthorityAccount.spentTokenAmount.toNumber() < STAKE_AMOUNT) {
    throw new Error(
      `Expected session spend >= ${STAKE_AMOUNT}, got ${sessionAuthorityAccount.spentTokenAmount.toString()}.`,
    );
  }
  console.log(
    "session_spend_tracked:",
    sessionAuthorityAccount.spentTokenAmount.toString(),
  );

  await program.methods
    .endSession()
    .accountsPartial({
      player: walletPubkey,
      sessionKey: sessionKeypair.publicKey,
      sessionAuthority: sessionAuthorityPda,
      global: globalPda,
      playerTokenAccount,
      tokenProgram: TOKEN_PROGRAM_ID,
    })
    .rpc();
  console.log("end_session: ok");

  console.log("=== Session Join Job Smoke Test Passed ===");
}

main().catch((thrownObject: unknown) => {
  const error = ensureError(thrownObject);
  console.error("Session smoke raw error:", thrownObject);
  console.error("Session smoke test failed:", error.message);
  process.exit(1);
});
