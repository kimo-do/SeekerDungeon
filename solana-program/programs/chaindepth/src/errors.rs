use anchor_lang::prelude::*;

#[error_code]
pub enum ChainDepthError {
    // Movement errors
    #[msg("Invalid move: target room is not adjacent")]
    NotAdjacent,

    #[msg("Invalid move: wall is not open")]
    WallNotOpen,

    #[msg("Invalid move: coordinates out of bounds")]
    OutOfBounds,

    // Job errors
    #[msg("Invalid direction: must be 0-3 (N/S/E/W)")]
    InvalidDirection,

    #[msg("Wall is not rubble: cannot start job")]
    NotRubble,

    #[msg("Already joined this job")]
    AlreadyJoined,

    #[msg("Job is full: maximum helpers reached")]
    JobFull,

    #[msg("Not a helper on this job")]
    NotHelper,

    #[msg("Job not ready: progress insufficient")]
    JobNotReady,

    #[msg("No active job at this location")]
    NoActiveJob,

    #[msg("Job has already been completed")]
    JobAlreadyCompleted,

    #[msg("Job has not been completed yet")]
    JobNotCompleted,

    #[msg("Too many active jobs: abandon one first")]
    TooManyActiveJobs,

    #[msg("Inventory is full")]
    InventoryFull,

    #[msg("Invalid item id")]
    InvalidItemId,

    #[msg("Invalid item amount")]
    InvalidItemAmount,

    #[msg("Not enough items")]
    InsufficientItemAmount,

    // Loot errors
    #[msg("Room has no chest")]
    NoChest,

    #[msg("Already looted this chest")]
    AlreadyLooted,

    #[msg("This chest has reached the maximum number of looters")]
    MaxLootersReached,

    #[msg("Player not in this room")]
    NotInRoom,

    #[msg("No boss in this room center")]
    NoBoss,

    #[msg("Boss is already defeated")]
    BossAlreadyDefeated,

    #[msg("Boss has not been defeated yet")]
    BossNotDefeated,

    #[msg("Player is already fighting this boss")]
    AlreadyFightingBoss,

    #[msg("Player is not a fighter for this boss")]
    NotBossFighter,

    #[msg("Invalid center type")]
    InvalidCenterType,

    #[msg("Display name is too long")]
    DisplayNameTooLong,

    #[msg("Invalid session expiry values")]
    InvalidSessionExpiry,

    #[msg("Session instruction allowlist cannot be empty")]
    InvalidSessionAllowlist,

    #[msg("Session has expired")]
    SessionExpired,

    #[msg("Session is inactive")]
    SessionInactive,

    #[msg("Instruction is not allowed by session policy")]
    SessionInstructionNotAllowed,

    #[msg("Session spend cap exceeded")]
    SessionSpendCapExceeded,

    // Season errors
    #[msg("Season has not ended yet")]
    SeasonNotEnded,

    #[msg("Unauthorized: only admin can perform this action")]
    Unauthorized,

    // Token errors
    #[msg("Insufficient balance for stake")]
    InsufficientBalance,

    #[msg("Token transfer failed")]
    TransferFailed,

    // Math errors
    #[msg("Arithmetic overflow")]
    Overflow,
}
