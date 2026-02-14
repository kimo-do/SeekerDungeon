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

    /// Force reset the season immediately (admin override).
    pub fn force_reset_season(ctx: Context<ForceResetSeason>) -> Result<()> {
        instructions::force_reset_season::handler(ctx)
    }

    /// Admin-only test helper to reset a specific player's core PDAs.
    pub fn reset_player_for_testing(ctx: Context<ResetPlayerForTesting>) -> Result<()> {
        instructions::reset_player_for_testing::handler(ctx)
    }

    /// Ensure the start room exists for the current season seed.
    pub fn ensure_start_room(ctx: Context<EnsureStartRoom>) -> Result<()> {
        instructions::ensure_start_room::handler(ctx)
    }

    /// Initialize a new player at the spawn point
    pub fn init_player(ctx: Context<InitPlayer>) -> Result<()> {
        instructions::move_player::init_player_handler(ctx)
    }

    /// Begin a delegated session key for gameplay actions.
    pub fn begin_session(
        ctx: Context<BeginSession>,
        expires_at_slot: u64,
        expires_at_unix_timestamp: i64,
        instruction_allowlist: u64,
        max_token_spend: u64,
    ) -> Result<()> {
        instructions::begin_session::handler(
            ctx,
            expires_at_slot,
            expires_at_unix_timestamp,
            instruction_allowlist,
            max_token_spend,
        )
    }

    /// Revoke a delegated session key.
    pub fn end_session(ctx: Context<EndSession>) -> Result<()> {
        instructions::end_session::handler(ctx)
    }

    /// Move player to an adjacent room
    pub fn move_player(ctx: Context<MovePlayer>, new_x: i8, new_y: i8) -> Result<()> {
        instructions::move_player::handler(ctx, new_x, new_y)
    }

    /// Unlock a locked door using the required key item.
    pub fn unlock_door(ctx: Context<UnlockDoor>, direction: u8) -> Result<()> {
        instructions::unlock_door::handler(ctx, direction)
    }

    /// Join a job to clear rubble (stakes SKR)
    pub fn join_job(ctx: Context<JoinJob>, direction: u8) -> Result<()> {
        instructions::join_job::handler(ctx, direction)
    }

    /// Join a job using a delegated session key.
    pub fn join_job_with_session(
        ctx: Context<JoinJobWithSession>,
        direction: u8,
    ) -> Result<()> {
        instructions::join_job_with_session::handler(ctx, direction)
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

    /// Loot boss rewards after boss defeat (fighters only)
    pub fn loot_boss(ctx: Context<LootBoss>) -> Result<()> {
        instructions::loot_boss::handler(ctx)
    }

    /// Abandon a job and receive partial refund
    pub fn abandon_job(ctx: Context<AbandonJob>, direction: u8) -> Result<()> {
        instructions::abandon_job::handler(ctx, direction)
    }

    /// Claim stake + bonus after a job has been completed
    pub fn claim_job_reward(ctx: Context<ClaimJobReward>, direction: u8) -> Result<()> {
        instructions::claim_job_reward::handler(ctx, direction)
    }

    /// Equip an item id for combat (0 = unequip)
    pub fn equip_item(ctx: Context<EquipItem>, item_id: u16) -> Result<()> {
        instructions::equip_item::handler(ctx, item_id)
    }

    /// Set player skin id for visual profile
    pub fn set_player_skin(ctx: Context<SetPlayerSkin>, skin_id: u16) -> Result<()> {
        instructions::set_player_skin::handler(ctx, skin_id)
    }

    /// Create/update player profile and grant starter pickaxe once
    pub fn create_player_profile(
        ctx: Context<CreatePlayerProfile>,
        skin_id: u16,
        display_name: String,
    ) -> Result<()> {
        instructions::create_player_profile::handler(ctx, skin_id, display_name)
    }

    /// Join fight on boss in current room.
    pub fn join_boss_fight(ctx: Context<JoinBossFight>) -> Result<()> {
        instructions::join_boss_fight::handler(ctx)
    }

    /// Tick boss fight progress.
    pub fn tick_boss_fight(ctx: Context<TickBossFight>) -> Result<()> {
        instructions::tick_boss_fight::handler(ctx)
    }

    /// Add items to player's inventory (utility/admin-like action for testing flows)
    pub fn add_inventory_item(
        ctx: Context<AddInventoryItem>,
        item_id: u16,
        amount: u32,
        durability: u16,
    ) -> Result<()> {
        instructions::add_inventory_item::handler(ctx, item_id, amount, durability)
    }

    /// Remove items from player's inventory (e.g. spending items)
    pub fn remove_inventory_item(
        ctx: Context<RemoveInventoryItem>,
        item_id: u16,
        amount: u32,
    ) -> Result<()> {
        instructions::remove_inventory_item::handler(ctx, item_id, amount)
    }
}
