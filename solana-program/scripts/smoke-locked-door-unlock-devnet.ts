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

const DIRECTION_NORTH = 0;
const DIRECTION_SOUTH = 1;
const DIRECTION_EAST = 2;
const DIRECTION_WEST = 3;
const WALL_SOLID = 0;
const WALL_RUBBLE = 1;
const WALL_OPEN = 2;
const WALL_LOCKED = 3;

const SESSION_BOOST_JOB_BIT = 1 << 0;
const SESSION_CLAIM_JOB_REWARD_BIT = 1 << 2;
const SESSION_MOVE_PLAYER_BIT = 1 << 6;
const SESSION_JOIN_JOB_BIT = 1 << 7;
const SESSION_COMPLETE_JOB_BIT = 1 << 8;
const SESSION_UNLOCK_DOOR_BIT = 1 << 13;

const SESSION_FUNDING_LAMPORTS = 50_000_000;
const SESSION_DURATION_SLOTS = 5_000;
const SESSION_DURATION_SECONDS = 3_600;

const SKELETON_KEY_ITEM_ID = 214;
const BOOST_TIP_FOR_INSTANT_COMPLETE = 10_000_000;
const MAX_EXPLORATION_STEPS = 60;
const START_ROOM_X = 5;
const START_ROOM_Y = 5;

type PlayerAccount = Awaited<
  ReturnType<Program<Chaindepth>["account"]["playerAccount"]["fetch"]>
>;
type RoomAccount = Awaited<
  ReturnType<Program<Chaindepth>["account"]["roomAccount"]["fetch"]>
>;
type InventoryAccount = Awaited<
  ReturnType<Program<Chaindepth>["account"]["inventoryAccount"]["fetch"]>
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

const deriveInventoryPda = function (
  programId: anchor.web3.PublicKey,
  player: anchor.web3.PublicKey,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("inventory"), player.toBuffer()],
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

const roomKey = function (x: number, y: number): string {
  return `${x},${y}`;
};

const getSkeletonKeyAmount = function (inventory: InventoryAccount | null): number {
  if (inventory == null) {
    return 0;
  }
  let total = 0;
  for (const item of inventory.items) {
    if (item.itemId === SKELETON_KEY_ITEM_ID) {
      total += item.amount;
    }
  }
  return total;
};

const fetchInventoryNullable = async function (
  program: Program<Chaindepth>,
  inventoryPda: anchor.web3.PublicKey,
): Promise<InventoryAccount | null> {
  const maybeInventory = await program.account.inventoryAccount.fetchNullable(inventoryPda);
  return maybeInventory ?? null;
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

const clearRubbleDoor = async function (
  program: Program<Chaindepth>,
  context: RoomContext,
  globalPda: anchor.web3.PublicKey,
  playerPda: anchor.web3.PublicKey,
  walletPubkey: anchor.web3.PublicKey,
  playerTokenAccount: anchor.web3.PublicKey,
  sessionAuthorityPda: anchor.web3.PublicKey,
  sessionKeypair: Keypair,
  direction: number,
): Promise<void> {
  const globalAccount = await program.account.globalAccount.fetch(globalPda);
  const roomPresencePda = deriveRoomPresencePda(
    program.programId,
    globalAccount.seasonSeed,
    context.playerAccount.currentRoomX,
    context.playerAccount.currentRoomY,
    walletPubkey,
  );
  const escrowPda = deriveEscrowPda(program.programId, context.roomPda, direction);
  const helperStakePda = deriveHelperStakePda(
    program.programId,
    context.roomPda,
    direction,
    walletPubkey,
  );
  const helperStakeExists =
    (await program.provider.connection.getAccountInfo(helperStakePda)) !== null;

  if (!helperStakeExists) {
    await program.methods
      .joinJobWithSession(direction)
      .accountsPartial({
        authority: sessionKeypair.publicKey,
        player: walletPubkey,
        global: globalPda,
        playerAccount: playerPda,
        room: context.roomPda,
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
    console.log(`join_job_with_session: direction=${direction}`);
  }

  const refreshedRoom = await program.account.roomAccount.fetch(context.roomPda);
  if (!refreshedRoom.jobCompleted[direction]) {
    await program.methods
      .boostJob(direction, new BN(BOOST_TIP_FOR_INSTANT_COMPLETE))
      .accountsPartial({
        authority: sessionKeypair.publicKey,
        player: walletPubkey,
        global: globalPda,
        room: context.roomPda,
        prizePool: globalAccount.prizePool,
        playerTokenAccount,
        sessionAuthority: sessionAuthorityPda,
        tokenProgram: TOKEN_PROGRAM_ID,
      })
      .signers([sessionKeypair])
      .rpc();
    console.log(`boost_job: direction=${direction}`);

    const adjacentCoordinates = getAdjacentCoordinates(
      context.playerAccount.currentRoomX,
      context.playerAccount.currentRoomY,
      direction,
    );
    const adjacentRoomPda = deriveRoomPda(
      program.programId,
      globalAccount.seasonSeed,
      adjacentCoordinates.x,
      adjacentCoordinates.y,
    );

    await program.methods
      .completeJob(direction)
      .accountsPartial({
        authority: sessionKeypair.publicKey,
        player: walletPubkey,
        global: globalPda,
        playerAccount: playerPda,
        room: context.roomPda,
        helperStake: helperStakePda,
        adjacentRoom: adjacentRoomPda,
        escrow: escrowPda,
        prizePool: globalAccount.prizePool,
        sessionAuthority: sessionAuthorityPda,
        tokenProgram: TOKEN_PROGRAM_ID,
        systemProgram: SystemProgram.programId,
      })
      .signers([sessionKeypair])
      .rpc();
    console.log(`complete_job: direction=${direction}`);
  }

  const helperStakeStillExists =
    (await program.provider.connection.getAccountInfo(helperStakePda)) !== null;
  const postCompleteRoom = await program.account.roomAccount.fetch(context.roomPda);
  if (helperStakeStillExists && postCompleteRoom.jobCompleted[direction]) {
    await program.methods
      .claimJobReward(direction)
      .accountsPartial({
        authority: sessionKeypair.publicKey,
        player: walletPubkey,
        global: globalPda,
        playerAccount: playerPda,
        room: context.roomPda,
        roomPresence: roomPresencePda,
        escrow: escrowPda,
        helperStake: helperStakePda,
        playerTokenAccount,
        sessionAuthority: sessionAuthorityPda,
        tokenProgram: TOKEN_PROGRAM_ID,
      })
      .signers([sessionKeypair])
      .rpc();
    console.log(`claim_job_reward: direction=${direction}`);
  }
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
  const inventoryPda = deriveInventoryPda(program.programId, walletPubkey);
  const playerTokenAccount = deriveAta(globalAccount.skrMint, walletPubkey);

  const sessionKeypair = Keypair.generate();
  const sessionAuthorityPda = deriveSessionAuthorityPda(
    program.programId,
    walletPubkey,
    sessionKeypair.publicKey,
  );

  let sessionStarted = false;

  console.log("=== Devnet Locked Door Unlock Smoke Test ===");
  console.log("Wallet:", walletPubkey.toBase58());
  console.log("Program:", program.programId.toBase58());
  console.log("Global PDA:", globalPda.toBase58());
  console.log("Season seed:", globalAccount.seasonSeed.toString());

  try {
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
            START_ROOM_X,
            START_ROOM_Y,
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

    await program.methods
      .addInventoryItem(SKELETON_KEY_ITEM_ID, 2, 0)
      .accountsPartial({
        player: walletPubkey,
        inventory: inventoryPda,
        systemProgram: SystemProgram.programId,
      })
      .rpc();
    console.log("seed_inventory: added 2 SkeletonKey");

    const sessionBalance = await provider.connection.getBalance(sessionKeypair.publicKey);
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

    const currentSlot = await provider.connection.getSlot("confirmed");
    const currentTimestamp = await provider.connection.getBlockTime(currentSlot);
    if (currentTimestamp === null) {
      throw new Error("Failed to fetch current block time from devnet.");
    }

    const sessionAllowlist =
      SESSION_BOOST_JOB_BIT |
      SESSION_CLAIM_JOB_REWARD_BIT |
      SESSION_MOVE_PLAYER_BIT |
      SESSION_JOIN_JOB_BIT |
      SESSION_COMPLETE_JOB_BIT |
      SESSION_UNLOCK_DOOR_BIT;

    await program.methods
      .beginSession(
        new BN(currentSlot + SESSION_DURATION_SLOTS),
        new BN(currentTimestamp + SESSION_DURATION_SECONDS),
        new BN(sessionAllowlist),
        new BN(500_000_000),
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
    sessionStarted = true;
    console.log("begin_session: ok");

    const visited = new Set<string>();
    let previousDirection: number | null = null;

    for (let step = 0; step < MAX_EXPLORATION_STEPS; step += 1) {
      const context = await loadRoomContext(program, globalAccount.seasonSeed, playerPda);
      const x = context.playerAccount.currentRoomX;
      const y = context.playerAccount.currentRoomY;
      const visitKey = roomKey(x, y);
      visited.add(visitKey);

      const walls = context.roomAccount.walls;
      const lockKinds = context.roomAccount.doorLockKinds;
      console.log(
        `step=${step + 1} room=(${x},${y}) walls=[${walls.join(",")}] lockKinds=[${lockKinds.join(",")}]`,
      );

      let lockedDirection: number | null = null;
      for (let direction = DIRECTION_NORTH; direction <= DIRECTION_WEST; direction += 1) {
        if (walls[direction] === WALL_LOCKED) {
          lockedDirection = direction;
          break;
        }
      }

      if (lockedDirection !== null) {
        const beforeInventory = await fetchInventoryNullable(program, inventoryPda);
        const beforeKeyCount = getSkeletonKeyAmount(beforeInventory);
        const adjacentCoordinates = getAdjacentCoordinates(x, y, lockedDirection);
        const adjacentRoomPda = deriveRoomPda(
          program.programId,
          globalAccount.seasonSeed,
          adjacentCoordinates.x,
          adjacentCoordinates.y,
        );

        const signature = await program.methods
          .unlockDoor(lockedDirection)
          .accountsPartial({
            authority: sessionKeypair.publicKey,
            player: walletPubkey,
            global: globalPda,
            playerAccount: playerPda,
            room: context.roomPda,
            adjacentRoom: adjacentRoomPda,
            inventory: inventoryPda,
            sessionAuthority: sessionAuthorityPda,
            systemProgram: SystemProgram.programId,
          })
          .signers([sessionKeypair])
          .rpc();

        const afterRoom = await program.account.roomAccount.fetch(context.roomPda);
        const afterAdjacentRoom = await program.account.roomAccount.fetch(adjacentRoomPda);
        const afterInventory = await fetchInventoryNullable(program, inventoryPda);
        const afterKeyCount = getSkeletonKeyAmount(afterInventory);
        const oppositeDirection = getOppositeDirection(lockedDirection);

        if (afterRoom.walls[lockedDirection] !== WALL_OPEN) {
          throw new Error("Unlock succeeded but current room wall did not become open.");
        }
        if (afterRoom.doorLockKinds[lockedDirection] !== 0) {
          throw new Error("Unlock succeeded but current room lock kind was not cleared.");
        }
        if (afterAdjacentRoom.walls[oppositeDirection] !== WALL_OPEN) {
          throw new Error("Unlock succeeded but adjacent return wall is not open.");
        }
        if (afterAdjacentRoom.doorLockKinds[oppositeDirection] !== 0) {
          throw new Error("Unlock succeeded but adjacent return lock kind was not cleared.");
        }
        if (!(afterKeyCount < beforeKeyCount)) {
          throw new Error(
            `Unlock succeeded but key amount did not decrease. before=${beforeKeyCount} after=${afterKeyCount}`,
          );
        }

        console.log("unlock_door: ok", signature);
        console.log("=== Locked Door Unlock Smoke Test Passed ===");
        return;
      }

      let moveDirection: number | null = null;
      for (let direction = DIRECTION_NORTH; direction <= DIRECTION_WEST; direction += 1) {
        if (walls[direction] !== WALL_OPEN) {
          continue;
        }
        const adjacentCoordinates = getAdjacentCoordinates(x, y, direction);
        const adjacentKey = roomKey(adjacentCoordinates.x, adjacentCoordinates.y);
        if (visited.has(adjacentKey)) {
          continue;
        }
        moveDirection = direction;
        break;
      }

      if (moveDirection === null && previousDirection !== null) {
        const oppositeOfPrevious = getOppositeDirection(previousDirection);
        if (walls[oppositeOfPrevious] === WALL_OPEN) {
          moveDirection = oppositeOfPrevious;
        }
      }

      if (moveDirection === null) {
        for (let direction = DIRECTION_NORTH; direction <= DIRECTION_WEST; direction += 1) {
          if (walls[direction] === WALL_OPEN) {
            moveDirection = direction;
            break;
          }
        }
      }

      if (moveDirection === null) {
        let rubbleDirection: number | null = null;
        for (let direction = DIRECTION_NORTH; direction <= DIRECTION_WEST; direction += 1) {
          if (walls[direction] === WALL_RUBBLE) {
            rubbleDirection = direction;
            break;
          }
        }

        if (rubbleDirection !== null) {
          console.log(`no open exits, clearing rubble direction=${rubbleDirection}`);
          await clearRubbleDoor(
            program,
            context,
            globalPda,
            playerPda,
            walletPubkey,
            playerTokenAccount,
            sessionAuthorityPda,
            sessionKeypair,
            rubbleDirection,
          );
          moveDirection = rubbleDirection;
        }
      }

      if (moveDirection === null) {
        const solidCount = walls.filter((value) => value === WALL_SOLID).length;
        throw new Error(
          `Stuck at room (${x},${y}) with no open/rubble exits. solid_count=${solidCount}`,
        );
      }

      const targetCoordinates = getAdjacentCoordinates(x, y, moveDirection);
      const currentPresencePda = deriveRoomPresencePda(
        program.programId,
        globalAccount.seasonSeed,
        x,
        y,
        walletPubkey,
      );
      const targetPresencePda = deriveRoomPresencePda(
        program.programId,
        globalAccount.seasonSeed,
        targetCoordinates.x,
        targetCoordinates.y,
        walletPubkey,
      );
      const currentRoomPda = context.roomPda;
      const targetRoomPda = deriveRoomPda(
        program.programId,
        globalAccount.seasonSeed,
        targetCoordinates.x,
        targetCoordinates.y,
      );

      await program.methods
        .movePlayer(targetCoordinates.x, targetCoordinates.y)
        .accountsPartial({
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

      console.log(`move_player: to=(${targetCoordinates.x},${targetCoordinates.y})`);
      previousDirection = moveDirection;
    }

    throw new Error(
      `Did not find a locked door after ${MAX_EXPLORATION_STEPS} steps.`,
    );
  } finally {
    if (sessionStarted) {
      try {
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
      } catch (thrownObject: unknown) {
        const error = ensureError(thrownObject);
        console.log(`end_session: skipped (${error.message})`);
      }
    }
  }
}

main().catch((thrownObject: unknown) => {
  const error = ensureError(thrownObject);
  console.error("Locked-door smoke raw error:", thrownObject);
  console.error("Locked-door smoke failed:", error.message);
  process.exit(1);
});
