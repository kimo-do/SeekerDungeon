use anchor_lang::prelude::*;
use anchor_spl::token::{self, Token, TokenAccount, Transfer};

use crate::errors::ChainDepthError;
use crate::events::JobJoined;
use crate::state::{GlobalAccount, PlayerAccount, RoomAccount};

#[derive(Accounts)]
#[instruction(direction: u8)]
pub struct JoinJob<'info> {
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
        bump = player_account.bump,
        constraint = player_account.owner == player.key()
    )]
    pub player_account: Account<'info, PlayerAccount>,

    /// Room where the job is
    #[account(
        mut,
        seeds = [
            RoomAccount::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[player_account.current_room_x as u8],
            &[player_account.current_room_y as u8]
        ],
        bump = room.bump
    )]
    pub room: Account<'info, RoomAccount>,

    /// Escrow account for this job (holds staked SKR)
    /// CHECK: This is a token account that will be initialized if needed
    #[account(
        init_if_needed,
        payer = player,
        token::mint = skr_mint,
        token::authority = escrow,
        seeds = [b"escrow", room.key().as_ref(), &[direction]],
        bump
    )]
    pub escrow: Account<'info, TokenAccount>,

    /// Player's SKR token account
    #[account(
        mut,
        constraint = player_token_account.mint == global.skr_mint,
        constraint = player_token_account.owner == player.key()
    )]
    pub player_token_account: Account<'info, TokenAccount>,

    /// SKR mint
    #[account(constraint = skr_mint.key() == global.skr_mint)]
    pub skr_mint: Account<'info, anchor_spl::token::Mint>,

    pub token_program: Program<'info, Token>,
    pub system_program: Program<'info, System>,
}

pub fn handler(ctx: Context<JoinJob>, direction: u8) -> Result<()> {
    // Validate direction
    require!(
        RoomAccount::is_valid_direction(direction),
        ChainDepthError::InvalidDirection
    );

    let room = &mut ctx.accounts.room;
    let player_account = &mut ctx.accounts.player_account;
    let player_key = ctx.accounts.player.key();
    let clock = Clock::get()?;

    // Check wall is rubble (clearable)
    require!(room.is_rubble(direction), ChainDepthError::NotRubble);

    // Check player doesn't already have this job active
    require!(
        !player_account.has_active_job(room.x, room.y, direction),
        ChainDepthError::AlreadyJoined
    );

    // Add helper to room
    room.add_helper(direction, player_key)?;

    // If first helper, initialize job timing
    let dir_idx = direction as usize;
    if room.helper_counts[dir_idx] == 1 {
        room.start_slot[dir_idx] = clock.slot;
        room.base_slots[dir_idx] = RoomAccount::calculate_base_slots(ctx.accounts.global.depth);
        room.progress[dir_idx] = 0;
    }

    // Add to staked amount
    room.staked_amount[dir_idx] = room.staked_amount[dir_idx]
        .checked_add(RoomAccount::STAKE_AMOUNT)
        .ok_or(ChainDepthError::Overflow)?;

    // Add active job to player
    player_account.add_job(room.x, room.y, direction)?;

    // Transfer stake to escrow
    let transfer_ctx = CpiContext::new(
        ctx.accounts.token_program.to_account_info(),
        Transfer {
            from: ctx.accounts.player_token_account.to_account_info(),
            to: ctx.accounts.escrow.to_account_info(),
            authority: ctx.accounts.player.to_account_info(),
        },
    );
    token::transfer(transfer_ctx, RoomAccount::STAKE_AMOUNT)?;

    emit!(JobJoined {
        room_x: room.x,
        room_y: room.y,
        direction,
        player: player_key,
        helper_count: room.helper_counts[dir_idx],
        stake_amount: RoomAccount::STAKE_AMOUNT,
    });

    Ok(())
}
