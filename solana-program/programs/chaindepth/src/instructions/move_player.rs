use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::PlayerMoved;
use crate::state::{
    GlobalAccount, PlayerAccount, PlayerProfile, RoomAccount, RoomPresence, WALL_OPEN,
};

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

    #[account(
        init_if_needed,
        payer = player,
        space = PlayerProfile::DISCRIMINATOR.len() + PlayerProfile::INIT_SPACE,
        seeds = [PlayerProfile::SEED_PREFIX, player.key().as_ref()],
        bump
    )]
    pub profile: Account<'info, PlayerProfile>,

    /// Current room the player is in
    #[account(
        seeds = [
            RoomAccount::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[player_account.current_room_x as u8],
            &[player_account.current_room_y as u8]
        ],
        bump
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
        bump
    )]
    pub target_room: Account<'info, RoomAccount>,

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
    pub current_presence: Account<'info, RoomPresence>,

    #[account(
        init_if_needed,
        payer = player,
        space = RoomPresence::DISCRIMINATOR.len() + RoomPresence::INIT_SPACE,
        seeds = [
            RoomPresence::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[new_x as u8],
            &[new_y as u8],
            player.key().as_ref()
        ],
        bump
    )]
    pub target_presence: Account<'info, RoomPresence>,

    pub system_program: Program<'info, System>,
}

pub fn handler(ctx: Context<MovePlayer>, new_x: i8, new_y: i8) -> Result<()> {
    let player_account = &mut ctx.accounts.player_account;
    let profile = &mut ctx.accounts.profile;
    let current_room = &ctx.accounts.current_room;
    let global = &ctx.accounts.global;
    let player_key = ctx.accounts.player.key();

    if profile.owner == Pubkey::default() {
        profile.owner = player_key;
        profile.skin_id = PlayerProfile::DEFAULT_SKIN_ID;
        profile.display_name = String::new();
        profile.starter_pickaxe_granted = false;
        profile.bump = ctx.bumps.profile;
    }

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
        player_account.owner = player_key;
        player_account.current_room_x = GlobalAccount::START_X;
        player_account.current_room_y = GlobalAccount::START_Y;
        player_account.active_jobs = Vec::new();
        player_account.jobs_completed = 0;
        player_account.chests_looted = 0;
        player_account.equipped_item_id = 0;
        player_account.season_seed = global.season_seed;
        player_account.bump = ctx.bumps.player_account;
    }

    let from_x = player_account.current_room_x;
    let from_y = player_account.current_room_y;

    upsert_presence(
        &mut ctx.accounts.current_presence,
        player_key,
        global.season_seed,
        from_x,
        from_y,
        profile.skin_id,
        player_account.equipped_item_id,
        ctx.bumps.current_presence,
    );
    ctx.accounts.current_presence.is_current = false;

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

    upsert_presence(
        &mut ctx.accounts.target_presence,
        player_key,
        global.season_seed,
        new_x,
        new_y,
        profile.skin_id,
        player_account.equipped_item_id,
        ctx.bumps.target_presence,
    );
    ctx.accounts.target_presence.is_current = true;
    ctx.accounts.target_presence.set_idle();

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

    #[account(
        init,
        payer = player,
        space = PlayerProfile::DISCRIMINATOR.len() + PlayerProfile::INIT_SPACE,
        seeds = [PlayerProfile::SEED_PREFIX, player.key().as_ref()],
        bump
    )]
    pub profile: Account<'info, PlayerProfile>,

    #[account(
        init,
        payer = player,
        space = RoomPresence::DISCRIMINATOR.len() + RoomPresence::INIT_SPACE,
        seeds = [
            RoomPresence::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[GlobalAccount::START_X as u8],
            &[GlobalAccount::START_Y as u8],
            player.key().as_ref()
        ],
        bump
    )]
    pub room_presence: Account<'info, RoomPresence>,

    pub system_program: Program<'info, System>,
}

pub fn init_player_handler(ctx: Context<InitPlayer>) -> Result<()> {
    let player_account = &mut ctx.accounts.player_account;
    let profile = &mut ctx.accounts.profile;
    let room_presence = &mut ctx.accounts.room_presence;
    let global = &ctx.accounts.global;
    let player_key = ctx.accounts.player.key();

    player_account.owner = player_key;
    player_account.current_room_x = GlobalAccount::START_X;
    player_account.current_room_y = GlobalAccount::START_Y;
    player_account.active_jobs = Vec::new();
    player_account.jobs_completed = 0;
    player_account.chests_looted = 0;
    player_account.equipped_item_id = 0;
    player_account.season_seed = global.season_seed;
    player_account.bump = ctx.bumps.player_account;

    profile.owner = player_key;
    profile.skin_id = PlayerProfile::DEFAULT_SKIN_ID;
    profile.display_name = String::new();
    profile.starter_pickaxe_granted = false;
    profile.bump = ctx.bumps.profile;

    room_presence.player = player_key;
    room_presence.season_seed = global.season_seed;
    room_presence.room_x = GlobalAccount::START_X;
    room_presence.room_y = GlobalAccount::START_Y;
    room_presence.skin_id = profile.skin_id;
    room_presence.equipped_item_id = 0;
    room_presence.set_idle();
    room_presence.is_current = true;
    room_presence.bump = ctx.bumps.room_presence;

    emit!(PlayerMoved {
        player: ctx.accounts.player.key(),
        from_x: 0,
        from_y: 0,
        to_x: GlobalAccount::START_X,
        to_y: GlobalAccount::START_Y,
    });

    Ok(())
}

fn upsert_presence(
    presence: &mut Account<RoomPresence>,
    player: Pubkey,
    season_seed: u64,
    room_x: i8,
    room_y: i8,
    skin_id: u16,
    equipped_item_id: u16,
    bump: u8,
) {
    if presence.player == Pubkey::default() {
        presence.player = player;
        presence.season_seed = season_seed;
        presence.room_x = room_x;
        presence.room_y = room_y;
        presence.bump = bump;
    }

    presence.skin_id = skin_id;
    presence.equipped_item_id = equipped_item_id;
}
