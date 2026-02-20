type DuelConfig = {
  startingHp: number;
  minHit: number;
  maxHit: number;
  missChance: number;
  critChance: number;
  critMin: number;
  critMax: number;
  maxRounds: number;
  simulations: number;
  seed: number;
  simultaneous: boolean;
};

type HitType = "miss" | "hit" | "crit";

type Swing = {
  attacker: "A" | "B";
  damage: number;
  type: HitType;
  aHpAfter: number;
  bHpAfter: number;
};

type DuelResult = {
  winner: "A" | "B" | "draw";
  rounds: number;
  swings: Array<Swing>;
};

const DEFAULT_CONFIG: DuelConfig = {
  startingHp: 100,
  minHit: 5,
  maxHit: 15,
  missChance: 0.30,
  critChance: 0.12,
  critMin: 16,
  critMax: 25,
  maxRounds: 100,
  simulations: 10000,
  seed: 1337,
  simultaneous: false,
};

const parseNumber = function (value: string | undefined, fallback: number): number {
  if (value === undefined) {
    return fallback;
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
};

const parseArgs = function (): DuelConfig {
  const args = process.argv.slice(2);
  const argMap = new Map<string, string>();
  for (let i = 0; i < args.length; i += 2) {
    const key = args[i];
    const value = args[i + 1];
    if (key?.startsWith("--") && value !== undefined) {
      argMap.set(key, value);
    }
  }

  return {
    startingHp: parseNumber(argMap.get("--hp"), DEFAULT_CONFIG.startingHp),
    minHit: parseNumber(argMap.get("--min-hit"), DEFAULT_CONFIG.minHit),
    maxHit: parseNumber(argMap.get("--max-hit"), DEFAULT_CONFIG.maxHit),
    missChance: parseNumber(argMap.get("--miss"), DEFAULT_CONFIG.missChance),
    critChance: parseNumber(argMap.get("--crit"), DEFAULT_CONFIG.critChance),
    critMin: parseNumber(argMap.get("--crit-min"), DEFAULT_CONFIG.critMin),
    critMax: parseNumber(argMap.get("--crit-max"), DEFAULT_CONFIG.critMax),
    maxRounds: parseNumber(argMap.get("--max-rounds"), DEFAULT_CONFIG.maxRounds),
    simulations: parseNumber(argMap.get("--sims"), DEFAULT_CONFIG.simulations),
    seed: parseNumber(argMap.get("--seed"), DEFAULT_CONFIG.seed),
    simultaneous: argMap.get("--simultaneous") === "true",
  };
};

const createRng = function (startingSeed: number): () => number {
  let seed = startingSeed >>> 0;
  return () => {
    seed = (seed + 0x6d2b79f5) >>> 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t ^= t + Math.imul(t ^ (t >>> 7), 61 | t);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
};

const randomInt = function (rng: () => number, min: number, max: number): number {
  return Math.floor(rng() * (max - min + 1)) + min;
};

const rollDamage = function (
  config: DuelConfig,
  rng: () => number,
): { damage: number; type: HitType } {
  const missRoll = rng();
  if (missRoll < config.missChance) {
    return { damage: 0, type: "miss" };
  }

  const critRoll = rng();
  if (critRoll < config.critChance) {
    return {
      damage: randomInt(rng, config.critMin, config.critMax),
      type: "crit",
    };
  }

  return {
    damage: randomInt(rng, config.minHit, config.maxHit),
    type: "hit",
  };
};

const runDuel = function (config: DuelConfig, rng: () => number): DuelResult {
  let aHp = config.startingHp;
  let bHp = config.startingHp;
  const swings: Array<Swing> = [];

  const aStarts = rng() < 0.5;
  for (let round = 1; round <= config.maxRounds; round += 1) {
    const turnOrder: Array<"A" | "B"> = aStarts ? ["A", "B"] : ["B", "A"];
    if (!config.simultaneous) {
      for (const attacker of turnOrder) {
        const { damage, type } = rollDamage(config, rng);
        if (attacker === "A") {
          bHp = Math.max(0, bHp - damage);
        } else {
          aHp = Math.max(0, aHp - damage);
        }

        swings.push({
          attacker,
          damage,
          type,
          aHpAfter: aHp,
          bHpAfter: bHp,
        });

        if (aHp === 0 || bHp === 0) {
          return {
            winner: aHp === 0 ? "B" : "A",
            rounds: round,
            swings,
          };
        }
      }
      continue;
    }

    const aRoll = rollDamage(config, rng);
    const bRoll = rollDamage(config, rng);

    const bHpAfterA = Math.max(0, bHp - aRoll.damage);
    const aHpAfterB = Math.max(0, aHp - bRoll.damage);

    for (const attacker of turnOrder) {
      if (attacker === "A") {
        swings.push({
          attacker: "A",
          damage: aRoll.damage,
          type: aRoll.type,
          aHpAfter: aHpAfterB,
          bHpAfter: bHpAfterA,
        });
      } else {
        swings.push({
          attacker: "B",
          damage: bRoll.damage,
          type: bRoll.type,
          aHpAfter: aHpAfterB,
          bHpAfter: bHpAfterA,
        });
      }
    }

    aHp = aHpAfterB;
    bHp = bHpAfterA;
    if (aHp === 0 && bHp === 0) {
      return { winner: "draw", rounds: round, swings };
    }
    if (aHp === 0 || bHp === 0) {
      return {
        winner: aHp === 0 ? "B" : "A",
        rounds: round,
        swings,
      };
    }
  }

  // Safety fallback: if no KO in maxRounds, lower HP loses; exact tie random.
  let winner: "A" | "B" | "draw";
  if (aHp > bHp) {
    winner = "A";
  } else if (bHp > aHp) {
    winner = "B";
  } else {
    winner = config.simultaneous ? "draw" : rng() < 0.5 ? "A" : "B";
  }

  return { winner, rounds: config.maxRounds, swings };
};

const percent = function (value: number): string {
  return `${(value * 100).toFixed(2)}%`;
};

const main = function (): void {
  const config = parseArgs();
  const rng = createRng(config.seed);

  let aWins = 0;
  let bWins = 0;
  let draws = 0;
  let totalRounds = 0;
  let totalSwings = 0;
  let totalMisses = 0;
  let totalCrits = 0;

  let sample: DuelResult | null = null;
  for (let i = 0; i < config.simulations; i += 1) {
    const result = runDuel(config, rng);
    if (sample === null) {
      sample = result;
    }

    if (result.winner === "A") {
      aWins += 1;
    } else if (result.winner === "B") {
      bWins += 1;
    } else {
      draws += 1;
    }
    totalRounds += result.rounds;
    totalSwings += result.swings.length;
    totalMisses += result.swings.filter((swing) => swing.type === "miss").length;
    totalCrits += result.swings.filter((swing) => swing.type === "crit").length;
  }

  console.log("=== Duel Simulator (Local, Off-chain) ===");
  console.log("Config:", {
    hp: config.startingHp,
    hitRange: `${config.minHit}-${config.maxHit}`,
    missChance: config.missChance,
    critChance: config.critChance,
    critRange: `${config.critMin}-${config.critMax}`,
    maxRounds: config.maxRounds,
    simulations: config.simulations,
    seed: config.seed,
    simultaneous: config.simultaneous,
  });
  console.log("Results:", {
    aWinRate: percent(aWins / config.simulations),
    bWinRate: percent(bWins / config.simulations),
    drawRate: percent(draws / config.simulations),
    drawCount: draws,
    avgRounds: (totalRounds / config.simulations).toFixed(2),
    avgSwings: (totalSwings / config.simulations).toFixed(2),
    missRateObserved: percent(totalMisses / totalSwings),
    critRateObserved: percent(totalCrits / totalSwings),
  });

  if (sample !== null) {
    console.log("--- Sample Duel ---");
    console.log(`winner=${sample.winner} rounds=${sample.rounds} swings=${sample.swings.length}`);
    for (let i = 0; i < sample.swings.length; i += 1) {
      const swing = sample.swings[i];
      console.log(
        `${String(i + 1).padStart(2, "0")} ${swing.attacker} -> ${
          swing.damage
        } (${swing.type}) | A=${swing.aHpAfter} B=${swing.bHpAfter}`,
      );
    }
  }
};

main();
