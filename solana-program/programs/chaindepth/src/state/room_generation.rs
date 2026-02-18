use anchor_lang::prelude::*;

use super::{
    GlobalAccount, RoomAccount, CENTER_BOSS, CENTER_CHEST, CENTER_EMPTY, DIRECTION_NORTH,
    DIRECTION_WEST, LOCK_KIND_NONE, LOCK_KIND_SKELETON, WALL_LOCKED, WALL_OPEN, WALL_RUBBLE,
    WALL_SOLID,
};

const LOCK_MIN_DEPTH: u32 = 2;
const MAX_LOCKED_DOORS_PER_ROOM: usize = 1;
const FORCED_KEY_CHEST_MIN_DEPTH: u32 = 2;
const BONE_ROOM_MIN_DEPTH: u32 = 2;
const BONE_ROOM_CHANCE_PERCENT: u64 = 18;

pub fn calculate_depth(x: i8, y: i8) -> u32 {
    let dx = (x - GlobalAccount::START_X).abs() as u32;
    let dy = (y - GlobalAccount::START_Y).abs() as u32;
    dx.max(dy)
}

pub fn generate_room_hash(seed: u64, x: i8, y: i8) -> u64 {
    let mut hash = seed;
    hash = hash.wrapping_mul(31).wrapping_add(x as u64);
    hash = hash.wrapping_mul(31).wrapping_add(y as u64);
    hash
}

pub fn generate_walls(hash: u64, entrance_direction: u8) -> [u8; 4] {
    let mut walls = [WALL_SOLID; 4];

    for (direction, wall) in walls.iter_mut().enumerate() {
        if direction == entrance_direction as usize {
            *wall = WALL_OPEN;
            continue;
        }

        let wall_hash = (hash >> (direction * 8)) % 10;
        *wall = if wall_hash < 6 {
            WALL_RUBBLE
        } else if wall_hash < 9 {
            WALL_SOLID
        } else {
            WALL_OPEN
        };
    }

    walls
}

pub fn generate_room_center(season_seed: u64, room_x: i8, room_y: i8, depth: u32) -> (u8, u16, bool) {
    let room_hash = generate_room_hash(season_seed, room_x, room_y);
    let forced_key_drop = is_forced_key_chest(season_seed, room_x, room_y, depth);

    if depth == 1 {
        if is_forced_depth_one_chest(season_seed, room_x, room_y) || (room_hash % 100) < 50 {
            return (CENTER_CHEST, 1, false);
        }
        return (CENTER_EMPTY, 0, false);
    }

    if forced_key_drop {
        return (CENTER_CHEST, 1, true);
    }

    if depth >= 2 && (room_hash % 100) < 50 {
        let boss_id = ((room_hash % 4) + 1) as u16;
        return (CENTER_BOSS, boss_id, false);
    }

    (CENTER_EMPTY, 0, false)
}

pub fn initialize_discovered_room(
    room: &mut RoomAccount,
    season_seed: u64,
    room_x: i8,
    room_y: i8,
    entrance_direction: u8,
    created_by: Pubkey,
    created_slot: u64,
    bump: u8,
) {
    let room_depth = calculate_depth(room_x, room_y);
    let room_hash = generate_room_hash(season_seed, room_x, room_y);

    room.x = room_x;
    room.y = room_y;
    room.season_seed = season_seed;
    room.walls = generate_walls(room_hash, entrance_direction);
    RoomAccount::clamp_boundary_walls(&mut room.walls, room_x, room_y);
    room.door_lock_kinds = [LOCK_KIND_NONE; 4];
    apply_locked_doors(
        &mut room.walls,
        &mut room.door_lock_kinds,
        season_seed,
        room_x,
        room_y,
        room_depth,
        entrance_direction,
    );
    apply_bone_room_locks(
        &mut room.walls,
        &mut room.door_lock_kinds,
        season_seed,
        room_x,
        room_y,
        room_depth,
        entrance_direction,
    );
    enforce_special_room_topology(room);
    room.helper_counts = [0; 4];
    room.progress = [0; 4];
    room.start_slot = [0; 4];
    room.base_slots = [RoomAccount::calculate_base_slots(room_depth); 4];
    room.total_staked = [0; 4];
    room.job_completed = [false; 4];
    room.bonus_per_helper = [0; 4];

    let (center_type, center_id, forced_key_drop) =
        generate_room_center(season_seed, room_x, room_y, room_depth);
    let boss_max_hp = if center_type == CENTER_BOSS {
        RoomAccount::boss_hp_for_depth(room_depth, center_id)
    } else {
        0
    };

    room.has_chest = center_type == CENTER_CHEST;
    room.forced_key_drop = forced_key_drop;
    room.center_type = center_type;
    room.center_id = center_id;
    room.boss_max_hp = boss_max_hp;
    room.boss_current_hp = boss_max_hp;
    room.boss_last_update_slot = created_slot;
    room.boss_total_dps = 0;
    room.boss_fighter_count = 0;
    room.boss_defeated = false;
    room.looted_count = 0;
    room.created_by = created_by;
    room.created_slot = created_slot;
    room.bump = bump;
}

pub fn is_bone_room(season_seed: u64, room_x: i8, room_y: i8, depth: u32) -> bool {
    if depth < BONE_ROOM_MIN_DEPTH {
        return false;
    }

    // Parity gate guarantees no two orthogonally adjacent bone rooms.
    let parity = ((i16::from(room_x) + i16::from(room_y)).rem_euclid(2)) as u64;
    let target_parity = season_seed & 1;
    if parity != target_parity {
        return false;
    }

    let room_hash = generate_room_hash(season_seed ^ 0xB0DE_B0DE_B0DE_B0DE, room_x, room_y);
    (room_hash % 100) < BONE_ROOM_CHANCE_PERCENT
}

pub fn enforce_special_room_topology(room: &mut RoomAccount) {
    // Reserve start-room south edge for entrance stairs/extraction only:
    // room (5,4) north wall must never be passable or lockable.
    if room.x == GlobalAccount::START_X && room.y == GlobalAccount::START_Y - 1 {
        room.walls[DIRECTION_NORTH as usize] = WALL_SOLID;
        room.door_lock_kinds[DIRECTION_NORTH as usize] = LOCK_KIND_NONE;
    }
}

fn apply_locked_doors(
    walls: &mut [u8; 4],
    door_lock_kinds: &mut [u8; 4],
    season_seed: u64,
    room_x: i8,
    room_y: i8,
    room_depth: u32,
    entrance_direction: u8,
) {
    if room_depth < LOCK_MIN_DEPTH {
        return;
    }

    let mut eligible_directions = Vec::<u8>::new();
    for direction in 0..=DIRECTION_WEST {
        if direction == entrance_direction {
            continue;
        }
        if walls[direction as usize] == WALL_RUBBLE {
            eligible_directions.push(direction);
        }
    }

    if eligible_directions.is_empty() {
        return;
    }

    let room_hash = generate_room_hash(season_seed, room_x, room_y);
    let lock_limit = MAX_LOCKED_DOORS_PER_ROOM.min(eligible_directions.len());
    let mut locked_count = 0usize;

    for lock_attempt in 0..lock_limit {
        let lock_index = (((room_hash >> 28) as usize) + lock_attempt) % eligible_directions.len();
        let lock_direction = eligible_directions[lock_index];

        let mut remaining_non_locked_interactables = 0usize;
        for direction in 0..=DIRECTION_WEST {
            let wall = walls[direction as usize];
            if wall == WALL_OPEN || wall == WALL_RUBBLE {
                if direction != lock_direction {
                    remaining_non_locked_interactables += 1;
                }
            }
        }
        if remaining_non_locked_interactables == 0 {
            continue;
        }

        walls[lock_direction as usize] = WALL_LOCKED;
        door_lock_kinds[lock_direction as usize] = LOCK_KIND_SKELETON;
        locked_count += 1;
        if locked_count >= MAX_LOCKED_DOORS_PER_ROOM {
            break;
        }
    }
}

fn apply_bone_room_locks(
    walls: &mut [u8; 4],
    door_lock_kinds: &mut [u8; 4],
    season_seed: u64,
    room_x: i8,
    room_y: i8,
    room_depth: u32,
    entrance_direction: u8,
) {
    if room_depth < BONE_ROOM_MIN_DEPTH {
        return;
    }

    let current_is_bone_room = is_bone_room(season_seed, room_x, room_y, room_depth);
    for direction in 0..=DIRECTION_WEST {
        if !is_lockable_door_wall(walls[direction as usize]) {
            continue;
        }

        if current_is_bone_room {
            walls[direction as usize] = WALL_LOCKED;
            door_lock_kinds[direction as usize] = LOCK_KIND_SKELETON;
            continue;
        }

        // Preserve the room entry side for non-bone rooms.
        if direction == entrance_direction {
            continue;
        }

        let adjacent_x = adjacent_x(room_x, direction);
        let adjacent_y = adjacent_y(room_y, direction);
        if !is_within_dungeon_bounds(adjacent_x, adjacent_y) {
            continue;
        }

        let adjacent_depth = calculate_depth(adjacent_x, adjacent_y);
        if is_bone_room(season_seed, adjacent_x, adjacent_y, adjacent_depth) {
            walls[direction as usize] = WALL_LOCKED;
            door_lock_kinds[direction as usize] = LOCK_KIND_SKELETON;
        }
    }
}

fn is_lockable_door_wall(wall: u8) -> bool {
    wall == WALL_RUBBLE || wall == WALL_OPEN
}

fn is_within_dungeon_bounds(x: i8, y: i8) -> bool {
    x >= GlobalAccount::MIN_COORD
        && x <= GlobalAccount::MAX_COORD
        && y >= GlobalAccount::MIN_COORD
        && y <= GlobalAccount::MAX_COORD
}

fn adjacent_x(x: i8, direction: u8) -> i8 {
    match direction {
        2 => x + 1,
        3 => x - 1,
        _ => x,
    }
}

fn adjacent_y(y: i8, direction: u8) -> i8 {
    match direction {
        0 => y + 1,
        1 => y - 1,
        _ => y,
    }
}

fn is_forced_depth_one_chest(season_seed: u64, room_x: i8, room_y: i8) -> bool {
    let forced_direction = (season_seed % 4) as u8;
    let expected = match forced_direction {
        0 => (GlobalAccount::START_X, GlobalAccount::START_Y + 1),
        1 => (GlobalAccount::START_X, GlobalAccount::START_Y - 1),
        2 => (GlobalAccount::START_X + 1, GlobalAccount::START_Y),
        _ => (GlobalAccount::START_X - 1, GlobalAccount::START_Y),
    };
    room_x == expected.0 && room_y == expected.1
}

fn is_forced_key_chest(season_seed: u64, room_x: i8, room_y: i8, depth: u32) -> bool {
    if depth < FORCED_KEY_CHEST_MIN_DEPTH {
        return false;
    }

    if let Some((forced_x, forced_y)) = select_forced_key_chest_coords(season_seed, depth) {
        return room_x == forced_x && room_y == forced_y;
    }

    false
}

fn select_forced_key_chest_coords(season_seed: u64, depth: u32) -> Option<(i8, i8)> {
    let target_depth = depth as i8;
    let mut ring_coords = Vec::<(i8, i8)>::new();

    for x in GlobalAccount::MIN_COORD..=GlobalAccount::MAX_COORD {
        for y in GlobalAccount::MIN_COORD..=GlobalAccount::MAX_COORD {
            let candidate_depth = calculate_depth(x, y) as i8;
            if candidate_depth == target_depth {
                ring_coords.push((x, y));
            }
        }
    }

    if ring_coords.is_empty() {
        return None;
    }

    let ring_hash = season_seed
        .wrapping_mul(53)
        .wrapping_add(depth as u64)
        .wrapping_mul(97);
    let selected_index = (ring_hash as usize) % ring_coords.len();
    Some(ring_coords[selected_index])
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn room_generation_is_deterministic() {
        let seed = 12345u64;
        let x = 7i8;
        let y = 6i8;
        let entrance = 3u8;

        let first_hash = generate_room_hash(seed, x, y);
        let second_hash = generate_room_hash(seed, x, y);
        assert_eq!(first_hash, second_hash);

        let first_walls = generate_walls(first_hash, entrance);
        let second_walls = generate_walls(second_hash, entrance);
        assert_eq!(first_walls, second_walls);

        let depth = calculate_depth(x, y);
        let first_center = generate_room_center(seed, x, y, depth);
        let second_center = generate_room_center(seed, x, y, depth);
        assert_eq!(first_center, second_center);
    }

    #[test]
    fn no_locked_doors_before_depth_two() {
        let seed = 12345u64;
        let x = GlobalAccount::START_X;
        let y = GlobalAccount::START_Y + 1;
        let depth = calculate_depth(x, y);
        assert_eq!(depth, 1);

        let mut walls = generate_walls(generate_room_hash(seed, x, y), 1);
        let mut lock_kinds = [LOCK_KIND_NONE; 4];
        apply_locked_doors(&mut walls, &mut lock_kinds, seed, x, y, depth, 1);

        assert!(walls.iter().all(|wall| *wall != WALL_LOCKED));
        assert!(lock_kinds.iter().all(|lock_kind| *lock_kind == LOCK_KIND_NONE));
    }

    #[test]
    fn forced_key_chest_exists_for_depth_ring() {
        let seed = 424242u64;
        let depth = 3u32;
        let forced_coords = select_forced_key_chest_coords(seed, depth);
        assert!(forced_coords.is_some());

        let (forced_x, forced_y) = forced_coords.unwrap();
        assert_eq!(calculate_depth(forced_x, forced_y), depth);
        assert!(is_forced_key_chest(seed, forced_x, forced_y, depth));
    }

    #[test]
    fn room_below_start_never_opens_north() {
        let seed = 999u64;
        let mut room = RoomAccount {
            x: GlobalAccount::START_X,
            y: GlobalAccount::START_Y - 1,
            season_seed: seed,
            walls: [WALL_OPEN; 4],
            door_lock_kinds: [LOCK_KIND_SKELETON; 4],
            helper_counts: [0; 4],
            progress: [0; 4],
            start_slot: [0; 4],
            base_slots: [0; 4],
            total_staked: [0; 4],
            job_completed: [false; 4],
            bonus_per_helper: [0; 4],
            has_chest: false,
            forced_key_drop: false,
            center_type: CENTER_EMPTY,
            center_id: 0,
            boss_max_hp: 0,
            boss_current_hp: 0,
            boss_last_update_slot: 0,
            boss_total_dps: 0,
            boss_fighter_count: 0,
            boss_defeated: false,
            looted_count: 0,
            created_by: Pubkey::default(),
            created_slot: 0,
            bump: 0,
        };

        enforce_special_room_topology(&mut room);
        assert_eq!(room.walls[DIRECTION_NORTH as usize], WALL_SOLID);
        assert_eq!(room.door_lock_kinds[DIRECTION_NORTH as usize], LOCK_KIND_NONE);
    }

    #[test]
    fn bone_rooms_never_touch_orthogonally() {
        let seed = 123456u64;
        for x in GlobalAccount::MIN_COORD..=GlobalAccount::MAX_COORD {
            for y in GlobalAccount::MIN_COORD..=GlobalAccount::MAX_COORD {
                let depth = calculate_depth(x, y);
                if !is_bone_room(seed, x, y, depth) {
                    continue;
                }

                let neighbors = [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)];
                for (nx, ny) in neighbors {
                    if !is_within_dungeon_bounds(nx, ny) {
                        continue;
                    }

                    let neighbor_depth = calculate_depth(nx, ny);
                    assert!(
                        !is_bone_room(seed, nx, ny, neighbor_depth),
                        "Adjacent bone rooms found at ({x},{y}) and ({nx},{ny})"
                    );
                }
            }
        }
    }
}
