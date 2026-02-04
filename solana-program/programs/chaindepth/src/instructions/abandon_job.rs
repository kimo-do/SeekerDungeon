use anchor_lang::prelude::*;
use anchor_spl::token::{self, Token, TokenAccount, Transfer};

use crate::errors::ChainDepthError;
use crate::events::JobAbandoned;
use crate::state::{GlobalAccount, PlayerAccount, RoomAccount};

#[derive(Accounts)]
#[instruction(direction: u8)]
pub struct AbandonJob<'info> {
    #[account(mut)]
    pub player: Signer<'info>,

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
        bump = room.bump
    )]
    pub room: Account<'info, RoomAccount>,

    /// Escrow holding staked SKR
    #[account(
        mut,
        seeds = [b"escrow", room.key().as_ref(), &[direction]],
        bump
    )]
    pub escrow: Account<'info, TokenAccount>,

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

    pub token_program: Program<'info, Token>,
}

pub fn handler(ctx: Context<AbandonJob>, direction: u8) -> Result<()> {
    // Validate direction
    require!(
        RoomAccount::is_valid_direction(direction),
        ChainDepthError::InvalidDirection
    );

    let room = &mut ctx.accounts.room;
    let player_account = &mut ctx.accounts.player_account;
    let player_key = ctx.accounts.player.key();
    let dir_idx = direction as usize;

    // Check player is a helper
    require!(
        room.is_helper(direction, &player_key),
        ChainDepthError::NotHelper
    );

    // Remove helper from room
    let removed = room.remove_helper(direction, player_key);
    require!(removed, ChainDepthError::NotHelper);

    // Remove active job from player
    player_account.remove_job(room.x, room.y, direction);

    // Calculate refund (80%) and slash (20%)
    let stake = RoomAccount::STAKE_AMOUNT;
    let refund_amount = stake * RoomAccount::ABANDON_REFUND_PERCENT / 100;
    let slash_amount = stake - refund_amount;

    // Update staked amount
    room.staked_amount[dir_idx] = room.staked_amount[dir_idx].saturating_sub(stake);

    // Transfer refund from escrow to player
    let room_key = room.key();
    let escrow_seeds = &[
        b"escrow".as_ref(),
        room_key.as_ref(),
        &[direction],
        &[ctx.bumps.escrow],
    ];
    let escrow_signer = &[&escrow_seeds[..]];

    // Refund 80%
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

    // Slash 20% to prize pool
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
