use anchor_lang::prelude::*;

use crate::errors::ChainDepthError;
use crate::state::SessionAuthority;

pub fn authorize_player_action<'info>(
    authority: &Signer<'info>,
    player: &UncheckedAccount<'info>,
    session_authority: Option<&mut Account<'info, SessionAuthority>>,
    instruction_bit: u64,
    spend_amount: u64,
) -> Result<()> {
    if authority.key() == player.key() {
        return Ok(());
    }

    let session_authority = session_authority.ok_or(ChainDepthError::Unauthorized)?;
    require!(
        session_authority.player == player.key(),
        ChainDepthError::Unauthorized
    );
    require!(
        session_authority.session_key == authority.key(),
        ChainDepthError::Unauthorized
    );
    require!(
        session_authority.is_active,
        ChainDepthError::SessionInactive
    );

    let clock = Clock::get()?;
    let session_expired_by_slot = clock.slot > session_authority.expires_at_slot;
    let session_expired_by_time =
        clock.unix_timestamp > session_authority.expires_at_unix_timestamp;
    require!(
        !session_expired_by_slot && !session_expired_by_time,
        ChainDepthError::SessionExpired
    );

    let instruction_is_allowed = (session_authority.instruction_allowlist & instruction_bit) != 0;
    require!(
        instruction_is_allowed,
        ChainDepthError::SessionInstructionNotAllowed
    );

    if spend_amount > 0 {
        let total_spent_amount = session_authority
            .spent_token_amount
            .checked_add(spend_amount)
            .ok_or(ChainDepthError::Overflow)?;
        require!(
            total_spent_amount <= session_authority.max_token_spend,
            ChainDepthError::SessionSpendCapExceeded
        );
        session_authority.spent_token_amount = total_spent_amount;
    }

    Ok(())
}
