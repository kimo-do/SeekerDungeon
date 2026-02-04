use anchor_lang::prelude::*;

/// Emitted when a job is completed and a wall opens
#[event]
pub struct JobCompleted {
    pub room_x: i8,
    pub room_y: i8,
    pub direction: u8,
    pub new_depth: u32,
    pub helpers_count: u8,
    pub total_reward: u64,
}

/// Emitted when a player loots a chest
#[event]
pub struct ChestLooted {
    pub room_x: i8,
    pub room_y: i8,
    pub player: Pubkey,
    pub item_type: u8,
    pub item_amount: u8,
}

/// Emitted when a season resets
#[event]
pub struct SeasonReset {
    pub old_seed: u64,
    pub new_seed: u64,
    pub old_depth: u32,
    pub end_slot: u64,
}

/// Emitted when a player joins a job
#[event]
pub struct JobJoined {
    pub room_x: i8,
    pub room_y: i8,
    pub direction: u8,
    pub player: Pubkey,
    pub helper_count: u8,
    pub stake_amount: u64,
}

/// Emitted when a job is boosted
#[event]
pub struct JobBoosted {
    pub room_x: i8,
    pub room_y: i8,
    pub direction: u8,
    pub player: Pubkey,
    pub tip_amount: u64,
    pub new_progress: u64,
}

/// Emitted when a player abandons a job
#[event]
pub struct JobAbandoned {
    pub room_x: i8,
    pub room_y: i8,
    pub direction: u8,
    pub player: Pubkey,
    pub refund_amount: u64,
}

/// Emitted when a player moves to a new room
#[event]
pub struct PlayerMoved {
    pub player: Pubkey,
    pub from_x: i8,
    pub from_y: i8,
    pub to_x: i8,
    pub to_y: i8,
}

/// Emitted when global state is initialized
#[event]
pub struct GlobalInitialized {
    pub season_seed: u64,
    pub admin: Pubkey,
    pub skr_mint: Pubkey,
    pub end_slot: u64,
}

/// Item types for loot
pub mod item_types {
    pub const ORE: u8 = 0;
    pub const TOOL: u8 = 1;
    pub const BUFF: u8 = 2;
}
