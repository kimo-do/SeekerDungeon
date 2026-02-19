import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import type { Chaindepth } from "../target/types/chaindepth";
import { SystemProgram } from "@solana/web3.js";

const CENTER_BOSS = 2;
const WALL_OPEN = 2;
const START_ROOM_X = 5;
const START_ROOM_Y = 5;
const MAX_BOSS_SEARCH_STEPS = 40;
const DAMAGE_SLOT_STEP = 150;
const SLOT_POLL_MS = 1000;

type PlayerAccount = Awaited<
  ReturnType<Program<Chaindepth>["account"]["playerAccount"]["fetch"]>
>;
type RoomAccount = Awaited<
  ReturnType<Program<Chaindepth>["account"]["roomAccount"]["fetch"]>
>;

const ensureError = function (thrownObject: unknown): Error {
  if (thrownObject instanceof Error) {
    return thrownObject;
  }
  return new Error(`Non-Error thrown: ${String(thrownObject)}`);
};

const sleep = async function (ms: number): Promise<void> {
  await new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
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

const deriveBossFightPda = function (
  programId: anchor.web3.PublicKey,
  room: anchor.web3.PublicKey,
  player: anchor.web3.PublicKey,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("boss_fight"), room.toBuffer(), player.toBuffer()],
    programId,
  )[0];
};

const getAdjacentCoordinates = function (
  x: number,
  y: number,
  direction: number,
): { x: number; y: number } {
  if (direction === 0) {
    return { x, y: y + 1 };
  }
  if (direction === 1) {
    return { x, y: y - 1 };
  }
  if (direction === 2) {
    return { x: x + 1, y };
  }
  return { x: x - 1, y };
};

const loadPlayerAndRoom = async function (
  program: Program<Chaindepth>,
  seasonSeed: anchor.BN,
  playerPda: anchor.web3.PublicKey,
): Promise<{ player: PlayerAccount; room: RoomAccount; roomPda: anchor.web3.PublicKey }> {
  const player = await program.account.playerAccount.fetch(playerPda);
  const roomPda = deriveRoomPda(
    program.programId,
    seasonSeed,
    player.currentRoomX,
    player.currentRoomY,
  );
  const room = await program.account.roomAccount.fetch(roomPda);
  return { player, room, roomPda };
};

async function main(): Promise<void> {
  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);

  const program = anchor.workspace.Chaindepth as Program<Chaindepth>;
  const walletPubkey = provider.wallet.publicKey;
  const connection = provider.connection;

  const globalPda = deriveGlobalPda(program.programId);
  const global = await program.account.globalAccount.fetch(globalPda);
  const playerPda = derivePlayerPda(program.programId, walletPubkey);
  const profilePda = deriveProfilePda(program.programId, walletPubkey);
  const inventoryPda = deriveInventoryPda(program.programId, walletPubkey);

  console.log("=== Devnet Boss HP + Death Smoke Test ===");
  console.log("Wallet:", walletPubkey.toBase58());
  console.log("Program:", program.programId.toBase58());
  console.log("Season seed:", global.seasonSeed.toString());

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
          global.seasonSeed,
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

  let context = await loadPlayerAndRoom(program, global.seasonSeed, playerPda);
  let previousCoordinates: { x: number; y: number } | null = null;

  for (let step = 0; step < MAX_BOSS_SEARCH_STEPS && context.room.centerType !== CENTER_BOSS; step += 1) {
    let moveDirection: number | null = null;
    for (let direction = 0; direction < 4; direction += 1) {
      if (context.room.walls[direction] !== WALL_OPEN) {
        continue;
      }
      const adjacent = getAdjacentCoordinates(
        context.player.currentRoomX,
        context.player.currentRoomY,
        direction,
      );
      if (
        previousCoordinates !== null &&
        adjacent.x === previousCoordinates.x &&
        adjacent.y === previousCoordinates.y
      ) {
        continue;
      }
      moveDirection = direction;
      break;
    }

    if (moveDirection === null) {
      for (let direction = 0; direction < 4; direction += 1) {
        if (context.room.walls[direction] === WALL_OPEN) {
          moveDirection = direction;
          break;
        }
      }
    }

    if (moveDirection === null) {
      break;
    }

    const target = getAdjacentCoordinates(
      context.player.currentRoomX,
      context.player.currentRoomY,
      moveDirection,
    );

    await program.methods
      .movePlayer(target.x, target.y)
      .accountsPartial({
        authority: walletPubkey,
        player: walletPubkey,
        global: globalPda,
        playerAccount: playerPda,
        profile: profilePda,
        currentRoom: context.roomPda,
        targetRoom: deriveRoomPda(program.programId, global.seasonSeed, target.x, target.y),
        currentPresence: deriveRoomPresencePda(
          program.programId,
          global.seasonSeed,
          context.player.currentRoomX,
          context.player.currentRoomY,
          walletPubkey,
        ),
        targetPresence: deriveRoomPresencePda(
          program.programId,
          global.seasonSeed,
          target.x,
          target.y,
          walletPubkey,
        ),
        sessionAuthority: null,
        systemProgram: SystemProgram.programId,
      })
      .rpc();

    previousCoordinates = {
      x: context.player.currentRoomX,
      y: context.player.currentRoomY,
    };
    context = await loadPlayerAndRoom(program, global.seasonSeed, playerPda);
    console.log(
      `move: now at (${context.player.currentRoomX}, ${context.player.currentRoomY}), centerType=${context.room.centerType}`,
    );
  }

  if (context.room.centerType !== CENTER_BOSS) {
    throw new Error("Failed to find a boss room within search limit.");
  }

  const roomPresencePda = deriveRoomPresencePda(
    program.programId,
    global.seasonSeed,
    context.player.currentRoomX,
    context.player.currentRoomY,
    walletPubkey,
  );
  const bossFightPda = deriveBossFightPda(program.programId, context.roomPda, walletPubkey);

  await program.methods
    .joinBossFight()
    .accountsPartial({
      authority: walletPubkey,
      player: walletPubkey,
      global: globalPda,
      playerAccount: playerPda,
      profile: profilePda,
      room: context.roomPda,
      roomPresence: roomPresencePda,
      bossFight: bossFightPda,
      inventory: inventoryPda,
      sessionAuthority: null,
      systemProgram: SystemProgram.programId,
    })
    .rpc();
  console.log("join_boss_fight: ok");

  const hpBeforeTick = (await program.account.playerAccount.fetch(playerPda)).currentHp;
  const bossFight = await program.account.bossFightAccount.fetch(bossFightPda);
  const targetSlot = Number(bossFight.lastDamageSlot) + DAMAGE_SLOT_STEP;
  console.log(`hp_before_tick: ${hpBeforeTick}`);
  console.log(`waiting_for_slot: ${targetSlot}`);

  while (true) {
    const slot = await connection.getSlot("confirmed");
    if (slot >= targetSlot) {
      break;
    }
    await sleep(SLOT_POLL_MS);
  }

  await program.methods
    .tickBossFight()
    .accountsPartial({
      authority: walletPubkey,
      player: walletPubkey,
      global: globalPda,
      playerAccount: playerPda,
      room: context.roomPda,
      roomPresence: roomPresencePda,
      bossFight: bossFightPda,
      inventory: inventoryPda,
      sessionAuthority: null,
      systemProgram: SystemProgram.programId,
    })
    .rpc();
  console.log("tick_boss_fight: ok");

  const hpAfterTick = (await program.account.playerAccount.fetch(playerPda)).currentHp;
  console.log(`hp_after_tick: ${hpAfterTick}`);
  if (hpAfterTick >= hpBeforeTick) {
    throw new Error("Expected hp to decrease after boss tick, but it did not.");
  }

  await program.methods
    .forceExitOnDeath()
    .accountsPartial({
      authority: walletPubkey,
      player: walletPubkey,
      global: globalPda,
      playerAccount: playerPda,
      room: context.roomPda,
      inventory: inventoryPda,
      roomPresence: roomPresencePda,
      sessionAuthority: null,
      systemProgram: SystemProgram.programId,
    })
    .rpc();
  console.log("force_exit_on_death: ok");

  const finalPlayer = await program.account.playerAccount.fetch(playerPda);
  if (finalPlayer.inDungeon) {
    throw new Error("Expected player to be out of dungeon after death exit.");
  }
  if (finalPlayer.currentHp !== finalPlayer.maxHp) {
    throw new Error("Expected player HP to be restored to max after death exit.");
  }

  console.log(`final_hp: ${finalPlayer.currentHp}/${finalPlayer.maxHp}`);
  console.log("=== Boss HP + Death Smoke Test Passed ===");
}

main().catch((thrownObject: unknown) => {
  const error = ensureError(thrownObject);
  console.error("Boss HP/death smoke failed:", error.message);
  process.exit(1);
});
