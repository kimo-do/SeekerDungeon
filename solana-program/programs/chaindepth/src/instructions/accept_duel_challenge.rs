use anchor_lang::prelude::*;
use anchor_spl::token::{self, Token, TokenAccount, Transfer};
use ephemeral_vrf_sdk::anchor::vrf;
use ephemeral_vrf_sdk::instructions::{create_request_randomness_ix, RequestRandomnessParams};
use ephemeral_vrf_sdk::types::SerializableAccountMeta;

use crate::errors::ChainDepthError;
use crate::events::DuelChallengeAccepted;
use crate::state::{DuelChallenge, GlobalAccount, PlayerAccount};

#[vrf]
#[derive(Accounts)]
#[instruction(challenge_seed: u64)]
pub struct AcceptDuelChallenge<'info> {
    #[account(mut)]
    pub opponent: Signer<'info>,

    /// CHECK: challenger wallet for PDA derivations
    pub challenger: UncheckedAccount<'info>,

    #[account(
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Account<'info, GlobalAccount>,

    #[account(
        mut,
        seeds = [
            DuelChallenge::SEED_PREFIX,
            challenger.key().as_ref(),
            opponent.key().as_ref(),
            &challenge_seed.to_le_bytes()
        ],
        bump = duel_challenge.bump
    )]
    pub duel_challenge: Account<'info, DuelChallenge>,

    #[account(
        mut,
        seeds = [DuelChallenge::DUEL_ESCROW_SEED_PREFIX, duel_challenge.key().as_ref()],
        bump
    )]
    pub duel_escrow: Account<'info, TokenAccount>,

    #[account(
        seeds = [PlayerAccount::SEED_PREFIX, challenger.key().as_ref()],
        bump = challenger_player_account.bump
    )]
    pub challenger_player_account: Account<'info, PlayerAccount>,

    #[account(
        seeds = [PlayerAccount::SEED_PREFIX, opponent.key().as_ref()],
        bump = opponent_player_account.bump,
        constraint = opponent_player_account.owner == opponent.key()
    )]
    pub opponent_player_account: Account<'info, PlayerAccount>,

    #[account(
        mut,
        constraint = challenger_token_account.mint == global.skr_mint,
        constraint = challenger_token_account.owner == challenger.key()
    )]
    pub challenger_token_account: Account<'info, TokenAccount>,

    #[account(
        mut,
        constraint = opponent_token_account.mint == global.skr_mint,
        constraint = opponent_token_account.owner == opponent.key()
    )]
    pub opponent_token_account: Account<'info, TokenAccount>,

    /// CHECK: VRF oracle queue account.
    #[account(mut, address = ephemeral_vrf_sdk::consts::DEFAULT_QUEUE)]
    pub oracle_queue: AccountInfo<'info>,

    pub token_program: Program<'info, Token>,
}

pub fn handler(ctx: Context<AcceptDuelChallenge>, _challenge_seed: u64) -> Result<()> {
    let duel_challenge = &ctx.accounts.duel_challenge;
    require!(
        duel_challenge.status == DuelChallenge::STATUS_OPEN,
        ChainDepthError::InvalidDuelState
    );
    require!(
        duel_challenge.challenger == ctx.accounts.challenger.key()
            && duel_challenge.opponent == ctx.accounts.opponent.key(),
        ChainDepthError::Unauthorized
    );
    require!(
        duel_challenge.season_seed == ctx.accounts.global.season_seed,
        ChainDepthError::InvalidSeason
    );
    require!(
        duel_challenge.duel_escrow == ctx.accounts.duel_escrow.key(),
        ChainDepthError::InvalidDuelEscrow
    );

    let clock = Clock::get()?;
    require!(
        clock.slot <= duel_challenge.expires_at_slot,
        ChainDepthError::DuelChallengeExpired
    );
    require!(
        ctx.accounts.challenger_player_account.current_room_x
            == ctx.accounts.opponent_player_account.current_room_x
            && ctx.accounts.challenger_player_account.current_room_y
                == ctx.accounts.opponent_player_account.current_room_y
            && ctx.accounts.challenger_player_account.current_room_x == duel_challenge.room_x
            && ctx.accounts.challenger_player_account.current_room_y == duel_challenge.room_y,
        ChainDepthError::PlayersNotInSameRoom
    );
    require!(
        ctx.accounts.challenger_player_account.current_hp > 0
            && ctx.accounts.opponent_player_account.current_hp > 0,
        ChainDepthError::PlayerDead
    );

    let stake_amount = duel_challenge.stake_amount;
    let duel_challenge_key = duel_challenge.key();
    let duel_challenger = duel_challenge.challenger;
    let duel_opponent = duel_challenge.opponent;
    let duel_challenge_seed = duel_challenge.challenge_seed;
    let transfer_context = CpiContext::new(
        ctx.accounts.token_program.to_account_info(),
        Transfer {
            from: ctx.accounts.opponent_token_account.to_account_info(),
            to: ctx.accounts.duel_escrow.to_account_info(),
            authority: ctx.accounts.opponent.to_account_info(),
        },
    );
    token::transfer(transfer_context, stake_amount)?;

    let callback_accounts = vec![
        SerializableAccountMeta {
            pubkey: ctx.accounts.global.key(),
            is_signer: false,
            is_writable: false,
        },
        SerializableAccountMeta {
            pubkey: duel_challenge_key,
            is_signer: false,
            is_writable: true,
        },
        SerializableAccountMeta {
            pubkey: ctx.accounts.duel_escrow.key(),
            is_signer: false,
            is_writable: true,
        },
        SerializableAccountMeta {
            pubkey: ctx.accounts.challenger_token_account.key(),
            is_signer: false,
            is_writable: true,
        },
        SerializableAccountMeta {
            pubkey: ctx.accounts.opponent_token_account.key(),
            is_signer: false,
            is_writable: true,
        },
        SerializableAccountMeta {
            pubkey: ctx.accounts.token_program.key(),
            is_signer: false,
            is_writable: false,
        },
    ];
    let caller_seed = build_caller_seed(
        duel_challenge_seed,
        clock.slot,
        duel_challenger,
        duel_opponent,
    );
    let randomness_request_instruction = create_request_randomness_ix(RequestRandomnessParams {
        payer: ctx.accounts.opponent.key(),
        oracle_queue: ctx.accounts.oracle_queue.key(),
        callback_program_id: crate::ID,
        callback_discriminator: crate::instruction::ConsumeDuelRandomness::DISCRIMINATOR.to_vec(),
        caller_seed,
        accounts_metas: Some(callback_accounts),
        ..Default::default()
    });
    ctx.accounts.invoke_signed_vrf(
        &ctx.accounts.opponent.to_account_info(),
        &randomness_request_instruction,
    )?;

    let duel_challenge = &mut ctx.accounts.duel_challenge;
    duel_challenge.status = DuelChallenge::STATUS_PENDING_RANDOMNESS;
    duel_challenge.requested_slot = clock.slot;

    emit!(DuelChallengeAccepted {
        challenger: duel_challenge.challenger,
        opponent: duel_challenge.opponent,
        duel_challenge: duel_challenge.key(),
        stake_amount,
        requested_slot: clock.slot,
    });

    Ok(())
}

fn build_caller_seed(
    challenge_seed: u64,
    slot: u64,
    challenger: Pubkey,
    opponent: Pubkey,
) -> [u8; 32] {
    let mut caller_seed = [0u8; 32];
    caller_seed[..8].copy_from_slice(&challenge_seed.to_le_bytes());
    caller_seed[8..16].copy_from_slice(&slot.to_le_bytes());
    caller_seed[16..24].copy_from_slice(&challenger.to_bytes()[..8]);
    caller_seed[24..32].copy_from_slice(&opponent.to_bytes()[..8]);
    caller_seed
}
