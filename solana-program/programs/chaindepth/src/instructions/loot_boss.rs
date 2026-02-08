use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::{item_types, BossLooted};
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    item_ids, session_instruction_bits, BossFightAccount, GlobalAccount, InventoryAccount,
    PlayerAccount, RoomAccount, RoomPresence, SessionAuthority, MAX_LOOTERS, CENTER_BOSS,
};

#[derive(Accounts)]
pub struct LootBoss<'info> {
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
        bump = player_account.bump
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
        seeds = [BossFightAccount::SEED_PREFIX, room.key().as_ref(), player.key().as_ref()],
        bump = boss_fight.bump
    )]
    pub boss_fight: Account<'info, BossFightAccount>,

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
            SessionAuthority::SEED_PREFIX,
            player.key().as_ref(),
            authority.key().as_ref()
        ],
        bump = session_authority.bump
    )]
    pub session_authority: Option<Account<'info, SessionAuthority>>,

    pub system_program: Program<'info, System>,
}

pub fn handler(ctx: Context<LootBoss>) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::LOOT_BOSS,
        0,
    )?;

    let room = &mut ctx.accounts.room;
    let player_account = &mut ctx.accounts.player_account;
    let inventory = &mut ctx.accounts.inventory;
    let player_key = ctx.accounts.player.key();
    let clock = Clock::get()?;

    require!(room.center_type == CENTER_BOSS, ChainDepthError::NoBoss);
    require!(room.boss_defeated, ChainDepthError::BossNotDefeated);
    require!(
        player_account.is_at_room(room.x, room.y),
        ChainDepthError::NotInRoom
    );
    require!(!room.has_looted(&player_key), ChainDepthError::AlreadyLooted);
    require!(
        room.looted_by.len() < MAX_LOOTERS,
        ChainDepthError::MaxLootersReached
    );

    room.looted_by.push(player_key);
    player_account.chests_looted += 1;
    ctx.accounts.room_presence.set_idle();

    let loot_hash = generate_loot_hash(clock.slot, &player_key, room.center_id);
    let (item_type, item_amount) = calculate_boss_loot(loot_hash);
    let item_id = map_item_type_to_item_id(item_type);
    let durability = item_durability(item_type);

    if inventory.owner == Pubkey::default() {
        inventory.owner = player_key;
        inventory.items = Vec::new();
        inventory.bump = ctx.bumps.inventory;
    }
    inventory.add_item(item_id, u32::from(item_amount), durability)?;

    emit!(BossLooted {
        room_x: room.x,
        room_y: room.y,
        player: player_key,
        item_type,
        item_amount,
    });

    Ok(())
}

fn generate_loot_hash(slot: u64, player: &Pubkey, boss_id: u16) -> u64 {
    let player_bytes = player.to_bytes();
    let mut hash = slot
        .wrapping_mul(31)
        .wrapping_add(u64::from(boss_id));
    for chunk in player_bytes.chunks(8) {
        let mut bytes = [0u8; 8];
        bytes[..chunk.len()].copy_from_slice(chunk);
        let value = u64::from_le_bytes(bytes);
        hash = hash.wrapping_mul(31).wrapping_add(value);
    }
    hash
}

fn calculate_boss_loot(hash: u64) -> (u8, u8) {
    let type_roll = hash % 100;
    let item_type = if type_roll < 35 {
        item_types::ORE
    } else if type_roll < 75 {
        item_types::TOOL
    } else {
        item_types::BUFF
    };

    let amount_hash = (hash >> 32) as u8;
    let item_amount = match item_type {
        item_types::ORE => (amount_hash % 8) + 3,
        item_types::TOOL => 1,
        item_types::BUFF => (amount_hash % 5) + 2,
        _ => 1,
    };

    (item_type, item_amount)
}

fn map_item_type_to_item_id(item_type: u8) -> u16 {
    match item_type {
        item_types::ORE => item_ids::ORE,
        item_types::TOOL => item_ids::TOOL,
        item_types::BUFF => item_ids::BUFF,
        _ => item_ids::ORE,
    }
}

fn item_durability(item_type: u8) -> u16 {
    match item_type {
        item_types::TOOL => 100,
        _ => 0,
    }
}
