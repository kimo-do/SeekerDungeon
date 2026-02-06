use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::state::{GlobalAccount, RoomAccount};

#[derive(Accounts)]
#[instruction(direction: u8)]
pub struct TickJob<'info> {
    /// Anyone can submit a tick (permissionless, for relayers)
    pub caller: Signer<'info>,

    #[account(
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Account<'info, GlobalAccount>,

    /// Room with the active job
    #[account(
        mut,
        seeds = [
            RoomAccount::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[room.x as u8],
            &[room.y as u8]
        ],
        bump
    )]
    pub room: Account<'info, RoomAccount>,
}

pub fn handler(ctx: Context<TickJob>, direction: u8) -> Result<()> {
    // Validate direction
    require!(
        RoomAccount::is_valid_direction(direction),
        ChainDepthError::InvalidDirection
    );

    let room = &mut ctx.accounts.room;
    let clock = Clock::get()?;
    let dir_idx = direction as usize;

    // Check there are helpers working on this job
    require!(
        room.helper_counts[dir_idx] > 0,
        ChainDepthError::NoActiveJob
    );

    // Check wall is still rubble
    require!(room.is_rubble(direction), ChainDepthError::NotRubble);

    // Calculate elapsed slots since job started
    let elapsed_slots = clock.slot.saturating_sub(room.start_slot[dir_idx]);

    // Progress is inversely proportional to helper count
    // More helpers = faster progress
    // Each helper contributes: elapsed_slots / helper_count to progress
    let helper_count = room.helper_counts[dir_idx] as u64;
    
    // New progress calculation: 
    // With more helpers, the work is divided more efficiently
    // Total effective progress = elapsed_slots * helper_count / base_divider
    // This makes more helpers speed things up
    let effective_progress = elapsed_slots
        .checked_mul(helper_count)
        .ok_or(ChainDepthError::Overflow)?
        / 1; // Direct multiplication - more helpers = faster

    // Update progress (capped at base_slots to prevent overflow)
    room.progress[dir_idx] = effective_progress.min(room.base_slots[dir_idx]);

    Ok(())
}
