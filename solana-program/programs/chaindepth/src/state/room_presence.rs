use anchor_lang::prelude::*;

#[account]
#[derive(InitSpace)]
pub struct RoomPresence {
    pub player: Pubkey,
    pub season_seed: u64,
    pub room_x: i8,
    pub room_y: i8,
    pub skin_id: u16,
    pub equipped_item_id: u16,
    pub activity: u8,
    pub activity_direction: u8,
    pub is_current: bool,
    pub bump: u8,
}

impl RoomPresence {
    pub const SEED_PREFIX: &'static [u8] = b"presence";

    pub const ACTIVITY_IDLE: u8 = 0;
    pub const ACTIVITY_DOOR_JOB: u8 = 1;
    pub const ACTIVITY_BOSS_FIGHT: u8 = 2;

    pub fn set_idle(&mut self) {
        self.activity = Self::ACTIVITY_IDLE;
        self.activity_direction = 255;
    }

    pub fn set_door_job(&mut self, direction: u8) {
        self.activity = Self::ACTIVITY_DOOR_JOB;
        self.activity_direction = direction;
    }

    pub fn set_boss_fight(&mut self) {
        self.activity = Self::ACTIVITY_BOSS_FIGHT;
        self.activity_direction = 255;
    }
}
