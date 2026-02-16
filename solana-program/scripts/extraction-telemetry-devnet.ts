/**
 * Build extraction + lock telemetry dashboard from recent devnet onchain data.
 *
 * Usage:
 *   npm run telemetry-extraction -- --limit-signatures 600
 */

import * as anchor from "@coral-xyz/anchor";
import { Connection, PublicKey } from "@solana/web3.js";
import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";
import { DEVNET_RPC_URL, GLOBAL_SEED, PROGRAM_ID, START_X, START_Y } from "./constants";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.join(__dirname, "..");
const defaultSignatureLimit = 500;
const transactionFetchDelayMs = 120;

type ParsedEvent = {
  name: string;
  data: Record<string, unknown>;
};

type DungeonExitedSample = {
  signature: string;
  slot: number;
  player: string;
  runScore: number;
  timeScore: number;
  lootScore: number;
  runDurationSlots: number;
  extractedItemStacks: number;
  extractedItemUnits: number;
};

type DoorUnlockedSample = {
  signature: string;
  slot: number;
  player: string;
  roomX: number;
  roomY: number;
  direction: number;
  keyItemId: number;
};

type DungeonExitItemScoredSample = {
  signature: string;
  slot: number;
  player: string;
  itemId: number;
  amount: number;
  unitScore: number;
  stackScore: number;
};

type RoomAccountLike = {
  x: number;
  y: number;
  seasonSeed: anchor.BN | number;
  walls: Array<number>;
  forcedKeyDrop: boolean;
};

type GlobalAccountLike = {
  seasonSeed: anchor.BN | number;
};

const ensureError = function (thrownObject: unknown): Error {
  if (thrownObject instanceof Error) {
    return thrownObject;
  }
  return new Error(`Non-Error thrown: ${String(thrownObject)}`);
};

const parseArgs = function (): { signatureLimit: number } {
  let signatureLimit = defaultSignatureLimit;

  for (let index = 2; index < process.argv.length; index += 1) {
    const arg = process.argv[index];
    if (/^\d+$/.test(arg)) {
      const positionalLimit = Number.parseInt(arg, 10);
      if (Number.isFinite(positionalLimit) && positionalLimit > 0) {
        signatureLimit = positionalLimit;
      }
      continue;
    }

    if (arg === "--limit-signatures") {
      const next = process.argv[index + 1];
      if (next) {
        const parsed = Number.parseInt(next, 10);
        if (Number.isFinite(parsed) && parsed > 0) {
          signatureLimit = parsed;
        }
      }
    }
  }

  return { signatureLimit };
};

const readIdl = function (): anchor.Idl {
  const idlPath = path.join(repoRoot, "target", "idl", "chaindepth.json");
  if (!fs.existsSync(idlPath)) {
    throw new Error(
      `IDL not found at ${idlPath}. Build the program first (scripts/wsl/build.sh).`,
    );
  }
  const raw = fs.readFileSync(idlPath, "utf8");
  return JSON.parse(raw) as anchor.Idl;
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
      const parsed = candidate.toNumber();
      if (Number.isFinite(parsed)) {
        return parsed;
      }
    } catch {
      // Fall through to other parsing branches.
    }
  }

  if (typeof value === "string") {
    const parsed = Number(value);
    if (Number.isFinite(parsed)) {
      return parsed;
    }
  }

  return 0;
};

const toPublicKeyString = function (value: unknown): string {
  if (value instanceof PublicKey) {
    return value.toBase58();
  }
  if (value && typeof value === "object" && "toBase58" in value) {
    try {
      const candidate = value as { toBase58: () => string };
      return candidate.toBase58();
    } catch {
      return String(value);
    }
  }
  return String(value ?? "");
};

const getField = function (record: Record<string, unknown>, ...keys: Array<string>): unknown {
  for (const key of keys) {
    if (key in record) {
      return record[key];
    }
  }
  return undefined;
};

const getDiscriminatorFromIdl = function (
  idl: anchor.Idl,
  accountName: string,
): Array<number> {
  const accountDef = (idl.accounts ?? []).find((entry) => entry.name === accountName);
  if (!accountDef) {
    throw new Error(`Account '${accountName}' not found in IDL.`);
  }

  if (!("discriminator" in accountDef) || !Array.isArray(accountDef.discriminator)) {
    throw new Error(`Account '${accountName}' discriminator missing in IDL.`);
  }

  return accountDef.discriminator as Array<number>;
};

const hasDiscriminator = function (
  data: Buffer,
  discriminator: Array<number>,
): boolean {
  if (data.length < discriminator.length) {
    return false;
  }

  for (let index = 0; index < discriminator.length; index += 1) {
    if (data[index] !== discriminator[index]) {
      return false;
    }
  }

  return true;
};

const calculateDepth = function (x: number, y: number): number {
  const dx = Math.abs(x - START_X);
  const dy = Math.abs(y - START_Y);
  return Math.max(dx, dy);
};

const countCoordsInDepthRing = function (depthRing: number): number {
  let count = 0;
  for (let x = 0; x <= 10; x += 1) {
    for (let y = 0; y <= 10; y += 1) {
      if (calculateDepth(x, y) === depthRing) {
        count += 1;
      }
    }
  }
  return count;
};

const parseProgramEventsFromLogs = function (
  parser: anchor.EventParser,
  logs: Array<string>,
): Array<ParsedEvent> {
  const parsed: Array<ParsedEvent> = new Array<ParsedEvent>();
  for (const parsedEvent of parser.parseLogs(logs)) {
    parsed.push({
      name: parsedEvent.name,
      data: parsedEvent.data as Record<string, unknown>,
    });
  }
  return parsed;
};

const buildAlerts = function (input: {
  runCount: number;
  avgRunScore: number;
  timeShare: number;
  roomsInSeason: number;
  roomsWithLockedDoor: number;
  forcedKeyByRing: Map<number, number>;
  discoveredRoomCountByRing: Map<number, number>;
  discoveredDepthRings: Array<number>;
}): Array<string> {
  const alerts = new Array<string>();

  if (input.runCount < 10) {
    alerts.push("Low extraction sample size (<10 runs). Balance conclusions are weak.");
  }

  if (input.timeShare > 0.25) {
    alerts.push(
      `Time score share is high (${(input.timeShare * 100).toFixed(
        1,
      )}%). Target is <= 25% of total score.`,
    );
  }

  if (input.avgRunScore < 20 && input.runCount >= 10) {
    alerts.push("Average run score is low (<20). Loot values may be too conservative.");
  }

  if (input.roomsInSeason > 0) {
    const lockedRoomRate = input.roomsWithLockedDoor / input.roomsInSeason;
    if (lockedRoomRate > 0.55) {
      alerts.push(
        `Locked-door room rate is high (${(lockedRoomRate * 100).toFixed(
          1,
        )}%). Consider reducing lock cadence.`,
      );
    } else if (lockedRoomRate < 0.12) {
      alerts.push(
        `Locked-door room rate is low (${(lockedRoomRate * 100).toFixed(
          1,
        )}%). Consider increasing lock cadence.`,
      );
    }
  }

  const missingForcedKeyRings = input.discoveredDepthRings.filter((depthRing) => {
    if (depthRing < 2) {
      return false;
    }
    const discoveredCount = input.discoveredRoomCountByRing.get(depthRing) ?? 0;
    const totalCount = countCoordsInDepthRing(depthRing);
    if (discoveredCount < totalCount) {
      return false;
    }
    return (input.forcedKeyByRing.get(depthRing) ?? 0) < 1;
  });

  if (missingForcedKeyRings.length > 0) {
    alerts.push(
      `Missing forced key chest in discovered depth ring(s): ${missingForcedKeyRings.join(", ")}.`,
    );
  }

  if (alerts.length === 0) {
    alerts.push("No threshold alerts triggered.");
  }

  return alerts;
};

async function main(): Promise<void> {
  const { signatureLimit } = parseArgs();
  const idl = readIdl();
  const coder = new anchor.BorshCoder(idl);
  const connection = new Connection(DEVNET_RPC_URL, "confirmed");
  const programId = new PublicKey(PROGRAM_ID);
  const parser = new anchor.EventParser(programId, coder);

  const globalPda = PublicKey.findProgramAddressSync(
    [Buffer.from(GLOBAL_SEED)],
    programId,
  )[0];

  const globalInfo = await connection.getAccountInfo(globalPda, "confirmed");
  if (globalInfo == null) {
    throw new Error("Global account not found. Initialize devnet first.");
  }

  const globalDecoded = coder.accounts.decode("GlobalAccount", globalInfo.data) as unknown;
  const globalAccount = globalDecoded as GlobalAccountLike;
  const seasonSeed = toNumber(globalAccount.seasonSeed);

  const signatures = await connection.getSignaturesForAddress(programId, {
    limit: signatureLimit,
  });

  const dungeonExitedSamples = new Array<DungeonExitedSample>();
  const doorUnlockedSamples = new Array<DoorUnlockedSample>();
  const dungeonExitItemScoredSamples = new Array<DungeonExitItemScoredSample>();

  let truncatedByRateLimit = false;
  let scannedSignatureCount = 0;

  for (const signatureInfo of signatures) {
    let transaction;
    try {
      transaction = await connection.getTransaction(signatureInfo.signature, {
        commitment: "confirmed",
        maxSupportedTransactionVersion: 0,
      });
      scannedSignatureCount += 1;
    } catch (thrownObject: unknown) {
      const error = ensureError(thrownObject);
      if (error.message.includes("429")) {
        truncatedByRateLimit = true;
        break;
      }
      throw error;
    }

    const logs = transaction?.meta?.logMessages;
    if (logs == null || logs.length === 0) {
      await new Promise((resolve) => setTimeout(resolve, transactionFetchDelayMs));
      continue;
    }

    const parsedEvents = parseProgramEventsFromLogs(parser, logs);
    for (const parsedEvent of parsedEvents) {
      if (parsedEvent.name === "DungeonExited") {
        const player = toPublicKeyString(getField(parsedEvent.data, "player"));
        dungeonExitedSamples.push({
          signature: signatureInfo.signature,
          slot: signatureInfo.slot,
          player,
          runScore: toNumber(getField(parsedEvent.data, "runScore", "run_score")),
          timeScore: toNumber(getField(parsedEvent.data, "timeScore", "time_score")),
          lootScore: toNumber(getField(parsedEvent.data, "lootScore", "loot_score")),
          runDurationSlots: toNumber(
            getField(parsedEvent.data, "runDurationSlots", "run_duration_slots"),
          ),
          extractedItemStacks: toNumber(
            getField(parsedEvent.data, "extractedItemStacks", "extracted_item_stacks"),
          ),
          extractedItemUnits: toNumber(
            getField(parsedEvent.data, "extractedItemUnits", "extracted_item_units"),
          ),
        });
      }

      if (parsedEvent.name === "DoorUnlocked") {
        doorUnlockedSamples.push({
          signature: signatureInfo.signature,
          slot: signatureInfo.slot,
          player: toPublicKeyString(getField(parsedEvent.data, "player")),
          roomX: toNumber(getField(parsedEvent.data, "roomX", "room_x")),
          roomY: toNumber(getField(parsedEvent.data, "roomY", "room_y")),
          direction: toNumber(getField(parsedEvent.data, "direction")),
          keyItemId: toNumber(getField(parsedEvent.data, "keyItemId", "key_item_id")),
        });
      }

      if (parsedEvent.name === "DungeonExitItemScored") {
        dungeonExitItemScoredSamples.push({
          signature: signatureInfo.signature,
          slot: signatureInfo.slot,
          player: toPublicKeyString(getField(parsedEvent.data, "player")),
          itemId: toNumber(getField(parsedEvent.data, "itemId", "item_id")),
          amount: toNumber(getField(parsedEvent.data, "amount")),
          unitScore: toNumber(getField(parsedEvent.data, "unitScore", "unit_score")),
          stackScore: toNumber(getField(parsedEvent.data, "stackScore", "stack_score")),
        });
      }
    }

    await new Promise((resolve) => setTimeout(resolve, transactionFetchDelayMs));
  }

  const roomDiscriminator = getDiscriminatorFromIdl(idl, "RoomAccount");
  const roomAccountsRaw = await connection.getProgramAccounts(programId, {
    commitment: "confirmed",
  });
  const seasonRooms = new Array<RoomAccountLike>();

  for (const roomAccountRaw of roomAccountsRaw) {
    if (!hasDiscriminator(roomAccountRaw.account.data, roomDiscriminator)) {
      continue;
    }

    try {
      const decoded = coder.accounts.decode(
        "RoomAccount",
        roomAccountRaw.account.data,
      ) as unknown as RoomAccountLike;

      const roomSeasonSeed = toNumber(decoded.seasonSeed);
      if (roomSeasonSeed !== seasonSeed) {
        continue;
      }

      seasonRooms.push(decoded);
    } catch {
      continue;
    }
  }

  const uniqueExtractors = new Set<string>();
  let totalRunScore = 0;
  let totalLootScore = 0;
  let totalTimeScore = 0;
  let totalRunDurationSlots = 0;
  let totalExtractedStacks = 0;
  let totalExtractedUnits = 0;

  for (const sample of dungeonExitedSamples) {
    uniqueExtractors.add(sample.player);
    totalRunScore += sample.runScore;
    totalLootScore += sample.lootScore;
    totalTimeScore += sample.timeScore;
    totalRunDurationSlots += sample.runDurationSlots;
    totalExtractedStacks += sample.extractedItemStacks;
    totalExtractedUnits += sample.extractedItemUnits;
  }

  let roomsWithLockedDoor = 0;
  let lockedDoorCount = 0;
  let forcedKeyRoomCount = 0;
  const forcedKeyByRing = new Map<number, number>();
  const discoveredRoomCountByRing = new Map<number, number>();
  const discoveredDepthRingsSet = new Set<number>();

  for (const room of seasonRooms) {
    const roomX = toNumber(room.x);
    const roomY = toNumber(room.y);
    const depthRing = calculateDepth(roomX, roomY);
    discoveredDepthRingsSet.add(depthRing);
    discoveredRoomCountByRing.set(depthRing, (discoveredRoomCountByRing.get(depthRing) ?? 0) + 1);

    let roomLockedDoors = 0;
    for (const wall of room.walls ?? []) {
      if (toNumber(wall) === 3) {
        roomLockedDoors += 1;
      }
    }
    if (roomLockedDoors > 0) {
      roomsWithLockedDoor += 1;
      lockedDoorCount += roomLockedDoors;
    }

    if (Boolean(room.forcedKeyDrop)) {
      forcedKeyRoomCount += 1;
      forcedKeyByRing.set(depthRing, (forcedKeyByRing.get(depthRing) ?? 0) + 1);
    }
  }

  const runCount = dungeonExitedSamples.length;
  const avgRunScore = runCount > 0 ? totalRunScore / runCount : 0;
  const avgLootScore = runCount > 0 ? totalLootScore / runCount : 0;
  const avgTimeScore = runCount > 0 ? totalTimeScore / runCount : 0;
  const avgRunDurationMinutes =
    runCount > 0 ? (totalRunDurationSlots / runCount) * 0.4 / 60 : 0;
  const timeShare = totalRunScore > 0 ? totalTimeScore / totalRunScore : 0;

  const discoveredDepthRings = Array.from(discoveredDepthRingsSet).sort((a, b) => a - b);

  const itemStatsByItemId = new Map<number, { units: number; stackScore: number; unitScore: number }>();
  for (const itemSample of dungeonExitItemScoredSamples) {
    const current = itemStatsByItemId.get(itemSample.itemId) ?? {
      units: 0,
      stackScore: 0,
      unitScore: itemSample.unitScore,
    };
    current.units += itemSample.amount;
    current.stackScore += itemSample.stackScore;
    current.unitScore = itemSample.unitScore;
    itemStatsByItemId.set(itemSample.itemId, current);
  }
  const topScoredItems = Array.from(itemStatsByItemId.entries())
    .map(([itemId, stats]) => ({
      itemId,
      units: stats.units,
      score: stats.stackScore,
      unitScore: stats.unitScore,
    }))
    .sort((a, b) => b.score - a.score)
    .slice(0, 10);

  const alerts = buildAlerts({
    runCount,
    avgRunScore,
    timeShare,
    roomsInSeason: seasonRooms.length,
    roomsWithLockedDoor,
    forcedKeyByRing,
    discoveredRoomCountByRing,
    discoveredDepthRings,
  });

  const recommendations = new Array<string>();
  if (seasonRooms.length > 0) {
    const lockedRoomRate = roomsWithLockedDoor / seasonRooms.length;
    if (lockedRoomRate > 0.55) {
      recommendations.push(
        "Lower locked-door spawn chance by 5-15 percentage points and re-check after 50+ runs.",
      );
    } else if (lockedRoomRate < 0.12) {
      recommendations.push(
        "Increase locked-door spawn chance by 5-10 percentage points to keep keys meaningful.",
      );
    } else {
      recommendations.push("Keep locked-door cadence unchanged for now.");
    }
  }

  if (timeShare > 0.25) {
    recommendations.push(
      "Reduce pre-hour time bonus slope or tighten cap divisor so loot remains dominant.",
    );
  } else if (timeShare < 0.08 && runCount >= 10) {
    recommendations.push(
      "Time bonus share is very low; consider a small increase to early-run bonus if desired.",
    );
  } else {
    recommendations.push("Keep time-bonus constants unchanged for now.");
  }

  recommendations.push(
    "Review top extracted item IDs and adjust per-item score map in scoring.rs where loot feels under/over-valued.",
  );

  const logsDir = path.join(repoRoot, "logs");
  if (!fs.existsSync(logsDir)) {
    fs.mkdirSync(logsDir, { recursive: true });
  }

  const jsonOutputPath = path.join(logsDir, "extraction-telemetry-dashboard.json");
  const markdownOutputPath = path.join(logsDir, "extraction-telemetry-dashboard.md");

  const jsonOutput = {
    generatedAt: new Date().toISOString(),
    programId: programId.toBase58(),
    rpcUrl: DEVNET_RPC_URL,
    seasonSeed,
    sampledSignatures: signatures.length,
    scannedSignatures: scannedSignatureCount,
    truncatedByRateLimit,
    runCount,
    uniqueExtractors: uniqueExtractors.size,
    totalRunScore,
    totalLootScore,
    totalTimeScore,
    timeShare,
    avgRunScore,
    avgLootScore,
    avgTimeScore,
    avgRunDurationMinutes,
    totalExtractedStacks,
    totalExtractedUnits,
    doorUnlockCount: doorUnlockedSamples.length,
    roomsInSeason: seasonRooms.length,
    roomsWithLockedDoor,
    lockedDoorCount,
    forcedKeyRoomCount,
    discoveredDepthRings,
    discoveredRoomCountByRing: Object.fromEntries(discoveredRoomCountByRing.entries()),
    forcedKeyByRing: Object.fromEntries(forcedKeyByRing.entries()),
    topScoredItems,
    alerts,
    recommendations,
  };

  fs.writeFileSync(jsonOutputPath, JSON.stringify(jsonOutput, null, 2));

  const markdownLines = new Array<string>();
  markdownLines.push("# Extraction + Lock Telemetry Dashboard");
  markdownLines.push("");
  markdownLines.push(`Generated: ${jsonOutput.generatedAt}`);
  markdownLines.push(`Program: \`${jsonOutput.programId}\``);
  markdownLines.push(`Season seed: \`${seasonSeed}\``);
  markdownLines.push(`Sampled signatures: \`${signatures.length}\``);
  markdownLines.push(`Scanned signatures: \`${scannedSignatureCount}\``);
  markdownLines.push(`Rate-limit truncated: \`${truncatedByRateLimit}\``);
  markdownLines.push("");
  markdownLines.push("## Extraction Metrics");
  markdownLines.push("");
  markdownLines.push(`- Runs extracted: **${runCount}**`);
  markdownLines.push(`- Unique extractors: **${uniqueExtractors.size}**`);
  markdownLines.push(`- Avg run score: **${avgRunScore.toFixed(2)}**`);
  markdownLines.push(`- Avg loot score: **${avgLootScore.toFixed(2)}**`);
  markdownLines.push(`- Avg time score: **${avgTimeScore.toFixed(2)}**`);
  markdownLines.push(`- Time share of score: **${(timeShare * 100).toFixed(2)}%**`);
  markdownLines.push(`- Avg run duration: **${avgRunDurationMinutes.toFixed(2)} min**`);
  markdownLines.push(`- Total extracted stacks: **${totalExtractedStacks}**`);
  markdownLines.push(`- Total extracted units: **${totalExtractedUnits}**`);
  markdownLines.push("");
  markdownLines.push("## Lock + Key Metrics");
  markdownLines.push("");
  markdownLines.push(`- Door unlock events: **${doorUnlockedSamples.length}**`);
  markdownLines.push(`- Rooms in current season: **${seasonRooms.length}**`);
  markdownLines.push(`- Rooms with >=1 locked door: **${roomsWithLockedDoor}**`);
  markdownLines.push(`- Locked doors total: **${lockedDoorCount}**`);
  markdownLines.push(`- Forced-key rooms discovered: **${forcedKeyRoomCount}**`);
  markdownLines.push(
    `- Discovered depth rings: **${discoveredDepthRings.length > 0 ? discoveredDepthRings.join(", ") : "none"}**`,
  );
  markdownLines.push("");
  markdownLines.push("## Forced Key Chest By Ring");
  markdownLines.push("");

  if (forcedKeyByRing.size === 0) {
    markdownLines.push("- No forced key rooms found in sampled season rooms.");
  } else {
    for (const depthRing of Array.from(forcedKeyByRing.keys()).sort((a, b) => a - b)) {
      markdownLines.push(`- Depth ${depthRing}: ${forcedKeyByRing.get(depthRing) ?? 0}`);
    }
  }

  markdownLines.push("");
  markdownLines.push("## Ring Coverage");
  markdownLines.push("");
  if (discoveredDepthRings.length === 0) {
    markdownLines.push("- No depth rings discovered.");
  } else {
    for (const depthRing of discoveredDepthRings) {
      const discoveredCount = discoveredRoomCountByRing.get(depthRing) ?? 0;
      const totalCount = countCoordsInDepthRing(depthRing);
      markdownLines.push(`- Depth ${depthRing}: ${discoveredCount}/${totalCount} rooms discovered`);
    }
  }

  markdownLines.push("");
  markdownLines.push("## Top Scored Extracted Items");
  markdownLines.push("");
  if (topScoredItems.length === 0) {
    markdownLines.push("- No item-level extraction events found in sampled signatures.");
  } else {
    for (const itemStat of topScoredItems) {
      markdownLines.push(
        `- Item ${itemStat.itemId}: units=${itemStat.units}, totalScore=${itemStat.score}, unitScore=${itemStat.unitScore}`,
      );
    }
  }

  markdownLines.push("");
  markdownLines.push("## Alerts");
  markdownLines.push("");
  for (const alert of alerts) {
    markdownLines.push(`- ${alert}`);
  }

  markdownLines.push("");
  markdownLines.push("## Recommendations");
  markdownLines.push("");
  for (const recommendation of recommendations) {
    markdownLines.push(`- ${recommendation}`);
  }

  fs.writeFileSync(markdownOutputPath, `${markdownLines.join("\n")}\n`);

  console.log("Telemetry dashboard generated:");
  console.log(`- ${jsonOutputPath}`);
  console.log(`- ${markdownOutputPath}`);
}

main().catch((thrownObject: unknown) => {
  const error = ensureError(thrownObject);
  console.error("Telemetry dashboard failed:", error.message);
  process.exit(1);
});
