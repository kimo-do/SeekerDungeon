use anchor_lang::prelude::*;
use anchor_spl::token::{self, Approve, Token, TokenAccount};

use crate::errors::ChainDepthError;
use crate::state::{GlobalAccount, PlayerAccount, SessionAuthority};

const MAX_SESSION_DURATION_SLOTS: u64 = 216_000;
const MAX_SESSION_DURATION_SECONDS: i64 = 86_400;
const MIN_SESSION_DURATION_SLOTS: u64 = 1;
const MIN_SESSION_DURATION_SECONDS: i64 = 1;

#[derive(Accounts)]
pub struct BeginSession<'info> {
    #[account(mut)]
    pub player: Signer<'info>,

    /// Session keypair used for delegated signing during gameplay.
    pub session_key: Signer<'info>,

    #[account(
        seeds = [PlayerAccount::SEED_PREFIX, player.key().as_ref()],
        bump = player_account.bump,
        constraint = player_account.owner == player.key() @ ChainDepthError::Unauthorized
    )]
    pub player_account: Account<'info, PlayerAccount>,

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

    #[account(
        init_if_needed,
        payer = player,
        space = SessionAuthority::DISCRIMINATOR.len() + SessionAuthority::INIT_SPACE,
        seeds = [SessionAuthority::SEED_PREFIX, player.key().as_ref(), session_key.key().as_ref()],
        bump
    )]
    pub session_authority: Account<'info, SessionAuthority>,

    pub token_program: Program<'info, Token>,
    pub system_program: Program<'info, System>,
}

pub fn handler(
    ctx: Context<BeginSession>,
    expires_at_slot: u64,
    expires_at_unix_timestamp: i64,
    instruction_allowlist: u64,
    max_token_spend: u64,
) -> Result<()> {
    let clock = Clock::get()?;

    require!(
        expires_at_slot >= clock.slot + MIN_SESSION_DURATION_SLOTS,
        ChainDepthError::InvalidSessionExpiry
    );
    require!(
        expires_at_slot <= clock.slot + MAX_SESSION_DURATION_SLOTS,
        ChainDepthError::InvalidSessionExpiry
    );
    require!(
        expires_at_unix_timestamp >= clock.unix_timestamp + MIN_SESSION_DURATION_SECONDS,
        ChainDepthError::InvalidSessionExpiry
    );
    require!(
        expires_at_unix_timestamp <= clock.unix_timestamp + MAX_SESSION_DURATION_SECONDS,
        ChainDepthError::InvalidSessionExpiry
    );
    require!(
        instruction_allowlist != 0,
        ChainDepthError::InvalidSessionAllowlist
    );

    let session_authority = &mut ctx.accounts.session_authority;
    session_authority.player = ctx.accounts.player.key();
    session_authority.session_key = ctx.accounts.session_key.key();
    session_authority.expires_at_slot = expires_at_slot;
    session_authority.expires_at_unix_timestamp = expires_at_unix_timestamp;
    session_authority.instruction_allowlist = instruction_allowlist;
    session_authority.max_token_spend = max_token_spend;
    session_authority.spent_token_amount = 0;
    session_authority.is_active = true;
    session_authority.bump = ctx.bumps.session_authority;

    if max_token_spend > 0 {
        let approve_context = CpiContext::new(
            ctx.accounts.token_program.to_account_info(),
            Approve {
                to: ctx.accounts.player_token_account.to_account_info(),
                delegate: ctx.accounts.session_key.to_account_info(),
                authority: ctx.accounts.player.to_account_info(),
            },
        );
        token::approve(approve_context, max_token_spend)?;
    }

    Ok(())
}
