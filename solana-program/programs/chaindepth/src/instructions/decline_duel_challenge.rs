use anchor_lang::prelude::*;
use anchor_spl::token::{self, Token, TokenAccount, Transfer};

use crate::errors::ChainDepthError;
use crate::events::DuelChallengeDeclined;
use crate::state::{DuelChallenge, GlobalAccount};

#[derive(Accounts)]
#[instruction(challenge_seed: u64)]
pub struct DeclineDuelChallenge<'info> {
    #[account(mut)]
    pub opponent: Signer<'info>,

    /// CHECK: challenger wallet for PDA derivations
    pub challenger: UncheckedAccount<'info>,

    #[account(
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Account<'info, GlobalAccount>,

    #[account(
        mut,
        seeds = [
            DuelChallenge::SEED_PREFIX,
            challenger.key().as_ref(),
            opponent.key().as_ref(),
            &challenge_seed.to_le_bytes()
        ],
        bump = duel_challenge.bump
    )]
    pub duel_challenge: Account<'info, DuelChallenge>,

    #[account(
        mut,
        seeds = [DuelChallenge::DUEL_ESCROW_SEED_PREFIX, duel_challenge.key().as_ref()],
        bump
    )]
    pub duel_escrow: Account<'info, TokenAccount>,

    #[account(
        mut,
        constraint = challenger_token_account.owner == challenger.key(),
        constraint = challenger_token_account.mint == global.skr_mint
    )]
    pub challenger_token_account: Account<'info, TokenAccount>,

    pub token_program: Program<'info, Token>,
}

pub fn handler(ctx: Context<DeclineDuelChallenge>, _challenge_seed: u64) -> Result<()> {
    let duel_challenge = &mut ctx.accounts.duel_challenge;
    require!(
        duel_challenge.status == DuelChallenge::STATUS_OPEN,
        ChainDepthError::InvalidDuelState
    );
    require!(
        duel_challenge.opponent == ctx.accounts.opponent.key()
            && duel_challenge.challenger == ctx.accounts.challenger.key(),
        ChainDepthError::Unauthorized
    );

    let duel_challenge_key = duel_challenge.key();
    let duel_escrow_signer_seeds = &[
        DuelChallenge::DUEL_ESCROW_SEED_PREFIX,
        duel_challenge_key.as_ref(),
        &[ctx.bumps.duel_escrow],
    ];
    let duel_escrow_signer = &[&duel_escrow_signer_seeds[..]];
    let refund_context = CpiContext::new_with_signer(
        ctx.accounts.token_program.to_account_info(),
        Transfer {
            from: ctx.accounts.duel_escrow.to_account_info(),
            to: ctx.accounts.challenger_token_account.to_account_info(),
            authority: ctx.accounts.duel_escrow.to_account_info(),
        },
        duel_escrow_signer,
    );
    token::transfer(refund_context, duel_challenge.stake_amount)?;

    duel_challenge.status = DuelChallenge::STATUS_DECLINED;
    duel_challenge.settled_slot = Clock::get()?.slot;

    emit!(DuelChallengeDeclined {
        challenger: duel_challenge.challenger,
        opponent: duel_challenge.opponent,
        duel_challenge: duel_challenge.key(),
        stake_amount: duel_challenge.stake_amount,
    });

    Ok(())
}
