use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::InventoryItemRemoved;
use crate::state::InventoryAccount;

#[derive(Accounts)]
pub struct RemoveInventoryItem<'info> {
    #[account(mut)]
    pub player: Signer<'info>,

    #[account(
        mut,
        seeds = [InventoryAccount::SEED_PREFIX, player.key().as_ref()],
        bump = inventory.bump,
        constraint = inventory.owner == player.key() @ ChainDepthError::Unauthorized
    )]
    pub inventory: Account<'info, InventoryAccount>,
}

pub fn handler(ctx: Context<RemoveInventoryItem>, item_id: u16, amount: u32) -> Result<()> {
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

