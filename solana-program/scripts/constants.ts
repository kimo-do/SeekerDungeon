/**
 * Constants for ChainDepth scripts
 * Eliminates magic numbers throughout the codebase
 */

// Token configuration
export const SKR_DECIMALS = 9;
export const SKR_MULTIPLIER = 10 ** SKR_DECIMALS;

// SOL configuration
export const LAMPORTS_PER_SOL = 1_000_000_000n;
export const MIN_SOL_BALANCE_LAMPORTS = 500_000_000n; // 0.5 SOL
export const AIRDROP_AMOUNT_LAMPORTS = 2_000_000_000n; // 2 SOL

// Game configuration
export const START_X = 10;
export const START_Y = 10;
export const INITIAL_PRIZE_POOL_AMOUNT = 100 * SKR_MULTIPLIER; // 100 SKR
export const DEFAULT_MINT_AMOUNT = 1000 * SKR_MULTIPLIER; // 1000 SKR
export const DEFAULT_TEST_MINT_AMOUNT = 10 * SKR_MULTIPLIER; // 10 SKR

// Time constants
export const SECONDS_PER_MINUTE = 60;
export const SECONDS_PER_HOUR = 3600;
export const SECONDS_PER_DAY = 86400;
export const SECONDS_PER_SLOT = 0.4; // ~400ms per Solana slot

// Network configuration
export const DEVNET_RPC_URL = "https://api.devnet.solana.com";
export const DEVNET_WS_URL = "wss://api.devnet.solana.com";

// PDA seeds
export const GLOBAL_SEED = "global";
export const PRIZE_POOL_SEED = "prize_pool";
export const ROOM_SEED = "room";

// Program ID (from Anchor.toml / deployed program)
export const PROGRAM_ID = "3Ctc2FgnNHQtGAcZftMS4ykLhJYjLzBD3hELKy55DnKo";
