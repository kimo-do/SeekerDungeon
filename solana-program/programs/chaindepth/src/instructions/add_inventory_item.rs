use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::events::InventoryItemAdded;
use crate::state::InventoryAccount;

#[derive(Accounts)]
pub struct AddInventoryItem<'info> {
    #[account(mut)]
    pub player: Signer<'info>,

    #[account(
        init_if_needed,
        payer = player,
        space = InventoryAccount::DISCRIMINATOR.len() + InventoryAccount::INIT_SPACE,
        seeds = [InventoryAccount::SEED_PREFIX, player.key().as_ref()],
        bump
    )]
    pub inventory: Account<'info, InventoryAccount>,

    pub system_program: Program<'info, System>,
}

pub fn handler(ctx: Context<AddInventoryItem>, item_id: u16, amount: u32, durability: u16) -> Result<()> {
    require!(item_id > 0, ChainDepthError::InvalidItemId);
    require!(amount > 0, ChainDepthError::InvalidItemAmount);

    let inventory = &mut ctx.accounts.inventory;
    if inventory.owner == Pubkey::default() {
        inventory.owner = ctx.accounts.player.key();
        inventory.items = Vec::new();
        inventory.bump = ctx.bumps.inventory;
    }

    inventory.add_item(item_id, amount, durability)?;

    emit!(InventoryItemAdded {
        player: ctx.accounts.player.key(),
        item_id,
        amount,
        durability,
    });

    Ok(())
}
