using System;
using SeekerDungeon.Dungeon;
using UnityEngine;

namespace SeekerDungeon.Audio
{
    [Serializable]
    public sealed class SceneMusicEntry
    {
        public string sceneName;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.8f;
        public AudioClip firstVisitClip;
        [Range(0f, 1f)] public float firstVisitVolume = 0.8f;
    }

    [Serializable]
    public sealed class ButtonSfxEntry
    {
        public ButtonSfxCategory category;
        public AudioClip[] clips = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float volume = 1f;
        public Vector2 pitchRange = new(1f, 1f);
    }

    [Serializable]
    public sealed class StingerSfxEntry
    {
        public StingerSfxId id;
        public AudioClip[] clips = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float volume = 1f;
        public Vector2 pitchRange = new(1f, 1f);
    }

    [Serializable]
    public sealed class WorldSfxEntry
    {
        public WorldSfxId id;
        public AudioClip[] clips = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float volume = 1f;
        public Vector2 pitchRange = new(1f, 1f);
        public bool spatialized;
        [Range(0f, 1f)] public float spatialBlend = 1f;
        public float minDistance = 1f;
        public float maxDistance = 18f;
    }

    [Serializable]
    public sealed class LoopSfxEntry
    {
        public AudioLoopId id;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        public bool spatialized;
        [Range(0f, 1f)] public float spatialBlend = 0f;
        public float minDistance = 1f;
        public float maxDistance = 18f;
    }

    [Serializable]
    public sealed class LootRaritySfxEntry
    {
        public ItemRarity rarity;
        public AudioClip[] clips = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float volume = 1f;
        public Vector2 pitchRange = new(1f, 1f);
        public bool spatialized;
        [Range(0f, 1f)] public float spatialBlend = 0f;
    }

    [Serializable]
    public sealed class MonsterAudioEntry
    {
        public ushort monsterId;
        public AudioClip loopClip;
        [Range(0f, 1f)] public float loopVolume = 0.9f;
        public bool loopSpatialized;
        [Range(0f, 1f)] public float loopSpatialBlend = 0f;
        public AudioClip[] oneShotClips = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float oneShotVolume = 0.9f;
        public Vector2 oneShotPitchRange = new(1f, 1f);
        public Vector2 oneShotIntervalSeconds = new(4f, 9f);
        public bool oneShotSpatialized;
        [Range(0f, 1f)] public float oneShotSpatialBlend = 0f;
    }

    [CreateAssetMenu(menuName = "SeekerDungeon/Audio/AudioCatalog", fileName = "AudioCatalog")]
    public sealed class AudioCatalog : ScriptableObject
    {
        public SceneMusicEntry[] sceneMusic = Array.Empty<SceneMusicEntry>();
        public LoopSfxEntry[] loops = Array.Empty<LoopSfxEntry>();
        public ButtonSfxEntry[] buttonSfx = Array.Empty<ButtonSfxEntry>();
        public StingerSfxEntry[] stingers = Array.Empty<StingerSfxEntry>();
        public WorldSfxEntry[] worldSfx = Array.Empty<WorldSfxEntry>();
        public LootRaritySfxEntry[] lootRevealByRarity = Array.Empty<LootRaritySfxEntry>();
        public MonsterAudioEntry[] monsterAudio = Array.Empty<MonsterAudioEntry>();
    }
}
