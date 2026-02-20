import anchor from "@coral-xyz/anchor";

const DEFAULT_DEVNET_QUEUE = "Cuj97ggrhhidhbu39TijNVqE74xvKJ69gDervRUXAxGh";
const POLL_INTERVAL_MS = 2_500;
const MAX_POLL_ATTEMPTS = 40;

const ensureError = function (thrownObject: unknown): Error {
  if (thrownObject instanceof Error) {
    return thrownObject;
  }
  return new Error(`Non-Error thrown: ${String(thrownObject)}`);
};

const main = async function (): Promise<void> {
  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);

  const program = anchor.workspace.VrfPoc as anchor.Program;
  if (!program) {
    throw new Error("Anchor workspace did not expose VrfPoc. Did you build first?");
  }

  const payerPublicKey = provider.wallet.publicKey;
  const [rollStatePublicKey] = anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("roll_state"), payerPublicKey.toBuffer()],
    program.programId,
  );

  const queueAddress = process.env.VRF_QUEUE ?? DEFAULT_DEVNET_QUEUE;
  const oracleQueuePublicKey = new anchor.web3.PublicKey(queueAddress);

  console.log(`Program: ${program.programId.toBase58()}`);
  console.log(`Payer: ${payerPublicKey.toBase58()}`);
  console.log(`Roll state PDA: ${rollStatePublicKey.toBase58()}`);
  console.log(`Oracle queue: ${oracleQueuePublicKey.toBase58()}`);

  try {
    const initializeSignature = await program.methods
      .initialize()
      .accountsPartial({
        payer: payerPublicKey,
        rollState: rollStatePublicKey,
        systemProgram: anchor.web3.SystemProgram.programId,
      })
      .rpc({ skipPreflight: true });
    console.log(`Initialize signature: ${initializeSignature}`);
  } catch (thrownObject) {
    const error = ensureError(thrownObject);
    const message = error.message.toLowerCase();
    const alreadyInitialized =
      message.includes("already in use") ||
      message.includes("custom program error: 0x0");
    if (!alreadyInitialized) {
      throw error;
    }
    console.log("Roll state already initialized; continuing.");
  }

  const stateBefore = await program.account.rollState.fetch(rollStatePublicKey);
  const previousRollCount = Number(stateBefore.rollCount);
  console.log(
    `Before request: pending=${stateBefore.pending} rollCount=${previousRollCount} lastRandom=${stateBefore.lastRandomValue}`,
  );

  if (stateBefore.pending) {
    throw new Error(
      "Roll state is already pending. Wait for callback or reset the PDA before retrying.",
    );
  }

  const clientSeed = Math.floor(Math.random() * 255);
  const requestSignature = await program.methods
    .requestRandomRoll(clientSeed)
    .accountsPartial({
      payer: payerPublicKey,
      rollState: rollStatePublicKey,
      oracleQueue: oracleQueuePublicKey,
    })
    .rpc({ skipPreflight: true });
  console.log(`Request signature: ${requestSignature}`);

  let didResolve = false;
  for (let attempt = 1; attempt <= MAX_POLL_ATTEMPTS; attempt += 1) {
    await new Promise((resolve) => setTimeout(resolve, POLL_INTERVAL_MS));
    const rollState = await program.account.rollState.fetch(rollStatePublicKey);
    const rollCount = Number(rollState.rollCount);
    console.log(
      `Poll ${attempt}/${MAX_POLL_ATTEMPTS}: pending=${rollState.pending} rollCount=${rollCount} lastRandom=${rollState.lastRandomValue}`,
    );
    if (!rollState.pending && rollCount > previousRollCount) {
      didResolve = true;
      break;
    }
  }

  if (!didResolve) {
    throw new Error(
      "VRF callback did not resolve before timeout. Check queue, funding, and deployment.",
    );
  }

  const stateAfter = await program.account.rollState.fetch(rollStatePublicKey);
  console.log(
    `Success: random=${stateAfter.lastRandomValue} rollCount=${stateAfter.rollCount.toString()} slot=${stateAfter.lastUpdatedSlot.toString()}`,
  );
};

main().catch((thrownObject) => {
  const error = ensureError(thrownObject);
  console.error(error.message);
  process.exit(1);
});
