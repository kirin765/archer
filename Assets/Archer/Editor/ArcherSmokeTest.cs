using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Archer.EditorTools
{
    /// <summary>
    /// Headless play-mode smoke test. Enters play mode on the game scene,
    /// starts a raid automatically, lets the world run for a few seconds and
    /// reports any logged errors. Run from the menu or via
    /// -executeMethod Archer.EditorTools.ArcherSmokeTest.Run
    /// </summary>
    public static class ArcherSmokeTest
    {
        const string ScenePath = "Assets/Scenes/Game.unity";
        const double RunSeconds = 6.0;

        static int _errors;
        static double _startTime;
        static bool _enteredPlay;
        static bool _started;

        [MenuItem("Archer/Run Play Smoke Test")]
        public static void Run()
        {
            _errors = 0;
            _enteredPlay = false;
            _started = false;

            Application.logMessageReceived += OnLog;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += OnUpdate;

            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying = true;
        }

        static void OnLog(string condition, string stacktrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errors++;
                Debug.Log($"[SMOKE] {type}: {condition}\n{stacktrace}");
            }
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _enteredPlay = true;
                _startTime = EditorApplication.timeSinceStartup;
            }
        }

        static void OnUpdate()
        {
            if (!_enteredPlay) return;

            if (!_started)
            {
                _started = true;
                var gm = UnityEngine.Object.FindFirstObjectByType<GameManager>();
                if (gm == null)
                {
                    _errors++;
                    Debug.LogError("[SMOKE] GameManager not found after entering play mode.");
                }
                else
                {
                    gm.StartRaid();
                    Debug.Log($"[SMOKE] Raid {gm.RaidNumber} started with {gm.MonstersThisRaid} monsters.");
                }
            }

            if (EditorApplication.timeSinceStartup - _startTime < RunSeconds) return;

            Debug.Log(_errors == 0
                ? "[SMOKE] PASS - no errors during play."
                : $"[SMOKE] FAIL - {_errors} error(s).");

            EditorApplication.update -= OnUpdate;
            EditorApplication.Exit(_errors == 0 ? 0 : 1);
        }
    }
}
