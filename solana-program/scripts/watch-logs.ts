/**
 * Watch ChainDepth program logs in real-time
 * Subscribes to program logs via WebSocket and writes to logs/program.log
 * 
 * Usage: npx ts-node scripts/watch-logs.ts
 */

import { Connection, PublicKey } from "@solana/web3.js";
import * as fs from "fs";
import * as path from "path";

const PROGRAM_ID = "3Ctc2FgnNHQtGAcZftMS4ykLhJYjLzBD3hELKy55DnKo";
const RPC_URL = "https://api.devnet.solana.com";
const WS_URL = "wss://api.devnet.solana.com";

// Create logs directory
const logsDir = path.join(__dirname, "..", "logs");
if (!fs.existsSync(logsDir)) {
  fs.mkdirSync(logsDir, { recursive: true });
}

const logFile = path.join(logsDir, "program.log");

function log(message: string) {
  const timestamp = new Date().toISOString();
  const line = `[${timestamp}] ${message}\n`;
  
  // Write to console
  process.stdout.write(line);
  
  // Append to file
  fs.appendFileSync(logFile, line);
}

function formatLogs(logs: string[]): string {
  return logs
    .filter(l => !l.includes("consumed") && !l.includes("success"))
    .map(l => "  " + l)
    .join("\n");
}

async function main() {
  log("=".repeat(60));
  log("ChainDepth Log Watcher");
  log(`Program: ${PROGRAM_ID}`);
  log(`Network: Devnet`);
  log(`Log file: ${logFile}`);
  log("=".repeat(60));
  log("");
  log("Listening for transactions... (Ctrl+C to stop)");
  log("");

  const connection = new Connection(RPC_URL, {
    wsEndpoint: WS_URL,
    commitment: "confirmed",
  });

  const programId = new PublicKey(PROGRAM_ID);

  // Subscribe to program logs
  const subscriptionId = connection.onLogs(
    programId,
    (logInfo, context) => {
      const { signature, logs, err } = logInfo;
      
      log("-".repeat(60));
      log(`TX: ${signature}`);
      log(`Slot: ${context.slot}`);
      
      if (err) {
        log(`ERROR: ${JSON.stringify(err)}`);
      }
      
      // Parse instruction type from logs
      const instructionLog = logs.find(l => l.includes("Instruction:"));
      if (instructionLog) {
        const instruction = instructionLog.split("Instruction:")[1]?.trim();
        log(`Instruction: ${instruction}`);
      }
      
      // Parse any program logs (custom messages)
      const programLogs = logs.filter(l => 
        l.includes("Program log:") && 
        !l.includes("Instruction:")
      );
      
      if (programLogs.length > 0) {
        log("Logs:");
        programLogs.forEach(l => {
          const msg = l.replace("Program log:", "").trim();
          log(`  ${msg}`);
        });
      }
      
      // Check for events (Anchor events start with specific patterns)
      const eventLogs = logs.filter(l => 
        l.includes("Program data:") || 
        l.includes("Event:")
      );
      
      if (eventLogs.length > 0) {
        log("Events:");
        eventLogs.forEach(l => log(`  ${l}`));
      }
      
      log("");
    },
    "confirmed"
  );

  log(`Subscription ID: ${subscriptionId}`);

  // Keep the process running
  process.on("SIGINT", async () => {
    log("");
    log("Stopping log watcher...");
    await connection.removeOnLogsListener(subscriptionId);
    process.exit(0);
  });

  // Keep alive
  await new Promise(() => {});
}

main().catch((e) => {
  log(`Fatal error: ${e.message}`);
  process.exit(1);
});
