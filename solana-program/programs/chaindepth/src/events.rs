use anchor_lang::prelude::*;

/// Emitted when a job is completed and a wall opens
#[event]
pub struct JobCompleted {
    pub room_x: i8,
    pub room_y: i8,
    pub direction: u8,
    pub new_depth: u32,
    pub helpers_count: u32,
    pub reward_per_helper: u64,
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
    pub helper_count: u32,
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

/// Emitted when a helper claims reward from a completed job
#[event]
pub struct JobRewardClaimed {
    pub room_x: i8,
    pub room_y: i8,
    pub direction: u8,
    pub player: Pubkey,
    pub payout_amount: u64,
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

/// Emitted when a player unlocks a locked door with a key item
#[event]
pub struct DoorUnlocked {
    pub room_x: i8,
    pub room_y: i8,
    pub direction: u8,
    pub player: Pubkey,
    pub key_item_id: u16,
}

/// Emitted when a player extracts from dungeon entrance stairs.
#[event]
pub struct DungeonExited {
    pub player: Pubkey,
    pub run_score: u64,
    pub time_score: u64,
    pub loot_score: u64,
    pub extracted_item_stacks: u32,
    pub extracted_item_units: u32,
    pub total_score: u64,
    pub run_duration_slots: u64,
}

/// Emitted for each scored inventory stack during extraction.
#[event]
pub struct DungeonExitItemScored {
    pub player: Pubkey,
    pub item_id: u16,
    pub amount: u32,
    pub unit_score: u64,
    pub stack_score: u64,
}

/// Emitted when global state is initialized
#[event]
pub struct GlobalInitialized {
    pub season_seed: u64,
    pub admin: Pubkey,
    pub skr_mint: Pubkey,
    pub end_slot: u64,
}

/// Emitted when admin resets a specific player's state for testing.
#[event]
pub struct PlayerResetForTesting {
    pub admin: Pubkey,
    pub player: Pubkey,
}

/// Emitted when an item stack is added to inventory
#[event]
pub struct InventoryItemAdded {
    pub player: Pubkey,
    pub item_id: u16,
    pub amount: u32,
    pub durability: u16,
}

/// Emitted when items are removed from inventory
#[event]
pub struct InventoryItemRemoved {
    pub player: Pubkey,
    pub item_id: u16,
    pub amount: u32,
}

#[event]
pub struct ItemEquipped {
    pub player: Pubkey,
    pub item_id: u16,
}

#[event]
pub struct BossFightJoined {
    pub room_x: i8,
    pub room_y: i8,
    pub player: Pubkey,
    pub dps: u64,
    pub fighter_count: u32,
}

#[event]
pub struct BossTicked {
    pub room_x: i8,
    pub room_y: i8,
    pub boss_id: u16,
    pub current_hp: u64,
    pub max_hp: u64,
    pub fighter_count: u32,
}

#[event]
pub struct BossLooted {
    pub room_x: i8,
    pub room_y: i8,
    pub player: Pubkey,
    pub item_type: u8,
    pub item_amount: u8,
}

/// Item types for loot
pub mod item_types {
    pub const ORE: u8 = 0;
    pub const TOOL: u8 = 1;
    pub const BUFF: u8 = 2;
}
