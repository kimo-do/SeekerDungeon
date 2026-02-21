use anchor_lang::prelude::*;
use anchor_spl::token::{self, Token, TokenAccount, Transfer};

use crate::errors::ChainDepthError;
use crate::events::DuelChallengeCreated;
use crate::state::{DuelChallenge, GlobalAccount, PlayerAccount, PlayerProfile};

#[derive(Accounts)]
#[instruction(challenge_seed: u64)]
pub struct CreateDuelChallenge<'info> {
    #[account(mut)]
    pub challenger: Signer<'info>,

    /// CHECK: expected opponent wallet
    pub opponent: UncheckedAccount<'info>,

    #[account(
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Account<'info, GlobalAccount>,

    #[account(
        seeds = [PlayerAccount::SEED_PREFIX, challenger.key().as_ref()],
        bump = challenger_player_account.bump,
        constraint = challenger_player_account.owner == challenger.key()
    )]
    pub challenger_player_account: Account<'info, PlayerAccount>,

    #[account(
        seeds = [PlayerAccount::SEED_PREFIX, opponent.key().as_ref()],
        bump = opponent_player_account.bump
    )]
    pub opponent_player_account: Account<'info, PlayerAccount>,

    #[account(
        seeds = [PlayerProfile::SEED_PREFIX, challenger.key().as_ref()],
        bump = challenger_profile.bump,
        constraint = challenger_profile.owner == challenger.key()
    )]
    pub challenger_profile: Account<'info, PlayerProfile>,

    #[account(
        seeds = [PlayerProfile::SEED_PREFIX, opponent.key().as_ref()],
        bump = opponent_profile.bump,
        constraint = opponent_profile.owner == opponent.key()
    )]
    pub opponent_profile: Account<'info, PlayerProfile>,

    #[account(
        init,
        payer = challenger,
        space = DuelChallenge::DISCRIMINATOR.len() + DuelChallenge::INIT_SPACE,
        seeds = [
            DuelChallenge::SEED_PREFIX,
            challenger.key().as_ref(),
            opponent.key().as_ref(),
            &challenge_seed.to_le_bytes()
        ],
        bump
    )]
    pub duel_challenge: Account<'info, DuelChallenge>,

    #[account(
        init_if_needed,
        payer = challenger,
        token::mint = skr_mint,
        token::authority = duel_escrow,
        seeds = [DuelChallenge::DUEL_ESCROW_SEED_PREFIX, duel_challenge.key().as_ref()],
        bump
    )]
    pub duel_escrow: Account<'info, TokenAccount>,

    #[account(
        mut,
        constraint = challenger_token_account.mint == global.skr_mint,
        constraint = challenger_token_account.owner == challenger.key()
    )]
    pub challenger_token_account: Account<'info, TokenAccount>,

    #[account(constraint = skr_mint.key() == global.skr_mint)]
    pub skr_mint: Account<'info, anchor_spl::token::Mint>,

    pub token_program: Program<'info, Token>,
    pub system_program: Program<'info, System>,
}

pub fn handler(
    ctx: Context<CreateDuelChallenge>,
    challenge_seed: u64,
    stake_amount: u64,
    expires_at_slot: u64,
) -> Result<()> {
    let challenger = ctx.accounts.challenger.key();
    let opponent = ctx.accounts.opponent.key();
    require!(challenger != opponent, ChainDepthError::InvalidDuelOpponent);
    require!(stake_amount > 0, ChainDepthError::InvalidDuelStake);

    let clock = Clock::get()?;
    require!(
        expires_at_slot > clock.slot
            && expires_at_slot <= clock.slot + DuelChallenge::MAX_EXPIRY_SLOTS,
        ChainDepthError::InvalidDuelExpiry
    );

    let challenger_player_account = &ctx.accounts.challenger_player_account;
    let opponent_player_account = &ctx.accounts.opponent_player_account;

    require!(
        challenger_player_account.season_seed == ctx.accounts.global.season_seed
            && opponent_player_account.season_seed == ctx.accounts.global.season_seed,
        ChainDepthError::InvalidSeason
    );
    require!(
        challenger_player_account.current_room_x == opponent_player_account.current_room_x
            && challenger_player_account.current_room_y == opponent_player_account.current_room_y,
        ChainDepthError::PlayersNotInSameRoom
    );
    require!(
        challenger_player_account.current_hp > 0 && opponent_player_account.current_hp > 0,
        ChainDepthError::PlayerDead
    );

    let transfer_context = CpiContext::new(
        ctx.accounts.token_program.to_account_info(),
        Transfer {
            from: ctx.accounts.challenger_token_account.to_account_info(),
            to: ctx.accounts.duel_escrow.to_account_info(),
            authority: ctx.accounts.challenger.to_account_info(),
        },
    );
    token::transfer(transfer_context, stake_amount)?;

    let duel_challenge = &mut ctx.accounts.duel_challenge;
    duel_challenge.challenger = challenger;
    duel_challenge.opponent = opponent;
    duel_challenge.challenger_display_name_snapshot = ctx.accounts.challenger_profile.display_name.clone();
    duel_challenge.opponent_display_name_snapshot = ctx.accounts.opponent_profile.display_name.clone();
    duel_challenge.season_seed = ctx.accounts.global.season_seed;
    duel_challenge.room_x = challenger_player_account.current_room_x;
    duel_challenge.room_y = challenger_player_account.current_room_y;
    duel_challenge.stake_amount = stake_amount;
    duel_challenge.challenge_seed = challenge_seed;
    duel_challenge.expires_at_slot = expires_at_slot;
    duel_challenge.requested_slot = 0;
    duel_challenge.settled_slot = 0;
    duel_challenge.duel_escrow = ctx.accounts.duel_escrow.key();
    duel_challenge.winner = Pubkey::default();
    duel_challenge.is_draw = false;
    duel_challenge.challenger_final_hp = DuelChallenge::STARTING_HP;
    duel_challenge.opponent_final_hp = DuelChallenge::STARTING_HP;
    duel_challenge.turns_played = 0;
    duel_challenge.status = DuelChallenge::STATUS_OPEN;
    duel_challenge.starter = DuelChallenge::STARTER_UNSET;
    duel_challenge.challenger_hits = Vec::new();
    duel_challenge.opponent_hits = Vec::new();
    duel_challenge.bump = ctx.bumps.duel_challenge;

    emit!(DuelChallengeCreated {
        challenger,
        opponent,
        room_x: duel_challenge.room_x,
        room_y: duel_challenge.room_y,
        challenge_seed,
        stake_amount,
        expires_at_slot,
        duel_challenge: duel_challenge.key(),
    });

    Ok(())
}
