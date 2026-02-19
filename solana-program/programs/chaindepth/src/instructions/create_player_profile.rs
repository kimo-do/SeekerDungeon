use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    item_ids, session_instruction_bits, GlobalAccount, InventoryAccount, PlayerAccount,
    PlayerProfile, RoomPresence, SessionAuthority,
};

#[derive(Accounts)]
pub struct CreatePlayerProfile<'info> {
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
        init_if_needed,
        payer = authority,
        space = PlayerProfile::DISCRIMINATOR.len() + PlayerProfile::INIT_SPACE,
        seeds = [PlayerProfile::SEED_PREFIX, player.key().as_ref()],
        bump
    )]
    pub profile: Account<'info, PlayerProfile>,

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

    pub system_program: Program<'info, System>,
}

pub fn handler(ctx: Context<CreatePlayerProfile>, skin_id: u16, display_name: String) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::CREATE_PLAYER_PROFILE,
        0,
    )?;

    require!(display_name.len() <= 24, ChainDepthError::DisplayNameTooLong);

    let player_key = ctx.accounts.player.key();
    let profile = &mut ctx.accounts.profile;
    let inventory = &mut ctx.accounts.inventory;
    let room_presence = &mut ctx.accounts.room_presence;
    let player_account = &mut ctx.accounts.player_account;

    if profile.owner == Pubkey::default() {
        profile.owner = player_key;
        profile.skin_id = PlayerProfile::DEFAULT_SKIN_ID;
        profile.display_name = String::new();
        profile.starter_pickaxe_granted = false;
        profile.bump = ctx.bumps.profile;
    }

    if inventory.owner == Pubkey::default() {
        inventory.owner = player_key;
        inventory.items = Vec::new();
        inventory.bump = ctx.bumps.inventory;
    }

    if player_account.max_hp == 0 {
        player_account.max_hp = crate::state::DEFAULT_PLAYER_MAX_HP;
    }
    if player_account.current_hp == 0 {
        player_account.current_hp = player_account.max_hp;
    }

    profile.skin_id = skin_id;
    profile.display_name = display_name;

    if !profile.starter_pickaxe_granted {
        inventory.add_item(item_ids::BRONZE_PICKAXE, 1, 80)?;
        profile.starter_pickaxe_granted = true;

        if player_account.equipped_item_id == 0 {
            player_account.equipped_item_id = item_ids::BRONZE_PICKAXE;
        }
    }

    room_presence.skin_id = profile.skin_id;
    room_presence.equipped_item_id = player_account.equipped_item_id;

    Ok(())
}
