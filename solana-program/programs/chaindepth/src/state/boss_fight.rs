use anchor_lang::prelude::*;

#[account]
#[derive(InitSpace)]
pub struct BossFightAccount {
    pub player: Pubkey,
    pub room: Pubkey,
    pub dps: u64,
    pub joined_slot: u64,
    pub last_damage_slot: u64,
    pub is_active: bool,
    pub bump: u8,
}

impl BossFightAccount {
    pub const SEED_PREFIX: &'static [u8] = b"boss_fight";
}
