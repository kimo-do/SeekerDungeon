use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::PlayerMoved;
use crate::state::{GlobalAccount, PlayerAccount, RoomAccount, WALL_OPEN, WALL_RUBBLE};

#[derive(Accounts)]
#[instruction(new_x: i8, new_y: i8)]
pub struct MovePlayer<'info> {
    #[account(mut)]
    pub player: Signer<'info>,

    #[account(
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Account<'info, GlobalAccount>,

    #[account(
        init_if_needed,
        payer = player,
        space = 8 + PlayerAccount::INIT_SPACE,
        seeds = [PlayerAccount::SEED_PREFIX, player.key().as_ref()],
        bump
    )]
    pub player_account: Account<'info, PlayerAccount>,

    /// Current room the player is in
    #[account(
        seeds = [
            RoomAccount::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[player_account.current_room_x as u8],
            &[player_account.current_room_y as u8]
        ],
        bump = current_room.bump
    )]
    pub current_room: Account<'info, RoomAccount>,

    /// Target room to move to (must exist and be adjacent)
    #[account(
        seeds = [
            RoomAccount::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[new_x as u8],
            &[new_y as u8]
        ],
        bump = target_room.bump
    )]
    pub target_room: Account<'info, RoomAccount>,

    pub system_program: Program<'info, System>,
}

pub fn handler(ctx: Context<MovePlayer>, new_x: i8, new_y: i8) -> Result<()> {
    let player_account = &mut ctx.accounts.player_account;
    let current_room = &ctx.accounts.current_room;
    let global = &ctx.accounts.global;

    // Check bounds
    require!(
        new_x >= GlobalAccount::MIN_COORD && new_x <= GlobalAccount::MAX_COORD,
        ChainDepthError::OutOfBounds
    );
    require!(
        new_y >= GlobalAccount::MIN_COORD && new_y <= GlobalAccount::MAX_COORD,
        ChainDepthError::OutOfBounds
    );

    // Initialize player if first time (new player starts at spawn)
    if player_account.owner == Pubkey::default() {
        player_account.owner = ctx.accounts.player.key();
        player_account.current_room_x = GlobalAccount::START_X;
        player_account.current_room_y = GlobalAccount::START_Y;
        player_account.active_jobs = Vec::new();
        player_account.jobs_completed = 0;
        player_account.chests_looted = 0;
        player_account.season_seed = global.season_seed;
        player_account.bump = ctx.bumps.player_account;
    }

    let from_x = player_account.current_room_x;
    let from_y = player_account.current_room_y;

    // Check adjacency (only 1 step in cardinal direction)
    let dx = (new_x - from_x).abs();
    let dy = (new_y - from_y).abs();
    require!(
        (dx == 1 && dy == 0) || (dx == 0 && dy == 1),
        ChainDepthError::NotAdjacent
    );

    // Determine direction of movement
    let direction = if new_y > from_y {
        0 // North
    } else if new_y < from_y {
        1 // South
    } else if new_x > from_x {
        2 // East
    } else {
        3 // West
    };

    // Check if wall in that direction is open
    require!(
        current_room.walls[direction] == WALL_OPEN,
        ChainDepthError::WallNotOpen
    );

    // Update player position
    player_account.current_room_x = new_x;
    player_account.current_room_y = new_y;

    emit!(PlayerMoved {
        player: ctx.accounts.player.key(),
        from_x,
        from_y,
        to_x: new_x,
        to_y: new_y,
    });

    Ok(())
}

/// Simpler init instruction for new players (spawn at start)
#[derive(Accounts)]
pub struct InitPlayer<'info> {
    #[account(mut)]
    pub player: Signer<'info>,

    #[account(
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Account<'info, GlobalAccount>,

    #[account(
        init,
        payer = player,
        space = 8 + PlayerAccount::INIT_SPACE,
        seeds = [PlayerAccount::SEED_PREFIX, player.key().as_ref()],
        bump
    )]
    pub player_account: Account<'info, PlayerAccount>,

    pub system_program: Program<'info, System>,
}

pub fn init_player_handler(ctx: Context<InitPlayer>) -> Result<()> {
    let player_account = &mut ctx.accounts.player_account;
    let global = &ctx.accounts.global;

    player_account.owner = ctx.accounts.player.key();
    player_account.current_room_x = GlobalAccount::START_X;
    player_account.current_room_y = GlobalAccount::START_Y;
    player_account.active_jobs = Vec::new();
    player_account.jobs_completed = 0;
    player_account.chests_looted = 0;
    player_account.season_seed = global.season_seed;
    player_account.bump = ctx.bumps.player_account;

    emit!(PlayerMoved {
        player: ctx.accounts.player.key(),
        from_x: 0,
        from_y: 0,
        to_x: GlobalAccount::START_X,
        to_y: GlobalAccount::START_Y,
    });

    Ok(())
}
