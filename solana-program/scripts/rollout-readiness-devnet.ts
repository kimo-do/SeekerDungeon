/**
 * Shared-devnet rollout readiness check for locked doors + extraction changes.
 *
 * Usage:
 *   npm run rollout-readiness
 */

import * as anchor from "@coral-xyz/anchor";
import { PublicKey } from "@solana/web3.js";
import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";
import { DEVNET_RPC_URL, GLOBAL_SEED, PROGRAM_ID, ROOM_SEED, START_X, START_Y } from "./constants";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.join(__dirname, "..");

type GlobalAccountLike = {
  seasonSeed: anchor.BN | number | bigint | string;
};

type RoomAccountLike = {
  x: number;
  y: number;
  seasonSeed: anchor.BN | number | bigint | string;
  walls: Array<number>;
  centerType: number;
};

const ensureError = function (thrownObject: unknown): Error {
  if (thrownObject instanceof Error) {
    return thrownObject;
  }
  return new Error(`Non-Error thrown: ${String(thrownObject)}`);
};

const toNumber = function (value: unknown): number {
  if (typeof value === "number") {
    return value;
  }
  if (typeof value === "bigint") {
    return Number(value);
  }
  if (value != null && typeof value === "object" && "toNumber" in value) {
    try {
      const candidate = value as { toNumber: () => number };
      return candidate.toNumber();
    } catch {
      return 0;
    }
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
};

const toU64BigInt = function (value: unknown): bigint {
  if (typeof value === "bigint") {
    return value;
  }
  if (typeof value === "number") {
    if (!Number.isFinite(value) || value < 0) {
      return 0n;
    }
    return BigInt(Math.trunc(value));
  }
  if (typeof value === "string") {
    try {
      return BigInt(value);
    } catch {
      return 0n;
    }
  }
  if (value != null && typeof value === "object" && "toString" in value) {
    try {
      const candidate = value as { toString: () => string };
      return BigInt(candidate.toString());
    } catch {
      return 0n;
    }
  }
  return 0n;
};

const getField = function (record: Record<string, unknown>, ...keys: Array<string>): unknown {
  for (const key of keys) {
    if (key in record) {
      return record[key];
    }
  }
  return undefined;
};

const readIdl = function (): anchor.Idl {
  const idlPath = path.join(repoRoot, "target", "idl", "chaindepth.json");
  if (!fs.existsSync(idlPath)) {
    throw new Error(`IDL missing: ${idlPath}. Build first.`);
  }
  return JSON.parse(fs.readFileSync(idlPath, "utf8")) as anchor.Idl;
};

const deriveRoomPda = function (
  programId: PublicKey,
  seasonSeed: bigint,
  x: number,
  y: number,
): PublicKey {
  const seasonSeedBuffer = Buffer.alloc(8);
  seasonSeedBuffer.writeBigUInt64LE(seasonSeed);
  return PublicKey.findProgramAddressSync(
    [
      Buffer.from(ROOM_SEED),
      seasonSeedBuffer,
      Buffer.from([x & 0xff]),
      Buffer.from([y & 0xff]),
    ],
    programId,
  )[0];
};

async function main(): Promise<void> {
  const idl = readIdl();
  const coder = new anchor.BorshCoder(idl);
  const connection = new anchor.web3.Connection(DEVNET_RPC_URL, "confirmed");
  const programId = new PublicKey(PROGRAM_ID);

  const globalPda = PublicKey.findProgramAddressSync(
    [Buffer.from(GLOBAL_SEED)],
    programId,
  )[0];
  const globalInfo = await connection.getAccountInfo(globalPda, "confirmed");

  if (globalInfo == null) {
    throw new Error("Global account missing. Run npm run init-devnet first.");
  }

  const global = coder.accounts.decode("GlobalAccount", globalInfo.data) as unknown as Record<
    string,
    unknown
  >;
  const seasonSeed = toU64BigInt(getField(global, "seasonSeed", "season_seed"));

  const startRoomPda = deriveRoomPda(programId, seasonSeed, START_X, START_Y);
  const belowStartRoomPda = deriveRoomPda(programId, seasonSeed, START_X, START_Y - 1);
  const startRoomInfo = await connection.getAccountInfo(startRoomPda, "confirmed");
  const belowRoomInfo = await connection.getAccountInfo(belowStartRoomPda, "confirmed");

  const checks = new Array<{ name: string; pass: boolean; detail: string }>();
  checks.push({
    name: "Global account exists",
    pass: globalInfo != null,
    detail: globalPda.toBase58(),
  });
  checks.push({
    name: "Season seed is set",
    pass: seasonSeed > 0n,
    detail: seasonSeed.toString(),
  });

  let startRoom: RoomAccountLike | null = null;
  if (startRoomInfo != null) {
    startRoom = coder.accounts.decode("RoomAccount", startRoomInfo.data) as unknown as RoomAccountLike;
  }
  checks.push({
    name: "Start room account exists",
    pass: startRoom != null,
    detail: startRoomPda.toBase58(),
  });
  if (startRoom != null) {
    const southWall = toNumber(startRoom.walls[1]);
    checks.push({
      name: "Start room south is entrance stairs",
      pass: southWall === 4,
      detail: `wall[South]=${southWall}`,
    });
  }

  let belowRoom: RoomAccountLike | null = null;
  if (belowRoomInfo != null) {
    belowRoom = coder.accounts.decode("RoomAccount", belowRoomInfo.data) as unknown as RoomAccountLike;
  }
  checks.push({
    name: "Room below start exists",
    pass: belowRoom != null,
    detail: belowStartRoomPda.toBase58(),
  });
  if (belowRoom != null) {
    const northWall = toNumber(belowRoom.walls[0]);
    checks.push({
      name: "Room below start north is sealed",
      pass: northWall === 0,
      detail: `wall[North]=${northWall}`,
    });
  }

  console.log("=== Shared Devnet Rollout Readiness ===");
  for (const check of checks) {
    const status = check.pass ? "PASS" : "FAIL";
    console.log(`[${status}] ${check.name} :: ${check.detail}`);
  }

  console.log("");
  console.log("Manual-gate steps still required:");
  console.log("1) Deploy upgraded program + refresh IDL in Unity.");
  console.log("2) Reset/migrate season + player state for old-layout accounts.");
  console.log("3) Run smoke checks: npm test, npm run smoke-session-join-job, npm run smoke-room-routing.");
  console.log("4) Re-run telemetry: npm run telemetry-extraction -- --limit-signatures 600.");
}

main().catch((thrownObject: unknown) => {
  const error = ensureError(thrownObject);
  console.error("Rollout readiness check failed:", error.message);
  process.exit(1);
});
