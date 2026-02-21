import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import { Chaindepth } from "../target/types/chaindepth";
import {
  getAssociatedTokenAddressSync,
  getOrCreateAssociatedTokenAccount,
  TOKEN_PROGRAM_ID,
  transfer,
} from "@solana/spl-token";
import { Keypair, SystemProgram, Transaction } from "@solana/web3.js";
import BN from "bn.js";

const DEFAULT_QUEUE = new anchor.web3.PublicKey(
  "Cuj97ggrhhidhbu39TijNVqE74xvKJ69gDervRUXAxGh",
);
const DEFAULT_PUBKEY = new anchor.web3.PublicKey("11111111111111111111111111111111");
const START_ROOM_X = 5;
const START_ROOM_Y = 5;
const PLAYER_SOL_FUNDING_LAMPORTS = 250_000_000;
const DUEL_STAKE_AMOUNT = 5_000_000;
const PLAYER_SKR_FUNDING = 50_000_000;
const MAX_SETTLEMENT_POLLS = 40;
const POLL_DELAY_MS = 2_500;

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

const deriveRoomPresencePda = function (
  programId: anchor.web3.PublicKey,
  seasonSeed: BN,
  player: anchor.web3.PublicKey,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [
      Buffer.from("presence"),
      seasonSeed.toArrayLike(Buffer, "le", 8),
      Buffer.from([START_ROOM_X]),
      Buffer.from([START_ROOM_Y]),
      player.toBuffer(),
    ],
    programId,
  )[0];
};

const deriveDuelChallengePda = function (
  programId: anchor.web3.PublicKey,
  challenger: anchor.web3.PublicKey,
  opponent: anchor.web3.PublicKey,
  challengeSeed: BN,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [
      Buffer.from("duel_challenge"),
      challenger.toBuffer(),
      opponent.toBuffer(),
      challengeSeed.toArrayLike(Buffer, "le", 8),
    ],
    programId,
  )[0];
};

const deriveDuelEscrowPda = function (
  programId: anchor.web3.PublicKey,
  duelChallenge: anchor.web3.PublicKey,
): anchor.web3.PublicKey {
  return anchor.web3.PublicKey.findProgramAddressSync(
    [Buffer.from("duel_escrow"), duelChallenge.toBuffer()],
    programId,
  )[0];
};

const sleep = async function (milliseconds: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, milliseconds));
};

const getRawTokenAmount = async function (
  connection: anchor.web3.Connection,
  tokenAccount: anchor.web3.PublicKey,
): Promise<bigint> {
  const balanceResult = await connection.getTokenAccountBalance(tokenAccount, "confirmed");
  return BigInt(balanceResult.value.amount);
};

async function main(): Promise<void> {
  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);

  const program = anchor.workspace.Chaindepth as Program<Chaindepth>;
  const connection = provider.connection;
  const admin = (provider.wallet as anchor.Wallet).payer;

  const globalPda = deriveGlobalPda(program.programId);
  const global = await program.account.globalAccount.fetch(globalPda);
  const adminTokenAccount = getAssociatedTokenAddressSync(global.skrMint, admin.publicKey);

  const challenger = Keypair.generate();
  const opponent = Keypair.generate();
  const challengeSeed = new BN(Date.now());

  console.log("=== VRF Duel Devnet Smoke Test ===");
  console.log("Program:", program.programId.toBase58());
  console.log("Global:", globalPda.toBase58());
  console.log("SKR mint:", global.skrMint.toBase58());
  console.log("Challenger:", challenger.publicKey.toBase58());
  console.log("Opponent:", opponent.publicKey.toBase58());
  console.log("Challenge seed:", challengeSeed.toString());

  const fundingTransaction = new Transaction();
  fundingTransaction.add(
    SystemProgram.transfer({
      fromPubkey: admin.publicKey,
      toPubkey: challenger.publicKey,
      lamports: PLAYER_SOL_FUNDING_LAMPORTS,
    }),
  );
  fundingTransaction.add(
    SystemProgram.transfer({
      fromPubkey: admin.publicKey,
      toPubkey: opponent.publicKey,
      lamports: PLAYER_SOL_FUNDING_LAMPORTS,
    }),
  );
  await provider.sendAndConfirm(fundingTransaction);

  const challengerTokenAccount = (
    await getOrCreateAssociatedTokenAccount(
      connection,
      admin,
      global.skrMint,
      challenger.publicKey,
    )
  ).address;
  const opponentTokenAccount = (
    await getOrCreateAssociatedTokenAccount(
      connection,
      admin,
      global.skrMint,
      opponent.publicKey,
    )
  ).address;
  await transfer(
    connection,
    admin,
    adminTokenAccount,
    challengerTokenAccount,
    admin.publicKey,
    PLAYER_SKR_FUNDING,
  );
  await transfer(
    connection,
    admin,
    adminTokenAccount,
    opponentTokenAccount,
    admin.publicKey,
    PLAYER_SKR_FUNDING,
  );

  const challengerPlayerPda = derivePlayerPda(program.programId, challenger.publicKey);
  const opponentPlayerPda = derivePlayerPda(program.programId, opponent.publicKey);
  const challengerProfilePda = deriveProfilePda(program.programId, challenger.publicKey);
  const opponentProfilePda = deriveProfilePda(program.programId, opponent.publicKey);
  const challengerPresencePda = deriveRoomPresencePda(
    program.programId,
    global.seasonSeed,
    challenger.publicKey,
  );
  const opponentPresencePda = deriveRoomPresencePda(
    program.programId,
    global.seasonSeed,
    opponent.publicKey,
  );

  await program.methods
    .initPlayer()
    .accountsPartial({
      player: challenger.publicKey,
      global: globalPda,
      playerAccount: challengerPlayerPda,
      profile: challengerProfilePda,
      roomPresence: challengerPresencePda,
      systemProgram: SystemProgram.programId,
    })
    .signers([challenger])
    .rpc({ skipPreflight: true });

  await program.methods
    .initPlayer()
    .accountsPartial({
      player: opponent.publicKey,
      global: globalPda,
      playerAccount: opponentPlayerPda,
      profile: opponentProfilePda,
      roomPresence: opponentPresencePda,
      systemProgram: SystemProgram.programId,
    })
    .signers([opponent])
    .rpc({ skipPreflight: true });

  const currentSlot = await connection.getSlot("confirmed");
  const expiresAtSlot = new BN(currentSlot + 1_500);
  const duelChallengePda = deriveDuelChallengePda(
    program.programId,
    challenger.publicKey,
    opponent.publicKey,
    challengeSeed,
  );
  const duelEscrowPda = deriveDuelEscrowPda(program.programId, duelChallengePda);

  const challengerBefore = await getRawTokenAmount(connection, challengerTokenAccount);
  const opponentBefore = await getRawTokenAmount(connection, opponentTokenAccount);

  await program.methods
    .createDuelChallenge(challengeSeed, new BN(DUEL_STAKE_AMOUNT), expiresAtSlot)
    .accountsPartial({
      challenger: challenger.publicKey,
      opponent: opponent.publicKey,
      global: globalPda,
      challengerPlayerAccount: challengerPlayerPda,
      opponentPlayerAccount: opponentPlayerPda,
      challengerProfile: challengerProfilePda,
      opponentProfile: opponentProfilePda,
      duelChallenge: duelChallengePda,
      duelEscrow: duelEscrowPda,
      challengerTokenAccount,
      skrMint: global.skrMint,
      tokenProgram: TOKEN_PROGRAM_ID,
      systemProgram: SystemProgram.programId,
    })
    .signers([challenger])
    .rpc({ skipPreflight: true });
  console.log("create_duel_challenge: ok");

  await program.methods
    .acceptDuelChallenge(challengeSeed)
    .accountsPartial({
      opponent: opponent.publicKey,
      challenger: challenger.publicKey,
      global: globalPda,
      duelChallenge: duelChallengePda,
      duelEscrow: duelEscrowPda,
      challengerPlayerAccount: challengerPlayerPda,
      opponentPlayerAccount: opponentPlayerPda,
      challengerTokenAccount,
      opponentTokenAccount,
      oracleQueue: DEFAULT_QUEUE,
      tokenProgram: TOKEN_PROGRAM_ID,
    })
    .signers([opponent])
    .rpc({ skipPreflight: true });
  console.log("accept_duel_challenge: ok");

  let duelSettled = false;
  let settledChallenge: Awaited<ReturnType<typeof program.account.duelChallenge.fetch>> | null =
    null;
  for (let pollIndex = 1; pollIndex <= MAX_SETTLEMENT_POLLS; pollIndex += 1) {
    await sleep(POLL_DELAY_MS);
    const duelChallenge = await program.account.duelChallenge.fetch(duelChallengePda);
    console.log(
      `poll ${pollIndex}/${MAX_SETTLEMENT_POLLS}: status=${duelChallenge.status} turns=${duelChallenge.turnsPlayed}`,
    );
    if (duelChallenge.status === 2) {
      duelSettled = true;
      settledChallenge = duelChallenge;
      break;
    }
  }
  if (!duelSettled || settledChallenge === null) {
    throw new Error("VRF duel settlement did not complete before timeout.");
  }

  const challengerAfter = await getRawTokenAmount(connection, challengerTokenAccount);
  const opponentAfter = await getRawTokenAmount(connection, opponentTokenAccount);
  const winner = settledChallenge.winner.toBase58();
  const isDraw = settledChallenge.isDraw;
  const challengerAddress = challenger.publicKey.toBase58();
  const opponentAddress = opponent.publicKey.toBase58();
  if (!isDraw && winner !== challengerAddress && winner !== opponentAddress) {
    throw new Error(`Unexpected winner key for non-draw duel: ${winner}`);
  }
  if (isDraw && winner !== DEFAULT_PUBKEY.toBase58()) {
    throw new Error(`Draw duel must store default winner pubkey, got: ${winner}`);
  }

  const netChallenger = challengerAfter - challengerBefore;
  const netOpponent = opponentAfter - opponentBefore;
  const expectedWinNet = BigInt(DUEL_STAKE_AMOUNT);
  const expectedLossNet = -BigInt(DUEL_STAKE_AMOUNT);
  const expectedDrawNet = 0n;
  if (isDraw) {
    if (netChallenger !== expectedDrawNet || netOpponent !== expectedDrawNet) {
      throw new Error(
        `Unexpected payout delta for draw. challengerDelta=${netChallenger} opponentDelta=${netOpponent}`,
      );
    }
  } else if (winner === challengerAddress) {
    if (netChallenger !== expectedWinNet || netOpponent !== expectedLossNet) {
      throw new Error(
        `Unexpected payout delta for challenger winner. challengerDelta=${netChallenger} opponentDelta=${netOpponent}`,
      );
    }
  } else if (netChallenger !== expectedLossNet || netOpponent !== expectedWinNet) {
    throw new Error(
      `Unexpected payout delta for opponent winner. challengerDelta=${netChallenger} opponentDelta=${netOpponent}`,
    );
  }

  console.log("duel_settled: ok");
  console.log("is_draw:", isDraw);
  console.log("winner:", isDraw ? "draw" : winner);
  console.log("starter:", settledChallenge.starter === 0 ? "challenger" : "opponent");
  console.log(
    "challenger_hits:",
    settledChallenge.challengerHits.join(","),
    "opponent_hits:",
    settledChallenge.opponentHits.join(","),
  );
  console.log(
    `final_hp: challenger=${settledChallenge.challengerFinalHp} opponent=${settledChallenge.opponentFinalHp}`,
  );
  console.log("=== VRF Duel Devnet Smoke Test Passed ===");
}

main().catch((thrownObject: unknown) => {
  const error = ensureError(thrownObject);
  console.error("VRF duel smoke raw error:", thrownObject);
  console.error("VRF duel smoke failed:", error.message);
  process.exit(1);
});
