use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::BossFightJoined;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    item_ids, session_instruction_bits, BossFightAccount, GlobalAccount, PlayerAccount,
    PlayerProfile, RoomAccount, RoomPresence, SessionAuthority, CENTER_BOSS,
};

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
        init,
        payer = authority,
        space = BossFightAccount::DISCRIMINATOR.len() + BossFightAccount::INIT_SPACE,
        seeds = [BossFightAccount::SEED_PREFIX, room.key().as_ref(), player.key().as_ref()],
        bump
    )]
    pub boss_fight: Account<'info, BossFightAccount>,

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
    let player_account = &ctx.accounts.player_account;
    let clock = Clock::get()?;

    require!(
        room.center_type == CENTER_BOSS,
        ChainDepthError::NoBoss
    );
    require!(
        player_account.is_at_room(room.x, room.y),
        ChainDepthError::NotInRoom
    );
    require!(
        !room.boss_defeated,
        ChainDepthError::BossAlreadyDefeated
    );

    apply_boss_damage(room, clock.slot)?;

    let fighter_dps = weapon_dps(player_account.equipped_item_id);

    room.boss_fighter_count = room
        .boss_fighter_count
        .checked_add(1)
        .ok_or(ChainDepthError::Overflow)?;
    room.boss_total_dps = room
        .boss_total_dps
        .checked_add(fighter_dps)
        .ok_or(ChainDepthError::Overflow)?;

    let boss_fight = &mut ctx.accounts.boss_fight;
    boss_fight.player = ctx.accounts.player.key();
    boss_fight.room = room.key();
    boss_fight.dps = fighter_dps;
    boss_fight.joined_slot = clock.slot;
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
