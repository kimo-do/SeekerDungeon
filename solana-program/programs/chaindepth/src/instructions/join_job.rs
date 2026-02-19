use anchor_lang::prelude::*;
use anchor_spl::token::{self, Token, TokenAccount, Transfer};

use crate::errors::ChainDepthError;
use crate::events::JobJoined;
use crate::state::{GlobalAccount, HelperStake, PlayerAccount, RoomAccount, RoomPresence};

#[derive(Accounts)]
#[instruction(direction: u8)]
pub struct JoinJob<'info> {
    #[account(mut)]
    pub player: Signer<'info>,

    #[account(
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Box<Account<'info, GlobalAccount>>,

    #[account(
        mut,
        seeds = [PlayerAccount::SEED_PREFIX, player.key().as_ref()],
        bump = player_account.bump,
        constraint = player_account.owner == player.key()
    )]
    pub player_account: Box<Account<'info, PlayerAccount>>,

    /// Room where the job is
    #[account(
        mut,
        seeds = [
            RoomAccount::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[player_account.current_room_x as u8],
            &[player_account.current_room_y as u8]
        ],
        bump
    )]
    pub room: Box<Account<'info, RoomAccount>>,

    #[account(
        init_if_needed,
        payer = player,
        space = RoomPresence::DISCRIMINATOR.len() + RoomPresence::INIT_SPACE,
        seeds = [
            RoomPresence::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[player_account.current_room_x as u8],
            &[player_account.current_room_y as u8],
            player.key().as_ref()
        ],
        bump
    )]
    pub room_presence: Box<Account<'info, RoomPresence>>,

    /// Escrow token account for this room direction.
    #[account(
        init_if_needed,
        payer = player,
        token::mint = skr_mint,
        token::authority = escrow,
        seeds = [b"escrow", room.key().as_ref(), &[direction]],
        bump
    )]
    pub escrow: Box<Account<'info, TokenAccount>>,

    /// Per-helper stake marker for this room+direction.
    #[account(
        init,
        payer = player,
        space = 8 + HelperStake::INIT_SPACE,
        seeds = [
            HelperStake::SEED_PREFIX,
            room.key().as_ref(),
            &[direction],
            player.key().as_ref()
        ],
        bump
    )]
    pub helper_stake: Box<Account<'info, HelperStake>>,

    /// Player's SKR token account
    #[account(
        mut,
        constraint = player_token_account.mint == global.skr_mint,
        constraint = player_token_account.owner == player.key()
    )]
    pub player_token_account: Box<Account<'info, TokenAccount>>,

    /// SKR mint
    #[account(constraint = skr_mint.key() == global.skr_mint)]
    pub skr_mint: Box<Account<'info, anchor_spl::token::Mint>>,

    pub token_program: Program<'info, Token>,
    pub system_program: Program<'info, System>,
}

pub fn handler(ctx: Context<JoinJob>, direction: u8) -> Result<()> {
    require!(
        RoomAccount::is_valid_direction(direction),
        ChainDepthError::InvalidDirection
    );

    let room = &mut ctx.accounts.room;
    let player_account = &mut ctx.accounts.player_account;
    let player_key = ctx.accounts.player.key();
    let clock = Clock::get()?;
    let dir_idx = direction as usize;

    require!(room.is_rubble(direction), ChainDepthError::NotRubble);
    require!(
        !player_account.has_active_job(room.x, room.y, direction),
        ChainDepthError::AlreadyJoined
    );
    require!(
        player_account.active_jobs.is_empty(),
        ChainDepthError::TooManyActiveJobs
    );
    require!(
        !room.job_completed[dir_idx],
        ChainDepthError::JobAlreadyCompleted
    );
    require!(
        ctx.accounts.room_presence.activity != RoomPresence::ACTIVITY_BOSS_FIGHT,
        ChainDepthError::AlreadyFightingBoss
    );

    if room.helper_counts[dir_idx] == 0 {
        room.start_slot[dir_idx] = clock.slot;
        room.base_slots[dir_idx] = RoomAccount::calculate_base_slots(ctx.accounts.global.depth);
        room.progress[dir_idx] = 0;
        room.bonus_per_helper[dir_idx] = 0;
        room.job_completed[dir_idx] = false;
    }

    room.helper_counts[dir_idx] = room.helper_counts[dir_idx]
        .checked_add(1)
        .ok_or(ChainDepthError::Overflow)?;

    room.total_staked[dir_idx] = room.total_staked[dir_idx]
        .checked_add(RoomAccount::STAKE_AMOUNT)
        .ok_or(ChainDepthError::Overflow)?;

    player_account.add_job(room.x, room.y, direction)?;
    if ctx.accounts.room_presence.player == Pubkey::default() {
        ctx.accounts.room_presence.player = player_key;
        ctx.accounts.room_presence.season_seed = ctx.accounts.global.season_seed;
        ctx.accounts.room_presence.room_x = room.x;
        ctx.accounts.room_presence.room_y = room.y;
        ctx.accounts.room_presence.skin_id = 0;
        ctx.accounts.room_presence.equipped_item_id = player_account.equipped_item_id;
        ctx.accounts.room_presence.is_current = true;
        ctx.accounts.room_presence.bump = ctx.bumps.room_presence;
    }
    ctx.accounts.room_presence.set_door_job(direction);

    let helper_stake = &mut ctx.accounts.helper_stake;
    helper_stake.player = player_key;
    helper_stake.room = room.key();
    helper_stake.direction = direction;
    helper_stake.amount = RoomAccount::STAKE_AMOUNT;
    helper_stake.joined_slot = clock.slot;
    helper_stake.bump = ctx.bumps.helper_stake;

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
