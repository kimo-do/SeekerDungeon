use anchor_lang::prelude::*;
use anchor_spl::token::{self, Token, TokenAccount, Transfer};

use crate::errors::ChainDepthError;
use crate::events::JobCompleted;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    session_instruction_bits, GlobalAccount, HelperStake, PlayerAccount, RoomAccount,
    SessionAuthority, CENTER_BOSS, CENTER_CHEST, CENTER_EMPTY, WALL_OPEN, WALL_RUBBLE,
};

#[derive(Accounts)]
#[instruction(direction: u8)]
pub struct CompleteJob<'info> {
    #[account(mut)]
    pub authority: Signer<'info>,

    /// CHECK: wallet owner whose gameplay state is being modified
    pub player: UncheckedAccount<'info>,

    #[account(
        mut,
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Box<Account<'info, GlobalAccount>>,

    #[account(
        seeds = [PlayerAccount::SEED_PREFIX, player.key().as_ref()],
        bump = player_account.bump
    )]
    pub player_account: Box<Account<'info, PlayerAccount>>,

    /// Room with the completed job
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
    pub room: Box<Account<'info, RoomAccount>>,

    /// Helper stake of the completer (authorization that caller is participating)
    #[account(
        seeds = [
            HelperStake::SEED_PREFIX,
            room.key().as_ref(),
            &[direction],
            player.key().as_ref()
        ],
        bump = helper_stake.bump
    )]
    pub helper_stake: Account<'info, HelperStake>,

    /// Adjacent room that will be opened/initialized
    #[account(
        init_if_needed,
        payer = authority,
        space = 8 + RoomAccount::INIT_SPACE,
        seeds = [
            RoomAccount::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[adjacent_x(room.x, direction) as u8],
            &[adjacent_y(room.y, direction) as u8]
        ],
        bump
    )]
    pub adjacent_room: Box<Account<'info, RoomAccount>>,

    /// Escrow holding staked SKR (and bonus after completion)
    #[account(
        mut,
        seeds = [b"escrow", room.key().as_ref(), &[direction]],
        bump
    )]
    pub escrow: Box<Account<'info, TokenAccount>>,

    /// Prize pool for bonus rewards
    #[account(
        mut,
        constraint = prize_pool.key() == global.prize_pool
    )]
    pub prize_pool: Box<Account<'info, TokenAccount>>,

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
    pub system_program: Program<'info, System>,
}

fn adjacent_x(x: i8, direction: u8) -> i8 {
    match direction {
        2 => x + 1,
        3 => x - 1,
        _ => x,
    }
}

fn adjacent_y(y: i8, direction: u8) -> i8 {
    match direction {
        0 => y + 1,
        1 => y - 1,
        _ => y,
    }
}

pub fn handler(ctx: Context<CompleteJob>, direction: u8) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::COMPLETE_JOB,
        0,
    )?;

    require!(
        RoomAccount::is_valid_direction(direction),
        ChainDepthError::InvalidDirection
    );

    let clock = Clock::get()?;
    let dir_idx = direction as usize;

    {
        let room = &ctx.accounts.room;
        require!(room.is_rubble(direction), ChainDepthError::NotRubble);
        require!(room.helper_counts[dir_idx] > 0, ChainDepthError::NoActiveJob);
        require!(
            !room.job_completed[dir_idx],
            ChainDepthError::JobAlreadyCompleted
        );
        require!(
            room.progress[dir_idx] >= room.base_slots[dir_idx],
            ChainDepthError::JobNotReady
        );
        require!(
            ctx.accounts
                .player_account
                .has_active_job(room.x, room.y, direction),
            ChainDepthError::NotHelper
        );
    }

    let global_account_info = ctx.accounts.global.to_account_info();
    let room_x = ctx.accounts.room.x;
    let room_y = ctx.accounts.room.y;
    let helper_count = ctx.accounts.room.helper_counts[dir_idx] as u64;
    let season_seed = ctx.accounts.global.season_seed;
    let global_bump = ctx.accounts.global.bump;

    {
        let room = &mut ctx.accounts.room;
        room.walls[dir_idx] = WALL_OPEN;
        room.job_completed[dir_idx] = true;
    }

    let opposite_dir = RoomAccount::opposite_direction(direction);
    let is_new_adjacent_room;
    {
        let adjacent = &mut ctx.accounts.adjacent_room;
        is_new_adjacent_room = adjacent.season_seed == 0;

        if is_new_adjacent_room {
            adjacent.x = adjacent_x(room_x, direction);
            adjacent.y = adjacent_y(room_y, direction);
            adjacent.season_seed = season_seed;

            let room_hash = generate_room_hash(season_seed, adjacent.x, adjacent.y);
            adjacent.walls = generate_walls(room_hash, opposite_dir);
            adjacent.helper_counts = [0; 4];
            adjacent.progress = [0; 4];
            adjacent.start_slot = [0; 4];
            adjacent.base_slots = [RoomAccount::calculate_base_slots(ctx.accounts.global.depth + 1); 4];
            adjacent.total_staked = [0; 4];
            adjacent.job_completed = [false; 4];
            adjacent.bonus_per_helper = [0; 4];
            let room_depth = calculate_depth(adjacent.x, adjacent.y);
            let (center_type, center_id) =
                generate_room_center(season_seed, adjacent.x, adjacent.y, room_depth);
            let boss_max_hp = if center_type == CENTER_BOSS {
                RoomAccount::boss_hp_for_depth(room_depth, center_id)
            } else {
                0
            };

            adjacent.has_chest = center_type == CENTER_CHEST;
            adjacent.center_type = center_type;
            adjacent.center_id = center_id;
            adjacent.boss_max_hp = boss_max_hp;
            adjacent.boss_current_hp = boss_max_hp;
            adjacent.boss_last_update_slot = clock.slot;
            adjacent.boss_total_dps = 0;
            adjacent.boss_fighter_count = 0;
            adjacent.boss_defeated = false;
            adjacent.looted_count = 0;
            adjacent.created_by = ctx.accounts.player.key();
            adjacent.created_slot = clock.slot;
            adjacent.bump = ctx.bumps.adjacent_room;
        }

        adjacent.walls[opposite_dir as usize] = WALL_OPEN;
        let return_wall_state = adjacent.walls[opposite_dir as usize];
        msg!(
            "complete_job_topology from=({}, {}) to=({}, {}) dir={} return_dir={} return_wall_state={}",
            room_x,
            room_y,
            adjacent.x,
            adjacent.y,
            direction,
            opposite_dir,
            return_wall_state
        );
        require!(
            return_wall_state == WALL_OPEN,
            ChainDepthError::WallNotOpen
        );
    }

    // --- Token bonus transfer (CPI) BEFORE lamport manipulation ---
    let new_depth = calculate_depth(ctx.accounts.adjacent_room.x, ctx.accounts.adjacent_room.y);
    {
        let global = &mut ctx.accounts.global;
        if new_depth > global.depth {
            global.depth = new_depth;
        }
        global.jobs_completed += 1;
    }

    let base_bonus_per_helper = calculate_bonus(ctx.accounts.global.jobs_completed, helper_count);
    let desired_bonus_total = base_bonus_per_helper
        .checked_mul(helper_count)
        .ok_or(ChainDepthError::Overflow)?;
    let bonus_total = desired_bonus_total.min(ctx.accounts.prize_pool.amount);

    if bonus_total > 0 {
        let global_seeds = &[GlobalAccount::SEED_PREFIX, &[global_bump]];
        let global_signer = &[&global_seeds[..]];

        let bonus_ctx = CpiContext::new_with_signer(
            ctx.accounts.token_program.to_account_info(),
            Transfer {
                from: ctx.accounts.prize_pool.to_account_info(),
                to: ctx.accounts.escrow.to_account_info(),
                authority: global_account_info,
            },
            global_signer,
        );
        token::transfer(bonus_ctx, bonus_total)?;
    }

    // --- Reimburse authority for adjacent room rent (manual lamport transfer) ---
    // Done AFTER all CPIs to avoid interference with runtime balance tracking.
    if is_new_adjacent_room {
        let room_space = 8 + RoomAccount::INIT_SPACE;
        let rent_cost = Rent::get()?.minimum_balance(room_space);

        let global_info = ctx.accounts.global.to_account_info();
        let authority_info = ctx.accounts.authority.to_account_info();

        msg!(
            "reimburse: rent_cost={} global_before={} auth_before={}",
            rent_cost,
            global_info.lamports(),
            authority_info.lamports()
        );

        **global_info.try_borrow_mut_lamports()? = global_info
            .lamports()
            .checked_sub(rent_cost)
            .ok_or(ChainDepthError::TreasuryInsufficientFunds)?;
        **authority_info.try_borrow_mut_lamports()? = authority_info
            .lamports()
            .checked_add(rent_cost)
            .ok_or(ChainDepthError::Overflow)?;

        msg!(
            "reimburse: global_after={} auth_after={}",
            global_info.lamports(),
            authority_info.lamports()
        );
    }

    let bonus_per_helper = bonus_total / helper_count;
    {
        let room = &mut ctx.accounts.room;
        room.bonus_per_helper[dir_idx] = bonus_per_helper;
    }

    emit!(JobCompleted {
        room_x,
        room_y,
        direction,
        new_depth: ctx.accounts.global.depth,
        helpers_count: ctx.accounts.room.helper_counts[dir_idx],
        reward_per_helper: RoomAccount::STAKE_AMOUNT + bonus_per_helper,
    });

    Ok(())
}

fn generate_room_hash(seed: u64, x: i8, y: i8) -> u64 {
    let mut hash = seed;
    hash = hash.wrapping_mul(31).wrapping_add(x as u64);
    hash = hash.wrapping_mul(31).wrapping_add(y as u64);
    hash
}

fn generate_walls(hash: u64, entrance_dir: u8) -> [u8; 4] {
    let mut walls = [0u8; 4];

    for i in 0..4 {
        if i == entrance_dir as usize {
            walls[i] = WALL_OPEN;
        } else {
            let wall_hash = (hash >> (i * 8)) % 10;
            walls[i] = if wall_hash < 6 {
                WALL_RUBBLE
            } else if wall_hash < 9 {
                0
            } else {
                WALL_OPEN
            };
        }
    }

    walls
}

fn calculate_depth(x: i8, y: i8) -> u32 {
    let dx = (x - 5).abs() as u32;
    let dy = (y - 5).abs() as u32;
    dx.max(dy)
}

fn generate_room_center(season_seed: u64, room_x: i8, room_y: i8, depth: u32) -> (u8, u16) {
    let room_hash = generate_room_hash(season_seed, room_x, room_y);

    if depth == 1 {
        if is_forced_depth_one_chest(season_seed, room_x, room_y) || (room_hash % 100) < 50 {
            return (CENTER_CHEST, 1);
        }
        return (CENTER_EMPTY, 0);
    }

    if depth >= 2 && (room_hash % 100) < 50 {
        let boss_id = ((room_hash % 4) + 1) as u16;
        return (CENTER_BOSS, boss_id);
    }

    (CENTER_EMPTY, 0)
}

fn is_forced_depth_one_chest(season_seed: u64, room_x: i8, room_y: i8) -> bool {
    let forced_direction = (season_seed % 4) as u8;
    let expected = match forced_direction {
        0 => (5, 6),
        1 => (5, 4),
        2 => (6, 5),
        _ => (4, 5),
    };

    room_x == expected.0 && room_y == expected.1
}

fn calculate_bonus(jobs_completed: u64, helper_count: u64) -> u64 {
    let base_bonus = RoomAccount::MIN_BOOST_TIP;
    base_bonus / (1 + jobs_completed / 100) / helper_count
}
