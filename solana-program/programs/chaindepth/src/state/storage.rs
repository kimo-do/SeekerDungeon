use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::state::inventory::InventoryItem;

pub const MAX_STORAGE_SLOTS: usize = 64;

#[account]
#[derive(InitSpace)]
pub struct StorageAccount {
    pub owner: Pubkey,
    #[max_len(MAX_STORAGE_SLOTS)]
    pub items: Vec<InventoryItem>,
    pub bump: u8,
}

impl StorageAccount {
    pub const SEED_PREFIX: &'static [u8] = b"storage";

    pub fn add_item(&mut self, item_id: u16, amount: u32, durability: u16) -> Result<()> {
        require!(item_id > 0, ChainDepthError::InvalidItemId);
        require!(amount > 0, ChainDepthError::InvalidItemAmount);

        if let Some(existing_item) = self
            .items
            .iter_mut()
            .find(|item| item.item_id == item_id && item.durability == durability)
        {
            existing_item.amount = existing_item
                .amount
                .checked_add(amount)
                .ok_or(ChainDepthError::Overflow)?;
            return Ok(());
        }

        require!(
            self.items.len() < MAX_STORAGE_SLOTS,
            ChainDepthError::InventoryFull
        );

        self.items.push(InventoryItem {
            item_id,
            amount,
            durability,
        });

        Ok(())
    }
}
