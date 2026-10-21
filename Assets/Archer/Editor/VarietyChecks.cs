using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Archer.EditorTools
{
    /// <summary>
    /// Checks the variety/accessibility pass: new upgrade synergies, defeat
    /// feedback, records, challenges and persisted settings.
    /// </summary>
    public static class VarietyChecks
    {
        static IEnumerator _checks;
        static double _next;
        static int _lastFrame, _passed;
        public static string Status { get; private set; } = "Not run";
        static readonly string ReportPath = "Temp/variety-checks.txt";

        static bool _sound, _reduced, _guide;
        static float _music, _ambience, _effects, _sensitivity;
        static int _recordScore, _recordHeadshots, _recordRaids;
        static float _recordAccuracy;

        [MenuItem("Archer/Run Variety and Accessibility Checks")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || GameManager.Instance == null)
                throw new InvalidOperationException("Open Game.unity and enter Play mode first.");
            if (_checks != null) throw new InvalidOperationException("Checks already running.");

            _sound = GameSettings.SoundEnabled;
            _music = GameSettings.MusicVolume;
            _ambience = GameSettings.AmbienceVolume;
            _effects = GameSettings.EffectsVolume;
            _sensitivity = GameSettings.AimSensitivity;
            _reduced = GameSettings.ReducedMotion;
            _guide = GameSettings.ShowAimGuide;
            _recordScore = PlayerPrefs.GetInt("LastRanger.Record.Score", 0);
            _recordAccuracy = PlayerPrefs.GetFloat("LastRanger.Record.Accuracy", 0f);
            _recordHeadshots = PlayerPrefs.GetInt("LastRanger.Record.Headshots", 0);
            _recordRaids = PlayerPrefs.GetInt("LastRanger.Record.Raids", 0);

            _passed = 0; _lastFrame = -1; _next = 0;
            Status = "Running";
            File.WriteAllText(ReportPath, $"Variety / accessibility checks — Unity {Application.unityVersion}\n");
            _checks = Checks(); EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) { Finish("FAIL: Play mode stopped during checks"); return; }
            if (EditorApplication.timeSinceStartup < _next || _lastFrame == Time.frameCount) return;
            _lastFrame = Time.frameCount;
            try
            {
                if (!_checks.MoveNext()) { Finish($"PASS: {_passed} assertions"); return; }
                _next = EditorApplication.timeSinceStartup + (_checks.Current is float seconds ? seconds : 0f);
            }
            catch (Exception e) { Finish("FAIL: " + e.Message); }
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception(label);
            _passed++; File.AppendAllText(ReportPath, "PASS " + label + "\n");
        }

        static void SetStat(GameManager gm, string name, object value)
        {
            var property = typeof(GameManager).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property == null) throw new Exception("Missing stat: " + name);
            property.SetValue(gm, value);
        }

        static IEnumerator Checks()
        {
            var gm = GameManager.Instance;
            var archer = gm.Archer;
            gm.Restart();
            yield return null;

            // 1 · Persisted accessibility settings and defaults.
            GameSettings.AimSensitivity = 1.4f;
            GameSettings.MusicVolume = 0.5f;
            GameSettings.AmbienceVolume = 0.25f;
            GameSettings.EffectsVolume = 0.75f;
            GameSettings.ReducedMotion = true;
            GameSettings.SoundEnabled = false;
            archer.ApplySettings();
            Check(Mathf.Abs(GameSettings.AimSensitivity - 1.4f) < 0.001f && Mathf.Abs(archer.AimSensitivity - 1.4f) < 0.001f,
                "Aim sensitivity persists and reaches the archer");
            Check(Mathf.Abs(GameSettings.MusicVolume - 0.5f) < 0.001f
                && Mathf.Abs(GameSettings.AmbienceVolume - 0.25f) < 0.001f
                && Mathf.Abs(GameSettings.EffectsVolume - 0.75f) < 0.001f,
                "Music, ambience and effects levels persist independently");
            Check(GameSettings.ReducedMotion && !GameSettings.SoundEnabled, "Reduced motion and master sound persist");
            GameSettings.RestoreDefaults();
            Check(GameSettings.SoundEnabled && !GameSettings.ReducedMotion
                && Mathf.Abs(GameSettings.AimSensitivity - GameSettings.DefaultSensitivity) < 0.001f,
                "Restore defaults resets accessibility without touching records");
            archer.ApplySettings();
            GameFeedback.Instance?.ApplySettings();

            // 2 · New upgrades form precision, piercing and crowd-control paths.
            var apply = typeof(GameManager).GetMethod("ApplyUpgrade", BindingFlags.NonPublic | BindingFlags.Instance);
            apply.Invoke(gm, new object[] { UpgradeKind.Piercing });
            apply.Invoke(gm, new object[] { UpgradeKind.DeepPierce });
            Check(gm.DeepPierce && gm.PierceCapacity == 2, "Split shaft adds a second piercing target");
            apply.Invoke(gm, new object[] { UpgradeKind.Slowing });
            apply.Invoke(gm, new object[] { UpgradeKind.Crippling });
            Check(gm.CripplingShot, "Crippling shot unlocks bonus damage against slowed enemies");
            apply.Invoke(gm, new object[] { UpgradeKind.Deadeye });
            Check(gm.Deadeye && gm.HeadshotDamageMultiplier > 1.75f, "Deadeye raises headshot damage");
            Check(gm.UpgradeLabel(UpgradeKind.Deadeye).Contains("Deadeye")
                && gm.UpgradeLabel(UpgradeKind.DeepPierce).Contains("Split shaft")
                && gm.UpgradeLabel(UpgradeKind.Crippling).Contains("Crippling"),
                "Synergy upgrades explain their effect in the supply card");

            // 3 · Personal records and eligibility.
            PlayerPrefs.SetInt("LastRanger.Record.Score", 0);
            PlayerPrefs.SetFloat("LastRanger.Record.Accuracy", 0f);
            PlayerPrefs.SetInt("LastRanger.Record.Headshots", 0);
            PlayerPrefs.SetInt("LastRanger.Record.Raids", 0);
            PlayerPrefs.Save();
            SetStat(gm, "Score", 4200);
            SetStat(gm, "ShotsFired", 25);
            SetStat(gm, "ShotsHit", 20);
            SetStat(gm, "Headshots", 9);
            SetStat(gm, "RaidsCleared", 4);
            var summary = PlayerRecords.SubmitRun(gm);
            Check(summary.NewScore && summary.NewHeadshots && summary.NewRaids && summary.NewAccuracy,
                "A strong run sets score, headshot, raid and eligible accuracy records");
            Check(PlayerRecords.BestScore == 4200 && PlayerRecords.BestHeadshots == 9
                && PlayerRecords.BestRaids == 4 && Mathf.Abs(PlayerRecords.BestAccuracy - 0.8f) < 0.001f,
                "Records persist their best values");
            SetStat(gm, "Score", 100);
            SetStat(gm, "ShotsFired", 10);
            SetStat(gm, "ShotsHit", 10);
            var shortRun = PlayerRecords.SubmitRun(gm);
            Check(!shortRun.AccuracyEligible && !shortRun.NewAccuracy,
                "Accuracy records require a meaningful number of shots");
            Check(gm.HeadhunterComplete && gm.UntouchedRaids == 0, "Challenge tracks headshots per run");
            SetStat(gm, "UntouchedRaids", GameManager.UntouchedTarget);
            SetStat(gm, "ThriftyRaids", GameManager.ThriftyTarget);
            Check(gm.UntouchedComplete && gm.ThriftyComplete && gm.ChallengesCompleted == 3,
                "Challenge tracks untouched and limited-arrow raids");

            // 4 · Defeat explains the killing blow after restoring a clean run.
            gm.Restart();
            gm.DamageArcher(9999f, MonsterRole.Runner);
            Check(gm.State == GameState.GameOver && gm.LastDamageSource == MonsterRole.Runner,
                "Defeat records the killing enemy role");
            Check(gm.DefeatCause.Length > 0 && gm.DefeatTip.Length > 0,
                "Defeat card has an immediate cause and one actionable tip");
            Check(gm.HasRunRecords && gm.LastRunRecords.BestScore == 4200,
                "Run end submits the personal-record comparison");
        }

        static void Finish(string status)
        {
            EditorApplication.update -= Tick; _checks = null;
            Status = status; File.AppendAllText(ReportPath, status + "\n");

            GameSettings.SoundEnabled = _sound;
            GameSettings.MusicVolume = _music;
            GameSettings.AmbienceVolume = _ambience;
            GameSettings.EffectsVolume = _effects;
            GameSettings.AimSensitivity = _sensitivity;
            GameSettings.ReducedMotion = _reduced;
            GameSettings.ShowAimGuide = _guide;

            PlayerPrefs.SetInt("LastRanger.Record.Score", _recordScore);
            PlayerPrefs.SetFloat("LastRanger.Record.Accuracy", _recordAccuracy);
            PlayerPrefs.SetInt("LastRanger.Record.Headshots", _recordHeadshots);
            PlayerPrefs.SetInt("LastRanger.Record.Raids", _recordRaids);
            PlayerPrefs.Save();

            if (GameManager.Instance != null)
            {
                if (GameManager.Instance.State == GameState.Paused) GameManager.Instance.TogglePause();
                GameManager.Instance.Restart();
            }
            Time.timeScale = 1f;
            if (status.StartsWith("PASS")) Debug.Log("[Variety checks] " + status);
            else Debug.LogError("[Variety checks] " + status);
        }
    }
}
