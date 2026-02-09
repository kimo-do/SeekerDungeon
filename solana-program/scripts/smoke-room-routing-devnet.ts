import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import type { Chaindepth } from "../target/types/chaindepth";
import { Keypair, SystemProgram, Transaction } from "@solana/web3.js";
import BN from "bn.js";

const DIRECTION_NORTH = 0;
const DIRECTION_SOUTH = 1;
const DIRECTION_EAST = 2;
const DIRECTION_WEST = 3;
const WALL_OPEN = 2;

const SESSION_MOVE_PLAYER_BIT = 1 << 6;
const SESSION_FUNDING_LAMPORTS = 200_000_000;
const SESSION_DURATION_SLOTS = 5_000;
const SESSION_DURATION_SECONDS = 3_600;
const FORWARD_BACKWARD_LOOP_COUNT = 8;
const MAX_SINGLE_OPEN_SEARCH_STEPS = 20;

const TOKEN_PROGRAM_ID = new anchor.web3.PublicKey(
  "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA",
);
const ASSOCIATED_TOKEN_PROGRAM_ID = new anchor.web3.PublicKey(
  "ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL",
);

type PlayerAccount = Awaited<
  ReturnType<Program<Chaindepth>["account"]["playerAccount"]["fetch"]>
>;
type RoomAccount = Awaited<
  ReturnType<Program<Chaindepth>["account"]["roomAccount"]["fetch"]>
>;
type RoomContext = {
  playerAccount: PlayerAccount;
  roomPda: anchor.web3.PublicKey;
  roomAccount: RoomAccount;
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

const deriveProfilePda = function (
  programId: anchor.web3.PublicKey,
  player: anchor.web3.PublicKey,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("profile"), player.toBuffer()],
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

const getAdjacentCoordinates = function (
  x: number,
  y: number,
  direction: number,
): { x: number; y: number } {
  if (direction === DIRECTION_NORTH) {
    return { x, y: y + 1 };
  }
  if (direction === DIRECTION_SOUTH) {
    return { x, y: y - 1 };
  }
  if (direction === DIRECTION_EAST) {
    return { x: x + 1, y };
  }
  return { x: x - 1, y };
};

const getOppositeDirection = function (direction: number): number {
  if (direction === DIRECTION_NORTH) {
    return DIRECTION_SOUTH;
  }
  if (direction === DIRECTION_SOUTH) {
    return DIRECTION_NORTH;
  }
  if (direction === DIRECTION_EAST) {
    return DIRECTION_WEST;
  }
  return DIRECTION_EAST;
};

const getOpenDirections = function (roomAccount: RoomAccount): Array<number> {
  const openDirections: Array<number> = [];
  for (let direction = 0; direction <= DIRECTION_WEST; direction += 1) {
    if (roomAccount.walls[direction] === WALL_OPEN) {
      openDirections.push(direction);
    }
  }
  return openDirections;
};

const loadRoomContext = async function (
  program: Program<Chaindepth>,
  seasonSeed: anchor.BN,
  playerPda: anchor.web3.PublicKey,
): Promise<RoomContext> {
  const playerAccount = await program.account.playerAccount.fetch(playerPda);
  const roomPda = deriveRoomPda(
    program.programId,
    seasonSeed,
    playerAccount.currentRoomX,
    playerAccount.currentRoomY,
  );
  const roomAccount = await program.account.roomAccount.fetch(roomPda);
  return {
    playerAccount,
    roomPda,
    roomAccount,
  };
};

const moveWithSession = async function (
  program: Program<Chaindepth>,
  globalPda: anchor.web3.PublicKey,
  seasonSeed: anchor.BN,
  walletPubkey: anchor.web3.PublicKey,
  playerPda: anchor.web3.PublicKey,
  profilePda: anchor.web3.PublicKey,
  sessionAuthorityPda: anchor.web3.PublicKey,
  sessionKeypair: Keypair,
  context: RoomContext,
  direction: number,
): Promise<void> {
  const targetCoordinates = getAdjacentCoordinates(
    context.playerAccount.currentRoomX,
    context.playerAccount.currentRoomY,
    direction,
  );
  const currentPresencePda = deriveRoomPresencePda(
    program.programId,
    seasonSeed,
    context.playerAccount.currentRoomX,
    context.playerAccount.currentRoomY,
    walletPubkey,
  );
  const targetPresencePda = deriveRoomPresencePda(
    program.programId,
    seasonSeed,
    targetCoordinates.x,
    targetCoordinates.y,
    walletPubkey,
  );
  const currentRoomPda = deriveRoomPda(
    program.programId,
    seasonSeed,
    context.playerAccount.currentRoomX,
    context.playerAccount.currentRoomY,
  );
  const targetRoomPda = deriveRoomPda(
    program.programId,
    seasonSeed,
    targetCoordinates.x,
    targetCoordinates.y,
  );

  console.log(
    "move_precheck:",
    `from=(${context.playerAccount.currentRoomX},${context.playerAccount.currentRoomY})`,
    `to=(${targetCoordinates.x},${targetCoordinates.y})`,
    `direction=${direction}`,
    `wall_state=${context.roomAccount.walls[direction]}`,
  );

  await program.methods
    .movePlayer(targetCoordinates.x, targetCoordinates.y)
    .accounts({
      authority: sessionKeypair.publicKey,
      player: walletPubkey,
      global: globalPda,
      playerAccount: playerPda,
      profile: profilePda,
      currentRoom: currentRoomPda,
      targetRoom: targetRoomPda,
      currentPresence: currentPresencePda,
      targetPresence: targetPresencePda,
      sessionAuthority: sessionAuthorityPda,
      systemProgram: SystemProgram.programId,
    })
    .signers([sessionKeypair])
    .rpc();
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
  const playerTokenAccount = deriveAta(globalAccount.skrMint, walletPubkey);

  console.log("=== Devnet Room Routing Smoke Test ===");
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
        roomPresence: deriveRoomPresencePda(
          program.programId,
          globalAccount.seasonSeed,
          5,
          5,
          walletPubkey,
        ),
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
      new BN(SESSION_MOVE_PLAYER_BIT),
      new BN(0),
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

  let sawSingleOpenRoom = false;

  for (let loopIndex = 0; loopIndex < FORWARD_BACKWARD_LOOP_COUNT; loopIndex += 1) {
    const fromContext = await loadRoomContext(program, globalAccount.seasonSeed, playerPda);
    const openDirections = getOpenDirections(fromContext.roomAccount);
    if (openDirections.length === 0) {
      throw new Error(
        `Room (${fromContext.playerAccount.currentRoomX}, ${fromContext.playerAccount.currentRoomY}) has zero open doors.`,
      );
    }

    const forwardDirection = openDirections[loopIndex % openDirections.length];
    const expectedBackDirection = getOppositeDirection(forwardDirection);

    await moveWithSession(
      program,
      globalPda,
      globalAccount.seasonSeed,
      walletPubkey,
      playerPda,
      profilePda,
      sessionAuthorityPda,
      sessionKeypair,
      fromContext,
      forwardDirection,
    );

    const targetContext = await loadRoomContext(program, globalAccount.seasonSeed, playerPda);
    if (targetContext.roomAccount.walls[expectedBackDirection] !== WALL_OPEN) {
      throw new Error(
        `Topology break: moved to (${targetContext.playerAccount.currentRoomX}, ${targetContext.playerAccount.currentRoomY}) but return wall dir=${expectedBackDirection} is ${targetContext.roomAccount.walls[expectedBackDirection]}.`,
      );
    }

    const targetOpenDirections = getOpenDirections(targetContext.roomAccount);
    if (targetOpenDirections.length === 1) {
      sawSingleOpenRoom = true;
      if (targetOpenDirections[0] !== expectedBackDirection) {
        throw new Error(
          `Single-open-door mismatch: expected only return direction ${expectedBackDirection}, got ${targetOpenDirections[0]}.`,
        );
      }
      console.log(
        "single_open_room_validated:",
        `room=(${targetContext.playerAccount.currentRoomX},${targetContext.playerAccount.currentRoomY})`,
        `return_dir=${expectedBackDirection}`,
      );
    }

    await moveWithSession(
      program,
      globalPda,
      globalAccount.seasonSeed,
      walletPubkey,
      playerPda,
      profilePda,
      sessionAuthorityPda,
      sessionKeypair,
      targetContext,
      expectedBackDirection,
    );

    const backContext = await loadRoomContext(program, globalAccount.seasonSeed, playerPda);
    if (
      backContext.playerAccount.currentRoomX !== fromContext.playerAccount.currentRoomX ||
      backContext.playerAccount.currentRoomY !== fromContext.playerAccount.currentRoomY
    ) {
      throw new Error(
        `Backtrack mismatch: expected (${fromContext.playerAccount.currentRoomX}, ${fromContext.playerAccount.currentRoomY}), got (${backContext.playerAccount.currentRoomX}, ${backContext.playerAccount.currentRoomY}).`,
      );
    }

    console.log(
      "forward_backward_loop_ok:",
      `loop=${loopIndex + 1}`,
      `forward_dir=${forwardDirection}`,
      `back_dir=${expectedBackDirection}`,
    );
  }

  if (!sawSingleOpenRoom) {
    let searchPreviousDirection: number | null = null;
    for (let stepIndex = 0; stepIndex < MAX_SINGLE_OPEN_SEARCH_STEPS; stepIndex += 1) {
      const searchContext = await loadRoomContext(
        program,
        globalAccount.seasonSeed,
        playerPda,
      );
      const searchOpenDirections = getOpenDirections(searchContext.roomAccount);
      if (searchOpenDirections.length === 1) {
        sawSingleOpenRoom = true;
        console.log(
          "single_open_room_validated:",
          `room=(${searchContext.playerAccount.currentRoomX},${searchContext.playerAccount.currentRoomY})`,
          `direction=${searchOpenDirections[0]}`,
        );
        break;
      }

      let searchDirection = searchOpenDirections[0];
      if (searchPreviousDirection !== null && searchOpenDirections.length > 1) {
        const backDirection = getOppositeDirection(searchPreviousDirection);
        const nonBackDirection = searchOpenDirections.find((direction) => {
          return direction !== backDirection;
        });
        if (nonBackDirection !== undefined) {
          searchDirection = nonBackDirection;
        }
      }

      await moveWithSession(
        program,
        globalPda,
        globalAccount.seasonSeed,
        walletPubkey,
        playerPda,
        profilePda,
        sessionAuthorityPda,
        sessionKeypair,
        searchContext,
        searchDirection,
      );
      searchPreviousDirection = searchDirection;
    }
  }

  if (!sawSingleOpenRoom) {
    throw new Error(
      `Unable to locate a single-open-door room after ${MAX_SINGLE_OPEN_SEARCH_STEPS} search steps.`,
    );
  }

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

  console.log("=== Room Routing Smoke Test Passed ===");
}

main().catch((thrownObject: unknown) => {
  const error = ensureError(thrownObject);
  console.error("Room routing smoke raw error:", thrownObject);
  console.error("Room routing smoke failed:", error.message);
  process.exit(1);
});

