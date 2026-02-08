use anchor_lang::prelude::*;
use anchor_spl::token::{self, Token, TokenAccount, Transfer};

use crate::errors::ChainDepthError;
use crate::events::JobBoosted;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{session_instruction_bits, GlobalAccount, RoomAccount, SessionAuthority};

#[derive(Accounts)]
#[instruction(direction: u8, boost_amount: u64)]
pub struct BoostJob<'info> {
    #[account(mut)]
    pub authority: Signer<'info>,

    /// CHECK: wallet owner whose gameplay state is being modified
    pub player: UncheckedAccount<'info>,

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

    /// Prize pool receives tips
    #[account(
        mut,
        constraint = prize_pool.key() == global.prize_pool
    )]
    pub prize_pool: Account<'info, TokenAccount>,

    /// Player's SKR token account
    #[account(
        mut,
        constraint = player_token_account.mint == global.skr_mint,
        constraint = player_token_account.owner == player.key()
    )]
    pub player_token_account: Account<'info, TokenAccount>,

    #[account(
        mut,
        seeds = [
            SessionAuthority::SEED_PREFIX,
            player.key().as_ref(),
            authority.key().as_ref()
        ],
        bump = session_authority.bump
    )]
    pub session_authority: Option<Account<'info, SessionAuthority>>,

    pub token_program: Program<'info, Token>,
}

pub fn handler(ctx: Context<BoostJob>, direction: u8, boost_amount: u64) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::BOOST_JOB,
        boost_amount,
    )?;

    // Validate direction
    require!(
        RoomAccount::is_valid_direction(direction),
        ChainDepthError::InvalidDirection
    );

    // Validate minimum boost
    require!(
        boost_amount >= RoomAccount::MIN_BOOST_TIP,
        ChainDepthError::InsufficientBalance
    );

    let room = &mut ctx.accounts.room;
    let dir_idx = direction as usize;

    // Check there's an active job
    require!(
        room.helper_counts[dir_idx] > 0,
        ChainDepthError::NoActiveJob
    );

    // Check wall is still rubble
    require!(room.is_rubble(direction), ChainDepthError::NotRubble);

    // Calculate boost progress (proportional to tip amount)
    // Each MIN_BOOST_TIP gives BOOST_PROGRESS slots worth of progress
    let boost_progress = (boost_amount / RoomAccount::MIN_BOOST_TIP)
        .checked_mul(RoomAccount::BOOST_PROGRESS)
        .ok_or(ChainDepthError::Overflow)?;

    // Add progress (capped at base_slots)
    room.progress[dir_idx] = room.progress[dir_idx]
        .checked_add(boost_progress)
        .ok_or(ChainDepthError::Overflow)?
        .min(room.base_slots[dir_idx]);

    // Transfer tip to prize pool
    let transfer_ctx = CpiContext::new(
        ctx.accounts.token_program.to_account_info(),
        Transfer {
            from: ctx.accounts.player_token_account.to_account_info(),
            to: ctx.accounts.prize_pool.to_account_info(),
            authority: ctx.accounts.authority.to_account_info(),
        },
    );
    token::transfer(transfer_ctx, boost_amount)?;

    emit!(JobBoosted {
        room_x: room.x,
        room_y: room.y,
        direction,
        player: ctx.accounts.player.key(),
        tip_amount: boost_amount,
        new_progress: room.progress[dir_idx],
    });

    Ok(())
}
