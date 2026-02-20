use anchor_lang::prelude::*;
use anchor_spl::token::{self, Token, TokenAccount, Transfer};

use crate::errors::ChainDepthError;
use crate::events::DuelSettled;
use crate::state::{DuelChallenge, GlobalAccount, MAX_DUEL_HITS_PER_PLAYER};

#[derive(Accounts)]
pub struct ConsumeDuelRandomness<'info> {
    #[account(address = ephemeral_vrf_sdk::consts::VRF_PROGRAM_IDENTITY)]
    pub vrf_program_identity: Signer<'info>,

    #[account(
        seeds = [GlobalAccount::SEED_PREFIX],
        bump = global.bump
    )]
    pub global: Account<'info, GlobalAccount>,

    #[account(mut)]
    pub duel_challenge: Account<'info, DuelChallenge>,

    #[account(
        mut,
        seeds = [DuelChallenge::DUEL_ESCROW_SEED_PREFIX, duel_challenge.key().as_ref()],
        bump
    )]
    pub duel_escrow: Account<'info, TokenAccount>,

    #[account(mut)]
    pub challenger_token_account: Account<'info, TokenAccount>,

    #[account(mut)]
    pub opponent_token_account: Account<'info, TokenAccount>,

    pub token_program: Program<'info, Token>,
}

pub fn handler(
    ctx: Context<ConsumeDuelRandomness>,
    randomness: [u8; 32],
) -> Result<()> {
    let duel_challenge = &mut ctx.accounts.duel_challenge;
    require!(
        duel_challenge.status == DuelChallenge::STATUS_PENDING_RANDOMNESS,
        ChainDepthError::InvalidDuelState
    );
    require!(
        duel_challenge.season_seed == ctx.accounts.global.season_seed,
        ChainDepthError::InvalidSeason
    );
    require!(
        duel_challenge.duel_escrow == ctx.accounts.duel_escrow.key(),
        ChainDepthError::InvalidDuelEscrow
    );
    require!(
        ctx.accounts.challenger_token_account.mint == ctx.accounts.global.skr_mint
            && ctx.accounts.opponent_token_account.mint == ctx.accounts.global.skr_mint,
        ChainDepthError::InvalidDuelEscrow
    );
    require!(
        ctx.accounts.challenger_token_account.owner == duel_challenge.challenger
            && ctx.accounts.opponent_token_account.owner == duel_challenge.opponent,
        ChainDepthError::Unauthorized
    );

    let duel_outcome = simulate_duel(duel_challenge, randomness);
    let total_pot = duel_challenge
        .stake_amount
        .checked_mul(2)
        .ok_or(ChainDepthError::Overflow)?;

    let duel_challenge_key = duel_challenge.key();
    let duel_escrow_bump = ctx.bumps.duel_escrow;
    let duel_escrow_seeds = &[
        DuelChallenge::DUEL_ESCROW_SEED_PREFIX,
        duel_challenge_key.as_ref(),
        &[duel_escrow_bump],
    ];
    let duel_escrow_signer = &[&duel_escrow_seeds[..]];
    if duel_outcome.is_draw {
        let refund_challenger_context = CpiContext::new_with_signer(
            ctx.accounts.token_program.to_account_info(),
            Transfer {
                from: ctx.accounts.duel_escrow.to_account_info(),
                to: ctx.accounts.challenger_token_account.to_account_info(),
                authority: ctx.accounts.duel_escrow.to_account_info(),
            },
            duel_escrow_signer,
        );
        token::transfer(refund_challenger_context, duel_challenge.stake_amount)?;

        let refund_opponent_context = CpiContext::new_with_signer(
            ctx.accounts.token_program.to_account_info(),
            Transfer {
                from: ctx.accounts.duel_escrow.to_account_info(),
                to: ctx.accounts.opponent_token_account.to_account_info(),
                authority: ctx.accounts.duel_escrow.to_account_info(),
            },
            duel_escrow_signer,
        );
        token::transfer(refund_opponent_context, duel_challenge.stake_amount)?;
        duel_challenge.winner = Pubkey::default();
    } else {
        let winner = duel_outcome.winner.ok_or(ChainDepthError::InvalidDuelState)?;
        let winner_token_account = if winner == duel_challenge.challenger {
            ctx.accounts.challenger_token_account.to_account_info()
        } else {
            ctx.accounts.opponent_token_account.to_account_info()
        };
        let payout_context = CpiContext::new_with_signer(
            ctx.accounts.token_program.to_account_info(),
            Transfer {
                from: ctx.accounts.duel_escrow.to_account_info(),
                to: winner_token_account,
                authority: ctx.accounts.duel_escrow.to_account_info(),
            },
            duel_escrow_signer,
        );
        token::transfer(payout_context, total_pot)?;
        duel_challenge.winner = winner;
    }

    duel_challenge.is_draw = duel_outcome.is_draw;
    duel_challenge.starter = duel_outcome.starter;
    duel_challenge.challenger_final_hp = duel_outcome.challenger_final_hp;
    duel_challenge.opponent_final_hp = duel_outcome.opponent_final_hp;
    duel_challenge.turns_played = duel_outcome.turns_played;
    duel_challenge.challenger_hits = duel_outcome.challenger_hits;
    duel_challenge.opponent_hits = duel_outcome.opponent_hits;
    duel_challenge.status = DuelChallenge::STATUS_SETTLED;
    duel_challenge.settled_slot = Clock::get()?.slot;

    emit!(DuelSettled {
        challenger: duel_challenge.challenger,
        opponent: duel_challenge.opponent,
        winner: duel_challenge.winner,
        is_draw: duel_challenge.is_draw,
        duel_challenge: duel_challenge.key(),
        stake_amount: duel_challenge.stake_amount,
        starter: duel_challenge.starter,
        challenger_final_hp: duel_challenge.challenger_final_hp,
        opponent_final_hp: duel_challenge.opponent_final_hp,
        turns_played: duel_challenge.turns_played,
        challenger_hits: duel_challenge.challenger_hits.clone(),
        opponent_hits: duel_challenge.opponent_hits.clone(),
    });

    Ok(())
}

struct DuelOutcome {
    winner: Option<Pubkey>,
    is_draw: bool,
    starter: u8,
    challenger_final_hp: u16,
    opponent_final_hp: u16,
    turns_played: u8,
    challenger_hits: Vec<u8>,
    opponent_hits: Vec<u8>,
}

fn simulate_duel(duel_challenge: &DuelChallenge, randomness: [u8; 32]) -> DuelOutcome {
    let mut challenger_hits = Vec::<u8>::new();
    let mut opponent_hits = Vec::<u8>::new();
    let mut challenger_hp = DuelChallenge::STARTING_HP;
    let mut opponent_hp = DuelChallenge::STARTING_HP;
    let mut duel_rng = DuelRng::new(randomness);
    let starter_is_challenger = duel_rng.next_bool();
    let starter = if starter_is_challenger {
        DuelChallenge::STARTER_CHALLENGER
    } else {
        DuelChallenge::STARTER_OPPONENT
    };
    let mut turns_played: u8 = 0;
    let mut winner: Option<Pubkey> = None;
    let mut is_draw = false;
    for _round_index in 0..MAX_DUEL_HITS_PER_PLAYER {
        let challenger_damage = duel_rng.next_attack_damage();
        let opponent_damage = duel_rng.next_attack_damage();

        challenger_hits.push(challenger_damage);
        opponent_hits.push(opponent_damage);

        let opponent_after = opponent_hp.saturating_sub(u16::from(challenger_damage));
        let challenger_after = challenger_hp.saturating_sub(u16::from(opponent_damage));
        opponent_hp = opponent_after;
        challenger_hp = challenger_after;

        turns_played = turns_played.saturating_add(2);
        if challenger_hp == 0 && opponent_hp == 0 {
            is_draw = true;
            break;
        }
        if challenger_hp == 0 {
            winner = Some(duel_challenge.opponent);
            break;
        }
        if opponent_hp == 0 {
            winner = Some(duel_challenge.challenger);
            break;
        }
    }

    if !is_draw && winner.is_none() {
        if challenger_hp > opponent_hp {
            winner = Some(duel_challenge.challenger);
        } else if opponent_hp > challenger_hp {
            winner = Some(duel_challenge.opponent);
        } else {
            is_draw = true;
        }
    }

    DuelOutcome {
        winner,
        is_draw,
        starter,
        challenger_final_hp: challenger_hp,
        opponent_final_hp: opponent_hp,
        turns_played,
        challenger_hits,
        opponent_hits,
    }
}

struct DuelRng {
    state: u64,
}

impl DuelRng {
    fn new(randomness: [u8; 32]) -> Self {
        let mut state_bytes = [0u8; 8];
        state_bytes.copy_from_slice(&randomness[..8]);
        let state = u64::from_le_bytes(state_bytes) ^ 0x9E37_79B9_7F4A_7C15;
        Self { state }
    }

    fn next_u64(&mut self) -> u64 {
        self.state = self.state.wrapping_add(0x9E37_79B9_7F4A_7C15);
        let mut value = self.state;
        value = (value ^ (value >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
        value = (value ^ (value >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
        value ^ (value >> 31)
    }

    fn roll_miss(&mut self) -> bool {
        let roll = (self.next_u64() % 100) as u8;
        roll < DuelChallenge::MISS_CHANCE_PERCENT
    }

    fn roll_crit(&mut self) -> bool {
        let roll = (self.next_u64() % 100) as u8;
        roll < DuelChallenge::CRIT_CHANCE_PERCENT
    }

    fn random_inclusive(&mut self, min: u8, max: u8) -> u8 {
        let min_u64 = u64::from(min);
        let max_u64 = u64::from(max);
        let range = max_u64
            .checked_sub(min_u64)
            .and_then(|value| value.checked_add(1))
            .unwrap_or(1);
        (min_u64 + (self.next_u64() % range)) as u8
    }

    fn next_attack_damage(&mut self) -> u8 {
        if self.roll_miss() {
            return 0;
        }
        if self.roll_crit() {
            return self.random_inclusive(
                DuelChallenge::CRIT_MIN_HIT_DAMAGE,
                DuelChallenge::CRIT_MAX_HIT_DAMAGE,
            );
        }
        self.random_inclusive(DuelChallenge::MIN_HIT_DAMAGE, DuelChallenge::MAX_HIT_DAMAGE)
    }

    fn next_bool(&mut self) -> bool {
        (self.next_u64() & 1) == 0
    }
}
