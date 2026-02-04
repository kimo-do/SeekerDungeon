use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::{item_types, ChestLooted};
use crate::state::{GlobalAccount, PlayerAccount, RoomAccount, MAX_LOOTERS};

#[derive(Accounts)]
pub struct LootChest<'info> {
    #[account(mut)]
    pub player: Signer<'info>,

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

    /// Room with the chest
    #[account(
        mut,
        seeds = [
            RoomAccount::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[player_account.current_room_x as u8],
            &[player_account.current_room_y as u8]
        ],
        bump = room.bump
    )]
    pub room: Account<'info, RoomAccount>,
}

pub fn handler(ctx: Context<LootChest>) -> Result<()> {
    let room = &mut ctx.accounts.room;
    let player_account = &mut ctx.accounts.player_account;
    let player_key = ctx.accounts.player.key();
    let clock = Clock::get()?;

    // Check room has a chest
    require!(room.has_chest, ChainDepthError::NoChest);

    // Check player is in this room
    require!(
        player_account.is_at_room(room.x, room.y),
        ChainDepthError::NotInRoom
    );

    // Check player hasn't already looted
    require!(!room.has_looted(&player_key), ChainDepthError::AlreadyLooted);

    // Check we haven't hit max looters
    require!(
        room.looted_by.len() < MAX_LOOTERS,
        ChainDepthError::AlreadyLooted
    );

    // Add player to looted list
    room.looted_by.push(player_key);
    player_account.chests_looted += 1;

    // Generate deterministic loot based on slot + player pubkey
    let loot_hash = generate_loot_hash(clock.slot, &player_key);
    let (item_type, item_amount) = calculate_loot(loot_hash);

    emit!(ChestLooted {
        room_x: room.x,
        room_y: room.y,
        player: player_key,
        item_type,
        item_amount,
    });

    // Note: Actual item tracking would be done client-side in Unity
    // The event contains all the information Unity needs to update inventory
    // Items are deterministic based on hash, so Unity can verify

    Ok(())
}

/// Generate deterministic hash for loot
fn generate_loot_hash(slot: u64, player: &Pubkey) -> u64 {
    let player_bytes = player.to_bytes();
    let mut hash = slot;
    
    // Mix in player pubkey bytes
    for chunk in player_bytes.chunks(8) {
        let mut bytes = [0u8; 8];
        bytes[..chunk.len()].copy_from_slice(chunk);
        let val = u64::from_le_bytes(bytes);
        hash = hash.wrapping_mul(31).wrapping_add(val);
    }
    
    hash
}

/// Calculate loot item type and amount from hash
fn calculate_loot(hash: u64) -> (u8, u8) {
    // Item type: 0=Ore (60%), 1=Tool (25%), 2=Buff (15%)
    let type_roll = hash % 100;
    let item_type = if type_roll < 60 {
        item_types::ORE
    } else if type_roll < 85 {
        item_types::TOOL
    } else {
        item_types::BUFF
    };

    // Amount varies by type
    let amount_hash = (hash >> 32) as u8;
    let item_amount = match item_type {
        item_types::ORE => (amount_hash % 5) + 1,    // 1-5 ore
        item_types::TOOL => 1,                        // Always 1 tool
        item_types::BUFF => (amount_hash % 3) + 1,   // 1-3 buffs
        _ => 1,
    };

    (item_type, item_amount)
}
