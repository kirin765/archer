using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Archer
{
    /// <summary>Samples retargeted motion from the combat clock; gameplay owns movement and impact.</summary>
    public sealed class GoblinMotion : MonoBehaviour
    {
        public AnimationClip Walk;
        public AnimationClip Attack;
        [Range(0.1f, 0.9f)] public float ImpactTime = 0.55f;
        [Tooltip("Role cadence: runners cycle faster, shields and brutes slower.")]
        [Range(0.25f, 2.5f)] public float WalkRate = 1f;

        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        AnimationClipPlayable _walk, _attack;
        float _walkTime, _walkWeight, _attackWeight;
        bool _dirty = true;

        void Awake()
        {
            var animator = GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman || Walk == null || Attack == null || Walk.length <= 0f || Attack.length <= 0f)
            {
                enabled = false;
                return;
            }
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _graph = PlayableGraph.Create("Goblin combat motion");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            // Two inputs, not three: the idle pose and the walk pose were the
            // same clip, so the mixer used to evaluate one clip twice per
            // goblin for nothing.
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            _walk = AnimationClipPlayable.Create(_graph, Walk);
            _attack = AnimationClipPlayable.Create(_graph, Attack);
            // Generated foot IK targets use the source actor's proportions.
            // Retarget muscle motion directly to the goblin's shorter legs.
            _walk.SetApplyFootIK(false); _attack.SetApplyFootIK(false);
            _walk.SetSpeed(0); _attack.SetSpeed(0);
            _graph.Connect(_walk, 0, _mixer, 0);
            _graph.Connect(_attack, 0, _mixer, 1);
            _mixer.SetInputWeight(0, 1f);
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
            bool advance = moving;
            if (advance) _walkTime += dt * Mathf.Clamp(speed / 1.5f, 0.5f, 2.5f) * Mathf.Max(0.25f, WalkRate);
            float phase = winding ? Mathf.Lerp(0f, ImpactTime, windup)
                : Mathf.Lerp(ImpactTime, 1f, recovery);

            // A goblin that stands still and is not swinging keeps the same
            // pose, so the expensive graph evaluation is skipped entirely.
            bool poseChanged = Mathf.Abs(nextWalk - _walkWeight) > 0.0005f
                || Mathf.Abs(nextAttack - _attackWeight) > 0.0005f
                || advance
                || attacking;
            if (!poseChanged && !_dirty) return;

            _walkWeight = nextWalk;
            _attackWeight = nextAttack;
            // The idle pose used to be a second sample of the same clip at
            // time zero. Sampling the walk clip at a blended time gives the
            // same settle without evaluating the clip twice.
            float stride = _walkTime % Walk.length;
            _walk.SetTime(stride * _walkWeight);
            _attack.SetTime(phase * Attack.length);
            _mixer.SetInputWeight(0, 1f - _attackWeight);
            _mixer.SetInputWeight(1, _attackWeight);
            _graph.Evaluate(0);
            _dirty = false;
        }

        void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
