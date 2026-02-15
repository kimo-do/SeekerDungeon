using UnityEngine;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Configuration constants for LG Solana program
    /// </summary>
    public static class LGConfig
    {
        private const string DeploymentConfigResourcePath = "Solana/LGSolanaDeploymentConfig";

        // Program addresses (from devnet-config.json)
        public const string PROGRAM_ID = "3Ctc2FgnNHQtGAcZftMS4ykLhJYjLzBD3hELKy55DnKo";
        // Devnet testing token mint used today.
        public const string SKR_MINT = "Dkpjmf6mUxxLyw9HmbdkBKhVf7zjGZZ6jNjruhjYpkiN";
        // Real SKR mint on mainnet.
        public const string MAINNET_SKR_MINT = "SKRbvo6Gf7GondiT3BbTfuRDPqLWei4j2Qy2NPGZhW3";
        public const string GLOBAL_PDA = "9JudM6MujJyg5tBb7YaMw7DSQYVgCYNyzATzfyRSdy7G";
        public const string PRIZE_POOL_PDA = "5AuvdfSKKUsroC74RwVJ25jyhX5erMr8VCNLmj3EXQVg";
        
        // Network
        public const string RPC_URL = "https://api.devnet.solana.com";
        public const string RPC_FALLBACK_URL = "https://api.devnet.solana.com";
        public const string MAINNET_RPC_URL = "https://api.mainnet-beta.solana.com";
        public const string MAINNET_RPC_FALLBACK_URL = "https://api.mainnet-beta.solana.com";
        
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
        public const string LOOT_RECEIPT_SEED = "loot_receipt";
        
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
        public const byte WALL_LOCKED = 3;
        public const byte WALL_ENTRANCE_STAIRS = 4;

        // Center state constants
        public const byte CENTER_EMPTY = 0;
        public const byte CENTER_CHEST = 1;
        public const byte CENTER_BOSS = 2;
        
        // Token constants (9 decimals)
        public const int SKR_DECIMALS = 9;
        public const ulong SKR_MULTIPLIER = 1_000_000_000;
        public const ulong STAKE_AMOUNT = 10_000_000; // 0.01 SKR
        public const ulong MIN_BOOST_TIP = 1_000_000; // 0.001 SKR

        private static LGSolanaDeploymentConfig _cachedDeploymentConfig;
        private static bool _deploymentConfigLoadAttempted;

        /// <summary>
        /// Runtime-selected SKR mint. Defaults to devnet mock mint when no deployment config asset is present.
        /// </summary>
        public static SolanaRuntimeNetwork ActiveRuntimeNetwork => GetActiveRuntimeNetwork();
        public static bool IsMainnetRuntime => ActiveRuntimeNetwork == SolanaRuntimeNetwork.Mainnet;
        public static string ActiveSkrMint => GetActiveSkrMint();
        public static bool IsUsingMainnetSkrMint =>
            string.Equals(ActiveSkrMint, MAINNET_SKR_MINT, System.StringComparison.Ordinal);

        public static SolanaRuntimeNetwork GetActiveRuntimeNetwork()
        {
            var deploymentConfig = GetDeploymentConfig();
            if (deploymentConfig == null)
            {
                return SolanaRuntimeNetwork.Devnet;
            }

            return deploymentConfig.SolanaNetwork;
        }

        public static string GetRuntimeRpcUrl(string inspectorValue)
        {
            var deploymentConfig = GetDeploymentConfig();
            if (deploymentConfig == null)
            {
                // Safety guard: if no deployment config is present, default to devnet and
                // never allow accidental mainnet transaction signing from stale inspector values.
                var resolved = NormalizeUrl(inspectorValue, RPC_URL);
                return resolved.IndexOf("mainnet", System.StringComparison.OrdinalIgnoreCase) >= 0
                    ? RPC_URL
                    : resolved;
            }

            return deploymentConfig.SolanaNetwork == SolanaRuntimeNetwork.Mainnet
                ? NormalizeUrl(deploymentConfig.MainnetRpcUrl, MAINNET_RPC_URL)
                : NormalizeUrl(deploymentConfig.DevnetRpcUrl, RPC_URL);
        }

        public static string GetRuntimeFallbackRpcUrl(string inspectorValue, string resolvedPrimary)
        {
            var deploymentConfig = GetDeploymentConfig();
            var fallback = deploymentConfig == null
                ? NormalizeUrl(inspectorValue, RPC_FALLBACK_URL)
                : deploymentConfig.SolanaNetwork == SolanaRuntimeNetwork.Mainnet
                    ? NormalizeUrl(deploymentConfig.MainnetFallbackRpcUrl, MAINNET_RPC_FALLBACK_URL)
                    : NormalizeUrl(deploymentConfig.DevnetFallbackRpcUrl, RPC_FALLBACK_URL);

            if (!string.IsNullOrWhiteSpace(fallback))
            {
                if (LooksLikeClusterMismatch(resolvedPrimary, fallback))
                {
                    return NormalizeFallbackForPrimaryCluster(resolvedPrimary);
                }

                return fallback;
            }

            var resolvedFallback = NormalizeUrl(resolvedPrimary, RPC_URL);
            if (LooksLikeClusterMismatch(resolvedPrimary, resolvedFallback))
            {
                return NormalizeFallbackForPrimaryCluster(resolvedPrimary);
            }

            return resolvedFallback;
        }

        public static string GetActiveSkrMint()
        {
            var deploymentConfig = GetDeploymentConfig();
            if (deploymentConfig == null)
            {
                return SKR_MINT;
            }

            if (deploymentConfig.SkrMintMode == SkrMintMode.MainnetReal)
            {
                return MAINNET_SKR_MINT;
            }

            if (deploymentConfig.SkrMintMode == SkrMintMode.Custom)
            {
                var customMint = deploymentConfig.CustomSkrMint?.Trim();
                if (!string.IsNullOrWhiteSpace(customMint))
                {
                    return customMint;
                }
            }

            return SKR_MINT;
        }

        private static LGSolanaDeploymentConfig GetDeploymentConfig()
        {
            if (_deploymentConfigLoadAttempted)
            {
                return _cachedDeploymentConfig;
            }

            _deploymentConfigLoadAttempted = true;
            _cachedDeploymentConfig = Resources.Load<LGSolanaDeploymentConfig>(DeploymentConfigResourcePath);
            return _cachedDeploymentConfig;
        }

        private static string NormalizeUrl(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return value.Trim();
        }

        private static bool LooksLikeClusterMismatch(string primary, string fallback)
        {
            if (string.IsNullOrWhiteSpace(primary) || string.IsNullOrWhiteSpace(fallback))
            {
                return false;
            }

            var primaryIsDevnet = primary.IndexOf("devnet", System.StringComparison.OrdinalIgnoreCase) >= 0;
            var primaryIsMainnet = primary.IndexOf("mainnet", System.StringComparison.OrdinalIgnoreCase) >= 0;
            var fallbackIsDevnet = fallback.IndexOf("devnet", System.StringComparison.OrdinalIgnoreCase) >= 0;
            var fallbackIsMainnet = fallback.IndexOf("mainnet", System.StringComparison.OrdinalIgnoreCase) >= 0;

            return
                (primaryIsDevnet && fallbackIsMainnet) ||
                (primaryIsMainnet && fallbackIsDevnet);
        }

        private static string NormalizeFallbackForPrimaryCluster(string primary)
        {
            if (string.IsNullOrWhiteSpace(primary))
            {
                return RPC_FALLBACK_URL;
            }

            if (primary.IndexOf("mainnet", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MAINNET_RPC_FALLBACK_URL;
            }

            return RPC_FALLBACK_URL;
        }
        
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
                WALL_LOCKED => "Locked",
                WALL_ENTRANCE_STAIRS => "EntranceStairs",
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
