using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Archer.EditorTools
{
    /// <summary>
    /// Repeatable live-editor checks for the combat polish pass: feedback
    /// classification, pooled accents, readable wind-ups, body-hit neutrality,
    /// brute interruption, shield reaction and lane clearing.
    /// </summary>
    public static class CombatPolishChecks
    {
        static IEnumerator _checks;
        static double _next;
        static int _lastFrame, _passed;
        static GameObject _scope;
        static Transform _target;
        static float _spawnInterval;
        static float _health;
        public static string Status { get; private set; } = "Not run";
        static readonly string ReportPath = "Temp/combat-polish-checks.txt";

        [MenuItem("Archer/Run Combat Polish Checks")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || GameManager.Instance == null)
                throw new InvalidOperationException("Open Game.unity and enter Play mode first.");
            if (_checks != null) throw new InvalidOperationException("Checks already running.");
            var gm = GameManager.Instance;
            _spawnInterval = gm.SpawnInterval;
            // A clean deterministic raid: the previous check run may have been
            // left running long enough for the authored wave to reach the gate.
            gm.Restart();
            _health = gm.Health;
            gm.SpawnInterval = 1000f;

            _passed = 0; _lastFrame = -1; _next = 0;
            _scope = new GameObject("Combat polish checks");
            _target = new GameObject("Check target").transform;
            _target.SetParent(_scope.transform, false);
            _target.position = Vector3.zero;

            Status = "Running";
            File.WriteAllText(ReportPath, $"Combat polish checks — Unity {Application.unityVersion}, {Screen.width}x{Screen.height}\n");
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

        static Monster Spawn(MonsterRole role, Vector3 position)
        {
            Color color = role switch
            {
                MonsterRole.Runner => new Color(0.72f, 0.86f, 0.40f),
                MonsterRole.Shield => new Color(0.54f, 0.52f, 0.32f),
                MonsterRole.Ogre => new Color(0.54f, 0.42f, 0.46f),
                _ => new Color(0.46f, 0.64f, 0.36f)
            };
            return Monster.Create(_scope.transform, position, _target, role,
                role == MonsterRole.Ogre ? 300f : 120f, 0f, 8f, 1f, color);
        }

        static IEnumerator Checks()
        {
            var gm = GameManager.Instance;
            var feedback = GameFeedback.Instance;
            var effects = CombatEffects.Instance;
            Check(feedback != null && effects != null, "Feedback and effect systems are live");

            // 1 · Role rhythm is authored, not just a speed number.
            var raider = Spawn(MonsterRole.Raider, new Vector3(0f, 0f, 1.4f));
            var runner = Spawn(MonsterRole.Runner, new Vector3(0f, 0f, 1.4f));
            var shield = Spawn(MonsterRole.Shield, new Vector3(0f, 0f, 1.6f));
            var ogre = Spawn(MonsterRole.Ogre, new Vector3(0f, 0f, 2.0f));
            yield return null;
            Check(runner.GaitRate > raider.GaitRate && raider.GaitRate > shield.GaitRate && shield.GaitRate > ogre.GaitRate,
                "Runners, raiders, shields and brutes have distinct gait rhythms");
            UnityEngine.Object.Destroy(runner.gameObject);
            UnityEngine.Object.Destroy(shield.gameObject);
            UnityEngine.Object.Destroy(ogre.gameObject);

            // 2 · A body hit flinches but never pauses or cancels the wind-up.
            while (!raider.IsWinding) yield return null;
            float before = raider.WindupRemaining;
            raider.TakeDamage(1f, false, Vector3.forward);
            int waitedFrames = 0;
            while (raider.WindupRemaining >= before - 0.001f && waitedFrames < 5) { yield return null; waitedFrames++; }
            Check(raider.IsWinding, "Body hit leaves the attack wind-up running");
            Check(raider.WindupRemaining < before - 0.001f, "Body hit does not pause the attack clock");
            healthBefore = gm.Health;
            yield return 0.7f;
            Check(gm.Health < healthBefore, "A flinching raider still lands the telegraphed blow");
            gm.Health = _health;
            UnityEngine.Object.Destroy(raider.gameObject);

            // 3 · Shield blocks react visibly without changing attack timing.
            shield = Spawn(MonsterRole.Shield, new Vector3(0f, 0f, 1.6f));
            yield return null;
            while (!shield.IsWinding) yield return null;
            before = shield.WindupRemaining;
            shield.BlockHit(shield.transform.position + Vector3.forward, Vector3.forward);
            yield return 0.06f;
            Check(shield.ShieldReaction01 > 0f, "Blocked arrow kicks the shield");
            Check(shield.IsWinding && shield.WindupRemaining < before,
                "Shield block does not cancel or pause the wind-up");
            UnityEngine.Object.Destroy(shield.gameObject);

            // 4 · Headshots intentionally break the brute and announce recovery.
            ogre = Spawn(MonsterRole.Ogre, new Vector3(0f, 0f, 2.0f));
            while (!ogre.IsWinding) yield return null;
            healthBefore = gm.Health;
            ogre.TakeDamage(1f, true, Vector3.forward);
            Check(!ogre.IsWinding && ogre.IsInterrupted, "Headshot interrupts the brute's swing");
            Check(ogre.InterruptRemaining > 0f, "Brute exposes a readable recovery window");
            Check(feedback.Message.Contains(GameLanguage.Text("INTERRUPTED", "공격 저지")),
                "Brute interrupt is called out in the HUD copy");
            float interruptBefore = ogre.InterruptRemaining;
            yield return 0.3f;
            Check(gm.Health == healthBefore, "Interrupted swing deals no damage");
            Check(ogre.InterruptRemaining < interruptBefore, "Brute recovery counts down");
            UnityEngine.Object.Destroy(ogre.gameObject);

            // 5 · Feedback chooses distinct accents and never grows the pool.
            effects.Clear();
            feedback.Hit(Vector3.zero, 25f, false, false);
            Check(!feedback.Critical && !feedback.Blocked, "Body hit reports no critical or block accent");
            feedback.Hit(Vector3.zero, 40f, true, false);
            Check(feedback.Critical && !feedback.Blocked && effects.ActiveRingCount > 0,
                "Headshot raises the critical accent and a ring");
            feedback.Hit(Vector3.zero, 0f, false, true);
            Check(feedback.Blocked && !feedback.Critical && effects.ActiveRingCount > 0,
                "Block raises the blue block accent and a ring");
            effects.Clear();
            feedback.Hit(Vector3.zero, 999f, true, false, lethal: true);
            Check(effects.ActiveRingCount == 0, "Lethal headshot leaves the ring to the kill accent");
            effects.Defeat(Vector3.zero, 1f, true);
            Check(effects.ActiveRingCount == 1, "A headshot kill produces exactly one shockwave");
            int pooledChildren = effects.transform.childCount;
            for (int i = 0; i < 24; i++) effects.Defeat(Vector3.zero, 1f, i % 2 == 0);
            Check(effects.transform.childCount == pooledChildren,
                "Repeated kills reuse the fixed effect pool");
            effects.Clear();
            Check(effects.ActiveRingCount == 0, "Clearing effects releases every pooled ring");

            // 6 · A defeat disables its colliders immediately and clears the lane.
            var doomed = Spawn(MonsterRole.Runner, new Vector3(0f, 0f, 3f));
            yield return null;
            doomed.TakeDamage(9999f, true, Vector3.forward);
            Check(doomed.IsDead, "Lethal headshot marks the enemy defeated");
            bool collidersOff = true;
            foreach (var collider in doomed.GetComponentsInChildren<Collider>())
                if (collider.enabled) collidersOff = false;
            Check(collidersOff, "Defeated enemy stops blocking shots immediately");
            yield return 0.8f;
            Check(doomed == null, "Defeated enemy clears the shooting lane promptly");

            // 7 · Record an editor-side busy-combat baseline for later comparison.
            var busy = new GameObject("Busy combat baseline");
            busy.transform.SetParent(_scope.transform, false);
            var busyTarget = new GameObject("Busy target").transform;
            busyTarget.SetParent(busy.transform, false);
            busyTarget.position = new Vector3(0f, 0f, -8f);
            for (int i = 0; i < 8; i++)
            {
                Monster.Create(busy.transform, new Vector3((i % 4 - 1.5f) * 1.2f, 0f, 8f + i * 1.5f),
                    busyTarget, i % 3 == 0 ? MonsterRole.Runner : i % 3 == 1 ? MonsterRole.Shield : MonsterRole.Raider,
                    500f, 1f, 0f, 10f, Color.green);
            }
            yield return null;
            long heapBefore = GC.GetTotalMemory(false);
            float total = 0f, worst = 0f;
            const int frames = 120;
            for (int i = 0; i < frames; i++)
            {
                if (i % 6 == 0) effects.Defeat(new Vector3(0f, 1f, 10f), 1f, i % 12 == 0);
                yield return null;
                float dt = Time.unscaledDeltaTime;
                total += dt;
                if (dt > worst) worst = dt;
            }
            long heapAfter = GC.GetTotalMemory(false);
            File.AppendAllText(ReportPath,
                $"PERF frames={frames} avgMs={(total / frames) * 1000f:0.00} worstMs={worst * 1000f:0.00} " +
                $"heapDeltaKB={(heapAfter - heapBefore) / 1024.0:0.0} (Editor baseline; compare like-for-like)\n");
            UnityEngine.Object.Destroy(busy);
        }

        static float healthBefore;

        static void Finish(string status)
        {
            EditorApplication.update -= Tick; _checks = null;
            Status = status; File.AppendAllText(ReportPath, status + "\n");
            if (_scope != null) UnityEngine.Object.Destroy(_scope);
            if (CombatEffects.Instance != null) CombatEffects.Instance.Clear();
            if (GameManager.Instance != null)
            {
                GameManager.Instance.SpawnInterval = _spawnInterval;
                if (GameManager.Instance.State == GameState.Paused) GameManager.Instance.TogglePause();
                GameManager.Instance.Health = Mathf.Max(GameManager.Instance.Health, _health);
                GameManager.Instance.Restart();
            }
            Time.timeScale = 1f;
            if (status.StartsWith("PASS")) Debug.Log("[Combat polish checks] " + status);
            else Debug.LogError("[Combat polish checks] " + status);
        }
    }
}
