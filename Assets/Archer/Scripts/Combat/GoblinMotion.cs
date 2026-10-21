using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Archer
{
    /// <summary>
    /// Samples retargeted motion from the combat clock; gameplay owns movement and impact.
    /// Works with both the legacy humanoid rig and the generic Goblin_Character
    /// rig: the only requirement is that the assigned clips animate the skeleton
    /// under the same Animator.
    /// </summary>
    public sealed class GoblinMotion : MonoBehaviour
    {
        public AnimationClip Idle;
        public AnimationClip Walk;
        public AnimationClip Attack;
        [Range(0.1f, 0.9f)] public float ImpactTime = 0.55f;
        [Tooltip("Role cadence: runners cycle faster, shields and brutes slower.")]
        [Range(0.25f, 2.5f)] public float WalkRate = 1f;

        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        AnimationClipPlayable _idle, _walk, _attack;
        int _idleIndex = -1, _walkIndex, _attackIndex;
        float _idleTime, _walkTime, _walkWeight, _attackWeight;
        bool _dirty = true;

        void Awake()
        {
            var animator = GetComponent<Animator>();
            if (animator == null || Walk == null || Attack == null || Walk.length <= 0f || Attack.length <= 0f)
            {
                enabled = false;
                return;
            }
            // A generic rig has no avatar at all; a humanoid avatar is still
            // accepted for the older retargeted Mixamo clips. Anything else is
            // a mismatch and would silently play the bind pose.
            if (animator.avatar != null && (!animator.avatar.isValid || !animator.avatar.isHuman))
            {
                enabled = false;
                return;
            }
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            _graph = PlayableGraph.Create("Goblin combat motion");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

            bool hasIdle = Idle != null && Idle.length > 0f;
            int inputs = hasIdle ? 3 : 2;
            _mixer = AnimationMixerPlayable.Create(_graph, inputs);
            if (hasIdle)
            {
                _idle = AnimationClipPlayable.Create(_graph, Idle);
                _idle.SetApplyFootIK(false);
                _idle.SetSpeed(0);
                _idleIndex = 0;
            }
            _walkIndex = hasIdle ? 1 : 0;
            _attackIndex = hasIdle ? 2 : 1;

            _walk = AnimationClipPlayable.Create(_graph, Walk);
            _attack = AnimationClipPlayable.Create(_graph, Attack);
            // Generated foot IK targets use the source actor's proportions.
            // Retarget muscle/copy motion directly to the shorter legs.
            _walk.SetApplyFootIK(false);
            _attack.SetApplyFootIK(false);
            _walk.SetSpeed(0);
            _attack.SetSpeed(0);
            if (hasIdle) _graph.Connect(_idle, 0, _mixer, _idleIndex);
            _graph.Connect(_walk, 0, _mixer, _walkIndex);
            _graph.Connect(_attack, 0, _mixer, _attackIndex);
            _mixer.SetInputWeight(_idleIndex >= 0 ? _idleIndex : _walkIndex, 1f);
            AnimationPlayableOutput.Create(_graph, "Goblin pose", animator).SetSourcePlayable(_mixer);
            _graph.Play();
            _graph.Evaluate(0);
        }

        public void Sample(bool moving, float speed, bool winding, float windup, float recovery)
        {
            if (!_graph.IsValid()) return;
            bool attacking = winding || recovery > 0f;
            float dt = Time.deltaTime;
            float nextWalk = Mathf.MoveTowards(_walkWeight, moving && !attacking ? 1f : 0f, dt * 10f);
            float nextAttack = Mathf.MoveTowards(_attackWeight, attacking ? 1f : 0f, dt * 14f);
            bool idlePlaying = _idleIndex >= 0;

            if (moving) _walkTime += dt * Mathf.Clamp(speed / 1.5f, 0.5f, 2.5f) * Mathf.Max(0.25f, WalkRate);
            if (idlePlaying) _idleTime += dt;
            float phase = winding ? Mathf.Lerp(0f, ImpactTime, windup)
                : Mathf.Lerp(ImpactTime, 1f, recovery);

            // With an idle clip the pose animates even when standing still, so
            // it is evaluated every frame while that clip has weight. The
            // legacy two-clip path keeps the cheap "hold the last pose" rule.
            bool idleVisible = idlePlaying && nextWalk + nextAttack < 0.999f;
            bool poseChanged = Mathf.Abs(nextWalk - _walkWeight) > 0.0005f
                || Mathf.Abs(nextAttack - _attackWeight) > 0.0005f
                || moving
                || attacking
                || idleVisible;
            if (!poseChanged && !_dirty) return;

            _walkWeight = nextWalk;
            _attackWeight = nextAttack;
            _walk.SetTime(_walkTime % Walk.length);
            _attack.SetTime(phase * Attack.length);
            if (idlePlaying) _idle.SetTime(_idleTime % Idle.length);

            if (idlePlaying)
            {
                _mixer.SetInputWeight(_idleIndex, Mathf.Clamp01(1f - nextWalk - nextAttack));
                _mixer.SetInputWeight(_walkIndex, nextWalk);
                _mixer.SetInputWeight(_attackIndex, nextAttack);
            }
            else
            {
                _mixer.SetInputWeight(_walkIndex, 1f - nextAttack);
                _mixer.SetInputWeight(_attackIndex, nextAttack);
            }
            _graph.Evaluate(0);
            _dirty = false;
        }

        void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
