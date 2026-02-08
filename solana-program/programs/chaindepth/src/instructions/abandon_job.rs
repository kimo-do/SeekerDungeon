use anchor_lang::prelude::*;
use anchor_spl::token::{self, Token, TokenAccount, Transfer};

use crate::errors::ChainDepthError;
use crate::events::JobAbandoned;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    session_instruction_bits, GlobalAccount, HelperStake, PlayerAccount, RoomAccount, RoomPresence,
    SessionAuthority,
};

#[derive(Accounts)]
#[instruction(direction: u8)]
pub struct AbandonJob<'info> {
    #[account(mut)]
    pub authority: Signer<'info>,

    /// CHECK: wallet owner whose gameplay state is being modified
    #[account(mut)]
    pub player: UncheckedAccount<'info>,

    #[account(
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Account<'info, GlobalAccount>,

    #[account(
        mut,
        seeds = [PlayerAccount::SEED_PREFIX, player.key().as_ref()],
        bump = player_account.bump
    )]
    pub player_account: Account<'info, PlayerAccount>,

    /// Room with the job to abandon
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

    #[account(
        mut,
        seeds = [
            RoomPresence::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[player_account.current_room_x as u8],
            &[player_account.current_room_y as u8],
            player.key().as_ref()
        ],
        bump = room_presence.bump
    )]
    pub room_presence: Account<'info, RoomPresence>,

    /// Escrow holding staked SKR
    #[account(
        mut,
        seeds = [b"escrow", room.key().as_ref(), &[direction]],
        bump
    )]
    pub escrow: Account<'info, TokenAccount>,

    /// Helper's individual stake account
    #[account(
        mut,
        close = player,
        seeds = [
            HelperStake::SEED_PREFIX,
            room.key().as_ref(),
            &[direction],
            player.key().as_ref()
        ],
        bump = helper_stake.bump
    )]
    pub helper_stake: Account<'info, HelperStake>,

    /// Prize pool receives slash amount
    #[account(
        mut,
        constraint = prize_pool.key() == global.prize_pool
    )]
    pub prize_pool: Account<'info, TokenAccount>,

    /// Player's token account for partial refund
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

pub fn handler(ctx: Context<AbandonJob>, direction: u8) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::ABANDON_JOB,
        0,
    )?;

    require!(
        RoomAccount::is_valid_direction(direction),
        ChainDepthError::InvalidDirection
    );

    let room = &mut ctx.accounts.room;
    let player_account = &mut ctx.accounts.player_account;
    let player_key = ctx.accounts.player.key();
    let dir_idx = direction as usize;

    require!(room.is_rubble(direction), ChainDepthError::NotRubble);
    require!(
        !room.job_completed[dir_idx],
        ChainDepthError::JobAlreadyCompleted
    );

    let stake = ctx.accounts.helper_stake.amount;
    let refund_amount = stake
        .checked_mul(RoomAccount::ABANDON_REFUND_PERCENT)
        .ok_or(ChainDepthError::Overflow)?
        / 100;
    let slash_amount = stake
        .checked_sub(refund_amount)
        .ok_or(ChainDepthError::Overflow)?;

    room.total_staked[dir_idx] = room.total_staked[dir_idx]
        .checked_sub(stake)
        .ok_or(ChainDepthError::Overflow)?;
    room.helper_counts[dir_idx] = room.helper_counts[dir_idx]
        .checked_sub(1)
        .ok_or(ChainDepthError::Overflow)?;

    if room.helper_counts[dir_idx] == 0 {
        room.progress[dir_idx] = 0;
        room.start_slot[dir_idx] = 0;
        room.bonus_per_helper[dir_idx] = 0;
        room.job_completed[dir_idx] = false;
    }

    player_account.remove_job(room.x, room.y, direction);
    ctx.accounts.room_presence.set_idle();

    let room_key = room.key();
    let escrow_seeds = &[
        b"escrow".as_ref(),
        room_key.as_ref(),
        &[direction],
        &[ctx.bumps.escrow],
    ];
    let escrow_signer = &[&escrow_seeds[..]];

    let refund_ctx = CpiContext::new_with_signer(
        ctx.accounts.token_program.to_account_info(),
        Transfer {
            from: ctx.accounts.escrow.to_account_info(),
            to: ctx.accounts.player_token_account.to_account_info(),
            authority: ctx.accounts.escrow.to_account_info(),
        },
        escrow_signer,
    );
    token::transfer(refund_ctx, refund_amount)?;

    let slash_ctx = CpiContext::new_with_signer(
        ctx.accounts.token_program.to_account_info(),
        Transfer {
            from: ctx.accounts.escrow.to_account_info(),
            to: ctx.accounts.prize_pool.to_account_info(),
            authority: ctx.accounts.escrow.to_account_info(),
        },
        escrow_signer,
    );
    token::transfer(slash_ctx, slash_amount)?;

    emit!(JobAbandoned {
        room_x: room.x,
        room_y: room.y,
        direction,
        player: player_key,
        refund_amount,
    });

    Ok(())
}
