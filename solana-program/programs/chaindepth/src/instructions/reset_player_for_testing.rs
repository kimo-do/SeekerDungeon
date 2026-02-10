use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::PlayerResetForTesting;
use crate::state::{GlobalAccount, PlayerAccount, PlayerProfile};

#[derive(Accounts)]
pub struct ResetPlayerForTesting<'info> {
    /// Admin-only testing helper.
    #[account(mut)]
    pub authority: Signer<'info>,

    /// CHECK: target player wallet whose PDAs are being reset
    pub player: UncheckedAccount<'info>,

    #[account(
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump,
        constraint = global.admin == authority.key() @ ChainDepthError::Unauthorized
    )]
    pub global: Account<'info, GlobalAccount>,

    #[account(
        mut,
        seeds = [PlayerAccount::SEED_PREFIX, player.key().as_ref()],
        bump = player_account.bump,
        close = authority
    )]
    pub player_account: Account<'info, PlayerAccount>,

    #[account(
        mut,
        seeds = [PlayerProfile::SEED_PREFIX, player.key().as_ref()],
        bump = profile.bump,
        close = authority
    )]
    pub profile: Account<'info, PlayerProfile>,
}

pub fn handler(ctx: Context<ResetPlayerForTesting>) -> Result<()> {
    emit!(PlayerResetForTesting {
        admin: ctx.accounts.authority.key(),
        player: ctx.accounts.player.key(),
    });
    Ok(())
}
