use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::PlayerMoved;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    session_instruction_bits, GlobalAccount, PlayerAccount, PlayerProfile, RoomAccount,
    RoomPresence, SessionAuthority, CENTER_BOSS, CENTER_CHEST, CENTER_EMPTY, WALL_OPEN, WALL_RUBBLE,
};

#[derive(Accounts)]
#[instruction(new_x: i8, new_y: i8)]
pub struct MovePlayer<'info> {
    #[account(mut)]
    pub authority: Signer<'info>,

    /// CHECK: wallet owner whose gameplay state is being modified
    pub player: UncheckedAccount<'info>,

    /// Global game state - also acts as the SOL treasury for room creation rent
    #[account(
        mut,
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Account<'info, GlobalAccount>,

    #[account(
        init_if_needed,
        payer = authority,
        space = 8 + PlayerAccount::INIT_SPACE,
        seeds = [PlayerAccount::SEED_PREFIX, player.key().as_ref()],
        bump
    )]
    pub player_account: Account<'info, PlayerAccount>,

    #[account(
        init_if_needed,
        payer = authority,
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

    /// Target room to move to (adjacent; initialized on first travel if needed)
    #[account(
        init_if_needed,
        payer = authority,
        space = 8 + RoomAccount::INIT_SPACE,
        seeds = [
            RoomAccount::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[new_x as u8],
            &[new_y as u8]
        ],
        bump
    )]
    pub target_room: Account<'info, RoomAccount>,

    /// Closed on move so rent returns to the treasury (global PDA)
    #[account(
        mut,
        close = global,
        seeds = [
            RoomPresence::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[player_account.current_room_x as u8],
            &[player_account.current_room_y as u8],
            player.key().as_ref()
        ],
        bump = current_presence.bump
    )]
    pub current_presence: Account<'info, RoomPresence>,

    #[account(
        init_if_needed,
        payer = authority,
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

pub fn handler(ctx: Context<MovePlayer>, new_x: i8, new_y: i8) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::MOVE_PLAYER,
        0,
    )?;

    let player_account = &mut ctx.accounts.player_account;
    let profile = &mut ctx.accounts.profile;
    let current_room = &ctx.accounts.current_room;
    let season_seed = ctx.accounts.global.season_seed;
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
        player_account.season_seed = season_seed;
        player_account.bump = ctx.bumps.player_account;
    }

    let from_x = player_account.current_room_x;
    let from_y = player_account.current_room_y;

    // current_presence will be closed by Anchor (close = global),
    // returning rent to the treasury. No need to update fields.

    // Check adjacency (only 1 step in cardinal direction)
    let dx = (new_x - from_x).abs();
    let dy = (new_y - from_y).abs();
    require!(
        (dx == 1 && dy == 0) || (dx == 0 && dy == 1),
        ChainDepthError::NotAdjacent
    );

    // Determine direction of movement
    let direction: u8 = if new_y > from_y {
        0 // North
    } else if new_y < from_y {
        1 // South
    } else if new_x > from_x {
        2 // East
    } else {
        3 // West
    };

    // Check if wall in that direction is open
    let current_wall_state = current_room.walls[direction as usize];
    msg!(
        "move_validation from=({}, {}) to=({}, {}) dir={} wall_state={}",
        from_x,
        from_y,
        new_x,
        new_y,
        direction,
        current_wall_state
    );
    require!(
        current_wall_state == WALL_OPEN,
        ChainDepthError::WallNotOpen
    );

    let opposite_direction = RoomAccount::opposite_direction(direction);
    let target_room = &mut ctx.accounts.target_room;
    let is_new_room = target_room.season_seed == 0;
    if is_new_room {
        let room_depth = calculate_depth(new_x, new_y);
        let room_hash = generate_room_hash(season_seed, new_x, new_y);

        target_room.x = new_x;
        target_room.y = new_y;
        target_room.season_seed = season_seed;
        target_room.walls = generate_walls(room_hash, opposite_direction);
        target_room.helper_counts = [0; 4];
        target_room.progress = [0; 4];
        target_room.start_slot = [0; 4];
        target_room.base_slots = [RoomAccount::calculate_base_slots(room_depth); 4];
        target_room.total_staked = [0; 4];
        target_room.job_completed = [false; 4];
        target_room.bonus_per_helper = [0; 4];

        let (center_type, center_id) = generate_room_center(season_seed, new_x, new_y, room_depth);
        let boss_max_hp = if center_type == CENTER_BOSS {
            RoomAccount::boss_hp_for_depth(room_depth, center_id)
        } else {
            0
        };

        target_room.has_chest = center_type == CENTER_CHEST;
        target_room.center_type = center_type;
        target_room.center_id = center_id;
        target_room.boss_max_hp = boss_max_hp;
        target_room.boss_current_hp = boss_max_hp;
        target_room.boss_last_update_slot = Clock::get()?.slot;
        target_room.boss_total_dps = 0;
        target_room.boss_fighter_count = 0;
        target_room.boss_defeated = false;
        target_room.looted_count = 0;
        target_room.created_by = player_key;
        target_room.created_slot = Clock::get()?.slot;
        target_room.bump = ctx.bumps.target_room;
    }
    target_room.walls[opposite_direction as usize] = WALL_OPEN;
    let return_wall_state = target_room.walls[opposite_direction as usize];
    msg!(
        "move_topology target=({}, {}) return_dir={} return_wall_state={}",
        target_room.x,
        target_room.y,
        opposite_direction,
        return_wall_state
    );
    require!(
        return_wall_state == WALL_OPEN,
        ChainDepthError::WallNotOpen
    );

    let room_depth = calculate_depth(new_x, new_y);
    if room_depth > ctx.accounts.global.depth {
        ctx.accounts.global.depth = room_depth;
    }

    // Reimburse authority for room creation rent from treasury (manual lamport transfer)
    if is_new_room {
        let room_space = 8 + RoomAccount::INIT_SPACE;
        let rent_cost = Rent::get()?.minimum_balance(room_space);
        let global_info = ctx.accounts.global.to_account_info();
        let authority_info = ctx.accounts.authority.to_account_info();
        **global_info.try_borrow_mut_lamports()? = global_info
            .lamports()
            .checked_sub(rent_cost)
            .ok_or(ChainDepthError::TreasuryInsufficientFunds)?;
        **authority_info.try_borrow_mut_lamports()? = authority_info
            .lamports()
            .checked_add(rent_cost)
            .ok_or(ChainDepthError::Overflow)?;
    }

    // Update player position
    player_account.current_room_x = new_x;
    player_account.current_room_y = new_y;

    upsert_presence(
        &mut ctx.accounts.target_presence,
        player_key,
        season_seed,
        new_x,
        new_y,
        profile.skin_id,
        player_account.equipped_item_id,
        ctx.bumps.target_presence,
    );
    ctx.accounts.target_presence.is_current = true;
    ctx.accounts.target_presence.set_idle();

    emit!(PlayerMoved {
        player: player_key,
        from_x,
        from_y,
        to_x: new_x,
        to_y: new_y,
    });

    Ok(())
}

fn generate_room_hash(seed: u64, x: i8, y: i8) -> u64 {
    let mut hash = seed;
    hash = hash.wrapping_mul(31).wrapping_add(x as u64);
    hash = hash.wrapping_mul(31).wrapping_add(y as u64);
    hash
}

fn generate_walls(hash: u64, entrance_direction: u8) -> [u8; 4] {
    let mut walls = [0u8; 4];

    for direction in 0..4 {
        if direction == entrance_direction as usize {
            walls[direction] = WALL_OPEN;
        } else {
            let wall_hash = (hash >> (direction * 8)) % 10;
            walls[direction] = if wall_hash < 6 {
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
    let dx = (x - GlobalAccount::START_X).abs() as u32;
    let dy = (y - GlobalAccount::START_Y).abs() as u32;
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
        0 => (GlobalAccount::START_X, GlobalAccount::START_Y + 1),
        1 => (GlobalAccount::START_X, GlobalAccount::START_Y - 1),
        2 => (GlobalAccount::START_X + 1, GlobalAccount::START_Y),
        _ => (GlobalAccount::START_X - 1, GlobalAccount::START_Y),
    };

    room_x == expected.0 && room_y == expected.1
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
