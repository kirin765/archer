using UnityEngine;

namespace Archer
{
    /// <summary>
    /// Persisted player preferences shared by gameplay, audio and the HUD.
    /// Kept in one place so pause/opening settings behave identically and a
    /// reset cannot leave a stale value behind.
    /// </summary>
    public static class GameSettings
    {
        const string SoundKey = "LastRanger.Sound";
        const string MusicKey = "LastRanger.MusicVolume";
        const string AmbienceKey = "LastRanger.AmbienceVolume";
        const string EffectsKey = "LastRanger.EffectsVolume";
        const string SensitivityKey = "LastRanger.AimSensitivity";
        const string ReducedMotionKey = "LastRanger.ReducedMotion";
        const string GuideKey = "LastRanger.Guide";

        public const float DefaultMusic = 1f;
        public const float DefaultAmbience = 1f;
        public const float DefaultEffects = 1f;
        public const float DefaultSensitivity = 1f;
        public const float MinSensitivity = 0.6f;
        public const float MaxSensitivity = 1.4f;

        public static bool SoundEnabled
        {
            get => PlayerPrefs.GetInt(SoundKey, 1) != 0;
            set { PlayerPrefs.SetInt(SoundKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static float MusicVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, DefaultMusic));
            set { PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        public static float AmbienceVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(AmbienceKey, DefaultAmbience));
            set { PlayerPrefs.SetFloat(AmbienceKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        public static float EffectsVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, DefaultEffects));
            set { PlayerPrefs.SetFloat(EffectsKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        public static float AimSensitivity
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, DefaultSensitivity), MinSensitivity, MaxSensitivity);
            set { PlayerPrefs.SetFloat(SensitivityKey, Mathf.Clamp(value, MinSensitivity, MaxSensitivity)); PlayerPrefs.Save(); }
        }

        public static bool ReducedMotion
        {
            get => PlayerPrefs.GetInt(ReducedMotionKey, 0) != 0;
            set { PlayerPrefs.SetInt(ReducedMotionKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool ShowAimGuide
        {
            get => PlayerPrefs.GetInt(GuideKey, 1) != 0;
            set { PlayerPrefs.SetInt(GuideKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Restore accessibility/audio defaults without touching progress or records.</summary>
        public static void RestoreDefaults()
        {
            SoundEnabled = true;
            MusicVolume = DefaultMusic;
            AmbienceVolume = DefaultAmbience;
            EffectsVolume = DefaultEffects;
            AimSensitivity = DefaultSensitivity;
            ReducedMotion = false;
            ShowAimGuide = true;
        }

        /// <summary>Cycle helper used by the compact settings rows.</summary>
        public static float NextSensitivity(float current, int direction)
        {
            float[] steps = { 0.6f, 0.8f, 1f, 1.2f, 1.4f };
            int index = 2;
            float best = float.MaxValue;
            for (int i = 0; i < steps.Length; i++)
            {
                float distance = Mathf.Abs(steps[i] - current);
                if (distance < best) { best = distance; index = i; }
            }
            index = Mathf.Clamp(index + direction, 0, steps.Length - 1);
            return steps[index];
        }

        public static float NextVolume(float current)
        {
            float[] steps = { 0f, 0.25f, 0.5f, 0.75f, 1f };
            int index = 0;
            float best = float.MaxValue;
            for (int i = 0; i < steps.Length; i++)
            {
                float distance = Mathf.Abs(steps[i] - current);
                if (distance < best) { best = distance; index = i; }
            }
            return steps[(index + 1) % steps.Length];
        }
    }
}
