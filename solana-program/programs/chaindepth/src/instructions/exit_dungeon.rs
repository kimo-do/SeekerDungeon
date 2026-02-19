use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::{DungeonExitItemScored, DungeonExited};
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    compute_time_bonus, is_scored_loot_item, score_value_for_item, session_instruction_bits,
    GlobalAccount, InventoryAccount, PlayerAccount, RoomAccount, RoomPresence, SessionAuthority,
    StorageAccount, DIRECTION_SOUTH, WALL_ENTRANCE_STAIRS,
};

#[derive(Accounts)]
pub struct ExitDungeon<'info> {
    #[account(mut)]
    pub authority: Signer<'info>,

    /// CHECK: wallet owner whose gameplay state is being modified
    pub player: UncheckedAccount<'info>,

    #[account(
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Account<'info, GlobalAccount>,

    #[account(
        mut,
        seeds = [PlayerAccount::SEED_PREFIX, player.key().as_ref()],
        bump = player_account.bump,
        constraint = player_account.owner == player.key() @ ChainDepthError::Unauthorized
    )]
    pub player_account: Account<'info, PlayerAccount>,

    #[account(
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
        space = InventoryAccount::DISCRIMINATOR.len() + InventoryAccount::INIT_SPACE,
        seeds = [InventoryAccount::SEED_PREFIX, player.key().as_ref()],
        bump
    )]
    pub inventory: Account<'info, InventoryAccount>,

    #[account(
        init_if_needed,
        payer = authority,
        space = StorageAccount::DISCRIMINATOR.len() + StorageAccount::INIT_SPACE,
        seeds = [StorageAccount::SEED_PREFIX, player.key().as_ref()],
        bump
    )]
    pub storage: Account<'info, StorageAccount>,

    #[account(
        mut,
        seeds = [
            RoomPresence::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[player_account.current_room_x as u8],
            &[player_account.current_room_y as u8],
            player.key().as_ref()
        ],
        bump = room_presence.bump
    )]
    pub room_presence: Account<'info, RoomPresence>,

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

pub fn handler(ctx: Context<ExitDungeon>) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::EXIT_DUNGEON,
        0,
    )?;

    let room = &ctx.accounts.room;
    let player = &mut ctx.accounts.player_account;
    let player_key = ctx.accounts.player.key();

    require!(
        player.is_at_room(room.x, room.y),
        ChainDepthError::NotInRoom
    );
    require!(
        room.x == GlobalAccount::START_X && room.y == GlobalAccount::START_Y,
        ChainDepthError::NotAtEntranceRoom
    );
    require!(
        room.walls[DIRECTION_SOUTH as usize] == WALL_ENTRANCE_STAIRS,
        ChainDepthError::EntranceStairsRequired
    );
    require!(
        player.active_jobs.is_empty(),
        ChainDepthError::CannotExitWithActiveJobs
    );

    let inventory = &mut ctx.accounts.inventory;
    if inventory.owner == Pubkey::default() {
        inventory.owner = player_key;
        inventory.items = Vec::new();
        inventory.bump = ctx.bumps.inventory;
    }
    require!(
        inventory.owner == player_key,
        ChainDepthError::Unauthorized
    );
    let storage = &mut ctx.accounts.storage;
    if storage.owner == Pubkey::default() {
        storage.owner = player_key;
        storage.items = Vec::new();
        storage.bump = ctx.bumps.storage;
    }
    require!(storage.owner == player_key, ChainDepthError::Unauthorized);

    let mut loot_score = 0u64;
    let mut extracted_item_stacks = 0u32;
    let mut extracted_item_units = 0u32;
    let mut kept_items = Vec::with_capacity(inventory.items.len());
    for item in inventory.items.iter() {
        if is_scored_loot_item(item.item_id) {
            storage.add_item(item.item_id, item.amount, item.durability)?;

            let unit_score = score_value_for_item(item.item_id);
            let stack_score = unit_score
                .checked_mul(item.amount as u64)
                .ok_or(ChainDepthError::Overflow)?;
            loot_score = loot_score
                .checked_add(stack_score)
                .ok_or(ChainDepthError::Overflow)?;
            extracted_item_stacks = extracted_item_stacks
                .checked_add(1)
                .ok_or(ChainDepthError::Overflow)?;
            extracted_item_units = extracted_item_units
                .checked_add(item.amount)
                .ok_or(ChainDepthError::Overflow)?;

            emit!(DungeonExitItemScored {
                player: player_key,
                item_id: item.item_id,
                amount: item.amount,
                unit_score,
                stack_score,
            });
        } else {
            kept_items.push(item.clone());
        }
    }
    inventory.items = kept_items;

    let now_slot = Clock::get()?.slot;
    let run_start_slot = if player.current_run_start_slot == 0 {
        now_slot
    } else {
        player.current_run_start_slot
    };
    let run_duration_slots = now_slot.saturating_sub(run_start_slot);
    let time_score = compute_time_bonus(run_duration_slots, loot_score);
    let run_score = loot_score
        .checked_add(time_score)
        .ok_or(ChainDepthError::Overflow)?;

    player.total_score = player
        .total_score
        .checked_add(run_score)
        .ok_or(ChainDepthError::Overflow)?;
    player.runs_extracted = player
        .runs_extracted
        .checked_add(1)
        .ok_or(ChainDepthError::Overflow)?;
    player.last_extraction_slot = now_slot;
    player.current_run_start_slot = now_slot;
    player.in_dungeon = false;

    let room_presence = &mut ctx.accounts.room_presence;
    room_presence.is_current = true;
    room_presence.set_idle();

    emit!(DungeonExited {
        player: player_key,
        run_score,
        time_score,
        loot_score,
        extracted_item_stacks,
        extracted_item_units,
        total_score: player.total_score,
        run_duration_slots,
    });

    Ok(())
}
