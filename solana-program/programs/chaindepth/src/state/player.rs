use anchor_lang::prelude::*;

/// Maximum number of active jobs a player can have at once
pub const MAX_ACTIVE_JOBS: usize = 4;

/// Player account - one per wallet
/// PDA seeds: ["player", user_pubkey]
#[account]
#[derive(InitSpace)]
pub struct PlayerAccount {
    /// Player's wallet pubkey
    pub owner: Pubkey,

    /// Current room coordinates (x, y)
    pub current_room_x: i8,
    pub current_room_y: i8,

    /// Active jobs: [(room_x, room_y, direction)]
    /// Direction: 0=North, 1=South, 2=East, 3=West
    #[max_len(MAX_ACTIVE_JOBS)]
    pub active_jobs: Vec<ActiveJob>,

    /// Total jobs completed by this player (lifetime stats)
    pub jobs_completed: u64,

    /// Total chests looted by this player
    pub chests_looted: u64,

    /// Item id currently equipped for combat (0 = none)
    pub equipped_item_id: u16,

    /// Season this player data belongs to (for cleanup on reset)
    pub season_seed: u64,

    /// PDA bump seed
    pub bump: u8,
}

/// Represents an active job the player is working on
#[derive(AnchorSerialize, AnchorDeserialize, Clone, InitSpace)]
pub struct ActiveJob {
    pub room_x: i8,
    pub room_y: i8,
    pub direction: u8,
}

impl PlayerAccount {
    pub const SEED_PREFIX: &'static [u8] = b"player";

    /// Check if player is at the given room
    pub fn is_at_room(&self, x: i8, y: i8) -> bool {
        self.current_room_x == x && self.current_room_y == y
    }

    /// Check if player is already working on a job at given room/direction
    pub fn has_active_job(&self, room_x: i8, room_y: i8, direction: u8) -> bool {
        self.active_jobs.iter().any(|job| {
            job.room_x == room_x && job.room_y == room_y && job.direction == direction
        })
    }

    /// Add an active job
    pub fn add_job(&mut self, room_x: i8, room_y: i8, direction: u8) -> Result<()> {
        require!(
            self.active_jobs.len() < MAX_ACTIVE_JOBS,
            crate::errors::ChainDepthError::TooManyActiveJobs
        );
        self.active_jobs.push(ActiveJob {
            room_x,
            room_y,
            direction,
        });
        Ok(())
    }

    /// Remove an active job
    pub fn remove_job(&mut self, room_x: i8, room_y: i8, direction: u8) {
        self.active_jobs.retain(|job| {
            !(job.room_x == room_x && job.room_y == room_y && job.direction == direction)
        });
    }
}
