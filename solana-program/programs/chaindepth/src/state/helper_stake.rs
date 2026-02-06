use anchor_lang::prelude::*;

/// Tracks one helper's participation and stake for a room direction.
/// PDA seeds: ["stake", room_pubkey, direction (1 byte), player_pubkey]
#[account]
#[derive(InitSpace)]
pub struct HelperStake {
    pub player: Pubkey,
    pub room: Pubkey,
    pub direction: u8,
    pub amount: u64,
    pub joined_slot: u64,
    pub bump: u8,
}

impl HelperStake {
    pub const SEED_PREFIX: &'static [u8] = b"stake";
}
