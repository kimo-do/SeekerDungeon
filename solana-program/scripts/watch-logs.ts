/**
 * Watch ChainDepth program logs in real-time
 * Subscribes to program logs via WebSocket and writes to logs/program.log
 *
 * Usage: npm run watch-logs
 */

import { connect } from "solana-kite";
import { address, type Address } from "@solana/kit";
import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
import { PROGRAM_ID, DEVNET_RPC_URL, DEVNET_WS_URL } from "./constants";

const logsDir = path.join(__dirname, "..", "logs");
if (!fs.existsSync(logsDir)) {
  fs.mkdirSync(logsDir, { recursive: true });
}

const logFile = path.join(logsDir, "program.log");

function log(message: string): void {
  const timestamp = new Date().toISOString();
  const line = `[${timestamp}] ${message}\n`;

  process.stdout.write(line);
  fs.appendFileSync(logFile, line);
}

async function main(): Promise<void> {
  log("=".repeat(60));
  log("ChainDepth Log Watcher");
  log(`Program: ${PROGRAM_ID}`);
  log(`Network: Devnet`);
  log(`Log file: ${logFile}`);
  log("=".repeat(60));
  log("");
  log("Listening for transactions... (Ctrl+C to stop)");
  log("");

  const connection = connect(DEVNET_RPC_URL, DEVNET_WS_URL);
  const programAddress = address(PROGRAM_ID);
  const abortController = new AbortController();

  // Subscribe to program logs using Kite's RPC subscriptions
  const rpcSubscriptions = connection.rpcSubscriptions;

  const logsNotifications = await rpcSubscriptions
    .logsNotifications({ mentions: [programAddress] }, { commitment: "confirmed" })
    .subscribe({ abortSignal: abortController.signal });

  log("Subscription active");

  // Handle graceful shutdown
  process.on("SIGINT", async () => {
    log("");
    log("Stopping log watcher...");
    abortController.abort();
    process.exit(0);
  });

  // Process incoming logs
  try {
    for await (const notification of logsNotifications) {
      const { value, context } = notification;
      const { signature, logs, err } = value;

      log("-".repeat(60));
      log(`TX: ${signature}`);
      log(`Slot: ${context.slot}`);

      if (err) {
        log(`ERROR: ${JSON.stringify(err)}`);
      }

      // Parse instruction type from logs
      const instructionLog = logs.find((l) => l.includes("Instruction:"));
      if (instructionLog) {
        const instruction = instructionLog.split("Instruction:")[1]?.trim();
        log(`Instruction: ${instruction}`);
      }

      // Parse any program logs (custom messages)
      const programLogs = logs.filter(
        (l) => l.includes("Program log:") && !l.includes("Instruction:")
      );

      if (programLogs.length > 0) {
        log("Logs:");
        for (const l of programLogs) {
          const msg = l.replace("Program log:", "").trim();
          log(`  ${msg}`);
        }
      }

      // Check for events (Anchor events start with specific patterns)
      const eventLogs = logs.filter(
        (l) => l.includes("Program data:") || l.includes("Event:")
      );

      if (eventLogs.length > 0) {
        log("Events:");
        for (const l of eventLogs) {
          log(`  ${l}`);
        }
      }

      log("");
    }
  } catch (error) {
    if (error instanceof Error && error.name === "AbortError") {
      // Normal shutdown
    } else {
      throw error;
    }
  }
}

main().catch((error: unknown) => {
  const message = error instanceof Error ? error.message : String(error);
  log(`Fatal error: ${message}`);
  process.exit(1);
});
