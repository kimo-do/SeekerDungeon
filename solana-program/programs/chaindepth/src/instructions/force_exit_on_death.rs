use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::DungeonDeathExited;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    is_scored_loot_item, session_instruction_bits, GlobalAccount, InventoryAccount, PlayerAccount,
    RoomAccount, RoomPresence, SessionAuthority,
};

#[derive(Accounts)]
pub struct ForceExitOnDeath<'info> {
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

pub fn handler(ctx: Context<ForceExitOnDeath>) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::FORCE_EXIT_ON_DEATH,
        0,
    )?;

    let room = &ctx.accounts.room;
    let player = &mut ctx.accounts.player_account;
    let player_key = ctx.accounts.player.key();

    require!(
        player.is_at_room(room.x, room.y),
        ChainDepthError::NotInRoom
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

    let now_slot = Clock::get()?.slot;
    let outcome = apply_death_outcome(player, inventory, &mut ctx.accounts.room_presence, now_slot)?;

    emit!(DungeonDeathExited {
        player: player_key,
        lost_item_stacks: outcome.lost_item_stacks,
        lost_item_units: outcome.lost_item_units,
        run_score: outcome.run_score,
        total_score: player.total_score,
        run_duration_slots: outcome.run_duration_slots,
    });

    Ok(())
}

#[derive(Clone, Copy)]
pub struct DeathOutcome {
    pub lost_item_stacks: u32,
    pub lost_item_units: u32,
    pub run_duration_slots: u64,
    pub run_score: u64,
}

pub fn apply_death_outcome(
    player: &mut Account<PlayerAccount>,
    inventory: &mut Account<InventoryAccount>,
    room_presence: &mut Account<RoomPresence>,
    now_slot: u64,
) -> Result<DeathOutcome> {
    let mut lost_item_stacks = 0u32;
    let mut lost_item_units = 0u32;
    let mut kept_items = Vec::with_capacity(inventory.items.len());
    for item in inventory.items.iter() {
        if is_scored_loot_item(item.item_id) {
            lost_item_stacks = lost_item_stacks
                .checked_add(1)
                .ok_or(ChainDepthError::Overflow)?;
            lost_item_units = lost_item_units
                .checked_add(item.amount)
                .ok_or(ChainDepthError::Overflow)?;
        } else {
            kept_items.push(item.clone());
        }
    }
    inventory.items = kept_items;

    let run_start_slot = if player.current_run_start_slot == 0 {
        now_slot
    } else {
        player.current_run_start_slot
    };
    let run_duration_slots = now_slot.saturating_sub(run_start_slot);
    player.last_extraction_slot = now_slot;
    player.current_run_start_slot = now_slot;
    player.in_dungeon = false;
    if player.max_hp == 0 {
        player.max_hp = crate::state::DEFAULT_PLAYER_MAX_HP;
    }
    player.current_hp = player.max_hp;

    room_presence.is_current = true;
    room_presence.set_idle();

    Ok(DeathOutcome {
        lost_item_stacks,
        lost_item_units,
        run_duration_slots,
        run_score: 0,
    })
}
