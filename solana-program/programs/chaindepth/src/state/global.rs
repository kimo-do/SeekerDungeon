use anchor_lang::prelude::*;

/// Global game state - one per season
/// PDA seeds: ["global"]
#[account]
#[derive(InitSpace)]
pub struct GlobalAccount {
    /// Random seed for procedural dungeon generation
    /// Unity uses this to deterministically generate room layouts
    pub season_seed: u64,

    /// Current maximum depth reached by any player this season
    pub depth: u32,

    /// SPL token mint for SKR (or mock token on devnet)
    pub skr_mint: Pubkey,

    /// Prize pool token account (ATA owned by this PDA)
    pub prize_pool: Pubkey,

    /// Admin pubkey authorized to reset seasons
    pub admin: Pubkey,

    /// Slot when this season ends (for weekly resets)
    pub end_slot: u64,

    /// Total jobs completed this season (for stats)
    pub jobs_completed: u64,

    /// PDA bump seed
    pub bump: u8,
}

impl GlobalAccount {
    pub const SEED_PREFIX: &'static [u8] = b"global";

    /// Calculate slots for one week (~2.5 days worth at 400ms/slot)
    /// 1 week = 7 * 24 * 60 * 60 = 604800 seconds
    /// At 400ms per slot: 604800 / 0.4 = 1,512,000 slots
    pub const SEASON_DURATION_SLOTS: u64 = 1_512_000;

    /// Starting room coordinates (center of 10x10 grid)
    pub const START_X: i8 = 5;
    pub const START_Y: i8 = 5;

    /// Grid boundaries
    pub const MIN_COORD: i8 = 0;
    pub const MAX_COORD: i8 = 9;
}
