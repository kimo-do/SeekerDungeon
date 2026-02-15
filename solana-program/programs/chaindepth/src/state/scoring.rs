pub const TIME_BONUS_FIRST_HOUR_SLOT_STEP: u64 = 300;
pub const TIME_BONUS_POST_HOUR_SLOT_STEP: u64 = 3000;
pub const TIME_BONUS_FIRST_HOUR_SLOTS: u64 = 60 * 60 * 5; // 9000 slots at 400ms
pub const TIME_BONUS_CAP_DIVISOR: u64 = 4;
pub const TIME_BONUS_MIN_CAP: u64 = 5;

pub fn score_value_for_item(item_id: u16) -> u64 {
    match item_id {
        200 => 1,  // Silver Coin
        201 => 3,  // Gold Coin
        202 => 8,  // Gold Bar
        203 => 12, // Diamond
        204 => 10, // Ruby
        205 => 9,  // Sapphire
        206 => 9,  // Emerald
        207 => 20, // Ancient Crown
        208 => 2,  // Goblin Tooth
        209 => 15, // Dragon Scale
        210 => 11, // Cursed Amulet
        211 => 4,  // Dusty Tome
        212 => 7,  // Enchanted Scroll
        213 => 14, // Golden Chalice
        214 => 0,  // Skeleton Key
        215 => 13, // Mystic Orb
        216 => 3,  // Rusted Compass
        217 => 8,  // Dwarf Beard Ring
        218 => 18, // Phoenix Feather
        219 => 16, // Void Shard
        _ => 0,
    }
}

pub fn is_scored_loot_item(item_id: u16) -> bool {
    (200..=299).contains(&item_id)
}

pub fn compute_time_bonus(elapsed_slots: u64, loot_score: u64) -> u64 {
    let first_hour_slots = elapsed_slots.min(TIME_BONUS_FIRST_HOUR_SLOTS);
    let post_hour_slots = elapsed_slots.saturating_sub(TIME_BONUS_FIRST_HOUR_SLOTS);

    let base_time_bonus = (first_hour_slots / TIME_BONUS_FIRST_HOUR_SLOT_STEP)
        .saturating_add(post_hour_slots / TIME_BONUS_POST_HOUR_SLOT_STEP);
    let time_bonus_cap = (loot_score / TIME_BONUS_CAP_DIVISOR).max(TIME_BONUS_MIN_CAP);

    base_time_bonus.min(time_bonus_cap)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn time_bonus_front_loaded_then_hard_diminish() {
        let loot_score = 200;
        let fifty_nine_minutes_slots = 59 * 60 * 5;
        let sixty_five_minutes_slots = 65 * 60 * 5;

        let first_bonus = compute_time_bonus(fifty_nine_minutes_slots, loot_score);
        let later_bonus = compute_time_bonus(sixty_five_minutes_slots, loot_score);

        assert!(first_bonus >= 29);
        assert_eq!(later_bonus, 30);
    }

    #[test]
    fn time_bonus_respects_loot_cap() {
        let long_elapsed_slots = 6 * 60 * 60 * 5;
        let low_loot_score = 8;
        let time_bonus = compute_time_bonus(long_elapsed_slots, low_loot_score);
        assert_eq!(time_bonus, 5);
    }
}
