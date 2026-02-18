use anchor_lang::prelude::*;
use anchor_lang::system_program;

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
        bump
    )]
    /// CHECK: PDA seed validation guarantees this is the player's canonical player_account PDA.
    /// We intentionally avoid deserialization so legacy layouts can still be closed by admin.
    pub player_account: UncheckedAccount<'info>,

    #[account(
        mut,
        seeds = [PlayerProfile::SEED_PREFIX, player.key().as_ref()],
        bump
    )]
    /// CHECK: PDA seed validation guarantees this is the player's canonical profile PDA.
    /// We intentionally avoid deserialization so legacy layouts can still be closed by admin.
    pub profile: UncheckedAccount<'info>,
}

pub fn handler(ctx: Context<ResetPlayerForTesting>) -> Result<()> {
    close_program_owned_pda(
        &ctx.accounts.player_account.to_account_info(),
        &ctx.accounts.authority.to_account_info(),
    )?;
    close_program_owned_pda(
        &ctx.accounts.profile.to_account_info(),
        &ctx.accounts.authority.to_account_info(),
    )?;

    emit!(PlayerResetForTesting {
        admin: ctx.accounts.authority.key(),
        player: ctx.accounts.player.key(),
    });
    Ok(())
}

fn close_program_owned_pda(account_info: &AccountInfo<'_>, recipient_info: &AccountInfo<'_>) -> Result<()> {
    require_keys_eq!(*account_info.owner, crate::ID, ChainDepthError::Unauthorized);

    let lamports_to_return = account_info.lamports();
    if lamports_to_return > 0 {
        **recipient_info.try_borrow_mut_lamports()? = recipient_info
            .lamports()
            .checked_add(lamports_to_return)
            .ok_or(ChainDepthError::Overflow)?;
        **account_info.try_borrow_mut_lamports()? = 0;
    }

    account_info.assign(&system_program::ID);
    account_info.realloc(0, false)?;
    Ok(())
}
