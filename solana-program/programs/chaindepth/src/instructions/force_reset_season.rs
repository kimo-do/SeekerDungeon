use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::SeasonReset;
use crate::instructions::reset_season::apply_season_reset;
use crate::state::GlobalAccount;

#[derive(Accounts)]
pub struct ForceResetSeason<'info> {
    /// Admin override authority
    pub authority: Signer<'info>,

    #[account(
        mut,
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump,
        constraint = global.admin == authority.key() @ ChainDepthError::Unauthorized
    )]
    pub global: Account<'info, GlobalAccount>,
}

pub fn handler(ctx: Context<ForceResetSeason>) -> Result<()> {
    let global = &mut ctx.accounts.global;
    let clock = Clock::get()?;

    let (old_seed, new_seed, old_depth, end_slot) = apply_season_reset(global, clock.slot);

    emit!(SeasonReset {
        old_seed,
        new_seed,
        old_depth,
        end_slot,
    });

    Ok(())
}
