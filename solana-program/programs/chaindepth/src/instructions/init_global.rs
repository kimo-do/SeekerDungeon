use anchor_lang::prelude::*;
use anchor_spl::token::{self, Mint, Token, TokenAccount, Transfer};

use crate::events::GlobalInitialized;
use crate::state::{GlobalAccount, RoomAccount, WALL_RUBBLE};

#[derive(Accounts)]
#[instruction(initial_prize_pool_amount: u64, season_seed: u64)]
pub struct InitGlobal<'info> {
    #[account(mut)]
    pub admin: Signer<'info>,

    #[account(
        init,
        payer = admin,
        space = 8 + GlobalAccount::INIT_SPACE,
        seeds = [GlobalAccount::SEED_PREFIX],
        bump
    )]
    pub global: Account<'info, GlobalAccount>,

    /// The SKR token mint (or mock SPL token on devnet)
    pub skr_mint: Account<'info, Mint>,

    /// Prize pool token account (ATA for global PDA)
    #[account(
        init,
        payer = admin,
        token::mint = skr_mint,
        token::authority = global,
        seeds = [b"prize_pool", global.key().as_ref()],
        bump
    )]
    pub prize_pool: Account<'info, TokenAccount>,

    /// Admin's SKR token account to fund prize pool
    #[account(
        mut,
        constraint = admin_token_account.mint == skr_mint.key(),
        constraint = admin_token_account.owner == admin.key()
    )]
    pub admin_token_account: Account<'info, TokenAccount>,

    /// Starting room at (5, 5) - uses season_seed passed as instruction arg
    #[account(
        init,
        payer = admin,
        space = 8 + RoomAccount::INIT_SPACE,
        seeds = [
            RoomAccount::SEED_PREFIX,
            &season_seed.to_le_bytes(),
            &[GlobalAccount::START_X as u8],
            &[GlobalAccount::START_Y as u8]
        ],
        bump
    )]
    pub start_room: Account<'info, RoomAccount>,

    pub token_program: Program<'info, Token>,
    pub system_program: Program<'info, System>,
}

pub fn handler(ctx: Context<InitGlobal>, initial_prize_pool_amount: u64, season_seed: u64) -> Result<()> {
    let clock = Clock::get()?;
    
    // Initialize global state
    let global = &mut ctx.accounts.global;
    global.season_seed = season_seed;
    global.depth = 0;
    global.skr_mint = ctx.accounts.skr_mint.key();
    global.prize_pool = ctx.accounts.prize_pool.key();
    global.admin = ctx.accounts.admin.key();
    global.end_slot = clock.slot + GlobalAccount::SEASON_DURATION_SLOTS;
    global.jobs_completed = 0;
    global.bump = ctx.bumps.global;

    // Initialize starting room with open center and rubble walls
    let start_room = &mut ctx.accounts.start_room;
    start_room.x = GlobalAccount::START_X;
    start_room.y = GlobalAccount::START_Y;
    start_room.season_seed = season_seed;
    
    // Starting room: all 4 walls are rubble (can be cleared)
    // This allows players to expand in any direction
    start_room.walls = [WALL_RUBBLE; 4];
    
    // Initialize helpers arrays
    start_room.helpers = [[Pubkey::default(); 4]; 4];
    start_room.helper_counts = [0; 4];
    start_room.progress = [0; 4];
    start_room.start_slot = [0; 4];
    start_room.base_slots = [RoomAccount::calculate_base_slots(0); 4];
    start_room.staked_amount = [0; 4];
    
    // Starting room has a chest (welcome gift)
    start_room.has_chest = true;
    start_room.looted_by = Vec::new();
    start_room.bump = ctx.bumps.start_room;

    // Transfer initial prize pool from admin
    if initial_prize_pool_amount > 0 {
        let transfer_ctx = CpiContext::new(
            ctx.accounts.token_program.to_account_info(),
            Transfer {
                from: ctx.accounts.admin_token_account.to_account_info(),
                to: ctx.accounts.prize_pool.to_account_info(),
                authority: ctx.accounts.admin.to_account_info(),
            },
        );
        token::transfer(transfer_ctx, initial_prize_pool_amount)?;
    }

    emit!(GlobalInitialized {
        season_seed,
        admin: ctx.accounts.admin.key(),
        skr_mint: ctx.accounts.skr_mint.key(),
        end_slot: global.end_slot,
    });

    Ok(())
}
