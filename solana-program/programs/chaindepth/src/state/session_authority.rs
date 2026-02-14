use anchor_lang::prelude::*;

#[account]
#[derive(InitSpace)]
pub struct SessionAuthority {
    pub player: Pubkey,
    pub session_key: Pubkey,
    pub expires_at_slot: u64,
    pub expires_at_unix_timestamp: i64,
    pub instruction_allowlist: u64,
    pub max_token_spend: u64,
    pub spent_token_amount: u64,
    pub is_active: bool,
    pub bump: u8,
}

impl SessionAuthority {
    pub const SEED_PREFIX: &'static [u8] = b"session";
}

pub mod session_instruction_bits {
    pub const BOOST_JOB: u64 = 1 << 0;
    pub const ABANDON_JOB: u64 = 1 << 1;
    pub const CLAIM_JOB_REWARD: u64 = 1 << 2;
    pub const EQUIP_ITEM: u64 = 1 << 3;
    pub const SET_PLAYER_SKIN: u64 = 1 << 4;
    pub const REMOVE_INVENTORY_ITEM: u64 = 1 << 5;
    pub const MOVE_PLAYER: u64 = 1 << 6;
    pub const JOIN_JOB: u64 = 1 << 7;
    pub const COMPLETE_JOB: u64 = 1 << 8;
    pub const CREATE_PLAYER_PROFILE: u64 = 1 << 9;
    pub const JOIN_BOSS_FIGHT: u64 = 1 << 10;
    pub const LOOT_CHEST: u64 = 1 << 11;
    pub const LOOT_BOSS: u64 = 1 << 12;
    pub const UNLOCK_DOOR: u64 = 1 << 13;
}
