use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;

pub const MAX_INVENTORY_SLOTS: usize = 64;

pub mod item_ids {
    pub const ORE: u16 = 1;
    pub const TOOL: u16 = 2;
    pub const BUFF: u16 = 3;
}

#[derive(AnchorSerialize, AnchorDeserialize, Clone, InitSpace)]
pub struct InventoryItem {
    pub item_id: u16,
    pub amount: u32,
    pub durability: u16,
}

#[account]
#[derive(InitSpace)]
pub struct InventoryAccount {
    pub owner: Pubkey,
    #[max_len(MAX_INVENTORY_SLOTS)]
    pub items: Vec<InventoryItem>,
    pub bump: u8,
}

impl InventoryAccount {
    pub const SEED_PREFIX: &'static [u8] = b"inventory";

    pub fn add_item(&mut self, item_id: u16, amount: u32, durability: u16) -> Result<()> {
        require!(item_id > 0, ChainDepthError::InvalidItemId);
        require!(amount > 0, ChainDepthError::InvalidItemAmount);

        if let Some(existing) = self
            .items
            .iter_mut()
            .find(|item| item.item_id == item_id && item.durability == durability)
        {
            existing.amount = existing
                .amount
                .checked_add(amount)
                .ok_or(ChainDepthError::Overflow)?;
            return Ok(());
        }

        require!(
            self.items.len() < MAX_INVENTORY_SLOTS,
            ChainDepthError::InventoryFull
        );

        self.items.push(InventoryItem {
            item_id,
            amount,
            durability,
        });

        Ok(())
    }

    pub fn remove_item(&mut self, item_id: u16, amount: u32) -> Result<()> {
        require!(item_id > 0, ChainDepthError::InvalidItemId);
        require!(amount > 0, ChainDepthError::InvalidItemAmount);

        let mut remaining = amount;
        for item in self.items.iter_mut().filter(|item| item.item_id == item_id) {
            if remaining == 0 {
                break;
            }
            let remove_here = remaining.min(item.amount);
            item.amount = item
                .amount
                .checked_sub(remove_here)
                .ok_or(ChainDepthError::Overflow)?;
            remaining = remaining
                .checked_sub(remove_here)
                .ok_or(ChainDepthError::Overflow)?;
        }

        require!(remaining == 0, ChainDepthError::InsufficientItemAmount);

        self.items.retain(|item| item.amount > 0);
        Ok(())
    }
}

