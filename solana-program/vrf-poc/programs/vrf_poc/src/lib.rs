use anchor_lang::prelude::*;
use ephemeral_vrf_sdk::anchor::vrf;
use ephemeral_vrf_sdk::instructions::{create_request_randomness_ix, RequestRandomnessParams};
use ephemeral_vrf_sdk::types::SerializableAccountMeta;

declare_id!("2JDex7BacQRsCSx5c2svgCj6or5sEgq1QpWzDSQZ4mRp");

#[program]
pub mod vrf_poc {
    use super::*;

    pub fn initialize(ctx: Context<Initialize>) -> Result<()> {
        let roll_state = &mut ctx.accounts.roll_state;
        roll_state.authority = ctx.accounts.payer.key();
        roll_state.last_random_value = 0;
        roll_state.roll_count = 0;
        roll_state.pending = false;
        roll_state.last_updated_slot = Clock::get()?.slot;
        roll_state.bump = ctx.bumps.roll_state;
        Ok(())
    }

    pub fn request_random_roll(ctx: Context<RequestRandomRoll>, client_seed: u8) -> Result<()> {
        let roll_state = &ctx.accounts.roll_state;
        require!(
            roll_state.authority == ctx.accounts.payer.key(),
            VrfPocError::Unauthorized
        );
        require!(!roll_state.pending, VrfPocError::RequestAlreadyPending);

        let callback_accounts = vec![SerializableAccountMeta {
            pubkey: roll_state.key(),
            is_signer: false,
            is_writable: true,
        }];

        let randomness_request_instruction = create_request_randomness_ix(RequestRandomnessParams {
            payer: ctx.accounts.payer.key(),
            oracle_queue: ctx.accounts.oracle_queue.key(),
            callback_program_id: ID,
            callback_discriminator: instruction::ConsumeRandomRoll::DISCRIMINATOR.to_vec(),
            caller_seed: [client_seed; 32],
            accounts_metas: Some(callback_accounts),
            ..Default::default()
        });

        ctx.accounts.invoke_signed_vrf(
            &ctx.accounts.payer.to_account_info(),
            &randomness_request_instruction,
        )?;

        ctx.accounts.roll_state.pending = true;
        Ok(())
    }

    pub fn consume_random_roll(
        ctx: Context<ConsumeRandomRoll>,
        randomness: [u8; 32],
    ) -> Result<()> {
        let roll_state = &mut ctx.accounts.roll_state;
        require!(roll_state.pending, VrfPocError::NoPendingRequest);

        let random_value = ephemeral_vrf_sdk::rnd::random_u8_with_range(&randomness, 1, 100);
        roll_state.last_random_value = random_value;
        roll_state.roll_count = roll_state.roll_count.saturating_add(1);
        roll_state.pending = false;
        roll_state.last_updated_slot = Clock::get()?.slot;

        emit!(RandomRollConsumed {
            authority: roll_state.authority,
            random_value,
            roll_count: roll_state.roll_count,
        });

        Ok(())
    }
}

#[derive(Accounts)]
pub struct Initialize<'info> {
    #[account(mut)]
    pub payer: Signer<'info>,
    #[account(
        init_if_needed,
        payer = payer,
        space = RollState::DISCRIMINATOR.len() + RollState::INIT_SPACE,
        seeds = [RollState::SEED_PREFIX, payer.key().as_ref()],
        bump
    )]
    pub roll_state: Account<'info, RollState>,
    pub system_program: Program<'info, System>,
}

#[vrf]
#[derive(Accounts)]
pub struct RequestRandomRoll<'info> {
    #[account(mut)]
    pub payer: Signer<'info>,
    #[account(
        mut,
        seeds = [RollState::SEED_PREFIX, payer.key().as_ref()],
        bump = roll_state.bump
    )]
    pub roll_state: Account<'info, RollState>,
    /// CHECK: VRF oracle queue account.
    #[account(mut)]
    pub oracle_queue: AccountInfo<'info>,
}

#[derive(Accounts)]
pub struct ConsumeRandomRoll<'info> {
    #[account(address = ephemeral_vrf_sdk::consts::VRF_PROGRAM_IDENTITY)]
    pub vrf_program_identity: Signer<'info>,
    #[account(mut)]
    pub roll_state: Account<'info, RollState>,
}

#[event]
pub struct RandomRollConsumed {
    pub authority: Pubkey,
    pub random_value: u8,
    pub roll_count: u64,
}

#[error_code]
pub enum VrfPocError {
    #[msg("Only the roll state authority may request randomness.")]
    Unauthorized,
    #[msg("A randomness request is already pending.")]
    RequestAlreadyPending,
    #[msg("No randomness request is currently pending.")]
    NoPendingRequest,
}

#[account]
#[derive(InitSpace)]
pub struct RollState {
    pub authority: Pubkey,
    pub last_random_value: u8,
    pub roll_count: u64,
    pub pending: bool,
    pub last_updated_slot: u64,
    pub bump: u8,
}

impl RollState {
    pub const SEED_PREFIX: &'static [u8] = b"roll_state";
}
