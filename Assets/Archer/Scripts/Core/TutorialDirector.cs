using UnityEngine;

namespace Archer
{
    /// <summary>
    /// A short, hands-on archery lesson that runs before the first raid:
    /// hit a still target, then a moving goblin, then the exposed head above a
    /// shield. Arrows are free and practice dummies never hurt the player, so
    /// gravity and lead can be learned without punishment.
    /// </summary>
    public class TutorialDirector : MonoBehaviour
    {
        public enum Step { StillTarget, MovingTarget, ShieldedHead, Done }

        public Step Current { get; private set; } = Step.Done;
        public bool Active => Current != Step.Done;
        public int HitsThisStep { get; private set; }
        public int HitsNeeded { get; private set; } = 1;

        /// <summary>1-based lesson number for the HUD, out of <see cref="StepCount"/>.</summary>
        public int StepNumber => Current switch
        {
            Step.StillTarget => 1,
            Step.MovingTarget => 2,
            Step.ShieldedHead => 3,
            _ => 3
        };
        public const int StepCount = 3;

        GameManager _gm;
        Monster _target;
        float _stepDelay;
        float _doneDelay = -1f;

        void Start()
        {
            _gm = GameManager.Instance;
            if (_gm != null)
            {
                _gm.MonsterHit += OnMonsterHit;
                _gm.Missed += OnMiss;
            }
        }

        void OnDestroy()
        {
            if (_gm != null)
            {
                _gm.MonsterHit -= OnMonsterHit;
                _gm.Missed -= OnMiss;
            }
        }

        /// <summary>Begin (or restart) the practice. Called by GameManager.StartTutorial.</summary>
        public void Begin()
        {
            ClearTarget();
            HitsThisStep = 0;
            _doneDelay = -1f;
            Current = Step.StillTarget;
            HitsNeeded = 2;
            Announce();
            SpawnStillTarget();
        }

        void OnMonsterHit(Monster monster, bool headshot)
        {
            if (_gm == null || _gm.State != GameState.Tutorial || monster != _target || _target == null) return;

            switch (Current)
            {
                case Step.StillTarget:
                    HitsThisStep++;
                    if (HitsThisStep >= HitsNeeded) Advance();
                    else GameFeedback.Instance?.Notify(
                        GameLanguage.Text("GOOD HIT", "명중"),
                        GameLanguage.Text("One more on the target", "표적을 한 번 더 맞히세요"), 1f);
                    break;

                case Step.MovingTarget:
                    Advance();
                    break;

                case Step.ShieldedHead:
                    if (headshot) Advance();
                    else GameFeedback.Instance?.Notify(
                        GameLanguage.Text("AIM HIGHER", "더 높게 조준"),
                        GameLanguage.Text("The shield covers the body — hit the head", "방패가 몸을 가리니 머리를 노리세요"), 1.4f);
                    break;
            }
        }

        void Update()
        {
            if (_gm == null || _gm.State != GameState.Tutorial) return;

            if (_doneDelay >= 0f)
            {
                _doneDelay -= Time.deltaTime;
                if (_doneDelay <= 0f) { _doneDelay = -1f; _gm.FinishTutorial(); }
                return;
            }

            // If a practice dummy somehow walks out of play, respawn the step.
            if (_target == null && Current != Step.Done)
            {
                _stepDelay -= Time.deltaTime;
                if (_stepDelay <= 0f) SpawnCurrent();
            }
        }

        /// <summary>
        /// Practice should teach from misses too: name what went wrong without
        /// punishing the player, and respawn a target that was destroyed by a hit.
        /// </summary>
        void OnMiss(MissReason reason, Vector3 position)
        {
            if (_gm == null || _gm.State != GameState.Tutorial) return;
            switch (reason)
            {
                case MissReason.Short:
                    GameFeedback.Instance?.Notify(GameLanguage.Text("FELL SHORT", "짧게 떨어짐"),
                        GameLanguage.Text("Draw longer — hold until the bar fills", "게이지가 가득 찰 때까지 더 당기세요"), 1.4f);
                    break;
                case MissReason.Blocked:
                    GameFeedback.Instance?.Notify(GameLanguage.Text("SHIELD BLOCKED", "방패에 막혔습니다"),
                        GameLanguage.Text("Aim at the head above the shield", "방패 위 머리를 노리세요"), 1.4f);
                    break;
            }
        }

        void Advance()
        {
            ClearTarget();
            HitsThisStep = 0;

            switch (Current)
            {
                case Step.StillTarget:
                    Current = Step.MovingTarget;
                    HitsNeeded = 1;
                    Announce();
                    _stepDelay = 1f;
                    break;

                case Step.MovingTarget:
                    Current = Step.ShieldedHead;
                    HitsNeeded = 1;
                    Announce();
                    _stepDelay = 1f;
                    break;

                default:
                    Current = Step.Done;
                    GameFeedback.Instance?.Notify(
                        GameLanguage.Text("PRACTICE COMPLETE", "연습 완료"),
                        GameLanguage.Text("The raid begins", "이제 습격이 시작됩니다"), 2f);
                    _doneDelay = 1.8f;
                    break;
            }
        }

        void SpawnCurrent()
        {
            switch (Current)
            {
                case Step.StillTarget: SpawnStillTarget(); break;
                case Step.MovingTarget: SpawnMovingTarget(); break;
                case Step.ShieldedHead: SpawnShieldedHead(); break;
            }
        }

        void SpawnStillTarget()
        {
            float damage = _gm.Archer != null ? _gm.Archer.ArrowDamage : 34f;
            _target = Spawn(MonsterRole.Raider, new Vector3(0f, 0f, 11f), damage * 2f, 0f);
            HitsNeeded = 2;
        }

        void SpawnMovingTarget()
        {
            float damage = _gm.Archer != null ? _gm.Archer.ArrowDamage : 34f;
            _target = Spawn(MonsterRole.Runner, new Vector3(-3.2f, 0f, 15f), damage * 3f, 2.4f);
            HitsNeeded = 1;
        }

        void SpawnShieldedHead()
        {
            _target = Spawn(MonsterRole.Shield, new Vector3(0f, 0f, 10f), 500f, 0f);
            HitsNeeded = 1;
        }

        Monster Spawn(MonsterRole role, Vector3 position, float health, float speed)
        {
            var color = role switch
            {
                MonsterRole.Runner => new Color(0.62f, 0.80f, 0.42f),
                MonsterRole.Shield => new Color(0.54f, 0.52f, 0.32f),
                _ => new Color(0.46f, 0.64f, 0.36f)
            };
            var monster = Monster.Create(_gm.MonsterParent, position, _gm.ArcherTarget, role,
                health, speed, 0f, 999f, color, practice: true);
            _stepDelay = 1f;
            return monster;
        }

        void ClearTarget()
        {
            if (_target != null) Destroy(_target.gameObject);
            _target = null;
        }

        void Announce()
        {
            // The HUD renders ObjectiveTitle/ObjectiveHint persistently, so no
            // toast here — that would draw the same copy twice and overlap.
            _stepDelay = 1f;
        }

        /// <summary>Headline shown by the HUD during the matching step.</summary>
        public string ObjectiveTitle() => Current switch
        {
            Step.StillTarget => GameLanguage.Text($"PRACTICE {StepNumber}/{StepCount} · STILL TARGET", $"연습 {StepNumber}/{StepCount} · 고정 표적"),
            Step.MovingTarget => GameLanguage.Text($"PRACTICE {StepNumber}/{StepCount} · THE RUNNER", $"연습 {StepNumber}/{StepCount} · 질주병"),
            Step.ShieldedHead => GameLanguage.Text($"PRACTICE {StepNumber}/{StepCount} · THE SHIELD", $"연습 {StepNumber}/{StepCount} · 방패병"),
            _ => GameLanguage.Text("PRACTICE COMPLETE", "연습 완료")
        };

        public string ObjectiveHint() => Current switch
        {
            Step.StillTarget => GameLanguage.Text("Hold to draw, then release.\nHit the target twice.", "활을 길게 당긴 뒤 놓으세요.\n표적을 두 번 맞히면 됩니다."),
            Step.MovingTarget => GameLanguage.Text("Aim ahead of the runner.", "질주병의 앞을 겨누세요."),
            Step.ShieldedHead => GameLanguage.Text("The shield blocks the body.\nHit the head above it.", "방패가 몸을 가립니다.\n방패 위 머리를 맞히세요."),
            _ => GameLanguage.Text("Now hold the path.", "이제 숲길을 지키세요.")
        };
    }
}
