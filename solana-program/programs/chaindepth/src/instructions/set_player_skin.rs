use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    session_instruction_bits, GlobalAccount, PlayerAccount, PlayerProfile, RoomPresence,
    SessionAuthority,
};

#[derive(Accounts)]
pub struct SetPlayerSkin<'info> {
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
        bump = player_account.bump,
        constraint = player_account.owner == player.key() @ ChainDepthError::Unauthorized
    )]
    pub player_account: Account<'info, PlayerAccount>,

    #[account(
        mut,
        seeds = [PlayerProfile::SEED_PREFIX, player.key().as_ref()],
        bump = profile.bump,
        constraint = profile.owner == player.key() @ ChainDepthError::Unauthorized
    )]
    pub profile: Account<'info, PlayerProfile>,

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
}

pub fn handler(ctx: Context<SetPlayerSkin>, skin_id: u16) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::SET_PLAYER_SKIN,
        0,
    )?;

    let profile = &mut ctx.accounts.profile;
    let room_presence = &mut ctx.accounts.room_presence;

    profile.skin_id = skin_id;
    room_presence.skin_id = skin_id;

    Ok(())
}
