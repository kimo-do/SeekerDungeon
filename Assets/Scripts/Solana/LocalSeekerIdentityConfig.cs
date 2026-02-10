using System.Collections.Generic;
using UnityEngine;

namespace SeekerDungeon.Solana
{
    [CreateAssetMenu(
        fileName = "LocalSeekerIdentityConfig",
        menuName = "SeekerDungeon/Solana/Local Seeker Identity Config")]
    public sealed class LocalSeekerIdentityConfig : ScriptableObject
    {
        [SerializeField] private bool preferEnhancedLookup = true;
        [SerializeField] private string backendResolveUrlTemplate = string.Empty;
        [SerializeField] private List<string> fallbackBackendResolveUrlTemplates = new();
        [SerializeField] private string mainnetRpcUrl = string.Empty;
        [SerializeField] private List<string> fallbackMainnetRpcUrls = new();
        [SerializeField] private string enhancedHistoryUrlTemplate = string.Empty;
        [SerializeField] private List<string> fallbackEnhancedHistoryUrlTemplates = new();

        public bool PreferEnhancedLookup => preferEnhancedLookup;
        public string BackendResolveUrlTemplate => backendResolveUrlTemplate;
        public List<string> FallbackBackendResolveUrlTemplates => fallbackBackendResolveUrlTemplates;
        public string MainnetRpcUrl => mainnetRpcUrl;
        public List<string> FallbackMainnetRpcUrls => fallbackMainnetRpcUrls;
        public string EnhancedHistoryUrlTemplate => enhancedHistoryUrlTemplate;
        public List<string> FallbackEnhancedHistoryUrlTemplates => fallbackEnhancedHistoryUrlTemplates;
    }
}
