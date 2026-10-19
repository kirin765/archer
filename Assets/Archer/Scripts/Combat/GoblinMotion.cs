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

        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        AnimationClipPlayable _walk, _attack;
        float _walkTime, _walkWeight, _attackWeight;

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
            _mixer = AnimationMixerPlayable.Create(_graph, 3);
            var idle = AnimationClipPlayable.Create(_graph, Walk);
            _walk = AnimationClipPlayable.Create(_graph, Walk);
            _attack = AnimationClipPlayable.Create(_graph, Attack);
            // Generated foot IK targets use the source actor's proportions.
            // Retarget muscle motion directly to the goblin's shorter legs.
            idle.SetApplyFootIK(false); _walk.SetApplyFootIK(false); _attack.SetApplyFootIK(false);
            idle.SetSpeed(0); _walk.SetSpeed(0); _attack.SetSpeed(0);
            _graph.Connect(idle, 0, _mixer, 0);
            _graph.Connect(_walk, 0, _mixer, 1);
            _graph.Connect(_attack, 0, _mixer, 2);
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
            _walkWeight = Mathf.MoveTowards(_walkWeight, moving && !attacking ? 1f : 0f, dt * 10f);
            _attackWeight = Mathf.MoveTowards(_attackWeight, attacking ? 1f : 0f, dt * 14f);
            if (moving) _walkTime += dt * Mathf.Clamp(speed / 1.5f, 0.5f, 2.5f);
            _walk.SetTime(_walkTime % Walk.length);
            float phase = winding ? Mathf.Lerp(0f, ImpactTime, windup)
                : Mathf.Lerp(ImpactTime, 1f, recovery);
            _attack.SetTime(phase * Attack.length);
            _mixer.SetInputWeight(0, (1f - _walkWeight) * (1f - _attackWeight));
            _mixer.SetInputWeight(1, _walkWeight * (1f - _attackWeight));
            _mixer.SetInputWeight(2, _attackWeight);
            _graph.Evaluate(0);
        }

        void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
