use anchor_lang::prelude::*;

pub mod errors;
pub mod events;
pub mod instructions;
pub mod state;

use instructions::*;

declare_id!("3Ctc2FgnNHQtGAcZftMS4ykLhJYjLzBD3hELKy55DnKo");

#[program]
pub mod chaindepth {
    use super::*;

    /// Initialize the global game state (admin only, once per season start)
    /// season_seed should be generated client-side (e.g., current slot number)
    pub fn init_global(
        ctx: Context<InitGlobal>,
        initial_prize_pool_amount: u64,
        season_seed: u64,
    ) -> Result<()> {
        instructions::init_global::handler(ctx, initial_prize_pool_amount, season_seed)
    }

    /// Reset the season (creates new seed, resets depth)
    pub fn reset_season(ctx: Context<ResetSeason>) -> Result<()> {
        instructions::reset_season::handler(ctx)
    }

    /// Initialize a new player at the spawn point
    pub fn init_player(ctx: Context<InitPlayer>) -> Result<()> {
        instructions::move_player::init_player_handler(ctx)
    }

    /// Move player to an adjacent room
    pub fn move_player(ctx: Context<MovePlayer>, new_x: i8, new_y: i8) -> Result<()> {
        instructions::move_player::handler(ctx, new_x, new_y)
    }

    /// Join a job to clear rubble (stakes SKR)
    pub fn join_job(ctx: Context<JoinJob>, direction: u8) -> Result<()> {
        instructions::join_job::handler(ctx, direction)
    }

    /// Update job progress based on elapsed slots
    pub fn tick_job(ctx: Context<TickJob>, direction: u8) -> Result<()> {
        instructions::tick_job::handler(ctx, direction)
    }

    /// Boost job progress with a tip
    pub fn boost_job(ctx: Context<BoostJob>, direction: u8, boost_amount: u64) -> Result<()> {
        instructions::boost_job::handler(ctx, direction, boost_amount)
    }

    /// Complete a job when progress is sufficient
    pub fn complete_job(ctx: Context<CompleteJob>, direction: u8) -> Result<()> {
        instructions::complete_job::handler(ctx, direction)
    }

    /// Loot a chest in the current room
    pub fn loot_chest(ctx: Context<LootChest>) -> Result<()> {
        instructions::loot_chest::handler(ctx)
    }

    /// Abandon a job and receive partial refund
    pub fn abandon_job(ctx: Context<AbandonJob>, direction: u8) -> Result<()> {
        instructions::abandon_job::handler(ctx, direction)
    }
}
