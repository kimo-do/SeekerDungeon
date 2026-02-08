use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::InventoryItemRemoved;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{session_instruction_bits, InventoryAccount, SessionAuthority};

#[derive(Accounts)]
pub struct RemoveInventoryItem<'info> {
    pub authority: Signer<'info>,

    /// CHECK: wallet owner whose gameplay state is being modified
    pub player: UncheckedAccount<'info>,

    #[account(
        mut,
        seeds = [InventoryAccount::SEED_PREFIX, player.key().as_ref()],
        bump = inventory.bump,
        constraint = inventory.owner == player.key() @ ChainDepthError::Unauthorized
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
}

pub fn handler(ctx: Context<RemoveInventoryItem>, item_id: u16, amount: u32) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::REMOVE_INVENTORY_ITEM,
        0,
    )?;

    require!(item_id > 0, ChainDepthError::InvalidItemId);
    require!(amount > 0, ChainDepthError::InvalidItemAmount);

    let inventory = &mut ctx.accounts.inventory;
    inventory.remove_item(item_id, amount)?;

    emit!(InventoryItemRemoved {
        player: ctx.accounts.player.key(),
        item_id,
        amount,
    });

    Ok(())
}
