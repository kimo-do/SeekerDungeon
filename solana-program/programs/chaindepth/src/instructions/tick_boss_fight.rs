use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::BossTicked;
use crate::instructions::join_boss_fight::apply_boss_damage;
use crate::state::{GlobalAccount, RoomAccount, CENTER_BOSS};

#[derive(Accounts)]
pub struct TickBossFight<'info> {
    pub caller: Signer<'info>,

    #[account(
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Account<'info, GlobalAccount>,

    #[account(
        mut,
        seeds = [
            RoomAccount::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[room.x as u8],
            &[room.y as u8]
        ],
        bump
    )]
    pub room: Account<'info, RoomAccount>,
}

pub fn handler(ctx: Context<TickBossFight>) -> Result<()> {
    let room = &mut ctx.accounts.room;
    let clock = Clock::get()?;

    require!(
        room.center_type == CENTER_BOSS,
        ChainDepthError::NoBoss
    );
    require!(
        !room.boss_defeated,
        ChainDepthError::BossAlreadyDefeated
    );
    require!(
        room.boss_fighter_count > 0,
        ChainDepthError::NoActiveJob
    );

    apply_boss_damage(room, clock.slot)?;

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
