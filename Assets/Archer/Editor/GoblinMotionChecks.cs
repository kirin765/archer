using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Archer.EditorTools
{
    public static class GoblinMotionChecks
    {
        const string Report = "docs/verification/goblin-motion-checks.txt";
        static IEnumerator _checks;
        static double _next;
        static GameObject _scope;
        static Monster _monster;
        static float _spawnInterval;

        [MenuItem("Archer/Verify Goblin Motion Assets")]
        public static void VerifyAssets()
        {
            var prefab = Resources.Load<GameObject>("Prefabs/Goblin_Visual");
            var go = UnityEngine.Object.Instantiate(prefab);
            var graph = PlayableGraph.Create("Goblin motion verification");
            try
            {
                var animator = go.GetComponentInChildren<Animator>();
                Require(animator.avatar.isHuman && animator.avatar.isValid, "Valid Humanoid avatar");
                var motion = go.GetComponentInChildren<GoblinMotion>();
                var skin = go.GetComponentInChildren<SkinnedMeshRenderer>();
                Require(skin.bones.Length >= 20, "Skinned goblin skeleton");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var mixer = AnimationMixerPlayable.Create(graph, 2);
                var walk = AnimationClipPlayable.Create(graph, motion.Walk);
                var attack = AnimationClipPlayable.Create(graph, motion.Attack);
                walk.SetApplyFootIK(false); attack.SetApplyFootIK(false);
                graph.Connect(walk, 0, mixer, 0); graph.Connect(attack, 0, mixer, 1);
                AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(mixer);
                graph.Play();
                foreach (bool attacking in new[] { false, true })
                {
                    mixer.SetInputWeight(0, attacking ? 0 : 1); mixer.SetInputWeight(1, attacking ? 1 : 0);
                    var playable = attacking ? attack : walk;
                    var clip = attacking ? motion.Attack : motion.Walk;
                    Vector3 first = Vector3.zero;
                    float movement = 0;
                    for (int i = 0; i < 20; i++)
                    {
                        playable.SetTime(i / 20f * clip.length); graph.Evaluate(0);
                        var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                        var hip = animator.GetBoneTransform(HumanBodyBones.Hips);
                        Require(foot.position.y < hip.position.y - 0.12f, "Feet stay below hips (foot IK regression)");
                        var bone = animator.GetBoneTransform(attacking ? HumanBodyBones.RightHand : HumanBodyBones.LeftLowerLeg);
                        if (i == 0) first = bone.position;
                        movement = Mathf.Max(movement, Vector3.Distance(first, bone.position));
                    }
                    Require(movement > 0.03f, clip.name + " visibly animates joints");
                    Debug.Log("PASS " + clip.name + ": " + clip.length + " seconds; joint motion=" + movement);
                }
                Directory.CreateDirectory("docs/verification");
                File.WriteAllText(Report, "PASS: Valid Humanoid avatar, skinned mesh, walking and attack joint movement, feet below hips across 40 sampled poses.\n");
            }
            finally { graph.Destroy(); UnityEngine.Object.DestroyImmediate(go); }
        }

        [MenuItem("Archer/Run Goblin Live Motion Checks")]
        public static void RunLive()
        {
            Require(EditorApplication.isPlaying && GameManager.Instance != null, "Enter Game scene Play mode first");
            Require(_checks == null, "Checks already running");
            var gm = GameManager.Instance;
            if (gm.State == GameState.Intermission) gm.StartRaid();
            if (gm.State == GameState.Paused) gm.TogglePause();
            Require(gm.State == GameState.Playing, "Active raid required");
            _spawnInterval = gm.SpawnInterval; gm.SpawnInterval = 1000;
            _scope = new GameObject("Goblin motion verification");
            var target = new GameObject("Target").transform; target.SetParent(_scope.transform); target.position = new Vector3(0, 0, -100);
            _monster = Monster.Create(_scope.transform, target.position + Vector3.forward * 4, target, MonsterRole.Raider, 100, 1.5f, 1, 2, Color.white);
            _checks = Live(); _next = 0;
            EditorApplication.update += Tick;
        }

        static IEnumerator Live()
        {
            var gm = GameManager.Instance;
            var start = _monster.transform.position;
            var animator = _monster.GetComponentInChildren<Animator>();
            var knee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var first = knee.localRotation;
            yield return 0.35f;
            Require(Vector3.Distance(start, _monster.transform.position) > 0.1f, "Walk advances monster");
            Require(Quaternion.Angle(first, knee.localRotation) > 1f, "Walking changes knee pose");
            gm.TogglePause(); var frozenPosition = _monster.transform.position; var frozenPose = knee.localRotation;
            yield return 0.25f;
            Require(_monster.transform.position == frozenPosition && Quaternion.Angle(frozenPose, knee.localRotation) < 0.01f, "Pause freezes motion");
            gm.TogglePause();
            while (!_monster.IsWinding) yield return 0f;
            float health = gm.Health;
            _monster.TakeDamage(1, true);
            Require(!_monster.IsWinding, "Precision hit cancels attack");
            yield return 0.25f;
            Require(gm.Health == health, "Interrupted attack deals no damage");
            while (!_monster.IsWinding) yield return 0f;
            while (_monster.IsWinding) yield return 0f;
            Require(gm.Health == health - 1, "Attack deals damage once at impact");
            var visual = _monster.transform.Find("Goblin_3D_Mesh");
            Require(Mathf.Abs(visual.localPosition.z) < 0.001f, "Root motion does not move visual out of hitbox");
            _monster.TakeDamage(1000);
            Require(_monster.IsDead, "Death interrupts animation");
            yield return 1f;
            Require(_monster == null, "Death cleans up graph and monster");
            File.AppendAllText(Report, "PASS: Live walking, pause/resume, precision interruption, single impact damage, no visual root drift, death cleanup.\n");
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            try
            {
                if (!_checks.MoveNext()) { Finish(); return; }
                _next = EditorApplication.timeSinceStartup + (float)_checks.Current;
            }
            catch (Exception e) { File.AppendAllText(Report, "FAIL: " + e.Message + "\n"); Debug.LogException(e); Finish(); }
        }
        static void Finish()
        {
            EditorApplication.update -= Tick; _checks = null;
            if (GameManager.Instance != null)
            {
                if (GameManager.Instance.State == GameState.Paused) GameManager.Instance.TogglePause();
                GameManager.Instance.SpawnInterval = _spawnInterval;
            }
            if (_scope != null) UnityEngine.Object.Destroy(_scope);
            Debug.Log(File.ReadAllText(Report));
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
