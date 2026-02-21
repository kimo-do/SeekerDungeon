use anchor_lang::prelude::*;

pub const MAX_DUEL_HITS_PER_PLAYER: usize = 100;
pub const MAX_DUEL_NAME_LEN: usize = 24;

#[account]
#[derive(InitSpace)]
pub struct DuelChallenge {
    pub challenger: Pubkey,
    pub opponent: Pubkey,
    #[max_len(MAX_DUEL_NAME_LEN)]
    pub challenger_display_name_snapshot: String,
    #[max_len(MAX_DUEL_NAME_LEN)]
    pub opponent_display_name_snapshot: String,
    pub season_seed: u64,
    pub room_x: i8,
    pub room_y: i8,
    pub stake_amount: u64,
    pub challenge_seed: u64,
    pub expires_at_slot: u64,
    pub requested_slot: u64,
    pub settled_slot: u64,
    pub duel_escrow: Pubkey,
    pub winner: Pubkey,
    pub is_draw: bool,
    pub challenger_final_hp: u16,
    pub opponent_final_hp: u16,
    pub turns_played: u8,
    pub status: u8,
    pub starter: u8,
    #[max_len(MAX_DUEL_HITS_PER_PLAYER)]
    pub challenger_hits: Vec<u8>,
    #[max_len(MAX_DUEL_HITS_PER_PLAYER)]
    pub opponent_hits: Vec<u8>,
    pub bump: u8,
}

impl DuelChallenge {
    pub const SEED_PREFIX: &'static [u8] = b"duel_challenge";
    pub const DUEL_ESCROW_SEED_PREFIX: &'static [u8] = b"duel_escrow";
    pub const STATUS_OPEN: u8 = 0;
    pub const STATUS_PENDING_RANDOMNESS: u8 = 1;
    pub const STATUS_SETTLED: u8 = 2;
    pub const STATUS_DECLINED: u8 = 3;
    pub const STATUS_EXPIRED: u8 = 4;
    pub const STARTER_CHALLENGER: u8 = 0;
    pub const STARTER_OPPONENT: u8 = 1;
    pub const STARTER_UNSET: u8 = u8::MAX;
    pub const MAX_EXPIRY_SLOTS: u64 = 21_600;
    pub const STARTING_HP: u16 = 100;
    pub const MISS_CHANCE_PERCENT: u8 = 30;
    pub const MIN_HIT_DAMAGE: u8 = 1;
    pub const MAX_HIT_DAMAGE: u8 = 15;
    pub const CRIT_CHANCE_PERCENT: u8 = 10;
    pub const CRIT_MIN_HIT_DAMAGE: u8 = 16;
    pub const CRIT_MAX_HIT_DAMAGE: u8 = 25;
}
