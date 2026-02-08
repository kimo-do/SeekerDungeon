use anchor_lang::prelude::*;
use anchor_spl::token::{self, Revoke, Token, TokenAccount};

use crate::errors::ChainDepthError;
use crate::state::{GlobalAccount, SessionAuthority};

#[derive(Accounts)]
pub struct EndSession<'info> {
    pub player: Signer<'info>,

    /// Session keypair to revoke.
    /// CHECK: key is used in PDA seeds only.
    pub session_key: UncheckedAccount<'info>,

    #[account(
        mut,
        close = player,
        seeds = [SessionAuthority::SEED_PREFIX, player.key().as_ref(), session_key.key().as_ref()],
        bump = session_authority.bump,
        constraint = session_authority.player == player.key() @ ChainDepthError::Unauthorized,
        constraint = session_authority.session_key == session_key.key() @ ChainDepthError::Unauthorized
    )]
    pub session_authority: Account<'info, SessionAuthority>,

    #[account(
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Account<'info, GlobalAccount>,

    #[account(
        mut,
        constraint = player_token_account.owner == player.key(),
        constraint = player_token_account.mint == global.skr_mint
    )]
    pub player_token_account: Account<'info, TokenAccount>,

    pub token_program: Program<'info, Token>,
}

pub fn handler(ctx: Context<EndSession>) -> Result<()> {
    let session_authority = &mut ctx.accounts.session_authority;

    if session_authority.max_token_spend > 0 {
        let revoke_context = CpiContext::new(
            ctx.accounts.token_program.to_account_info(),
            Revoke {
                source: ctx.accounts.player_token_account.to_account_info(),
                authority: ctx.accounts.player.to_account_info(),
            },
        );
        token::revoke(revoke_context)?;
    }

    session_authority.is_active = false;
    session_authority.expires_at_slot = 0;
    session_authority.expires_at_unix_timestamp = 0;
    session_authority.instruction_allowlist = 0;
    session_authority.max_token_spend = 0;
    session_authority.spent_token_amount = 0;
    Ok(())
}
