using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using SeekerDungeon.Dungeon;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeekerDungeon.Audio
{
    public sealed class GameAudioManager : MonoBehaviour
    {
        private const float MinFadeSeconds = 0.01f;
        private const string MutedPrefKey = "seeker_audio_muted";
        private const float DefaultPitchMin = 0.96f;
        private const float DefaultPitchMax = 1.04f;
        private const float MinimumUsablePitch = 0.85f;
        private const float DefaultSpatialMinDistance = 1.5f;
        private const float DefaultSpatialMaxDistance = 18f;
        private const string MusicEnabledPrefKey = "seeker_audio_music_enabled";
        private const string SfxEnabledPrefKey = "seeker_audio_sfx_enabled";
        private const string MusicVolumePrefKey = "seeker_audio_music_volume";
        private const string SfxVolumePrefKey = "seeker_audio_sfx_volume";

        public static GameAudioManager Instance { get; private set; }

        [SerializeField] private AudioCatalog audioCatalog;
        [SerializeField] private bool logDebugMessages;

        private readonly Dictionary<AudioLoopId, AudioSource> _loopSourcesById = new();
        private AudioSource _musicSource;
        private AudioSource _uiOneShotSource;
        private AudioSource _bossMonsterLoopSource;
        private ushort _activeBossMonsterId;
        private bool _bossMonsterActive;
        private bool _bossMonsterHasFighters;
        private Vector3 _bossMonsterWorldPos;
        private float _nextBossMonsterOneShotAt;
        private bool _isMuted;
        private bool _musicEnabled = true;
        private bool _sfxEnabled = true;
        private float _musicVolume = 1f;
        private float _sfxVolume = 1f;
        private float _activeSceneMusicBaseVolume = 1f;

        public bool IsMuted => _isMuted;
        public bool IsMusicEnabled => _musicEnabled;
        public bool IsSfxEnabled => _sfxEnabled;
        public float MusicVolume => _musicVolume;
        public float SfxVolume => _sfxVolume;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureCoreSources();
            _isMuted = PlayerPrefs.GetInt(MutedPrefKey, 0) == 1;
            _musicEnabled = PlayerPrefs.GetInt(MusicEnabledPrefKey, 1) == 1;
            _sfxEnabled = PlayerPrefs.GetInt(SfxEnabledPrefKey, 1) == 1;
            _musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumePrefKey, 1f));
            _sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumePrefKey, 1f));
            ApplyMuteState();
        }

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        }

        private void Start()
        {
            HandleSceneAudio(SceneManager.GetActiveScene());
        }

        private void Update()
        {
            TickBossMonsterOneShot();
        }

        public void PlayButton(ButtonSfxCategory category)
        {
            if (_isMuted || !_sfxEnabled || _sfxVolume <= 0.001f)
            {
                return;
            }

            var entry = FindButtonEntry(category);
            if (entry == null)
            {
                return;
            }

            var clip = PickClip(entry.clips);
            if (clip == null)
            {
                return;
            }

            EnsureCoreSources();
            _uiOneShotSource.pitch = RandomPitch(entry.pitchRange);
            _uiOneShotSource.PlayOneShot(clip, Mathf.Clamp01(entry.volume) * _sfxVolume);
        }

        public void PlayStinger(StingerSfxId id)
        {
            if (_isMuted || !_sfxEnabled || _sfxVolume <= 0.001f)
            {
                return;
            }

            var entry = FindStingerEntry(id);
            if (entry == null)
            {
                return;
            }

            var clip = PickClip(entry.clips);
            if (clip == null)
            {
                return;
            }

            EnsureCoreSources();
            _uiOneShotSource.pitch = RandomPitch(entry.pitchRange);
            _uiOneShotSource.PlayOneShot(clip, Mathf.Clamp01(entry.volume) * _sfxVolume);
        }

        public void PlayWorld(WorldSfxId id, Vector3 worldPos, bool spatialOverride = false)
        {
            if (_isMuted || !_sfxEnabled || _sfxVolume <= 0.001f)
            {
                return;
            }

            var entry = FindWorldEntryWithFallback(id);
            if (entry == null)
            {
                Log($"Missing world SFX mapping for '{id}'.");
                return;
            }

            var clip = PickClip(entry.clips);
            if (clip == null)
            {
                return;
            }

            var useSpatial = spatialOverride || entry.spatialized;
            PlayClipAtPosition(
                clip,
                worldPos,
                entry.volume * _sfxVolume,
                RandomPitch(entry.pitchRange),
                useSpatial ? entry.spatialBlend : 0f,
                entry.minDistance,
                entry.maxDistance);
        }

        public void SetLoop(AudioLoopId id, bool isActive, Vector3 worldPos)
        {
            if (id == AudioLoopId.BossMonsterLoop)
            {
                return;
            }

            var entry = FindLoopEntry(id);
            if (entry == null || entry.clip == null)
            {
                StopLoop(id);
                return;
            }

            if (!isActive)
            {
                StopLoop(id);
                return;
            }

            if (_isMuted || !_sfxEnabled || _sfxVolume <= 0.001f)
            {
                StopLoop(id);
                return;
            }

            if (!_loopSourcesById.TryGetValue(id, out var source) || source == null)
            {
                source = CreateLoopSource($"Loop_{id}");
                _loopSourcesById[id] = source;
            }

            source.transform.position = worldPos;
            source.clip = entry.clip;
            source.volume = Mathf.Clamp01(entry.volume) * _sfxVolume;
            source.pitch = 1f;
            source.loop = true;
            source.spatialBlend = entry.spatialized ? Mathf.Clamp01(entry.spatialBlend) : 0f;
            source.minDistance = Mathf.Max(0.01f, entry.minDistance);
            source.maxDistance = Mathf.Max(source.minDistance, entry.maxDistance);
            if (!source.isPlaying)
            {
                source.Play();
            }
        }

        public void PlayLootRevealByRarity(ItemRarity rarity, Vector3 worldPos)
        {
            if (_isMuted || !_sfxEnabled || _sfxVolume <= 0.001f)
            {
                return;
            }

            var entry = FindLootEntryWithFallback(rarity);
            if (entry == null)
            {
                Log($"Missing loot reveal SFX mapping for rarity '{rarity}'.");
                return;
            }

            var clip = PickClip(entry.clips);
            if (clip == null)
            {
                return;
            }

            PlayClipAtPosition(
                clip,
                worldPos,
                entry.volume * _sfxVolume,
                RandomPitch(entry.pitchRange),
                entry.spatialized ? Mathf.Clamp01(entry.spatialBlend) : 0f,
                1f,
                20f);
        }

        public async UniTask FadeOutMusicForSceneTransitionAsync(float durationSeconds)
        {
            EnsureCoreSources();
            if (_musicSource == null || !_musicSource.isPlaying)
            {
                return;
            }

            var startVolume = _musicSource.volume;
            var targetDuration = Mathf.Max(MinFadeSeconds, durationSeconds);
            var elapsed = 0f;
            while (elapsed < targetDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / targetDuration);
                _musicSource.volume = Mathf.Lerp(startVolume, 0f, t);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            _musicSource.volume = 0f;
        }

        public bool SetMuted(bool muted)
        {
            if (_isMuted == muted)
            {
                return _isMuted;
            }

            _isMuted = muted;
            PlayerPrefs.SetInt(MutedPrefKey, _isMuted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyMuteState();
            return _isMuted;
        }

        public bool ToggleMute()
        {
            return SetMuted(!_isMuted);
        }

        public void SetMusicEnabled(bool enabled)
        {
            _musicEnabled = enabled;
            PlayerPrefs.SetInt(MusicEnabledPrefKey, _musicEnabled ? 1 : 0);
            PlayerPrefs.Save();
            if (_musicEnabled)
            {
                EnsureNotMasterMuted();
            }

            ApplyMusicState();
        }

        public void SetSfxEnabled(bool enabled)
        {
            _sfxEnabled = enabled;
            PlayerPrefs.SetInt(SfxEnabledPrefKey, _sfxEnabled ? 1 : 0);
            PlayerPrefs.Save();
            if (_sfxEnabled)
            {
                EnsureNotMasterMuted();
            }

            ApplySfxState();
        }

        public void SetMusicVolume(float value)
        {
            _musicVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MusicVolumePrefKey, _musicVolume);
            PlayerPrefs.Save();
            if (_musicVolume > 0.001f)
            {
                EnsureNotMasterMuted();
            }

            ApplyMusicState();
        }

        public void SetSfxVolume(float value)
        {
            _sfxVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(SfxVolumePrefKey, _sfxVolume);
            PlayerPrefs.Save();
            if (_sfxVolume > 0.001f)
            {
                EnsureNotMasterMuted();
            }

            ApplySfxState();
        }

        public void UpdateBossMonsterAudio(ushort monsterId, bool isAlive, bool hasFighters, Vector3 worldPos)
        {
            _bossMonsterHasFighters = hasFighters;
            _bossMonsterWorldPos = worldPos;

            if (!isAlive || monsterId == 0)
            {
                StopBossMonsterAudio();
                return;
            }

            _bossMonsterActive = true;

            if (_activeBossMonsterId != monsterId)
            {
                _activeBossMonsterId = monsterId;
                _nextBossMonsterOneShotAt = 0f;
                ConfigureBossMonsterLoopForCurrent();
            }

            if (_bossMonsterLoopSource != null)
            {
                _bossMonsterLoopSource.transform.position = worldPos;
            }
        }

        public void ClearBossMonsterAudio()
        {
            StopBossMonsterAudio();
        }

        private void StopLoop(AudioLoopId id)
        {
            if (!_loopSourcesById.TryGetValue(id, out var source) || source == null)
            {
                return;
            }

            source.Stop();
            source.clip = null;
        }

        private void StopBossMonsterAudio()
        {
            _bossMonsterActive = false;
            _bossMonsterHasFighters = false;
            _activeBossMonsterId = 0;
            _nextBossMonsterOneShotAt = 0f;
            if (_bossMonsterLoopSource != null)
            {
                _bossMonsterLoopSource.Stop();
                _bossMonsterLoopSource.clip = null;
            }
        }

        private void HandleActiveSceneChanged(Scene _, Scene next)
        {
            HandleSceneAudio(next);
        }

        private void ApplyMuteState()
        {
            AudioListener.volume = _isMuted ? 0f : 1f;
            ApplyMusicState();
            ApplySfxState();
        }

        private void EnsureNotMasterMuted()
        {
            if (!_isMuted)
            {
                return;
            }

            _isMuted = false;
            PlayerPrefs.SetInt(MutedPrefKey, 0);
            PlayerPrefs.Save();
            AudioListener.volume = 1f;
        }

        private void HandleSceneAudio(Scene scene)
        {
            EnsureCoreSources();

            var musicEntry = FindSceneMusic(scene.name);
            if (musicEntry != null && musicEntry.clip != null)
            {
                if (_musicSource.clip != musicEntry.clip || !_musicSource.isPlaying)
                {
                    _musicSource.Stop();
                    _musicSource.clip = musicEntry.clip;
                    _musicSource.loop = true;
                    _activeSceneMusicBaseVolume = Mathf.Clamp01(musicEntry.volume);
                    _musicSource.volume = _activeSceneMusicBaseVolume * _musicVolume;
                    _musicSource.Play();
                }
                else
                {
                    _activeSceneMusicBaseVolume = Mathf.Clamp01(musicEntry.volume);
                    _musicSource.volume = _activeSceneMusicBaseVolume * _musicVolume;
                }
            }
            else
            {
                _musicSource.Stop();
                _musicSource.clip = null;
                _activeSceneMusicBaseVolume = 1f;
            }

            var isGameScene = string.Equals(scene.name, "GameScene", StringComparison.OrdinalIgnoreCase);
            SetLoop(AudioLoopId.GameAmbience, isGameScene, Vector3.zero);
            if (!isGameScene)
            {
                StopLoop(AudioLoopId.Mining);
                StopLoop(AudioLoopId.BossAttack);
                StopBossMonsterAudio();
            }

            if (isGameScene)
            {
                PlayStinger(StingerSfxId.DungeonEntered);
            }

            ApplyMusicState();
            ApplySfxState();
            Log($"Scene audio applied for '{scene.name}'.");
        }

        private void ApplyMusicState()
        {
            if (_musicSource == null)
            {
                return;
            }

            if (!_musicEnabled || _musicVolume <= 0.001f)
            {
                _musicSource.mute = true;
                return;
            }

            _musicSource.mute = false;
            _musicSource.volume = Mathf.Clamp01(_activeSceneMusicBaseVolume) * _musicVolume;
        }

        private void ApplySfxState()
        {
            if (!_sfxEnabled || _sfxVolume <= 0.001f)
            {
                StopLoop(AudioLoopId.GameAmbience);
                StopLoop(AudioLoopId.Mining);
                StopLoop(AudioLoopId.BossAttack);
                StopBossMonsterAudio();
                return;
            }

            if (audioCatalog?.loops != null)
            {
                for (var i = 0; i < audioCatalog.loops.Length; i += 1)
                {
                    var entry = audioCatalog.loops[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    if (_loopSourcesById.TryGetValue(entry.id, out var source) && source != null)
                    {
                        source.volume = Mathf.Clamp01(entry.volume) * _sfxVolume;
                    }
                }
            }

            if (_bossMonsterLoopSource != null && _activeBossMonsterId != 0)
            {
                var monsterEntry = FindMonsterEntry(_activeBossMonsterId);
                if (monsterEntry != null)
                {
                    _bossMonsterLoopSource.volume = Mathf.Clamp01(monsterEntry.loopVolume) * _sfxVolume;
                }
            }
        }

        private void TickBossMonsterOneShot()
        {
            if (!_bossMonsterActive || _activeBossMonsterId == 0)
            {
                return;
            }

            var monsterEntry = FindMonsterEntry(_activeBossMonsterId);
            if (monsterEntry == null || monsterEntry.oneShotClips == null || monsterEntry.oneShotClips.Length == 0)
            {
                return;
            }
            if (!_sfxEnabled || _sfxVolume <= 0.001f)
            {
                return;
            }

            var now = Time.unscaledTime;
            if (now < _nextBossMonsterOneShotAt)
            {
                return;
            }

            var clip = PickClip(monsterEntry.oneShotClips);
            if (clip != null)
            {
                var pitch = RandomPitch(monsterEntry.oneShotPitchRange);
                var spatialBlend = monsterEntry.oneShotSpatialized
                    ? Mathf.Clamp01(monsterEntry.oneShotSpatialBlend)
                    : 0f;
                PlayClipAtPosition(
                    clip,
                    _bossMonsterWorldPos,
                    monsterEntry.oneShotVolume * _sfxVolume,
                    pitch,
                    spatialBlend,
                    1f,
                    24f);
            }

            var interval = RandomRange(monsterEntry.oneShotIntervalSeconds);
            if (_bossMonsterHasFighters)
            {
                interval = Mathf.Max(0.25f, interval * 0.7f);
            }

            _nextBossMonsterOneShotAt = now + Mathf.Max(0.25f, interval);
        }

        private void ConfigureBossMonsterLoopForCurrent()
        {
            var monsterEntry = FindMonsterEntry(_activeBossMonsterId);
            if (monsterEntry == null || monsterEntry.loopClip == null)
            {
                if (_bossMonsterLoopSource != null)
                {
                    _bossMonsterLoopSource.Stop();
                    _bossMonsterLoopSource.clip = null;
                }
                return;
            }

            _bossMonsterLoopSource ??= CreateLoopSource("Loop_BossMonster");
            _bossMonsterLoopSource.transform.position = _bossMonsterWorldPos;
            _bossMonsterLoopSource.clip = monsterEntry.loopClip;
            _bossMonsterLoopSource.volume = Mathf.Clamp01(monsterEntry.loopVolume) * _sfxVolume;
            _bossMonsterLoopSource.pitch = 1f;
            _bossMonsterLoopSource.loop = true;
            _bossMonsterLoopSource.spatialBlend = monsterEntry.loopSpatialized
                ? Mathf.Clamp01(monsterEntry.loopSpatialBlend)
                : 0f;
            if (!_bossMonsterLoopSource.isPlaying)
            {
                _bossMonsterLoopSource.Play();
            }
        }

        private AudioSource CreateLoopSource(string sourceName)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            return source;
        }

        private void PlayClipAtPosition(
            AudioClip clip,
            Vector3 worldPos,
            float volume,
            float pitch,
            float spatialBlend,
            float minDistance,
            float maxDistance)
        {
            if (clip == null)
            {
                return;
            }

            var go = new GameObject($"Sfx_{clip.name}");
            go.transform.position = worldPos;
            go.transform.SetParent(transform, true);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = pitch;
            source.spatialBlend = Mathf.Clamp01(spatialBlend);
            var useSpatial = source.spatialBlend > 0.001f;
            source.minDistance = useSpatial
                ? Mathf.Max(0.01f, minDistance > 0f ? minDistance : DefaultSpatialMinDistance)
                : 1f;
            source.maxDistance = useSpatial
                ? Mathf.Max(source.minDistance + 0.01f, maxDistance > 0f ? maxDistance : DefaultSpatialMaxDistance)
                : source.minDistance;
            source.Play();
            Destroy(go, Mathf.Max(0.1f, clip.length / Mathf.Max(0.01f, Mathf.Abs(pitch))) + 0.2f);
        }

        private void EnsureCoreSources()
        {
            if (_musicSource == null)
            {
                _musicSource = CreateCoreSource("MusicSource");
                _musicSource.loop = true;
            }

            if (_uiOneShotSource == null)
            {
                _uiOneShotSource = CreateCoreSource("UiOneShotSource");
                _uiOneShotSource.loop = false;
            }
        }

        private AudioSource CreateCoreSource(string sourceName)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        private SceneMusicEntry FindSceneMusic(string sceneName)
        {
            if (audioCatalog == null || audioCatalog.sceneMusic == null)
            {
                return null;
            }

            for (var i = 0; i < audioCatalog.sceneMusic.Length; i += 1)
            {
                var entry = audioCatalog.sceneMusic[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.sceneName))
                {
                    continue;
                }

                if (string.Equals(entry.sceneName, sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private ButtonSfxEntry FindButtonEntry(ButtonSfxCategory category)
        {
            if (audioCatalog == null || audioCatalog.buttonSfx == null)
            {
                return null;
            }

            for (var i = 0; i < audioCatalog.buttonSfx.Length; i += 1)
            {
                var entry = audioCatalog.buttonSfx[i];
                if (entry != null && entry.category == category)
                {
                    return entry;
                }
            }

            return null;
        }

        private StingerSfxEntry FindStingerEntry(StingerSfxId id)
        {
            if (audioCatalog == null || audioCatalog.stingers == null)
            {
                return null;
            }

            for (var i = 0; i < audioCatalog.stingers.Length; i += 1)
            {
                var entry = audioCatalog.stingers[i];
                if (entry != null && entry.id == id)
                {
                    return entry;
                }
            }

            return null;
        }

        private WorldSfxEntry FindWorldEntry(WorldSfxId id)
        {
            if (audioCatalog == null || audioCatalog.worldSfx == null)
            {
                return null;
            }

            for (var i = 0; i < audioCatalog.worldSfx.Length; i += 1)
            {
                var entry = audioCatalog.worldSfx[i];
                if (entry != null && entry.id == id)
                {
                    return entry;
                }
            }

            return null;
        }

        private WorldSfxEntry FindWorldEntryWithFallback(WorldSfxId id)
        {
            var exact = FindWorldEntry(id);
            if (HasUsableClips(exact?.clips))
            {
                return exact;
            }

            var fallbackId = id switch
            {
                WorldSfxId.DoorOpenRubble => WorldSfxId.DoorOpenOpen,
                WorldSfxId.DoorOpenLocked => WorldSfxId.DoorOpenOpen,
                WorldSfxId.StairsExit => WorldSfxId.DoorOpenOpen,
                _ => id
            };

            if (fallbackId != id)
            {
                var fallback = FindWorldEntry(fallbackId);
                if (HasUsableClips(fallback?.clips))
                {
                    return fallback;
                }
            }

            if (audioCatalog?.worldSfx == null)
            {
                return null;
            }

            for (var i = 0; i < audioCatalog.worldSfx.Length; i += 1)
            {
                var entry = audioCatalog.worldSfx[i];
                if (HasUsableClips(entry?.clips))
                {
                    return entry;
                }
            }

            return null;
        }

        private LoopSfxEntry FindLoopEntry(AudioLoopId id)
        {
            if (audioCatalog == null || audioCatalog.loops == null)
            {
                return null;
            }

            for (var i = 0; i < audioCatalog.loops.Length; i += 1)
            {
                var entry = audioCatalog.loops[i];
                if (entry != null && entry.id == id)
                {
                    return entry;
                }
            }

            return null;
        }

        private LootRaritySfxEntry FindLootEntry(ItemRarity rarity)
        {
            if (audioCatalog == null || audioCatalog.lootRevealByRarity == null)
            {
                return null;
            }

            for (var i = 0; i < audioCatalog.lootRevealByRarity.Length; i += 1)
            {
                var entry = audioCatalog.lootRevealByRarity[i];
                if (entry != null && entry.rarity == rarity)
                {
                    return entry;
                }
            }

            return null;
        }

        private LootRaritySfxEntry FindLootEntryWithFallback(ItemRarity rarity)
        {
            var exact = FindLootEntry(rarity);
            if (HasUsableClips(exact?.clips))
            {
                return exact;
            }

            var common = FindLootEntry(ItemRarity.Common);
            if (HasUsableClips(common?.clips))
            {
                return common;
            }

            if (audioCatalog?.lootRevealByRarity == null)
            {
                return null;
            }

            for (var i = 0; i < audioCatalog.lootRevealByRarity.Length; i += 1)
            {
                var entry = audioCatalog.lootRevealByRarity[i];
                if (HasUsableClips(entry?.clips))
                {
                    return entry;
                }
            }

            return null;
        }

        private MonsterAudioEntry FindMonsterEntry(ushort monsterId)
        {
            if (audioCatalog == null || audioCatalog.monsterAudio == null)
            {
                return null;
            }

            for (var i = 0; i < audioCatalog.monsterAudio.Length; i += 1)
            {
                var entry = audioCatalog.monsterAudio[i];
                if (entry != null && entry.monsterId == monsterId)
                {
                    return entry;
                }
            }

            return null;
        }

        private static AudioClip PickClip(IReadOnlyList<AudioClip> clips)
        {
            if (clips == null || clips.Count == 0)
            {
                return null;
            }

            var validCount = 0;
            for (var i = 0; i < clips.Count; i += 1)
            {
                if (clips[i] != null)
                {
                    validCount += 1;
                }
            }

            if (validCount == 0)
            {
                return null;
            }

            var choice = UnityEngine.Random.Range(0, validCount);
            for (var i = 0; i < clips.Count; i += 1)
            {
                if (clips[i] == null)
                {
                    continue;
                }

                if (choice == 0)
                {
                    return clips[i];
                }

                choice -= 1;
            }

            return null;
        }

        private static float RandomPitch(Vector2 range)
        {
            var minRaw = Mathf.Min(range.x, range.y);
            var maxRaw = Mathf.Max(range.x, range.y);

            if (maxRaw <= 0.001f)
            {
                return UnityEngine.Random.Range(DefaultPitchMin, DefaultPitchMax);
            }

            var min = Mathf.Max(MinimumUsablePitch, minRaw);
            var max = Mathf.Max(min, maxRaw);
            return UnityEngine.Random.Range(min, max);
        }

        private static float RandomRange(Vector2 range)
        {
            var min = Mathf.Min(range.x, range.y);
            var max = Mathf.Max(range.x, range.y);
            return UnityEngine.Random.Range(min, max);
        }

        private static bool HasUsableClips(IReadOnlyList<AudioClip> clips)
        {
            if (clips == null || clips.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < clips.Count; i += 1)
            {
                if (clips[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void Log(string message)
        {
            if (logDebugMessages)
            {
                Debug.Log($"[GameAudio] {message}");
            }
        }
    }
}
