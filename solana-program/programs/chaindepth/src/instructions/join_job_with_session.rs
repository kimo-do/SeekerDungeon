use anchor_lang::prelude::*;
use anchor_spl::token::{self, Token, TokenAccount, Transfer};

use crate::errors::ChainDepthError;
use crate::events::JobJoined;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    session_instruction_bits, GlobalAccount, HelperStake, PlayerAccount, RoomAccount, RoomPresence,
    SessionAuthority,
};

#[derive(Accounts)]
#[instruction(direction: u8)]
pub struct JoinJobWithSession<'info> {
    #[account(mut)]
    pub authority: Signer<'info>,

    /// CHECK: wallet owner whose gameplay state is being modified
    pub player: UncheckedAccount<'info>,

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
        payer = authority,
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
        payer = authority,
        token::mint = skr_mint,
        token::authority = escrow,
        seeds = [b"escrow", room.key().as_ref(), &[direction]],
        bump
    )]
    pub escrow: Box<Account<'info, TokenAccount>>,

    /// Per-helper stake marker for this room+direction.
    #[account(
        init,
        payer = authority,
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

    #[account(
        mut,
        seeds = [
            SessionAuthority::SEED_PREFIX,
            player.key().as_ref(),
            authority.key().as_ref()
        ],
        bump = session_authority.bump
    )]
    pub session_authority: Box<Account<'info, SessionAuthority>>,

    pub token_program: Program<'info, Token>,
    pub system_program: Program<'info, System>,
}

pub fn handler(ctx: Context<JoinJobWithSession>, direction: u8) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        Some(&mut ctx.accounts.session_authority),
        session_instruction_bits::JOIN_JOB,
        RoomAccount::STAKE_AMOUNT,
    )?;

    require!(
        RoomAccount::is_valid_direction(direction),
        ChainDepthError::InvalidDirection
    );

    let player_account = &mut ctx.accounts.player_account;
    let player_key = ctx.accounts.player.key();
    let clock = Clock::get()?;
    let direction_index = direction as usize;

    let room = &mut ctx.accounts.room;
    require!(room.is_rubble(direction), ChainDepthError::NotRubble);
    require!(
        !player_account.has_active_job(room.x, room.y, direction),
        ChainDepthError::AlreadyJoined
    );
    require!(
        !room.job_completed[direction_index],
        ChainDepthError::JobAlreadyCompleted
    );

    if room.helper_counts[direction_index] == 0 {
        room.start_slot[direction_index] = clock.slot;
        room.base_slots[direction_index] = RoomAccount::calculate_base_slots(ctx.accounts.global.depth);
        room.progress[direction_index] = 0;
        room.bonus_per_helper[direction_index] = 0;
        room.job_completed[direction_index] = false;
    }

    room.helper_counts[direction_index] = room.helper_counts[direction_index]
        .checked_add(1)
        .ok_or(ChainDepthError::Overflow)?;

    room.total_staked[direction_index] = room.total_staked[direction_index]
        .checked_add(RoomAccount::STAKE_AMOUNT)
        .ok_or(ChainDepthError::Overflow)?;

    msg!("JoinJob: adding job for player, current jobs={}", player_account.active_jobs.len());
    player_account.add_job(room.x, room.y, direction)?;
    msg!("JoinJob: after add_job, jobs={}", player_account.active_jobs.len());

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
    require_keys_eq!(
        ctx.accounts.room_presence.player,
        player_key,
        ChainDepthError::NotInRoom
    );
    ctx.accounts.room_presence.set_door_job(direction);

    let helper_stake = &mut ctx.accounts.helper_stake;
    helper_stake.player = player_key;
    helper_stake.room = room.key();
    helper_stake.direction = direction;
    helper_stake.amount = RoomAccount::STAKE_AMOUNT;
    helper_stake.joined_slot = clock.slot;
    helper_stake.bump = ctx.bumps.helper_stake;

    let transfer_context = CpiContext::new(
        ctx.accounts.token_program.to_account_info(),
        Transfer {
            from: ctx.accounts.player_token_account.to_account_info(),
            to: ctx.accounts.escrow.to_account_info(),
            authority: ctx.accounts.authority.to_account_info(),
        },
    );
    token::transfer(transfer_context, RoomAccount::STAKE_AMOUNT)?;

    emit!(JobJoined {
        room_x: room.x,
        room_y: room.y,
        direction,
        player: player_key,
        helper_count: room.helper_counts[direction_index],
        stake_amount: RoomAccount::STAKE_AMOUNT,
    });

    Ok(())
}
