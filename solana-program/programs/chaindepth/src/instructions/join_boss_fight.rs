use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::{BossFightJoined, BossTicked, PlayerDamaged, PlayerDied};
use crate::instructions::force_exit_on_death::apply_death_outcome;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    item_ids, session_instruction_bits, BossFightAccount, GlobalAccount, PlayerAccount,
    PlayerProfile, RoomAccount, RoomPresence, SessionAuthority, InventoryAccount, CENTER_BOSS,
    calculate_depth,
};

pub const PLAYER_BOSS_DAMAGE_SLOT_STEP: u64 = 150;
pub const PLAYER_BOSS_BASE_DAMAGE_PER_TICK: u16 = 5;

#[derive(Accounts)]
pub struct JoinBossFight<'info> {
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
        seeds = [PlayerAccount::SEED_PREFIX, player.key().as_ref()],
        bump = player_account.bump
    )]
    pub player_account: Account<'info, PlayerAccount>,

    #[account(
        seeds = [PlayerProfile::SEED_PREFIX, player.key().as_ref()],
        bump = profile.bump
    )]
    pub profile: Account<'info, PlayerProfile>,

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
        init_if_needed,
        payer = authority,
        space = BossFightAccount::DISCRIMINATOR.len() + BossFightAccount::INIT_SPACE,
        seeds = [BossFightAccount::SEED_PREFIX, room.key().as_ref(), player.key().as_ref()],
        bump
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

pub fn handler(ctx: Context<JoinBossFight>) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::JOIN_BOSS_FIGHT,
        0,
    )?;

    let room = &mut ctx.accounts.room;
    let player_account = &mut ctx.accounts.player_account;
    let clock = Clock::get()?;

    require!(
        room.center_type == CENTER_BOSS,
        ChainDepthError::NoBoss
    );
    require!(
        player_account.is_at_room(room.x, room.y),
        ChainDepthError::NotInRoom
    );
    apply_boss_damage(room, clock.slot)?;
    require!(
        !room.boss_defeated,
        ChainDepthError::BossAlreadyDefeated
    );

    require!(
        player_account.current_hp > 0,
        ChainDepthError::PlayerDead
    );

    let fighter_dps = weapon_dps(player_account.equipped_item_id);

    let boss_fight = &mut ctx.accounts.boss_fight;
    if boss_fight.is_active {
        return err!(ChainDepthError::AlreadyFightingBoss);
    }

    room.boss_fighter_count = room
        .boss_fighter_count
        .checked_add(1)
        .ok_or(ChainDepthError::Overflow)?;
    room.boss_total_dps = room
        .boss_total_dps
        .checked_add(fighter_dps)
        .ok_or(ChainDepthError::Overflow)?;

    boss_fight.player = ctx.accounts.player.key();
    boss_fight.room = room.key();
    boss_fight.dps = fighter_dps;
    boss_fight.joined_slot = clock.slot;
    boss_fight.last_damage_slot = clock.slot;
    boss_fight.is_active = true;
    boss_fight.bump = ctx.bumps.boss_fight;

    let room_presence = &mut ctx.accounts.room_presence;
    room_presence.skin_id = ctx.accounts.profile.skin_id;
    room_presence.equipped_item_id = player_account.equipped_item_id;
    room_presence.set_boss_fight();
    room_presence.is_current = true;

    emit!(BossFightJoined {
        room_x: room.x,
        room_y: room.y,
        player: ctx.accounts.player.key(),
        dps: fighter_dps,
        fighter_count: room.boss_fighter_count,
    });

    Ok(())
}

pub fn boss_damage_per_tick(depth: u32, boss_id: u16) -> u16 {
    let depth_multiplier = 1 + (depth / 4) as u64;
    let id_multiplier = 1 + (boss_id % 5) as u64;
    let scaled = u64::from(PLAYER_BOSS_BASE_DAMAGE_PER_TICK)
        .saturating_mul(depth_multiplier)
        .saturating_mul(id_multiplier);
    scaled.min(u64::from(u16::MAX)) as u16
}

pub fn resolve_player_boss_damage(
    room: &mut Account<RoomAccount>,
    player_account: &mut Account<PlayerAccount>,
    room_presence: &mut Account<RoomPresence>,
    boss_fight: &mut Account<BossFightAccount>,
    inventory: &mut Account<InventoryAccount>,
    player_key: Pubkey,
    now_slot: u64,
) -> Result<bool> {
    if !boss_fight.is_active || boss_fight.player != player_key {
        return Ok(false);
    }

    let elapsed_slots = now_slot.saturating_sub(boss_fight.last_damage_slot);
    if elapsed_slots < PLAYER_BOSS_DAMAGE_SLOT_STEP {
        return Ok(false);
    }

    let ticks = elapsed_slots / PLAYER_BOSS_DAMAGE_SLOT_STEP;
    if ticks == 0 {
        return Ok(false);
    }

    let depth = calculate_depth(room.x, room.y);
    let per_tick_damage = boss_damage_per_tick(depth, room.center_id);
    let total_damage = u64::from(per_tick_damage)
        .saturating_mul(ticks)
        .min(u64::from(u16::MAX)) as u16;

    boss_fight.last_damage_slot = boss_fight
        .last_damage_slot
        .saturating_add(ticks.saturating_mul(PLAYER_BOSS_DAMAGE_SLOT_STEP));

    let previous_hp = player_account.current_hp;
    player_account.current_hp = previous_hp.saturating_sub(total_damage);
    let applied_damage = previous_hp.saturating_sub(player_account.current_hp);

    if applied_damage > 0 {
        emit!(PlayerDamaged {
            player: player_key,
            room_x: room.x,
            room_y: room.y,
            boss_id: room.center_id,
            damage: applied_damage,
            current_hp: player_account.current_hp,
            max_hp: player_account.max_hp,
        });
    }

    if player_account.current_hp > 0 {
        return Ok(false);
    }

    if inventory.owner == Pubkey::default() {
        inventory.owner = player_key;
        inventory.items = Vec::new();
    }

    let death_outcome = apply_death_outcome(player_account, inventory, room_presence, now_slot)?;

    if room.boss_fighter_count > 0 {
        room.boss_fighter_count = room.boss_fighter_count.saturating_sub(1);
    }
    room.boss_total_dps = room.boss_total_dps.saturating_sub(boss_fight.dps);
    boss_fight.is_active = false;
    boss_fight.dps = 0;
    room_presence.set_idle();

    emit!(PlayerDied {
        player: player_key,
        room_x: room.x,
        room_y: room.y,
        boss_id: room.center_id,
        lost_item_stacks: death_outcome.lost_item_stacks,
        lost_item_units: death_outcome.lost_item_units,
    });

    emit!(BossTicked {
        room_x: room.x,
        room_y: room.y,
        boss_id: room.center_id,
        current_hp: room.boss_current_hp,
        max_hp: room.boss_max_hp,
        fighter_count: room.boss_fighter_count,
    });

    Ok(true)
}

pub(crate) fn apply_boss_damage(room: &mut Account<RoomAccount>, current_slot: u64) -> Result<()> {
    if room.center_type != CENTER_BOSS || room.boss_defeated || room.boss_fighter_count == 0 {
        room.boss_last_update_slot = current_slot;
        return Ok(());
    }

    let elapsed_slots = current_slot.saturating_sub(room.boss_last_update_slot);
    if elapsed_slots == 0 || room.boss_total_dps == 0 {
        room.boss_last_update_slot = current_slot;
        return Ok(());
    }

    let damage = elapsed_slots
        .checked_mul(room.boss_total_dps)
        .ok_or(ChainDepthError::Overflow)?;

    room.boss_current_hp = room.boss_current_hp.saturating_sub(damage);
    room.boss_last_update_slot = current_slot;
    if room.boss_current_hp == 0 {
        room.boss_defeated = true;
    }

    Ok(())
}

fn weapon_dps(item_id: u16) -> u64 {
    match item_id {
        // Legacy tool id retained for old inventories.
        2 => 5,

        // Wearable weapon ids (100-199)
        item_ids::BRONZE_PICKAXE => 4,
        item_ids::IRON_PICKAXE => 6,
        item_ids::BRONZE_SWORD => 7,
        item_ids::IRON_SWORD => 10,
        item_ids::DIAMOND_SWORD => 16,
        item_ids::NOKIA_3310 => 22,
        item_ids::WOODEN_PIPE => 5,
        item_ids::IRON_SCIMITAR => 12,
        item_ids::WOODEN_TANKARD => 3,

        // Bare hands / unknown item.
        _ => 1,
    }
}
