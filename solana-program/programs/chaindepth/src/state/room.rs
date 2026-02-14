use anchor_lang::prelude::*;

use super::global::GlobalAccount;

pub const MAX_BOSS_HP: u64 = 100_000;

/// Direction constants
pub const DIRECTION_NORTH: u8 = 0;
pub const DIRECTION_SOUTH: u8 = 1;
pub const DIRECTION_EAST: u8 = 2;
pub const DIRECTION_WEST: u8 = 3;

/// Wall state constants
pub const WALL_SOLID: u8 = 0;
pub const WALL_RUBBLE: u8 = 1;
pub const WALL_OPEN: u8 = 2;

/// Center state constants
pub const CENTER_EMPTY: u8 = 0;
pub const CENTER_CHEST: u8 = 1;
pub const CENTER_BOSS: u8 = 2;

/// Room account - one per coordinate pair per season
/// PDA seeds: ["room", season_seed (8 bytes), x (1 byte), y (1 byte)]
#[account]
#[derive(InitSpace)]
pub struct RoomAccount {
    /// Room coordinates
    pub x: i8,
    pub y: i8,

    /// Season seed this room belongs to
    pub season_seed: u64,

    /// Wall states: [North, South, East, West]
    /// 0 = solid wall (impassable), 1 = rubble (can clear), 2 = open (passable)
    pub walls: [u8; 4],

    /// Count of active helpers per direction
    pub helper_counts: [u32; 4],

    /// Progress towards completion for each direction (in slots)
    pub progress: [u64; 4],

    /// Slot when job started for each direction
    pub start_slot: [u64; 4],

    /// Base slots required to complete for each direction
    pub base_slots: [u64; 4],

    /// Amount staked in escrow for each direction
    pub total_staked: [u64; 4],

    /// Whether each directional job has been completed and is in claim phase
    pub job_completed: [bool; 4],

    /// Bonus allocated per helper after completion
    pub bonus_per_helper: [u64; 4],

    /// Whether this room has a chest
    pub has_chest: bool,

    /// What is in the room center (empty/chest/boss)
    pub center_type: u8,

    /// Identifier used by Unity to pick boss prefab/variant
    pub center_id: u16,

    /// Boss max HP (0 if no boss in center)
    pub boss_max_hp: u64,

    /// Boss remaining HP (0 if no boss or defeated)
    pub boss_current_hp: u64,

    /// Slot of latest boss HP update
    pub boss_last_update_slot: u64,

    /// Total DPS from current fighters
    pub boss_total_dps: u64,

    /// Number of current fighters
    pub boss_fighter_count: u32,

    /// Whether boss has been defeated
    pub boss_defeated: bool,

    /// Number of players who have looted this chest (loot tracking moved to LootReceipt PDAs)
    pub looted_count: u32,

    /// Wallet that first discovered/created this room
    pub created_by: Pubkey,

    /// Slot when this room was created
    pub created_slot: u64,

    /// PDA bump seed
    pub bump: u8,
}

impl RoomAccount {
    pub const SEED_PREFIX: &'static [u8] = b"room";

    /// Stake amount per player joining a job (0.01 SKR with 9 decimals)
    pub const STAKE_AMOUNT: u64 = 10_000_000; // 0.01 * 10^9

    /// Minimum tip for boosting (0.001 SKR with 9 decimals)
    pub const MIN_BOOST_TIP: u64 = 1_000_000; // 0.001 * 10^9

    /// Base slots for depth 0 (~120 seconds at 400ms/slot)
    pub const BASE_SLOTS_DEPTH_0: u64 = 300;

    /// Boost progress per tip (in slots)
    pub const BOOST_PROGRESS: u64 = 30; // ~12 seconds worth

    /// Refund percentage when abandoning (80%)
    pub const ABANDON_REFUND_PERCENT: u64 = 80;
    pub const BOSS_BASE_HP: u64 = 300;

    /// Get opposite direction
    pub fn opposite_direction(direction: u8) -> u8 {
        match direction {
            DIRECTION_NORTH => DIRECTION_SOUTH,
            DIRECTION_SOUTH => DIRECTION_NORTH,
            DIRECTION_EAST => DIRECTION_WEST,
            DIRECTION_WEST => DIRECTION_EAST,
            _ => direction,
        }
    }

    /// Get adjacent room coordinates for a direction
    pub fn adjacent_coords(x: i8, y: i8, direction: u8) -> (i8, i8) {
        match direction {
            DIRECTION_NORTH => (x, y + 1),
            DIRECTION_SOUTH => (x, y - 1),
            DIRECTION_EAST => (x + 1, y),
            DIRECTION_WEST => (x - 1, y),
            _ => (x, y),
        }
    }

    /// Calculate base slots based on global depth
    pub fn calculate_base_slots(depth: u32) -> u64 {
        // Base increases by 10% every 10 depth levels
        Self::BASE_SLOTS_DEPTH_0 * ((depth / 10) as u64 + 1)
    }

    /// Check if a direction is valid (0-3)
    pub fn is_valid_direction(direction: u8) -> bool {
        direction <= DIRECTION_WEST
    }

    /// Check if wall at direction is rubble (clearable)
    pub fn is_rubble(&self, direction: u8) -> bool {
        self.walls[direction as usize] == WALL_RUBBLE
    }

    /// Check if wall at direction is open (passable)
    pub fn is_open(&self, direction: u8) -> bool {
        self.walls[direction as usize] == WALL_OPEN
    }

    pub fn is_valid_center_type(center_type: u8) -> bool {
        center_type == CENTER_EMPTY || center_type == CENTER_CHEST || center_type == CENTER_BOSS
    }

    pub fn boss_hp_for_depth(depth: u32, boss_id: u16) -> u64 {
        let depth_multiplier = 1 + (depth / 4) as u64;
        let id_multiplier = 1 + (boss_id % 5) as u64;
        let hp = Self::BOSS_BASE_HP
            .saturating_mul(depth_multiplier)
            .saturating_mul(id_multiplier);
        hp.min(MAX_BOSS_HP)
    }

    pub fn generate_start_walls(season_seed: u64, x: i8, y: i8) -> [u8; 4] {
        let mut walls = [WALL_RUBBLE; 4];
        for (direction, wall) in walls.iter_mut().enumerate() {
            let direction_hash = season_seed.wrapping_mul(31).wrapping_add(direction as u64);
            if (direction_hash % 2) == 0 {
                *wall = WALL_OPEN;
            } else {
                *wall = WALL_RUBBLE;
            }
        }
        Self::clamp_boundary_walls(&mut walls, x, y);
        walls
    }

    /// Force walls that face outside the grid boundary to Solid so
    /// players never see interactable doors that lead nowhere.
    pub fn clamp_boundary_walls(walls: &mut [u8; 4], x: i8, y: i8) {
        if y >= GlobalAccount::MAX_COORD {
            walls[DIRECTION_NORTH as usize] = WALL_SOLID;
        }
        if y <= GlobalAccount::MIN_COORD {
            walls[DIRECTION_SOUTH as usize] = WALL_SOLID;
        }
        if x >= GlobalAccount::MAX_COORD {
            walls[DIRECTION_EAST as usize] = WALL_SOLID;
        }
        if x <= GlobalAccount::MIN_COORD {
            walls[DIRECTION_WEST as usize] = WALL_SOLID;
        }
    }
}

/// Escrow account for holding staked SKR during jobs
/// PDA seeds: ["escrow", room_pubkey, direction (1 byte)]
#[account]
#[derive(InitSpace)]
pub struct EscrowAccount {
    pub room: Pubkey,
    pub direction: u8,
    pub bump: u8,
}

impl EscrowAccount {
    pub const SEED_PREFIX: &'static [u8] = b"escrow";
}
