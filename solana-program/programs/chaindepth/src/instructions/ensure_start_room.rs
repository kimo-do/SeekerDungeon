use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::state::{GlobalAccount, RoomAccount, CENTER_EMPTY};

#[derive(Accounts)]
pub struct EnsureStartRoom<'info> {
    #[account(mut)]
    pub authority: Signer<'info>,

    #[account(
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump,
        constraint = global.admin == authority.key() @ ChainDepthError::Unauthorized
    )]
    pub global: Account<'info, GlobalAccount>,

    #[account(
        init_if_needed,
        payer = authority,
        space = 8 + RoomAccount::INIT_SPACE,
        seeds = [
            RoomAccount::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[GlobalAccount::START_X as u8],
            &[GlobalAccount::START_Y as u8]
        ],
        bump
    )]
    pub start_room: Account<'info, RoomAccount>,

    pub system_program: Program<'info, System>,
}

pub fn handler(ctx: Context<EnsureStartRoom>) -> Result<()> {
    let start_room = &mut ctx.accounts.start_room;

    // Room already initialized for this season.
    if start_room.season_seed == ctx.accounts.global.season_seed
        && start_room.created_by != Pubkey::default()
    {
        return Ok(());
    }

    let clock = Clock::get()?;
    start_room.x = GlobalAccount::START_X;
    start_room.y = GlobalAccount::START_Y;
    start_room.season_seed = ctx.accounts.global.season_seed;
    start_room.walls = RoomAccount::generate_start_walls(ctx.accounts.global.season_seed);
    start_room.helper_counts = [0; 4];
    start_room.progress = [0; 4];
    start_room.start_slot = [0; 4];
    start_room.base_slots = [RoomAccount::calculate_base_slots(0); 4];
    start_room.total_staked = [0; 4];
    start_room.job_completed = [false; 4];
    start_room.bonus_per_helper = [0; 4];
    start_room.has_chest = false;
    start_room.center_type = CENTER_EMPTY;
    start_room.center_id = 0;
    start_room.boss_max_hp = 0;
    start_room.boss_current_hp = 0;
    start_room.boss_last_update_slot = clock.slot;
    start_room.boss_total_dps = 0;
    start_room.boss_fighter_count = 0;
    start_room.boss_defeated = false;
    start_room.looted_by = Vec::new();
    start_room.created_by = ctx.accounts.authority.key();
    start_room.created_slot = clock.slot;
    start_room.bump = ctx.bumps.start_room;

    Ok(())
}
