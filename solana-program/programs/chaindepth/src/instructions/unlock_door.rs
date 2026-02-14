use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::DoorUnlocked;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    calculate_depth, initialize_discovered_room, item_ids, session_instruction_bits, GlobalAccount,
    InventoryAccount, PlayerAccount, RoomAccount, SessionAuthority, LOCK_KIND_NONE,
    LOCK_KIND_SKELETON, WALL_LOCKED, WALL_OPEN,
};

#[derive(Accounts)]
#[instruction(direction: u8)]
pub struct UnlockDoor<'info> {
    #[account(mut)]
    pub authority: Signer<'info>,

    /// CHECK: wallet owner whose gameplay state is being modified
    pub player: UncheckedAccount<'info>,

    #[account(
        mut,
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Account<'info, GlobalAccount>,

    #[account(
        seeds = [PlayerAccount::SEED_PREFIX, player.key().as_ref()],
        bump = player_account.bump,
        constraint = player_account.owner == player.key() @ ChainDepthError::Unauthorized
    )]
    pub player_account: Account<'info, PlayerAccount>,

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
    pub room: Account<'info, RoomAccount>,

    #[account(
        init_if_needed,
        payer = authority,
        space = RoomAccount::DISCRIMINATOR.len() + RoomAccount::INIT_SPACE,
        seeds = [
            RoomAccount::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[adjacent_x(room.x, direction) as u8],
            &[adjacent_y(room.y, direction) as u8]
        ],
        bump
    )]
    pub adjacent_room: Account<'info, RoomAccount>,

    #[account(
        mut,
        seeds = [InventoryAccount::SEED_PREFIX, player.key().as_ref()],
        bump = inventory.bump,
        constraint = inventory.owner == player.key() @ ChainDepthError::Unauthorized
    )]
    pub inventory: Account<'info, InventoryAccount>,

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

    pub system_program: Program<'info, System>,
}

pub fn handler(ctx: Context<UnlockDoor>, direction: u8) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::UNLOCK_DOOR,
        0,
    )?;

    require!(
        RoomAccount::is_valid_direction(direction),
        ChainDepthError::InvalidDirection
    );

    let room = &mut ctx.accounts.room;
    let player_account = &ctx.accounts.player_account;
    let player_key = ctx.accounts.player.key();
    let direction_index = direction as usize;

    require!(
        player_account.is_at_room(room.x, room.y),
        ChainDepthError::NotInRoom
    );
    require!(
        room.walls[direction_index] == WALL_LOCKED,
        ChainDepthError::WallNotLocked
    );

    let lock_kind = room.door_lock_kinds[direction_index];
    let key_item_id = key_item_id_for_lock_kind(lock_kind)?;
    ctx.accounts.inventory.remove_item(key_item_id, 1)?;

    room.walls[direction_index] = WALL_OPEN;
    room.door_lock_kinds[direction_index] = LOCK_KIND_NONE;

    let opposite_direction = RoomAccount::opposite_direction(direction);
    let clock = Clock::get()?;
    let adjacent_room = &mut ctx.accounts.adjacent_room;
    if adjacent_room.season_seed == 0 {
        initialize_discovered_room(
            adjacent_room,
            ctx.accounts.global.season_seed,
            adjacent_x(room.x, direction),
            adjacent_y(room.y, direction),
            opposite_direction,
            player_key,
            clock.slot,
            ctx.bumps.adjacent_room,
        );
    }

    adjacent_room.walls[opposite_direction as usize] = WALL_OPEN;
    adjacent_room.door_lock_kinds[opposite_direction as usize] = LOCK_KIND_NONE;

    let new_depth = calculate_depth(adjacent_room.x, adjacent_room.y);
    if new_depth > ctx.accounts.global.depth {
        ctx.accounts.global.depth = new_depth;
    }

    emit!(DoorUnlocked {
        room_x: room.x,
        room_y: room.y,
        direction,
        player: player_key,
        key_item_id: key_item_id,
    });

    Ok(())
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

fn key_item_id_for_lock_kind(lock_kind: u8) -> Result<u16> {
    match lock_kind {
        LOCK_KIND_SKELETON => Ok(item_ids::SKELETON_KEY),
        LOCK_KIND_NONE => err!(ChainDepthError::WallNotLocked),
        _ => err!(ChainDepthError::InvalidLockKind),
    }
}
