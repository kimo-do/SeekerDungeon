use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::SeasonReset;
use crate::state::GlobalAccount;

#[derive(Accounts)]
pub struct ResetSeason<'info> {
    /// Admin or authorized relayer
    pub authority: Signer<'info>,

    #[account(
        mut,
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump,
        constraint = global.admin == authority.key() @ ChainDepthError::Unauthorized
    )]
    pub global: Account<'info, GlobalAccount>,
}

pub fn handler(ctx: Context<ResetSeason>) -> Result<()> {
    let global = &mut ctx.accounts.global;
    let clock = Clock::get()?;

    // Check season has ended
    require!(
        clock.slot >= global.end_slot,
        ChainDepthError::SeasonNotEnded
    );

    let (old_seed, new_seed, old_depth, end_slot) = apply_season_reset(global, clock.slot);

    // Note: Room and player accounts from old season become orphaned
    // They use the old season_seed in their PDA, so new rooms will use new PDAs
    // This is a "soft reset" - old data stays on chain but is no longer relevant

    emit!(SeasonReset {
        old_seed,
        new_seed,
        old_depth,
        end_slot,
    });

    Ok(())
}

pub(crate) fn apply_season_reset(global: &mut GlobalAccount, current_slot: u64) -> (u64, u64, u32, u64) {
    let old_seed = global.season_seed;
    let old_depth = global.depth;
    let new_seed = generate_new_seed(old_seed, current_slot);
    let end_slot = current_slot + GlobalAccount::SEASON_DURATION_SLOTS;

    global.season_seed = new_seed;
    global.depth = 0;
    global.jobs_completed = 0;
    global.end_slot = end_slot;

    (old_seed, new_seed, old_depth, end_slot)
}

/// Generate new seed from old seed and current slot
pub(crate) fn generate_new_seed(old_seed: u64, slot: u64) -> u64 {
    // Simple hash combining old seed with slot
    old_seed
        .wrapping_mul(6364136223846793005)
        .wrapping_add(slot)
        .wrapping_mul(1442695040888963407)
}
