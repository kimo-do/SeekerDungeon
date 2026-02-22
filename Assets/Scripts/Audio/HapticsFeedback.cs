using System;
using Cysharp.Threading.Tasks;
using SeekerDungeon.Dungeon;
using UnityEngine;

namespace SeekerDungeon.Audio
{
    public static class HapticsFeedback
    {
        private const long ButtonTapMs = 12L;
        private const long SliderAdjustMs = 10L;
        private const long DoorTapMs = 14L;
        private const long DamageTakenMs = 18L;
        private const long DuelTickMs = 10L;
        private const long LootPulseMs = 14L;

        private static float _lastVibrateRealtime;
#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject _androidVibrator;
        private static int _androidSdkInt;
        private static bool _androidInitAttempted;
#endif

        public static void ButtonTap()
        {
            VibrateWithCooldown(0.04f, ButtonTapMs);
        }

        public static void SliderAdjust()
        {
            VibrateWithCooldown(0.06f, SliderAdjustMs);
        }

        public static void DoorTap()
        {
            VibrateWithCooldown(0.07f, DoorTapMs);
        }

        public static void DamageTaken()
        {
            VibrateWithCooldown(0.10f, DamageTakenMs);
        }

        public static void DuelDamageTick()
        {
            VibrateWithCooldown(0.06f, DuelTickMs);
        }

        public static void LootReveal(ItemRarity rarity)
        {
            if (!CanUseHaptics())
            {
                return;
            }

            var pulseCount = rarity switch
            {
                ItemRarity.Legendary => 4,
                ItemRarity.Mystic => 3,
                ItemRarity.Rare => 2,
                _ => 1
            };

            PlayPulsePatternAsync(pulseCount, 0.06f).Forget();
        }

        private static void VibrateWithCooldown(float minIntervalSeconds)
        {
            VibrateWithCooldown(minIntervalSeconds, ButtonTapMs);
        }

        private static void VibrateWithCooldown(float minIntervalSeconds, long durationMs)
        {
            if (!CanUseHaptics())
            {
                return;
            }

            var now = Time.realtimeSinceStartup;
            if (now - _lastVibrateRealtime < Mathf.Max(0f, minIntervalSeconds))
            {
                return;
            }

            _lastVibrateRealtime = now;
            EmitPulse(durationMs);
        }

        private static bool CanUseHaptics()
        {
            return Application.isMobilePlatform;
        }

        private static async UniTaskVoid PlayPulsePatternAsync(int pulseCount, float intervalSeconds)
        {
            if (pulseCount <= 0 || !CanUseHaptics())
            {
                return;
            }

            var minInterval = Mathf.Max(0f, intervalSeconds);
            for (var i = 0; i < pulseCount; i += 1)
            {
                VibrateWithCooldown(0f, LootPulseMs);
                if (i < pulseCount - 1)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(minInterval));
                }
            }
        }

        private static void EmitPulse(long durationMs)
        {
            var clampedMs = Math.Max(1L, durationMs);
#if UNITY_ANDROID && !UNITY_EDITOR
            if (TryAndroidPulse(clampedMs))
            {
                return;
            }
#endif
            Handheld.Vibrate();
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool TryAndroidPulse(long durationMs)
        {
            try
            {
                if (!EnsureAndroidVibrator())
                {
                    return false;
                }

                if (_androidSdkInt >= 26)
                {
                    using (var vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                    {
                        var vibrationEffect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                            "createOneShot",
                            durationMs,
                            -1);
                        _androidVibrator.Call("vibrate", vibrationEffect);
                        vibrationEffect.Dispose();
                        return true;
                    }
                }

                _androidVibrator.Call("vibrate", durationMs);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool EnsureAndroidVibrator()
        {
            if (_androidVibrator != null)
            {
                return true;
            }

            if (_androidInitAttempted)
            {
                return false;
            }

            _androidInitAttempted = true;

            using (var versionClass = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                _androidSdkInt = versionClass.GetStatic<int>("SDK_INT");
            }

            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (activity == null)
                {
                    return false;
                }

                _androidVibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                return _androidVibrator != null;
            }
        }
#endif
    }
}
