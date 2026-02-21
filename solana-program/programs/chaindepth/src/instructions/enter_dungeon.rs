use anchor_lang::prelude::*;

use crate::events::PlayerMoved;
use crate::instructions::session_auth::authorize_player_action;
use crate::state::{
    session_instruction_bits, GlobalAccount, PlayerAccount, PlayerProfile, RoomAccount,
    RoomPresence, SessionAuthority, DEFAULT_PLAYER_MAX_HP,
};

#[derive(Accounts)]
pub struct EnterDungeon<'info> {
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
        init_if_needed,
        payer = authority,
        space = 8 + PlayerAccount::INIT_SPACE,
        seeds = [PlayerAccount::SEED_PREFIX, player.key().as_ref()],
        bump
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
        seeds = [
            RoomAccount::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[GlobalAccount::START_X as u8],
            &[GlobalAccount::START_Y as u8]
        ],
        bump
    )]
    pub start_room: Account<'info, RoomAccount>,

    #[account(
        init_if_needed,
        payer = authority,
        space = RoomPresence::DISCRIMINATOR.len() + RoomPresence::INIT_SPACE,
        seeds = [
            RoomPresence::SEED_PREFIX,
            &global.season_seed.to_le_bytes(),
            &[GlobalAccount::START_X as u8],
            &[GlobalAccount::START_Y as u8],
            player.key().as_ref()
        ],
        bump
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

pub fn handler(ctx: Context<EnterDungeon>) -> Result<()> {
    authorize_player_action(
        &ctx.accounts.authority,
        &ctx.accounts.player,
        ctx.accounts.session_authority.as_mut(),
        session_instruction_bits::ENTER_DUNGEON,
        0,
    )?;

    let clock = Clock::get()?;
    let global = &ctx.accounts.global;
    let player_key = ctx.accounts.player.key();
    let player = &mut ctx.accounts.player_account;
    let profile = &mut ctx.accounts.profile;
    let room_presence = &mut ctx.accounts.room_presence;

    let previous_x = player.current_room_x;
    let previous_y = player.current_room_y;
    let was_initialized = player.owner != Pubkey::default();
    let player_bump = ctx.bumps.player_account;

    if !was_initialized {
        player.owner = player_key;
        player.current_room_x = GlobalAccount::START_X;
        player.current_room_y = GlobalAccount::START_Y;
        player.active_jobs = Vec::new();
        player.jobs_completed = 0;
        player.chests_looted = 0;
        player.equipped_item_id = 0;
        player.total_score = 0;
        player.current_run_start_slot = clock.slot;
        player.runs_extracted = 0;
        player.last_extraction_slot = 0;
        player.in_dungeon = true;
        player.current_hp = DEFAULT_PLAYER_MAX_HP;
        player.max_hp = DEFAULT_PLAYER_MAX_HP;
        player.data_version = PlayerAccount::CURRENT_DATA_VERSION;
        player.season_seed = global.season_seed;
        player.bump = player_bump;
    } else {
        // Any explicit enter starts/restarts the run at room (10,10).
        player.current_room_x = GlobalAccount::START_X;
        player.current_room_y = GlobalAccount::START_Y;
        player.in_dungeon = true;
        player.current_run_start_slot = clock.slot;
        player.current_hp = player.max_hp.max(DEFAULT_PLAYER_MAX_HP);
        if player.max_hp == 0 {
            player.max_hp = DEFAULT_PLAYER_MAX_HP;
        }
        player.active_jobs = Vec::new();
        player.season_seed = global.season_seed;
        player.data_version = PlayerAccount::CURRENT_DATA_VERSION;
    }

    if profile.owner == Pubkey::default() {
        profile.owner = player_key;
        profile.skin_id = PlayerProfile::DEFAULT_SKIN_ID;
        profile.display_name = String::new();
        profile.starter_pickaxe_granted = false;
        profile.bump = ctx.bumps.profile;
    }

    room_presence.player = player_key;
    room_presence.season_seed = global.season_seed;
    room_presence.room_x = GlobalAccount::START_X;
    room_presence.room_y = GlobalAccount::START_Y;
    room_presence.skin_id = profile.skin_id;
    room_presence.equipped_item_id = player.equipped_item_id;
    room_presence.set_idle();
    room_presence.is_current = true;
    room_presence.bump = ctx.bumps.room_presence;

    emit!(PlayerMoved {
        player: player_key,
        from_x: if was_initialized {
            previous_x
        } else {
            GlobalAccount::START_X
        },
        from_y: if was_initialized {
            previous_y
        } else {
            GlobalAccount::START_Y
        },
        to_x: GlobalAccount::START_X,
        to_y: GlobalAccount::START_Y,
    });

    // Make start_room a required, validated account in the context.
    let _ = &ctx.accounts.start_room;

    Ok(())
}
