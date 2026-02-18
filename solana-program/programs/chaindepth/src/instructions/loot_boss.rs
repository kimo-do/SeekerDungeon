use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::{item_types, BossLooted};
use crate::instructions::join_boss_fight::apply_boss_damage;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    item_ids, session_instruction_bits, BossFightAccount, GlobalAccount, InventoryAccount,
    LootReceipt, PlayerAccount, RoomAccount, RoomPresence, SessionAuthority, CENTER_BOSS,
};

#[derive(Accounts)]
pub struct LootBoss<'info> {
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

    /// Per-player loot receipt for boss - existence proves player already looted
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

    let loot_receipt = &mut ctx.accounts.loot_receipt;

    require!(room.center_type == CENTER_BOSS, ChainDepthError::NoBoss);
    apply_boss_damage(room, clock.slot)?;
    require!(room.boss_defeated, ChainDepthError::BossNotDefeated);
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
    ctx.accounts.room_presence.set_idle();

    let loot_hash = generate_loot_hash(clock.slot, &player_key, room.center_id);
    let loot_bundle = build_boss_loot_bundle(loot_hash);

    if inventory.owner == Pubkey::default() {
        inventory.owner = player_key;
        inventory.items = Vec::new();
        inventory.bump = ctx.bumps.inventory;
    }

    let mut event_item_type = item_types::TOOL;
    let mut event_item_amount_total = 0u32;
    for stack in loot_bundle.iter() {
        inventory.add_item(stack.item_id, stack.amount, stack.durability)?;
        event_item_amount_total = event_item_amount_total.saturating_add(stack.amount);
        event_item_type = stack.item_type;
    }

    emit!(BossLooted {
        room_x: room.x,
        room_y: room.y,
        player: player_key,
        item_type: event_item_type,
        item_amount: event_item_amount_total.min(u32::from(u8::MAX)) as u8,
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

#[derive(Clone, Copy)]
struct LootSpec {
    item_id: u16,
    weight: u16,
    min_amount: u8,
    max_amount: u8,
}

#[derive(Clone, Copy)]
struct LootStack {
    item_id: u16,
    amount: u32,
    durability: u16,
    item_type: u8,
}

struct LootRng {
    state: u64,
}

impl LootRng {
    fn new(seed: u64) -> Self {
        Self { state: seed ^ 0x9E37_79B9_7F4A_7C15 }
    }

    fn next_u64(&mut self) -> u64 {
        self.state = self.state.wrapping_add(0x9E37_79B9_7F4A_7C15);
        let mut z = self.state;
        z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
        z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
        z ^ (z >> 31)
    }

    fn range_u32(&mut self, upper_exclusive: u32) -> u32 {
        if upper_exclusive <= 1 {
            return 0;
        }
        (self.next_u64() % u64::from(upper_exclusive)) as u32
    }
}

const BOSS_WEAPONS: [LootSpec; 9] = [
    LootSpec { item_id: item_ids::IRON_PICKAXE, weight: 12, min_amount: 1, max_amount: 1 },
    LootSpec { item_id: item_ids::IRON_SWORD, weight: 13, min_amount: 1, max_amount: 1 },
    LootSpec { item_id: item_ids::DIAMOND_SWORD, weight: 7, min_amount: 1, max_amount: 1 },
    LootSpec { item_id: item_ids::NOKIA_3310, weight: 4, min_amount: 1, max_amount: 1 },
    LootSpec { item_id: item_ids::IRON_SCIMITAR, weight: 10, min_amount: 1, max_amount: 1 },
    LootSpec { item_id: item_ids::BRONZE_SWORD, weight: 11, min_amount: 1, max_amount: 1 },
    LootSpec { item_id: item_ids::BRONZE_PICKAXE, weight: 10, min_amount: 1, max_amount: 1 },
    LootSpec { item_id: item_ids::WOODEN_PIPE, weight: 8, min_amount: 1, max_amount: 1 },
    LootSpec { item_id: item_ids::WOODEN_TANKARD, weight: 9, min_amount: 1, max_amount: 1 },
];

const BOSS_VALUABLES: [LootSpec; 15] = [
    LootSpec { item_id: item_ids::GOLD_COIN, weight: 19, min_amount: 6, max_amount: 18 },
    LootSpec { item_id: item_ids::GOLD_BAR, weight: 14, min_amount: 1, max_amount: 3 },
    LootSpec { item_id: item_ids::DIAMOND, weight: 7, min_amount: 1, max_amount: 2 },
    LootSpec { item_id: item_ids::RUBY, weight: 8, min_amount: 1, max_amount: 2 },
    LootSpec { item_id: item_ids::SAPPHIRE, weight: 8, min_amount: 1, max_amount: 2 },
    LootSpec { item_id: item_ids::EMERALD, weight: 8, min_amount: 1, max_amount: 2 },
    LootSpec { item_id: item_ids::ANCIENT_CROWN, weight: 4, min_amount: 1, max_amount: 1 },
    LootSpec { item_id: item_ids::DRAGON_SCALE, weight: 5, min_amount: 1, max_amount: 2 },
    LootSpec { item_id: item_ids::CURSED_AMULET, weight: 4, min_amount: 1, max_amount: 1 },
    LootSpec { item_id: item_ids::GOLDEN_CHALICE, weight: 5, min_amount: 1, max_amount: 1 },
    LootSpec { item_id: item_ids::MYSTIC_ORB, weight: 3, min_amount: 1, max_amount: 1 },
    LootSpec { item_id: item_ids::PHOENIX_FEATHER, weight: 3, min_amount: 1, max_amount: 1 },
    LootSpec { item_id: item_ids::VOID_SHARD, weight: 3, min_amount: 1, max_amount: 1 },
    LootSpec { item_id: item_ids::ENCHANTED_SCROLL, weight: 4, min_amount: 1, max_amount: 2 },
    LootSpec { item_id: item_ids::SKELETON_KEY, weight: 3, min_amount: 1, max_amount: 1 },
];

const BOSS_BUFFS: [LootSpec; 2] = [
    LootSpec { item_id: item_ids::MINOR_BUFF, weight: 8, min_amount: 1, max_amount: 3 },
    LootSpec { item_id: item_ids::MAJOR_BUFF, weight: 14, min_amount: 1, max_amount: 3 },
];

fn build_boss_loot_bundle(seed: u64) -> Vec<LootStack> {
    let mut rng = LootRng::new(seed);
    let mut drops = Vec::<LootStack>::new();

    // Boss always drops a weapon.
    append_single_roll(&mut drops, &BOSS_WEAPONS, item_types::TOOL, &mut rng);

    // Boss drops 2-4 different valuable stacks.
    let valuable_roll = rng.range_u32(100);
    let valuable_stacks = if valuable_roll < 50 {
        2
    } else if valuable_roll < 85 {
        3
    } else {
        4
    };
    append_unique_rolls(
        &mut drops,
        &BOSS_VALUABLES,
        valuable_stacks,
        item_types::ORE,
        &mut rng,
    );

    // Optional bonus buff stack.
    if rng.range_u32(100) < 60 {
        append_single_roll(&mut drops, &BOSS_BUFFS, item_types::BUFF, &mut rng);
    }

    drops
}

fn append_single_roll(
    drops: &mut Vec<LootStack>,
    pool: &[LootSpec],
    item_type: u8,
    rng: &mut LootRng,
) {
    if pool.is_empty() {
        return;
    }

    let index = draw_weighted_index(pool, rng, None);
    let spec = pool[index];
    drops.push(LootStack {
        item_id: spec.item_id,
        amount: roll_amount(spec, rng),
        durability: item_durability(item_type, spec.item_id),
        item_type,
    });
}

fn append_unique_rolls(
    drops: &mut Vec<LootStack>,
    pool: &[LootSpec],
    count: usize,
    item_type: u8,
    rng: &mut LootRng,
) {
    if pool.is_empty() || count == 0 {
        return;
    }

    let draw_count = count.min(pool.len());
    let mut picked = vec![false; pool.len()];

    for _ in 0..draw_count {
        let index = draw_weighted_index(pool, rng, Some(&picked));
        picked[index] = true;
        let spec = pool[index];
        drops.push(LootStack {
            item_id: spec.item_id,
            amount: roll_amount(spec, rng),
            durability: item_durability(item_type, spec.item_id),
            item_type,
        });
    }
}

fn draw_weighted_index(pool: &[LootSpec], rng: &mut LootRng, exclude: Option<&[bool]>) -> usize {
    let mut total_weight = 0u32;
    for (index, spec) in pool.iter().enumerate() {
        if exclude.is_some_and(|flags| flags[index]) {
            continue;
        }
        total_weight = total_weight.saturating_add(u32::from(spec.weight));
    }

    if total_weight == 0 {
        return 0;
    }

    let mut roll = rng.range_u32(total_weight);
    for (index, spec) in pool.iter().enumerate() {
        if exclude.is_some_and(|flags| flags[index]) {
            continue;
        }

        let weight = u32::from(spec.weight);
        if roll < weight {
            return index;
        }
        roll -= weight;
    }

    0
}

fn roll_amount(spec: LootSpec, rng: &mut LootRng) -> u32 {
    let min = u32::from(spec.min_amount);
    let max = u32::from(spec.max_amount.max(spec.min_amount));
    if max == min {
        return min;
    }

    min + rng.range_u32(max - min + 1)
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
