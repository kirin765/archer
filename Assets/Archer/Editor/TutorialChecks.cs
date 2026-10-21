using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Archer.EditorTools
{
    /// <summary>
    /// Repeatable live-editor checks for the practice tutorial, the distinct miss
    /// feedback, and the shot-changing upgrades. Enter Play mode on Game.unity and
    /// run this before starting any raid. Does not exit or close the Editor.
    /// </summary>
    public static class TutorialChecks
    {
        static IEnumerator _checks;
        static double _next;
        static int _lastFrame, _passed;
        public static string Status { get; private set; } = "Not run";
        static readonly string ReportPath = "Temp/tutorial-checks.txt";

        [MenuItem("Archer/Run Tutorial and Feedback Checks")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || GameManager.Instance == null)
                throw new InvalidOperationException("Open Game.unity and enter Play mode first.");
            if (_checks != null) throw new InvalidOperationException("Checks already running.");
            if (GameManager.Instance.State != GameState.Intermission || GameManager.Instance.RaidNumber != 0)
                throw new InvalidOperationException("Run these checks in a fresh Play session (before starting a raid).");
            _passed = 0; _lastFrame = -1; _next = 0;
            Status = "Running";
            File.WriteAllText(ReportPath, $"Tutorial / feedback checks — Unity {Application.unityVersion}\n");
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

        static Monster[] PracticeMonsters()
        {
            var all = UnityEngine.Object.FindObjectsByType<Monster>(FindObjectsSortMode.None);
            var list = new System.Collections.Generic.List<Monster>();
            foreach (var m in all) if (m.IsPractice) list.Add(m);
            return list.ToArray();
        }

        static IEnumerator Checks()
        {
            var gm = GameManager.Instance;
            var feedback = GameFeedback.Instance;

            // 0 · Authored raids differ in shape, not just enemy count.
            var plan = new System.Collections.Generic.List<SpawnOrder>();
            RaidPlan.Build(4, plan);
            Check(plan.Count == 8 && plan.Exists(o => o.Role == MonsterRole.Runner) && plan.Exists(o => o.Role == MonsterRole.Shield),
                "Raid four stages a staggered shield-and-runner ambush");
            Check(plan.FindIndex(o => o.Role == MonsterRole.Shield) < plan.FindIndex(o => o.Role == MonsterRole.Runner),
                "Runners are scheduled to arrive behind the shields");
            RaidPlan.Build(5, plan);
            Check(plan[0].Role == MonsterRole.Ogre, "Raid five headlines the brute");
            Check(plan.Exists(o => o.Role == MonsterRole.Shield), "The brute is screened by shields");
            RaidPlan.Build(2, plan);
            Check(plan.Exists(o => o.Role == MonsterRole.Runner), "Raid two introduces the runner");
            RaidPlan.Build(6, plan);
            Check(plan.Count >= 8 && plan.Exists(o => o.Role == MonsterRole.Shield) && plan.Exists(o => o.Role == MonsterRole.Runner),
                "Chapter two opens with a wider, mixed wave");
            RaidPlan.Build(10, plan);
            Check(plan[0].Role == MonsterRole.Ogre && plan.Exists(o => o.Role == MonsterRole.Shield),
                "Chapter two ends with the warden and its guard");
            Check(GameManager.TotalRaids == 10 && GameManager.RaidsPerChapter == 5 && GameManager.TotalChapters == 2,
                "Two chapters contain five raids each");
            Check(GameManager.ChapterOf(1) == 1 && GameManager.ChapterOf(5) == 1
                && GameManager.ChapterOf(6) == 2 && GameManager.ChapterOf(10) == 2,
                "Raid numbers map to the correct chapter");
            Check(GameManager.RaidInChapter(5) == 5 && GameManager.RaidInChapter(6) == 1
                && GameManager.EndsChapter(5) && GameManager.EndsChapter(10),
                "Chapter boundaries land after every fifth raid");

            // 1 · A fresh run opens into practice, not straight into a raid.
            PlayerPrefs.DeleteKey("LastRanger.Practiced");
            gm.BeginRun();
            Check(gm.State == GameState.Tutorial, "New run begins with the tutorial");
            Check(gm.RaidNumber == 0, "Tutorial runs before raid one");
            Check(gm.Tutorial != null && gm.Tutorial.Active && gm.Tutorial.Current == TutorialDirector.Step.StillTarget,
                "Tutorial opens on the still target");
            yield return 0.3f;

            // Pause must work during practice and return to the tutorial, not a raid.
            gm.TogglePause();
            Check(gm.State == GameState.Paused && Time.timeScale == 0f, "Pause works during practice");
            gm.TogglePause();
            Check(gm.State == GameState.Tutorial && Time.timeScale == 1f, "Resume returns to practice");

            // 2 · Still target: two hits advance to the moving goblin.
            var targets = PracticeMonsters();
            Check(targets.Length == 1 && targets[0].Speed == 0f, "Still target is stationary practice dummy");
            // One of the two hits comes from a real ballistic arrow, not a shortcut.
            var practiceShaft = Prim.Material(Color.white);
            Arrow.Create(gm.MonsterParent, new Vector3(0f, 1.2f, 0f), new Vector3(0f, 1.2f, 26f), 5f, practiceShaft);
            yield return 0.6f;
            Check(gm.Tutorial.HitsThisStep == 1, "A real arrow hit advances the practice step");
            UnityEngine.Object.Destroy(practiceShaft);
            gm.NotifyMonsterHit(targets[0], false);
            yield return 0.2f;
            Check(gm.Tutorial.Current == TutorialDirector.Step.MovingTarget, "Two hits move practice to the runner");

            // 3 · Moving target: one hit advances to the shielded head.
            yield return 1.2f;   // let the runner respawn
            targets = PracticeMonsters();
            Check(targets.Length == 1 && targets[0].Speed > 0f, "Moving goblin approaches the archer");
            gm.NotifyMonsterHit(targets[0], false);
            yield return 0.2f;
            Check(gm.Tutorial.Current == TutorialDirector.Step.ShieldedHead, "Hit on the runner moves practice to the shield");

            // 4 · Shielded head: a body hit does not count, a headshot completes practice.
            yield return 1.2f;
            targets = PracticeMonsters();
            Check(targets.Length == 1 && targets[0].Role == MonsterRole.Shield, "Shield target uses the shield silhouette");
            gm.NotifyMonsterHit(targets[0], false);
            yield return 0.1f;
            Check(gm.Tutorial.Current == TutorialDirector.Step.ShieldedHead, "Body shot on the shield does not complete the step");
            Check(feedback != null && feedback.Message.Contains(GameLanguage.Text("AIM HIGHER", "더 높게 조준")), "Body shot on the shield coaches aiming higher");
            gm.NotifyMonsterHit(targets[0], true);
            yield return 0.2f;
            Check(gm.Tutorial.Current == TutorialDirector.Step.Done, "Headshot above the shield completes practice");

            // 5 · Practice gives way to raid one with a clean field.
            yield return 2.2f;
            Check(gm.State == GameState.Playing && gm.RaidNumber == 1, "Practice ends and raid one begins");
            Check(PracticeMonsters().Length == 0, "Practice dummies are cleared before the raid");
            gm.SpawnInterval = 1000f;   // keep authored spawns out of the deterministic checks below

            // 6 · Distinct miss feedback rather than one generic message.
            gm.ReportMiss(MissReason.Blocked, Vector3.zero);
            feedback.Hit(Vector3.zero, 0f, false, true);
            Check(feedback.Message.Contains(GameLanguage.Text("SHIELD BLOCKED", "방패에 막혔습니다")) && gm.Blocked == 1, "Shield block reports the shield and counts");
            gm.ReportMiss(MissReason.Short, Vector3.zero);
            feedback.Miss(MissReason.Short, Vector3.zero);
            Check(feedback.Message.Contains(GameLanguage.Text("FELL SHORT", "짧게 떨어짐")) && gm.FellShort == 1, "A weak shot reports it fell short");
            gm.ReportMiss(MissReason.Missed, Vector3.zero);
            feedback.Miss(MissReason.Missed, Vector3.zero);
            Check(feedback.Message.Contains(GameLanguage.Text("MISSED", "빗나갔습니다")) && gm.WideMisses == 1, "A wide shot reports a plain miss");
            Check(gm.HitStreak == 0, "Every miss kind breaks the streak");

            // 7 · Shot-changing upgrades alter behaviour, not just numbers.
            var apply = typeof(GameManager).GetMethod("ApplyUpgrade", BindingFlags.NonPublic | BindingFlags.Instance);
            apply.Invoke(gm, new object[] { UpgradeKind.Piercing });
            Check(gm.Piercing && gm.UpgradeLabel(UpgradeKind.Piercing).Contains(GameLanguage.Text("Bodkin", "관통 화살촉")), "Piercing upgrade unlocks pass-through arrows");
            apply.Invoke(gm, new object[] { UpgradeKind.Slowing });
            Check(gm.SlowingShot, "Slowing upgrade unlocks the hamstring shot");
            var dummy = Monster.Create(gm.MonsterParent, new Vector3(0f, 0f, 12f), gm.ArcherTarget,
                MonsterRole.Raider, 500f, 0f, 0f, 10f, Color.green, practice: true);
            dummy.ApplySlow(gm.SlowFactor, gm.SlowDuration);
            Check(dummy.SlowFactor < 1f, "A slowing shot actually reduces enemy speed");
            apply.Invoke(gm, new object[] { UpgradeKind.HeadshotRefund });
            Check(gm.RefundHeadshotKills, "Headshot refund upgrade unlocks arrow recovery");
            UnityEngine.Object.Destroy(dummy.gameObject);

            // 8 · A piercing arrow survives the first hit and keeps flying.
            // Clear any raid enemies that wandered into the lane so the check is
            // not decided by an unrelated goblin intercepting the arrow.
            foreach (var stray in UnityEngine.Object.FindObjectsByType<Monster>(FindObjectsSortMode.None))
                if (!stray.IsPractice) UnityEngine.Object.Destroy(stray.gameObject);
            yield return 0.1f;
            var target = Monster.Create(gm.MonsterParent, new Vector3(0f, 0f, 9f), gm.ArcherTarget,
                MonsterRole.Raider, 500f, 0f, 0f, 10f, Color.green, practice: true);
            yield return 0.1f;
            var shaft = Prim.Material(Color.white);
            var piercer = Arrow.Create(gm.MonsterParent, new Vector3(0f, 0.9f, 0f),
                new Vector3(0f, 2f, 30f), 10f, shaft, piercing: true, pierceCapacity: 1);
            yield return 0.35f;
            Check(target.Health < 500f, "Piercing arrow damages the first enemy");
            Check(piercer != null && piercer.GetComponent<Collider>().enabled, "Piercing arrow keeps flying after a hit");
            if (piercer != null) UnityEngine.Object.Destroy(piercer.gameObject);
            UnityEngine.Object.Destroy(target.gameObject);
            UnityEngine.Object.Destroy(shaft);

            // 9 · On a phone, aim is lifted above the finger so the target stays visible.
            var lifted = ArcherController.LiftForTouch(new Vector2(200f, 300f), true, 0.08f, 800f);
            Check(Mathf.Abs(lifted.y - 364f) < 0.01f && Mathf.Abs(lifted.x - 200f) < 0.01f,
                "Touch aim lifts above the finger without shifting sideways");
            var unlifted = ArcherController.LiftForTouch(new Vector2(200f, 300f), false, 0.08f, 800f);
            Check(unlifted == new Vector2(200f, 300f), "Mouse aim stays exactly on the pointer");

            // 9b · Touch drag is relative to the anchor, so the finger can stay
            // away from the reticle instead of chasing the aim point.
            var startAngles = new Vector2(0f, 10f);
            var drag = new Vector2(136f, 136f);
            var relativeA = ArcherController.RelativeAimAngles(new Vector2(100f, 100f), new Vector2(100f, 100f) + drag,
                startAngles, 0.34f, 0.5f, new Vector2(1200f, 800f), 34f, -20f, 52f);
            var relativeB = ArcherController.RelativeAimAngles(new Vector2(700f, 500f), new Vector2(700f, 500f) + drag,
                startAngles, 0.34f, 0.5f, new Vector2(1200f, 800f), 34f, -20f, 52f);
            Check(Mathf.Abs(relativeA.x - 34f) < 0.01f && Mathf.Abs(relativeA.y - 28f) < 0.01f,
                "Touch drag applies yaw and vertical sensitivity from the anchor");
            Check(Vector2.Distance(relativeA, relativeB) < 0.001f,
                "Touch aim depends on drag distance, not absolute finger position");
            var clamped = ArcherController.RelativeAimAngles(new Vector2(100f, 100f), new Vector2(1100f, 1100f),
                startAngles, 0.34f, 0.5f, new Vector2(1200f, 800f), 34f, -20f, 52f);
            Check(Mathf.Abs(clamped.x - 34f) < 0.01f && Mathf.Abs(clamped.y - 52f) < 0.01f,
                "Touch aim clamps to the configured yaw and pitch limits");

            // 10 · Playtest telemetry records the signals we need, on disk.
            var before = System.IO.File.Exists(Telemetry.FilePath) ? System.IO.File.ReadAllText(Telemetry.FilePath) : "";
            Telemetry.RaidCleared(2, 4, 3);
            var after = System.IO.File.Exists(Telemetry.FilePath) ? System.IO.File.ReadAllText(Telemetry.FilePath) : "";
            Check(after.Length > before.Length && after.Contains("raid_clear") && after.Contains("\"accuracy\":0.75"),
                "Telemetry writes per-raid accuracy to disk");
        }

        static void Finish(string status)
        {
            EditorApplication.update -= Tick; _checks = null;
            Status = status; File.AppendAllText(ReportPath, status + "\n");
            if (GameManager.Instance != null) GameManager.Instance.Restart();
            Time.timeScale = 1f;
            if (status.StartsWith("PASS")) Debug.Log("[Tutorial checks] " + status);
            else Debug.LogError("[Tutorial checks] " + status);
        }
    }
}
