use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::ItemEquipped;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    session_instruction_bits, GlobalAccount, InventoryAccount, PlayerAccount, RoomPresence,
    SessionAuthority,
};

#[derive(Accounts)]
pub struct EquipItem<'info> {
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
        seeds = [InventoryAccount::SEED_PREFIX, player.key().as_ref()],
        bump = inventory.bump,
        constraint = inventory.owner == player.key() @ ChainDepthError::Unauthorized
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
}

pub fn handler(ctx: Context<EquipItem>, item_id: u16) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::EQUIP_ITEM,
        0,
    )?;

    if item_id != 0 {
        let has_item = ctx
            .accounts
            .inventory
            .items
            .iter()
            .any(|item| item.item_id == item_id && item.amount > 0);
        require!(has_item, ChainDepthError::InsufficientItemAmount);
    }

    ctx.accounts.player_account.equipped_item_id = item_id;
    ctx.accounts.room_presence.equipped_item_id = item_id;

    emit!(ItemEquipped {
        player: ctx.accounts.player.key(),
        item_id,
    });

    Ok(())
}
