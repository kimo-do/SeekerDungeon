use anchor_lang::prelude::*;
use anchor_spl::token::{self, Token, TokenAccount, Transfer};

use crate::errors::ChainDepthError;
use crate::events::JobRewardClaimed;
use crate::state::{GlobalAccount, HelperStake, PlayerAccount, RoomAccount, RoomPresence};

#[derive(Accounts)]
#[instruction(direction: u8)]
pub struct ClaimJobReward<'info> {
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

    #[account(
        mut,
        seeds = [b"escrow", room.key().as_ref(), &[direction]],
        bump
    )]
    pub escrow: Account<'info, TokenAccount>,

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

    #[account(
        mut,
        constraint = player_token_account.mint == global.skr_mint,
        constraint = player_token_account.owner == player.key()
    )]
    pub player_token_account: Account<'info, TokenAccount>,

    pub token_program: Program<'info, Token>,
}

pub fn handler(ctx: Context<ClaimJobReward>, direction: u8) -> Result<()> {
    require!(
        RoomAccount::is_valid_direction(direction),
        ChainDepthError::InvalidDirection
    );

    let room = &mut ctx.accounts.room;
    let player_account = &mut ctx.accounts.player_account;
    let dir_idx = direction as usize;

    require!(room.job_completed[dir_idx], ChainDepthError::JobNotCompleted);

    let stake_amount = ctx.accounts.helper_stake.amount;
    let bonus_amount = room.bonus_per_helper[dir_idx];
    let payout_amount = stake_amount
        .checked_add(bonus_amount)
        .ok_or(ChainDepthError::Overflow)?;

    room.total_staked[dir_idx] = room.total_staked[dir_idx]
        .checked_sub(stake_amount)
        .ok_or(ChainDepthError::Overflow)?;
    room.helper_counts[dir_idx] = room.helper_counts[dir_idx]
        .checked_sub(1)
        .ok_or(ChainDepthError::Overflow)?;

    if room.helper_counts[dir_idx] == 0 {
        room.progress[dir_idx] = 0;
        room.start_slot[dir_idx] = 0;
        room.base_slots[dir_idx] = RoomAccount::calculate_base_slots(ctx.accounts.global.depth);
        room.job_completed[dir_idx] = false;
        room.bonus_per_helper[dir_idx] = 0;
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

    let payout_ctx = CpiContext::new_with_signer(
        ctx.accounts.token_program.to_account_info(),
        Transfer {
            from: ctx.accounts.escrow.to_account_info(),
            to: ctx.accounts.player_token_account.to_account_info(),
            authority: ctx.accounts.escrow.to_account_info(),
        },
        escrow_signer,
    );
    token::transfer(payout_ctx, payout_amount)?;

    emit!(JobRewardClaimed {
        room_x: room.x,
        room_y: room.y,
        direction,
        player: ctx.accounts.player.key(),
        payout_amount,
    });

    Ok(())
}
