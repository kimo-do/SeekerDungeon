use anchor_lang::prelude::*;
use anchor_spl::token::{self, Token, TokenAccount, Transfer};

use crate::errors::ChainDepthError;
use crate::events::JobCompleted;
use crate::state::{GlobalAccount, PlayerAccount, RoomAccount, WALL_OPEN, WALL_RUBBLE};

#[derive(Accounts)]
#[instruction(direction: u8)]
pub struct CompleteJob<'info> {
    #[account(mut)]
    pub player: Signer<'info>,

    #[account(
        mut,
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

    /// Room with the completed job
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

    /// Adjacent room that will be opened/initialized
    /// This needs to be init_if_needed for newly discovered rooms
    #[account(
        init_if_needed,
        payer = player,
        space = 8 + RoomAccount::INIT_SPACE,
        seeds = [
            RoomAccount::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[adjacent_x(room.x, direction) as u8],
            &[adjacent_y(room.y, direction) as u8]
        ],
        bump
    )]
    pub adjacent_room: Account<'info, RoomAccount>,

    /// Escrow holding staked SKR
    #[account(
        mut,
        seeds = [b"escrow", room.key().as_ref(), &[direction]],
        bump
    )]
    pub escrow: Account<'info, TokenAccount>,

    /// Prize pool for bonus rewards
    #[account(
        mut,
        constraint = prize_pool.key() == global.prize_pool
    )]
    pub prize_pool: Account<'info, TokenAccount>,

    /// Player's token account for refund
    #[account(
        mut,
        constraint = player_token_account.mint == global.skr_mint,
        constraint = player_token_account.owner == player.key()
    )]
    pub player_token_account: Account<'info, TokenAccount>,

    pub token_program: Program<'info, Token>,
    pub system_program: Program<'info, System>,
}

/// Helper to calculate adjacent X coordinate
fn adjacent_x(x: i8, direction: u8) -> i8 {
    match direction {
        2 => x + 1, // East
        3 => x - 1, // West
        _ => x,
    }
}

/// Helper to calculate adjacent Y coordinate
fn adjacent_y(y: i8, direction: u8) -> i8 {
    match direction {
        0 => y + 1, // North
        1 => y - 1, // South
        _ => y,
    }
}

pub fn handler(ctx: Context<CompleteJob>, direction: u8) -> Result<()> {
    // Validate direction
    require!(
        RoomAccount::is_valid_direction(direction),
        ChainDepthError::InvalidDirection
    );

    let dir_idx = direction as usize;

    // Get account info references early to avoid borrow conflicts
    let global_account_info = ctx.accounts.global.to_account_info();
    let room_key = ctx.accounts.room.key();

    // Read-only checks first
    {
        let room = &ctx.accounts.room;
        
        // Check wall is rubble
        require!(room.is_rubble(direction), ChainDepthError::NotRubble);

        // Check job has helpers
        require!(
            room.helper_counts[dir_idx] > 0,
            ChainDepthError::NoActiveJob
        );

        // Check caller is a helper
        require!(
            room.is_helper(direction, &ctx.accounts.player.key()),
            ChainDepthError::NotHelper
        );

        // Check progress is sufficient
        require!(
            room.progress[dir_idx] >= room.base_slots[dir_idx],
            ChainDepthError::JobNotReady
        );
    }

    // Extract values before mutable borrows
    let helper_count = ctx.accounts.room.helper_counts[dir_idx] as u64;
    let staked_total = ctx.accounts.room.staked_amount[dir_idx];
    let room_x = ctx.accounts.room.x;
    let room_y = ctx.accounts.room.y;
    let global_bump = ctx.accounts.global.bump;
    let season_seed = ctx.accounts.global.season_seed;
    let current_depth = ctx.accounts.global.depth;

    // Mutable operations on room
    let room = &mut ctx.accounts.room;
    room.walls[dir_idx] = WALL_OPEN;

    // Initialize adjacent room if new
    let adjacent = &mut ctx.accounts.adjacent_room;
    let opposite_dir = RoomAccount::opposite_direction(direction);
    
    if adjacent.season_seed == 0 {
        // New room - initialize it
        adjacent.x = adjacent_x(room_x, direction);
        adjacent.y = adjacent_y(room_y, direction);
        adjacent.season_seed = season_seed;
        
        // Generate walls procedurally based on seed + coords
        let room_hash = generate_room_hash(season_seed, adjacent.x, adjacent.y);
        adjacent.walls = generate_walls(room_hash, opposite_dir);
        
        adjacent.helpers = [[Pubkey::default(); 4]; 4];
        adjacent.helper_counts = [0; 4];
        adjacent.progress = [0; 4];
        adjacent.start_slot = [0; 4];
        adjacent.base_slots = [RoomAccount::calculate_base_slots(current_depth + 1); 4];
        adjacent.staked_amount = [0; 4];
        
        // 30% chance of chest based on hash
        adjacent.has_chest = (room_hash % 10) < 3;
        adjacent.looted_by = Vec::new();
        adjacent.bump = ctx.bumps.adjacent_room;
    }
    
    // Set opposite wall to open (connection back)
    adjacent.walls[opposite_dir as usize] = WALL_OPEN;

    // Update global depth if this room is deeper
    let new_depth = calculate_depth(adjacent.x, adjacent.y);
    let global = &mut ctx.accounts.global;
    if new_depth > global.depth {
        global.depth = new_depth;
    }
    global.jobs_completed += 1;
    let final_depth = global.depth;
    let jobs_completed = global.jobs_completed;

    // Remove active job from player
    let player_account = &mut ctx.accounts.player_account;
    player_account.remove_job(room_x, room_y, direction);
    player_account.jobs_completed += 1;

    // Calculate rewards
    let per_helper_refund = staked_total / helper_count;
    let bonus_per_helper = calculate_bonus(jobs_completed, helper_count);

    // Transfer refund from escrow to player
    let escrow_seeds = &[
        b"escrow".as_ref(),
        room_key.as_ref(),
        &[direction],
        &[ctx.bumps.escrow],
    ];
    let escrow_signer = &[&escrow_seeds[..]];

    // Refund stake
    let refund_ctx = CpiContext::new_with_signer(
        ctx.accounts.token_program.to_account_info(),
        Transfer {
            from: ctx.accounts.escrow.to_account_info(),
            to: ctx.accounts.player_token_account.to_account_info(),
            authority: ctx.accounts.escrow.to_account_info(),
        },
        escrow_signer,
    );
    token::transfer(refund_ctx, per_helper_refund)?;

    // Transfer bonus from prize pool (if available)
    if ctx.accounts.prize_pool.amount >= bonus_per_helper && bonus_per_helper > 0 {
        let global_seeds = &[
            GlobalAccount::SEED_PREFIX,
            &[global_bump],
        ];
        let global_signer = &[&global_seeds[..]];

        let bonus_ctx = CpiContext::new_with_signer(
            ctx.accounts.token_program.to_account_info(),
            Transfer {
                from: ctx.accounts.prize_pool.to_account_info(),
                to: ctx.accounts.player_token_account.to_account_info(),
                authority: global_account_info,
            },
            global_signer,
        );
        token::transfer(bonus_ctx, bonus_per_helper)?;
    }

    // Reset job state
    let room = &mut ctx.accounts.room;
    room.helpers[dir_idx] = [Pubkey::default(); 4];
    room.helper_counts[dir_idx] = 0;
    room.progress[dir_idx] = 0;
    room.start_slot[dir_idx] = 0;
    room.staked_amount[dir_idx] = 0;

    emit!(JobCompleted {
        room_x,
        room_y,
        direction,
        new_depth: final_depth,
        helpers_count: helper_count as u8,
        total_reward: per_helper_refund + bonus_per_helper,
    });

    Ok(())
}

/// Generate deterministic hash for room based on seed and coordinates
fn generate_room_hash(seed: u64, x: i8, y: i8) -> u64 {
    // Simple hash combining seed with coordinates
    let mut hash = seed;
    hash = hash.wrapping_mul(31).wrapping_add(x as u64);
    hash = hash.wrapping_mul(31).wrapping_add(y as u64);
    hash
}

/// Generate wall states for a new room
fn generate_walls(hash: u64, entrance_dir: u8) -> [u8; 4] {
    let mut walls = [0u8; 4];
    
    for i in 0..4 {
        if i == entrance_dir as usize {
            // Entrance is always open (will be set after)
            walls[i] = WALL_OPEN;
        } else {
            // 60% rubble, 30% solid, 10% open (for variety)
            let wall_hash = (hash >> (i * 8)) % 10;
            walls[i] = if wall_hash < 6 {
                WALL_RUBBLE
            } else if wall_hash < 9 {
                0 // WALL_SOLID
            } else {
                WALL_OPEN
            };
        }
    }
    
    walls
}

/// Calculate room depth from center (5, 5)
fn calculate_depth(x: i8, y: i8) -> u32 {
    let dx = (x - 5).abs() as u32;
    let dy = (y - 5).abs() as u32;
    dx.max(dy)
}

/// Calculate bonus from prize pool
fn calculate_bonus(jobs_completed: u64, helper_count: u64) -> u64 {
    // Small bonus that decreases as more jobs are completed
    // Base: 0.001 SKR per helper, decreasing over time
    let base_bonus = RoomAccount::MIN_BOOST_TIP;
    base_bonus / (1 + jobs_completed / 100) / helper_count
}
