use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::BossTicked;
use crate::instructions::join_boss_fight::{apply_boss_damage, resolve_player_boss_damage};
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    session_instruction_bits, BossFightAccount, GlobalAccount, InventoryAccount, PlayerAccount,
    RoomAccount, RoomPresence, SessionAuthority, CENTER_BOSS,
};

#[derive(Accounts)]
pub struct LeaveBossFight<'info> {
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
        mut,
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

pub fn handler(ctx: Context<LeaveBossFight>) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::LEAVE_BOSS_FIGHT,
        0,
    )?;

    let room = &mut ctx.accounts.room;
    let clock = Clock::get()?;

    require!(room.center_type == CENTER_BOSS, ChainDepthError::NoBoss);
    require!(
        ctx.accounts
            .player_account
            .is_at_room(room.x, room.y),
        ChainDepthError::NotInRoom
    );
    require!(ctx.accounts.boss_fight.is_active, ChainDepthError::NotBossFighter);

    apply_boss_damage(room, clock.slot)?;
    let died = resolve_player_boss_damage(
        room,
        &mut ctx.accounts.player_account,
        &mut ctx.accounts.room_presence,
        &mut ctx.accounts.boss_fight,
        &mut ctx.accounts.inventory,
        ctx.accounts.player.key(),
        clock.slot,
    )?;

    if !died && ctx.accounts.boss_fight.is_active {
        if room.boss_fighter_count > 0 {
            room.boss_fighter_count = room.boss_fighter_count.saturating_sub(1);
        }
        room.boss_total_dps = room.boss_total_dps.saturating_sub(ctx.accounts.boss_fight.dps);
        ctx.accounts.boss_fight.is_active = false;
        ctx.accounts.boss_fight.dps = 0;
        ctx.accounts.room_presence.set_idle();
    }

    emit!(BossTicked {
        room_x: room.x,
        room_y: room.y,
        boss_id: room.center_id,
        current_hp: room.boss_current_hp,
        max_hp: room.boss_max_hp,
        fighter_count: room.boss_fighter_count,
    });

    Ok(())
}
