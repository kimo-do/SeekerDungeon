using UnityEngine;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Configuration constants for LG Solana program
    /// </summary>
    public static class LGConfig
    {
        // Program addresses (from devnet-config.json)
        public const string PROGRAM_ID = "3Ctc2FgnNHQtGAcZftMS4ykLhJYjLzBD3hELKy55DnKo";
        public const string SKR_MINT = "Dkpjmf6mUxxLyw9HmbdkBKhVf7zjGZZ6jNjruhjYpkiN";
        public const string GLOBAL_PDA = "9JudM6MujJyg5tBb7YaMw7DSQYVgCYNyzATzfyRSdy7G";
        public const string PRIZE_POOL_PDA = "5AuvdfSKKUsroC74RwVJ25jyhX5erMr8VCNLmj3EXQVg";
        
        // Network
        public const string RPC_URL = "https://api.devnet.solana.com";
        
        // PDA seeds
        public const string GLOBAL_SEED = "global";
        public const string PLAYER_SEED = "player";
        public const string ROOM_SEED = "room";
        public const string ESCROW_SEED = "escrow";
        public const string STAKE_SEED = "stake";
        public const string INVENTORY_SEED = "inventory";
        public const string BOSS_FIGHT_SEED = "boss_fight";
        public const string PROFILE_SEED = "profile";
        public const string PRESENCE_SEED = "presence";
        public const string PRIZE_POOL_SEED = "prize_pool";
        
        // Game constants
        public const int START_X = 5;
        public const int START_Y = 5;
        public const int MIN_COORD = 0;
        public const int MAX_COORD = 9;
        
        // Direction constants
        public const byte DIRECTION_NORTH = 0;
        public const byte DIRECTION_SOUTH = 1;
        public const byte DIRECTION_EAST = 2;
        public const byte DIRECTION_WEST = 3;
        
        // Wall state constants
        public const byte WALL_SOLID = 0;
        public const byte WALL_RUBBLE = 1;
        public const byte WALL_OPEN = 2;

        // Center state constants
        public const byte CENTER_EMPTY = 0;
        public const byte CENTER_CHEST = 1;
        public const byte CENTER_BOSS = 2;
        
        // Token constants (9 decimals)
        public const int SKR_DECIMALS = 9;
        public const ulong SKR_MULTIPLIER = 1_000_000_000;
        public const ulong STAKE_AMOUNT = 10_000_000; // 0.01 SKR
        public const ulong MIN_BOOST_TIP = 1_000_000; // 0.001 SKR
        
        /// <summary>
        /// Get direction name for debugging
        /// </summary>
        public static string GetDirectionName(byte direction)
        {
            return direction switch
            {
                DIRECTION_NORTH => "North",
                DIRECTION_SOUTH => "South",
                DIRECTION_EAST => "East",
                DIRECTION_WEST => "West",
                _ => "Unknown"
            };
        }
        
        /// <summary>
        /// Get wall state name for debugging
        /// </summary>
        public static string GetWallStateName(byte wallState)
        {
            return wallState switch
            {
                WALL_SOLID => "Solid",
                WALL_RUBBLE => "Rubble",
                WALL_OPEN => "Open",
                _ => "Unknown"
            };
        }
        
        /// <summary>
        /// Get adjacent coordinates for a direction
        /// </summary>
        public static (int x, int y) GetAdjacentCoords(int x, int y, byte direction)
        {
            return direction switch
            {
                DIRECTION_NORTH => (x, y + 1),
                DIRECTION_SOUTH => (x, y - 1),
                DIRECTION_EAST => (x + 1, y),
                DIRECTION_WEST => (x - 1, y),
                _ => (x, y)
            };
        }
    }
}
