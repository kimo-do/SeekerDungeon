use anchor_lang::prelude::*;

/// Maximum helpers per direction (reduced for stack safety)
pub const MAX_HELPERS_PER_DIRECTION: usize = 4;

/// Maximum players who can loot a chest (reduced for stack safety)
pub const MAX_LOOTERS: usize = 8;

/// Direction constants
pub const DIRECTION_NORTH: u8 = 0;
pub const DIRECTION_SOUTH: u8 = 1;
pub const DIRECTION_EAST: u8 = 2;
pub const DIRECTION_WEST: u8 = 3;

/// Wall state constants
pub const WALL_SOLID: u8 = 0;
pub const WALL_RUBBLE: u8 = 1;
pub const WALL_OPEN: u8 = 2;

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

    /// Helpers working on each wall direction
    #[max_len(4, 4)]
    pub helpers: [[Pubkey; 4]; 4],

    /// Count of active helpers per direction
    pub helper_counts: [u8; 4],

    /// Progress towards completion for each direction (in slots)
    pub progress: [u64; 4],

    /// Slot when job started for each direction
    pub start_slot: [u64; 4],

    /// Base slots required to complete for each direction
    pub base_slots: [u64; 4],

    /// Amount staked in escrow for each direction
    pub staked_amount: [u64; 4],

    /// Whether this room has a chest
    pub has_chest: bool,

    /// Players who have already looted this chest
    #[max_len(MAX_LOOTERS)]
    pub looted_by: Vec<Pubkey>,

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

    /// Add a helper to a job
    pub fn add_helper(&mut self, direction: u8, helper: Pubkey) -> Result<()> {
        let dir = direction as usize;
        let count = self.helper_counts[dir] as usize;

        require!(
            count < MAX_HELPERS_PER_DIRECTION,
            crate::errors::ChainDepthError::JobFull
        );

        // Check if already helping
        for i in 0..count {
            require!(
                self.helpers[dir][i] != helper,
                crate::errors::ChainDepthError::AlreadyJoined
            );
        }

        self.helpers[dir][count] = helper;
        self.helper_counts[dir] += 1;
        Ok(())
    }

    /// Remove a helper from a job
    pub fn remove_helper(&mut self, direction: u8, helper: Pubkey) -> bool {
        let dir = direction as usize;
        let count = self.helper_counts[dir] as usize;

        for i in 0..count {
            if self.helpers[dir][i] == helper {
                // Shift remaining helpers
                for j in i..(count - 1) {
                    self.helpers[dir][j] = self.helpers[dir][j + 1];
                }
                self.helpers[dir][count - 1] = Pubkey::default();
                self.helper_counts[dir] -= 1;
                return true;
            }
        }
        false
    }

    /// Check if helper is in the job
    pub fn is_helper(&self, direction: u8, helper: &Pubkey) -> bool {
        let dir = direction as usize;
        let count = self.helper_counts[dir] as usize;
        for i in 0..count {
            if &self.helpers[dir][i] == helper {
                return true;
            }
        }
        false
    }

    /// Check if player has already looted this chest
    pub fn has_looted(&self, player: &Pubkey) -> bool {
        self.looted_by.contains(player)
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
