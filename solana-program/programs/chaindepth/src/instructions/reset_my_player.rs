use anchor_lang::prelude::*;
use anchor_lang::system_program;

use crate::errors::ChainDepthError;
use crate::state::{InventoryAccount, PlayerAccount, PlayerProfile};

#[derive(Accounts)]
pub struct ResetMyPlayer<'info> {
    #[account(mut)]
    pub authority: Signer<'info>,

    #[account(
        mut,
        seeds = [PlayerAccount::SEED_PREFIX, authority.key().as_ref()],
        bump
    )]
    /// CHECK: PDA seed validation guarantees this is the caller's canonical player_account PDA.
    /// We intentionally avoid deserialization so legacy layouts can still be closed by the player.
    pub player_account: UncheckedAccount<'info>,

    pub system_program: Program<'info, System>,
}

pub fn handler(ctx: Context<ResetMyPlayer>) -> Result<()> {
    close_program_owned_pda(
        &ctx.accounts.player_account.to_account_info(),
        &ctx.accounts.authority.to_account_info(),
    )?;

    for account_info in ctx.remaining_accounts.iter() {
        let (expected_profile, _) = Pubkey::find_program_address(
            &[PlayerProfile::SEED_PREFIX, ctx.accounts.authority.key().as_ref()],
            &crate::ID,
        );
        let (expected_inventory, _) = Pubkey::find_program_address(
            &[InventoryAccount::SEED_PREFIX, ctx.accounts.authority.key().as_ref()],
            &crate::ID,
        );

        require!(
            account_info.key() == expected_profile || account_info.key() == expected_inventory,
            ChainDepthError::Unauthorized
        );

        close_program_owned_pda(account_info, &ctx.accounts.authority.to_account_info())?;
    }

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
