using System;
using System.IO;
using UnityEngine;

namespace Archer
{
    /// <summary>
    /// Lightweight playtest telemetry. Records the signals we need to see where
    /// players struggle — first hit, accuracy by raid, deaths, upgrade picks and
    /// quits — as JSON lines under <see cref="Application.persistentDataPath"/>.
    /// Every call is defensive: instrumentation must never take the game down.
    /// </summary>
    public static class Telemetry
    {
        static bool _runActive;
        static bool _firstHitLogged;
        static float _runStart;
        static float _raidStart;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "last-ranger-telemetry.jsonl");

        /// <summary>Called when a fresh run begins (restart / new game).</summary>
        public static void RunStarted()
        {
            _runActive = true;
            _firstHitLogged = false;
            _runStart = Time.realtimeSinceStartup;
            Emit("run_start", "{}");
        }

        /// <summary>Start timing if the player's first run was opened without a restart.</summary>
        public static void EnsureRun()
        {
            if (!_runActive) RunStarted();
        }

        public static void RaidStarted(int raid)
        {
            if (!_runActive) RunStarted();
            _raidStart = Time.realtimeSinceStartup;
            Emit("raid_start", $"{{\"raid\":{raid}}}");
        }

        public static void RaidCleared(int raid, int shots, int hits)
        {
            float accuracy = shots > 0 ? (float)hits / shots : 0f;
            Emit("raid_clear", $"{{\"raid\":{raid},\"shots\":{shots},\"hits\":{hits},\"accuracy\":{accuracy:0.###},\"seconds\":{Elapsed():0.#}}}");
        }

        /// <summary>Seconds from run start to the player's first successful hit.</summary>
        public static void FirstHit()
        {
            if (_firstHitLogged) return;
            _firstHitLogged = true;
            Emit("first_hit", $"{{\"seconds\":{Elapsed():0.###},\"raid\":{CurrentRaid()}}}");
        }

        public static void UpgradeChosen(string kind)
        {
            Emit("upgrade", $"{{\"kind\":\"{kind}\",\"raid\":{CurrentRaid()}}}");
        }

        /// <summary>Called on death or victory.</summary>
        public static void RunEnded(bool victory, int raid)
        {
            _runActive = false;
            Emit("run_end", $"{{\"victory\":{victory.ToString().ToLowerInvariant()},\"raid\":{raid},\"seconds\":{Elapsed():0.#}}}");
        }

        /// <summary>Called when the app closes so abandoned runs are still counted.</summary>
        public static void Quit(string state, int raid)
        {
            if (!_runActive) return;
            _runActive = false;
            Emit("quit", $"{{\"state\":\"{state}\",\"raid\":{raid},\"seconds\":{Elapsed():0.#}}}");
        }

        static float Elapsed() => _runStart > 0f ? Time.realtimeSinceStartup - _runStart : 0f;

        static int CurrentRaid() => GameManager.Instance != null ? GameManager.Instance.RaidNumber : 0;

        static void Emit(string name, string data)
        {
            try
            {
                string line = $"{{\"t\":{Time.realtimeSinceStartup:0.###},\"event\":\"{name}\",\"data\":{data}}}";
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (Exception e)
            {
                // Storage full / sandboxed / WebGL without persistence — never fatal.
                Debug.LogWarning($"[Telemetry] dropped {name}: {e.Message}");
            }
        }
    }
}
