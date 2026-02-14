use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::{item_types, ChestLooted};
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    item_ids, session_instruction_bits, GlobalAccount, InventoryAccount, LootReceipt,
    PlayerAccount, RoomAccount, SessionAuthority, CENTER_CHEST,
};

#[derive(Accounts)]
pub struct LootChest<'info> {
    #[account(mut)]
    pub authority: Signer<'info>,

    /// CHECK: wallet owner whose gameplay state is being modified
    pub player: UncheckedAccount<'info>,

    /// Global game state - also treasury for reimbursement
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

    /// Room with the chest
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
        space = InventoryAccount::DISCRIMINATOR.len() + InventoryAccount::INIT_SPACE,
        seeds = [InventoryAccount::SEED_PREFIX, player.key().as_ref()],
        bump
    )]
    pub inventory: Account<'info, InventoryAccount>,

    /// Per-player loot receipt - existence proves player already looted
    #[account(
        init_if_needed,
        payer = authority,
        space = LootReceipt::DISCRIMINATOR.len() + LootReceipt::INIT_SPACE,
        seeds = [
            LootReceipt::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[player_account.current_room_x as u8],
            &[player_account.current_room_y as u8],
            player.key().as_ref()
        ],
        bump
    )]
    pub loot_receipt: Account<'info, LootReceipt>,

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

pub fn handler(ctx: Context<LootChest>) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::LOOT_CHEST,
        0,
    )?;

    let room = &mut ctx.accounts.room;
    let player_account = &mut ctx.accounts.player_account;
    let inventory = &mut ctx.accounts.inventory;
    let loot_receipt = &mut ctx.accounts.loot_receipt;
    let player_key = ctx.accounts.player.key();
    let clock = Clock::get()?;

    require!(room.center_type == CENTER_CHEST, ChainDepthError::NoChest);

    // Check player is in this room
    require!(
        player_account.is_at_room(room.x, room.y),
        ChainDepthError::NotInRoom
    );

    // Check player hasn't already looted (receipt already initialized = already looted)
    require!(
        loot_receipt.player == Pubkey::default(),
        ChainDepthError::AlreadyLooted
    );

    // Initialize the loot receipt
    loot_receipt.player = player_key;
    loot_receipt.season_seed = ctx.accounts.global.season_seed;
    loot_receipt.room_x = room.x;
    loot_receipt.room_y = room.y;
    loot_receipt.bump = ctx.bumps.loot_receipt;

    // Update room looted count and player stats
    room.looted_count += 1;
    player_account.chests_looted += 1;

    // Generate deterministic loot based on slot + player pubkey
    let loot_hash = generate_loot_hash(clock.slot, &player_key);
    let (item_type, item_amount) = calculate_loot(loot_hash);
    let item_id = map_item_type_to_item_id(item_type, loot_hash);
    let durability = item_durability(item_type, item_id);

    if inventory.owner == Pubkey::default() {
        inventory.owner = player_key;
        inventory.items = Vec::new();
        inventory.bump = ctx.bumps.inventory;
    }
    inventory.add_item(item_id, u32::from(item_amount), durability)?;

    if room.forced_key_drop {
        inventory.add_item(item_ids::SKELETON_KEY, 1, 0)?;
    }

    emit!(ChestLooted {
        room_x: room.x,
        room_y: room.y,
        player: player_key,
        item_type,
        item_amount,
    });

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

fn map_item_type_to_item_id(item_type: u8, hash: u64) -> u16 {
    let picker = ((hash >> 16) & 0xFFFF) as usize;
    match item_type {
        item_types::TOOL => {
            // Chest drops: common weapons only (no diamond sword from chests)
            const TOOLS: [u16; 7] = [
                item_ids::BRONZE_PICKAXE,
                item_ids::IRON_PICKAXE,
                item_ids::BRONZE_SWORD,
                item_ids::IRON_SWORD,
                item_ids::WOODEN_PIPE,
                item_ids::IRON_SCIMITAR,
                item_ids::WOODEN_TANKARD,
            ];
            TOOLS[picker % TOOLS.len()]
        }
        item_types::ORE => {
            // Chest drops: common and mid-tier valuables
            const VALUABLES: [u16; 14] = [
                item_ids::SILVER_COIN,
                item_ids::SILVER_COIN,  // weighted: more common
                item_ids::GOLD_COIN,
                item_ids::GOLD_COIN,    // weighted: more common
                item_ids::GOLD_BAR,
                item_ids::RUBY,
                item_ids::SAPPHIRE,
                item_ids::EMERALD,
                item_ids::GOBLIN_TOOTH,
                item_ids::DUSTY_TOME,
                item_ids::SKELETON_KEY,
                item_ids::RUSTED_COMPASS,
                item_ids::DWARF_BEARD_RING,
                item_ids::ENCHANTED_SCROLL,
            ];
            VALUABLES[picker % VALUABLES.len()]
        }
        item_types::BUFF => {
            const BUFFS: [u16; 2] = [
                item_ids::MINOR_BUFF,
                item_ids::MAJOR_BUFF,
            ];
            BUFFS[picker % BUFFS.len()]
        }
        _ => item_ids::SILVER_COIN,
    }
}

fn item_durability(item_type: u8, item_id: u16) -> u16 {
    if item_type == item_types::TOOL {
        match item_id {
            // Bronze tier
            item_ids::BRONZE_PICKAXE | item_ids::BRONZE_SWORD => 80,
            // Iron tier
            item_ids::IRON_PICKAXE | item_ids::IRON_SWORD | item_ids::IRON_SCIMITAR => 120,
            // Diamond tier
            item_ids::DIAMOND_SWORD => 200,
            // Fun / novelty weapons
            item_ids::NOKIA_3310 => 9999,
            item_ids::WOODEN_PIPE | item_ids::WOODEN_TANKARD => 60,
            _ => 100,
        }
    } else {
        0
    }
}
