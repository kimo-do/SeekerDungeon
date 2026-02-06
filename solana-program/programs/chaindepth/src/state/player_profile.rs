use anchor_lang::prelude::*;

#[account]
#[derive(InitSpace)]
pub struct PlayerProfile {
    pub owner: Pubkey,
    pub skin_id: u16,
    #[max_len(24)]
    pub display_name: String,
    pub starter_pickaxe_granted: bool,
    pub bump: u8,
}

impl PlayerProfile {
    pub const SEED_PREFIX: &'static [u8] = b"profile";
    pub const DEFAULT_SKIN_ID: u16 = 0;
}
