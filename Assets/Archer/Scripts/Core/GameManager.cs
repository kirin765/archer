using System.Collections.Generic;
using UnityEngine;

namespace Archer
{
    public enum GameState
    {
        Playing,
        Tutorial,
        Intermission,
        GameOver,
        Paused,
        Victory
    }

    /// <summary>Free upgrades offered after each cleared raid.</summary>
    public enum UpgradeKind
    {
        Heal,
        MaxHealth,
        Damage,
        QuickDraw,
        Piercing,
        Slowing,
        Deadeye,
        DeepPierce,
        Crippling
    }

    /// <summary>Optional per-raid objective. Failure only costs the bonus, never the run.</summary>
    public enum RaidObjectiveKind
    {
        None,
        StopRunners,
        InterruptBrute
    }

    /// <summary>Why a shot failed to damage an enemy — distinct feedback for each.</summary>
    public enum MissReason
    {
        Blocked,   // stopped by a shield
        Short,     // hit the ground / scenery before reaching the enemy line
        Missed     // travelled past or wide of the enemy
    }

    /// <summary>
    /// Owns the raid loop: spawning waves, tracking archer health and switching between play / between-raid / game-over states.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>Five raids per chapter. All three chapters are authored.</summary>
        public const int RaidsPerChapter = 5;
        public const int TotalChapters = 3;
        public const int TotalRaids = RaidsPerChapter * TotalChapters;

        public static int ChapterOf(int raid) => Mathf.Clamp((raid - 1) / RaidsPerChapter + 1, 1, TotalChapters);
        public static int RaidInChapter(int raid) => ((raid - 1) % RaidsPerChapter) + 1;
        public static bool EndsChapter(int raid) => raid > 0 && raid % RaidsPerChapter == 0;
        public static string ChapterNumberWord(int chapter) => chapter switch
        {
            1 => "One",
            2 => "Two",
            3 => "Three",
            _ => chapter.ToString()
        };
        public static string ChapterName(int chapter) => chapter switch
        {
            1 => GameLanguage.Text("THE FOREST WATCH", "숲의 수호자"),
            2 => GameLanguage.Text("THE LONG NIGHT", "긴 밤"),
            3 => GameLanguage.Text("THE DARKEST HOUR", "가장 어두운 시간"),
            _ => GameLanguage.Text($"CHAPTER {chapter}", $"{chapter}장")
        };
        public static string ChapterClearTitle(int chapter) => chapter switch
        {
            1 => GameLanguage.Text("CHAPTER ONE CLEARED", "1장 완료"),
            2 => GameLanguage.Text("CHAPTER TWO CLEARED", "2장 완료"),
            3 => GameLanguage.Text("CHAPTER THREE CLEARED", "3장 완료"),
            _ => GameLanguage.Text($"CHAPTER {chapter} CLEARED", $"{chapter}장 완료")
        };
        /// <summary>Copy for the card that opens the next chapter.</summary>
        public static string ChapterStartLine(int chapter) => GameLanguage.Text(
            $"Chapter {ChapterNumberWord(chapter)} begins.\nFive raids remain.",
            $"{chapter}장이 시작됩니다.\n다섯 번의 습격이 남았습니다.");

        [Header("Archer")]
        public float MaxHealth = 100f;
        public float Health = 100f;

        [Header("Spawn lane")]
        public float SpawnHalfWidth = 8f;
        public float SpawnNearZ = 24f;
        public float SpawnFarZ = 42f;

        [Header("Upgrades")]
        public ArcherController Archer;
        public float HealAmount = 35f;
        public float MaxHealthStep = 25f;
        public float ArrowPowerStep = 8f;

        [Header("Shot-changing upgrades")]
        [Tooltip("How many enemies a piercing arrow passes through after the first.")]
        public int PierceCapacity = 1;
        [Tooltip("Movement multiplier applied by a slowing shot.")]
        public float SlowFactor = 0.45f;
        [Tooltip("Seconds a slowing shot lingers on its target.")]
        public float SlowDuration = 2.2f;
        public bool Piercing;
        public bool SlowingShot;
        [Header("Upgrade synergies")]
        public float HeadshotDamageMultiplier = 1.75f;
        public float HeadshotDamageStep = 0.35f;
        public float CrippleDamageBonus = 0.25f;
        public bool Deadeye;
        public bool DeepPierce;
        public bool CripplingShot;

        public GameState State { get; private set; } = GameState.Intermission;
        public int RaidNumber { get; private set; }
        public int MonstersRemaining { get; private set; }
        public int MonstersThisRaid { get; private set; }

        // Run statistics (results screen).
        public int Kills { get; private set; }
        public int ShotsFired { get; private set; }
        public int ShotsHit { get; private set; }
        public int RaidsCleared { get; private set; }
        public float DamageTaken { get; private set; }
        public int Score { get; private set; }
        public int Headshots { get; private set; }
        public int HitStreak { get; private set; }
        public int BestStreak { get; private set; }
        public int Blocked { get; private set; }
        public int FellShort { get; private set; }
        public int WideMisses { get; private set; }
        public int ScoreMultiplier => 1 + Mathf.Min(2, HitStreak / 3);
        public Monster Boss { get; private set; }
        public int SpawnedThisRaid => _spawnIndex;
        public float Accuracy => ShotsFired > 0 ? (float)ShotsHit / ShotsFired : 0f;

        // Optional raid objective.
        public const int ObjectiveScoreBonus = 250;
        public RaidObjectiveKind ObjectiveKind { get; private set; }
        public int ObjectiveTarget { get; private set; }
        public int ObjectiveProgress { get; private set; }
        public bool ObjectiveComplete { get; private set; }
        public bool ObjectiveFailed { get; private set; }
        public bool ObjectiveActive => ObjectiveKind != RaidObjectiveKind.None && !ObjectiveComplete && !ObjectiveFailed;
        public int ObjectivesCompleted { get; private set; }
        public int ObjectivesAttempted { get; private set; }

        // Defeat / records.
        public string DefeatCause { get; private set; } = "";
        public string DefeatTip { get; private set; } = "";
        public MonsterRole? LastDamageSource { get; private set; }
        public RunRecordSummary LastRunRecords { get; private set; }
        public bool HasRunRecords { get; private set; }
        public bool RecordsSubmitted { get; private set; }

        // Optional challenges, tracked per run.
        public const int HeadhunterTarget = 8;
        public const int UntouchedTarget = 3;
        public const int ThriftyTarget = 2;
        public const int ThriftyShotLimit = 6;
        public int UntouchedRaids { get; private set; }
        public int ThriftyRaids { get; private set; }
        public bool HeadhunterComplete => Headshots >= HeadhunterTarget;
        public bool UntouchedComplete => UntouchedRaids >= UntouchedTarget;
        public bool ThriftyComplete => ThriftyRaids >= ThriftyTarget;
        public int ChallengesCompleted => (HeadhunterComplete ? 1 : 0) + (UntouchedComplete ? 1 : 0) + (ThriftyComplete ? 1 : 0);

        /// <summary>Raised whenever a shot lands on an enemy (headshot flag included).</summary>
        public event System.Action<Monster, bool> MonsterHit;
        /// <summary>Raised whenever a shot fails, with the reason and world point.</summary>
        public event System.Action<MissReason, Vector3> Missed;

        public void RegisterShot() { if (State == GameState.Playing) ShotsFired++; }

        public void RegisterHit(bool headshot = false)
        {
            // Practice hits should not inflate the run's accuracy, but they are
            // still the player's first successful shot for playtest timing.
            if (State == GameState.Tutorial) { Telemetry.FirstHit(); return; }
            if (State != GameState.Playing) return;
            ShotsHit++;
            HitStreak++;
            BestStreak = Mathf.Max(BestStreak, HitStreak);
            if (headshot)
            {
                Headshots++;
                if (Headshots == HeadhunterTarget && !_headhunterAnnounced)
                {
                    _headhunterAnnounced = true;
                    GameFeedback.Instance?.Notify(GameLanguage.Text("CHALLENGE COMPLETE · HEADHUNTER", "도전 완료 · 명사수"),
                        GameLanguage.Text($"{HeadhunterTarget} headshots in one run", $"한 번의 도전에서 헤드샷 {HeadhunterTarget}회"), 2f);
                }
            }
            Score += (headshot ? 75 : 25) * ScoreMultiplier;
            if (ShotsHit == 1) Telemetry.FirstHit();
        }

        public void NotifyMonsterHit(Monster monster, bool headshot)
        {
            if (monster == null) return;
            MonsterHit?.Invoke(monster, headshot);
        }

        public string ObjectiveTitle() => ObjectiveKind switch
        {
            RaidObjectiveKind.StopRunners => GameLanguage.Text("STOP THE RUNNERS", "질주병 저지"),
            RaidObjectiveKind.InterruptBrute => GameLanguage.Text("BREAK THE BRUTE", "오우거 제압"),
            _ => ""
        };

        public string ObjectiveHint() => ObjectiveKind switch
        {
            RaidObjectiveKind.StopRunners => GameLanguage.Text("Kill every runner before one reaches the gate",
                "질주병이 성문에 닿기 전에 모두 처치하세요"),
            RaidObjectiveKind.InterruptBrute => GameLanguage.Text("Break the brute's wind-up with headshots",
                "헤드샷으로 오우거의 공격을 끊으세요"),
            _ => ""
        };

        public string ObjectiveProgressText()
        {
            if (ObjectiveKind == RaidObjectiveKind.None) return "";
            string count = $"{Mathf.Min(ObjectiveProgress, ObjectiveTarget)}/{ObjectiveTarget}";
            if (ObjectiveComplete) return GameLanguage.Text($"{count} · COMPLETE", $"{count} · 완료");
            if (ObjectiveFailed) return GameLanguage.Text($"{count} · FAILED", $"{count} · 실패");
            return count;
        }

        /// <summary>Called by a monster when a precision hit breaks its wind-up.</summary>
        public void NotifyMonsterInterrupted(Monster monster)
        {
            if (State != GameState.Playing || !ObjectiveActive || ObjectiveKind != RaidObjectiveKind.InterruptBrute)
                return;
            if (monster == null || monster.Role != MonsterRole.Ogre) return;
            ObjectiveProgress++;
            if (ObjectiveProgress >= ObjectiveTarget) CompleteObjective();
        }

        /// <summary>A runner reached the archer: the optional stop-the-runners objective fails.</summary>
        public void NotifyRunnerReachedGate(Monster monster)
        {
            if (State != GameState.Playing || !ObjectiveActive || ObjectiveKind != RaidObjectiveKind.StopRunners)
                return;
            if (monster == null || monster.Role != MonsterRole.Runner) return;
            FailObjective();
        }

        void CompleteObjective()
        {
            if (!ObjectiveActive) return;
            ObjectiveComplete = true;
            ObjectiveProgress = ObjectiveTarget;
            ObjectivesCompleted++;
            Score += ObjectiveScoreBonus;
            Telemetry.ObjectiveFinished(ObjectiveKind.ToString(), true, ObjectiveProgress, ObjectiveTarget);
            GameFeedback.Instance?.Notify(GameLanguage.Text("OBJECTIVE COMPLETE", "목표 달성"),
                GameLanguage.Text($"Bonus +{ObjectiveScoreBonus} points", $"보너스 +{ObjectiveScoreBonus}점"), 2f);
            GameFeedback.Instance?.Play(FeedbackSound.Clear, 0.55f);
        }

        void FailObjective()
        {
            if (!ObjectiveActive) return;
            ObjectiveFailed = true;
            Telemetry.ObjectiveFinished(ObjectiveKind.ToString(), false, ObjectiveProgress, ObjectiveTarget);
            GameFeedback.Instance?.Notify(GameLanguage.Text("OBJECTIVE FAILED", "목표 실패"),
                ObjectiveHint(), 2f);
        }

        void ConfigureObjective()
        {
            ObjectiveKind = RaidObjectiveKind.None;
            ObjectiveTarget = 0;
            ObjectiveProgress = 0;
            ObjectiveComplete = false;
            ObjectiveFailed = false;

            int runners = 0, ogres = 0;
            foreach (var order in _spawnQueue)
            {
                if (order.Role == MonsterRole.Runner) runners++;
                else if (order.Role == MonsterRole.Ogre) ogres++;
            }

            switch (RaidNumber)
            {
                case 2:
                    if (runners > 0) { ObjectiveKind = RaidObjectiveKind.StopRunners; ObjectiveTarget = runners; }
                    break;
                case 5:
                    if (ogres > 0) { ObjectiveKind = RaidObjectiveKind.InterruptBrute; ObjectiveTarget = 2; }
                    break;
                case 8:
                    if (runners > 0) { ObjectiveKind = RaidObjectiveKind.StopRunners; ObjectiveTarget = runners; }
                    break;
                case 9:
                    if (ogres > 0) { ObjectiveKind = RaidObjectiveKind.InterruptBrute; ObjectiveTarget = 1; }
                    break;
                case 10:
                    if (ogres > 0) { ObjectiveKind = RaidObjectiveKind.InterruptBrute; ObjectiveTarget = 2; }
                    break;
                // Chapter three alternates the two lessons: clear the runners
                // before they reach the gate, then break every brute's swing.
                case 11:
                case 13:
                    if (runners > 0) { ObjectiveKind = RaidObjectiveKind.StopRunners; ObjectiveTarget = runners; }
                    break;
                case 12:
                case 14:
                case 15:
                    if (ogres > 0) { ObjectiveKind = RaidObjectiveKind.InterruptBrute; ObjectiveTarget = ogres; }
                    break;
            }
            if (ObjectiveKind != RaidObjectiveKind.None) ObjectivesAttempted++;
        }

        /// <summary>Break the precision streak and record why the shot failed.</summary>
        public void ReportMiss(MissReason reason, Vector3 position)
        {
            bool tutorial = State == GameState.Tutorial;
            if (State != GameState.Playing && !tutorial) return;
            HitStreak = 0;
            // Practice misses coach the player but must not pollute run statistics.
            if (!tutorial)
            {
                switch (reason)
                {
                    case MissReason.Blocked: Blocked++; break;
                    case MissReason.Short: FellShort++; break;
                    default: WideMisses++; break;
                }
            }
            Missed?.Invoke(reason, position);
        }

        /// <summary>Kept for compatibility: an unclassified miss is treated as a wide miss.</summary>
        public void RegisterMiss() => ReportMiss(MissReason.Missed, Vector3.zero);

        /// <summary>
        /// Classify an environmental impact: an arrow that lands before the enemy
        /// line fell short; one that lands beyond it flew past.
        /// </summary>
        public MissReason ClassifyEnvironmentMiss(Vector3 point)
        {
            float landed = new Vector2(point.x, point.z).magnitude;
            float nearest = float.MaxValue;
            foreach (var m in _monsters)
            {
                if (m == null || m.IsDead) continue;
                float d = new Vector2(m.transform.position.x, m.transform.position.z).magnitude;
                if (d < nearest) nearest = d;
            }
            // Practice targets are not in the raid list; include them so practice
            // misses are classified against the dummy the player is aiming at.
            if (nearest == float.MaxValue)
            {
                foreach (var m in FindObjectsByType<Monster>(FindObjectsSortMode.None))
                {
                    if (m == null || m.IsDead) continue;
                    float d = new Vector2(m.transform.position.x, m.transform.position.z).magnitude;
                    if (d < nearest) nearest = d;
                }
            }
            if (nearest == float.MaxValue) return MissReason.Short;   // nothing left to reach
            return landed < nearest - 0.75f ? MissReason.Short : MissReason.Missed;
        }

        public Transform MonsterParent;
        public Transform ArcherTarget;
        public Monster[] ActiveMonsters => _monsters.ToArray();

        /// <summary>
        /// Nearest enemy currently telegraphing a blow, excluding an optional
        /// boss (whose own panel already shows the countdown). Iterates the
        /// live list rather than allocating an array for the HUD.
        /// </summary>
        public Monster NearestWinding(Monster exclude = null)
        {
            Monster nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (var monster in _monsters)
            {
                if (monster == null || monster.IsDead || !monster.IsWinding || monster == exclude) continue;
                float distance = monster.transform.position.sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = monster;
                }
            }
            return nearest;
        }

        readonly List<Monster> _monsters = new List<Monster>();
        readonly List<SpawnOrder> _spawnQueue = new List<SpawnOrder>();
        int _spawnIndex;
        float _spawnTimer;

        [Header("Spawning")]
        public float SpawnInterval = 0.9f;

        float _baseMaxHealth;
        float _baseArrowDamage, _baseChargeTime;
        int _basePierceCapacity;
        float _baseHeadshotMultiplier;

        /// <summary>Upgrades offered after a cleared raid; the player picks one, free.</summary>
        public IReadOnlyList<UpgradeKind> Offers => _offers;
        readonly List<UpgradeKind> _offers = new List<UpgradeKind>();

        /// <summary>The tutorial director, available once Start has run.</summary>
        public TutorialDirector Tutorial { get; private set; }

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            _baseMaxHealth = MaxHealth;
            _baseArrowDamage = Archer != null ? Archer.ArrowDamage : 0f;
            _baseChargeTime = Archer != null ? Archer.ChargeTime : 1f;
            _basePierceCapacity = PierceCapacity;
            _baseHeadshotMultiplier = HeadshotDamageMultiplier;
            Tutorial = gameObject.AddComponent<TutorialDirector>();
        }

        void RollOffers()
        {
            _offers.Clear();
            var pool = new List<UpgradeKind> { UpgradeKind.MaxHealth, UpgradeKind.Damage };
            if (Archer != null && Archer.ChargeTime > 0.46f) pool.Add(UpgradeKind.QuickDraw);
            if (Health < MaxHealth) pool.Add(UpgradeKind.Heal);   // don't offer a useless heal

            // Surface one shot-changing upgrade whenever one is still available, so
            // the choice is about how you shoot, not only how big the numbers are.
            var special = new List<UpgradeKind>();
            if (!Piercing) special.Add(UpgradeKind.Piercing);
            if (!SlowingShot) special.Add(UpgradeKind.Slowing);
            if (!Deadeye) special.Add(UpgradeKind.Deadeye);
            if (Piercing && !DeepPierce) special.Add(UpgradeKind.DeepPierce);
            if (SlowingShot && !CripplingShot) special.Add(UpgradeKind.Crippling);

            if (Health < MaxHealth * 0.4f)
            {
                _offers.Add(UpgradeKind.Heal);
                pool.Remove(UpgradeKind.Heal);
            }
            else if (special.Count > 0)
            {
                int j = Random.Range(0, special.Count);
                _offers.Add(special[j]);
            }

            while (_offers.Count < 2 && pool.Count > 0)
            {
                int j = Random.Range(0, pool.Count);
                _offers.Add(pool[j]);
                pool.RemoveAt(j);
            }
        }

        public string UpgradeLabel(UpgradeKind kind) => kind switch
        {
            UpgradeKind.Heal => GameLanguage.Text($"Field dressing\n{Mathf.CeilToInt(Health)} → {Mathf.CeilToInt(Mathf.Min(MaxHealth,Health+HealAmount))} HP", $"응급 처치\n체력 {Mathf.CeilToInt(Health)} → {Mathf.CeilToInt(Mathf.Min(MaxHealth,Health+HealAmount))}"),
            UpgradeKind.MaxHealth => GameLanguage.Text($"Fortitude\n{MaxHealth:0} → {MaxHealth+MaxHealthStep:0} max HP", $"강인함\n최대 체력 {MaxHealth:0} → {MaxHealth+MaxHealthStep:0}"),
            UpgradeKind.Damage => GameLanguage.Text($"Broadheads\n{Archer.ArrowDamage:0} → {Archer.ArrowDamage+ArrowPowerStep:0} damage", $"강철 화살촉\n공격력 {Archer.ArrowDamage:0} → {Archer.ArrowDamage+ArrowPowerStep:0}"),
            UpgradeKind.QuickDraw => GameLanguage.Text($"Quickdraw\n{Archer.ChargeTime:0.00}s → {Mathf.Max(0.45f,Archer.ChargeTime*0.8f):0.00}s draw", $"빠른 사격\n당기기 {Archer.ChargeTime:0.00} → {Mathf.Max(0.45f,Archer.ChargeTime*0.8f):0.00}초"),
            UpgradeKind.Piercing => GameLanguage.Text(
                "Bodkin points\nPierce the first enemy\n" + (SlowingShot ? "Synergy: slows pierced foes" : "Pairs with Hamstring"),
                "관통 화살촉\n첫 적을 꿰뚫습니다\n" + (SlowingShot ? "시너지: 관통한 적 둔화" : "급소 사격과 조합")),
            UpgradeKind.Slowing => GameLanguage.Text(
                "Hamstring shot\nHits slow the enemy\n" + (CripplingShot ? "Synergy: +25% to slowed foes" : "Pairs with Crippling"),
                "급소 사격\n명중한 적 둔화\n" + (CripplingShot ? "시너지: 둔화 적 +25% 피해" : "약점 사격과 조합")),
            UpgradeKind.Deadeye => GameLanguage.Text(
                $"Deadeye\nHeadshot damage +{Mathf.RoundToInt(HeadshotDamageStep / HeadshotDamageMultiplier * 100f)}%",
                $"정밀 사격\n헤드샷 피해 +{Mathf.RoundToInt(HeadshotDamageStep / HeadshotDamageMultiplier * 100f)}%"),
            UpgradeKind.DeepPierce => GameLanguage.Text(
                "Split shaft\nPiercing +1 more enemy\n" + (SlowingShot ? "Synergy: slows pierced foes" : "Pairs with Hamstring"),
                "갈라진 화살대\n관통 대상 +1\n" + (SlowingShot ? "시너지: 관통한 적 둔화" : "급소 사격과 조합")),
            UpgradeKind.Crippling => GameLanguage.Text(
                $"Crippling shot\nSlowed foes +{Mathf.RoundToInt(CrippleDamageBonus * 100f)}% damage\n" +
                (Piercing ? "Synergy: pierced foes slow" : "Pairs with Bodkin"),
                $"약점 사격\n둔화 적 +{Mathf.RoundToInt(CrippleDamageBonus * 100f)}% 피해\n" +
                (Piercing ? "시너지: 관통한 적 둔화" : "관통 화살촉과 조합")),
            _ => kind.ToString()
        };

        /// <summary>Pick one of the two offered upgrades (free).</summary>
        public void ChooseUpgrade(UpgradeKind kind)
        {
            if (State != GameState.Intermission || !_offers.Contains(kind)) return;
            ApplyUpgrade(kind);
            Telemetry.UpgradeChosen(kind.ToString());
            _offers.Clear();
        }

        void ApplyUpgrade(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.Heal:
                    Health = Mathf.Min(MaxHealth, Health + HealAmount);
                    break;
                case UpgradeKind.MaxHealth:
                    MaxHealth += MaxHealthStep;
                    Health += MaxHealthStep;
                    break;
                case UpgradeKind.Damage:
                    if (Archer != null) Archer.ArrowDamage += ArrowPowerStep;
                    break;
                case UpgradeKind.QuickDraw:
                    if (Archer != null) Archer.ChargeTime = Mathf.Max(0.45f, Archer.ChargeTime * 0.8f);
                    break;
                case UpgradeKind.Piercing:
                    Piercing = true;
                    break;
                case UpgradeKind.Slowing:
                    SlowingShot = true;
                    break;
                case UpgradeKind.Deadeye:
                    Deadeye = true;
                    HeadshotDamageMultiplier += HeadshotDamageStep;
                    break;
                case UpgradeKind.DeepPierce:
                    DeepPierce = true;
                    PierceCapacity += 1;
                    break;
                case UpgradeKind.Crippling:
                    CripplingShot = true;
                    break;
            }
        }

        void Update()
        {
            if (State != GameState.Playing) return;

            SpawnStep();
        }

        void SpawnStep()
        {
            if (_spawnIndex >= _spawnQueue.Count) return;
            _spawnTimer -= Time.deltaTime;
            if (_spawnTimer > 0f) return;

            SpawnMonster(_spawnQueue[_spawnIndex]);
            _spawnIndex++;
            // The next group's authored delay, scaled so tests can compress the pacing.
            _spawnTimer = _spawnIndex < _spawnQueue.Count
                ? _spawnQueue[_spawnIndex].Delay * SpawnInterval : 0f;
        }

        void SpawnMonster(SpawnOrder order)
        {
            float x = Mathf.Clamp(order.Lane, -1f, 1f) * SpawnHalfWidth + Random.Range(-0.25f, 0.25f);
            float z = order.Z > 0f ? order.Z : Random.Range(SpawnNearZ, SpawnFarZ);

            RoleStats(order.Role, RaidNumber, out float health, out float speed, out float damage);
            Color color = RoleColor(order.Role);

            var m = Monster.Create(MonsterParent, new Vector3(x, 0f, z), ArcherTarget, order.Role,
                health, speed, damage, 1.0f, color);
            m.Died += OnMonsterDied;
            _monsters.Add(m);
            if (order.Role == MonsterRole.Ogre)
            {
                Boss = m;
                GameFeedback.Instance?.Notify(GameLanguage.Text("THE BRUTE APPROACHES", "오우거가 다가옵니다"), GameLanguage.Text("Headshots interrupt his swing", "헤드샷으로 공격을 저지합니다"), 3f);
            }
        }

        // Authored per-role baselines with a small, even step every raid.
        static void RoleStats(MonsterRole role, int raid, out float health, out float speed, out float damage)
        {
            switch (role)
            {
                case MonsterRole.Runner: health = 22f; speed = 2.6f; damage = 5f; break;
                case MonsterRole.Shield: health = 72f; speed = 1.15f; damage = 9f; break;
                case MonsterRole.Ogre: health = 300f; speed = 1.0f; damage = 20f; break;
                default: health = 40f; speed = 1.5f; damage = 8f; break;   // Raider
            }
            DifficultyScale(raid, out float healthScale, out float speedScale, out float damageScale);
            health *= healthScale;
            speed *= speedScale;
            damage *= damageScale;
        }

        /// <summary>
        /// Per-raid difficulty multipliers. Raid one is the baseline and every
        /// later raid adds a small step, so the curve never leaps at a
        /// chapter boundary. At raid fifteen this reaches ≈1.42 health,
        /// 1.08 speed and 1.21 damage, spreading the whole climb evenly over
        /// all three chapters instead of one chapter-two step.
        /// </summary>
        public static void DifficultyScale(int raid, out float health, out float speed, out float damage)
        {
            float step = Mathf.Max(0, raid - 1);
            health = 1f + 0.030f * step;
            speed = 1f + 0.006f * step;
            damage = 1f + 0.015f * step;
        }

        // Bright, saturated values so enemies read against the cool forest.
        static Color RoleColor(MonsterRole role) => role switch
        {
            MonsterRole.Runner => new Color(0.72f, 0.86f, 0.40f),   // bright, light goblin
            MonsterRole.Shield => new Color(0.54f, 0.52f, 0.32f),   // dull, armored
            MonsterRole.Ogre => new Color(0.54f, 0.42f, 0.46f),     // ogre
            _ => new Color(0.46f, 0.64f, 0.36f)                     // goblin raider (green)
        };

        /// <summary>Shots are unlimited, but only active play can fire.</summary>
        public bool CanFire => State == GameState.Playing || State == GameState.Tutorial;

        /// <summary>Start the opening practice: still target, moving goblin, shielded head.</summary>
        public void StartTutorial()
        {
            if (State != GameState.Intermission || _offers.Count > 0) return;
            Archer?.CancelAim();
            ClearProjectiles();
            ClearMonsters();
            _offers.Clear();
            Boss = null;
            State = GameState.Tutorial;
            Telemetry.EnsureRun();
            Tutorial?.Begin();
        }

        /// <summary>Front-door entry used by the intro screen: practice first, then raid one.</summary>
        public void BeginRun()
        {
            if (State != GameState.Intermission || _offers.Count > 0) return;
            if (PlayerPrefs.GetInt("LastRanger.Practiced", 0) == 1) StartRaid();
            else StartTutorial();
        }

        /// <summary>Leave practice and begin the first real raid.</summary>
        public void FinishTutorial()
        {
            if (State != GameState.Tutorial) return;
            PlayerPrefs.SetInt("LastRanger.Practiced", 1);
            PlayerPrefs.Save();
            State = GameState.Intermission;
            ClearMonsters();
            Archer?.ApplySettings();
            StartRaid();
        }

        /// <summary>Skip practice entirely (returning players or a fast retry).</summary>
        public void SkipTutorial()
        {
            if (State != GameState.Tutorial && State != GameState.Intermission) return;
            PlayerPrefs.SetInt("LastRanger.Practiced", 1);
            PlayerPrefs.Save();
            State = GameState.Intermission;
            ClearMonsters();
            Archer?.ApplySettings();
            StartRaid();
        }

        public void StartRaid()
        {
            // Only a between-raids intermission may start a raid; this makes the
            // method idempotent-safe at the gameplay boundary regardless of UI.
            if (State != GameState.Intermission || _offers.Count > 0) return;

            Archer?.CancelAim();
            ClearProjectiles();
            RaidNumber++;
            State = GameState.Playing;
            _offers.Clear();
            Boss = null;

            RaidPlan.Build(RaidNumber, _spawnQueue);
            MonstersThisRaid = _spawnQueue.Count;
            MonstersRemaining = MonstersThisRaid;
            _spawnIndex = 0;
            _spawnTimer = 0.8f;
            _raidShotsAtStart = ShotsFired;
            _raidHitsAtStart = ShotsHit;
            _raidHealthAtStart = Health;
            ConfigureObjective();
            Telemetry.RaidStarted(RaidNumber);
            GameFeedback.Instance?.Notify(RaidTitle(RaidNumber), RaidSubtitle(RaidNumber), 2.5f);
        }

        int _raidShotsAtStart, _raidHitsAtStart;
        float _raidHealthAtStart;
        bool _headhunterAnnounced;

        static string RaidSubtitle(int raid) => raid switch
        {
            2 => GameLanguage.Text("Lead the runners", "질주병보다 앞을 겨누세요"),
            3 => GameLanguage.Text("Aim above the shields", "방패 위쪽을 노리세요"),
            4 => GameLanguage.Text("Two lines, staggered", "두 갈래로 나뉘어 옵니다"),
            5 => GameLanguage.Text("Break the brute's swing", "오우거의 공격을 끊으세요"),
            6 => GameLanguage.Text("They come faster now", "적이 더 빠르게 몰려옵니다"),
            7 => GameLanguage.Text("Shields first, runners behind", "방패 뒤에 질주병이 숨어 있습니다"),
            8 => GameLanguage.Text("Lead the hunt", "질주병의 앞을 겨누세요"),
            9 => GameLanguage.Text("Break the guard, then the brute", "호위를 뚫고 오우거를 노리세요"),
            10 => GameLanguage.Text("The long night ends here", "긴 밤이 여기서 끝납니다"),
            11 => GameLanguage.Text("Frost on the path, runners ahead", "서리 내린 길, 질주병이 앞섭니다"),
            12 => GameLanguage.Text("Shields and a brute together", "방패병과 오우거가 함께 옵니다"),
            13 => GameLanguage.Text("Hold the gate through the dark", "어둠 속에서 성문을 지키세요"),
            14 => GameLanguage.Text("Two brutes lead the war band", "오우거 둘이 무리를 이끕니다"),
            15 => GameLanguage.Text("The last night. The last stand.", "마지막 밤, 마지막 방어선"),
            _ => GameLanguage.Text("Hold the path", "숲길을 지키세요")
        };

        /// <summary>Short description of the upcoming raid for the shop panel.</summary>
        public string UpcomingRaidSummary()
        {
            RaidPlan.Tally(RaidNumber + 1, out int r, out int run, out int sh, out int og);
            string s = GameLanguage.Text($"{r} Raider", $"약탈자 {r}");
            if (run > 0) s += GameLanguage.Text($", {run} Runner", $", 질주병 {run}");
            if (sh > 0) s += GameLanguage.Text($", {sh} Shield", $", 방패병 {sh}");
            if (og > 0) s += GameLanguage.Text($", {og} Ogre", $", 오우거 {og}");
            return s;
        }

        void OnMonsterDied(Monster monster)
        {
            // Count each enemy exactly once.
            if (State != GameState.Playing || !_monsters.Remove(monster)) return;

            Kills++;
            Score += (monster.Role == MonsterRole.Ogre ? 500 : 100) * ScoreMultiplier;
            MonstersRemaining = Mathf.Max(0, MonstersRemaining - 1);

            // Objective progress and failure happen from the same death event.
            if (ObjectiveActive)
            {
                if (ObjectiveKind == RaidObjectiveKind.StopRunners && monster.Role == MonsterRole.Runner)
                {
                    ObjectiveProgress++;
                    if (ObjectiveProgress >= ObjectiveTarget) CompleteObjective();
                }
                else if (ObjectiveKind == RaidObjectiveKind.InterruptBrute && monster.Role == MonsterRole.Ogre)
                {
                    // The brute died before the required interrupts were earned.
                    FailObjective();
                }
            }

            if (MonstersRemaining <= 0)
            {
                if (ObjectiveActive) FailObjective();
                RaidsCleared++;
                Score += 200;
                if (Health >= _raidHealthAtStart - 0.01f)
                {
                    UntouchedRaids++;
                    if (UntouchedRaids == UntouchedTarget)
                        GameFeedback.Instance?.Notify(GameLanguage.Text("CHALLENGE COMPLETE · UNTOUCHED", "도전 완료 · 무결"),
                            GameLanguage.Text($"{UntouchedTarget} raids without damage", $"피해 없이 {UntouchedTarget}회 격퇴"), 2f);
                }
                if (ShotsFired - _raidShotsAtStart <= ThriftyShotLimit)
                {
                    ThriftyRaids++;
                    if (ThriftyRaids == ThriftyTarget)
                        GameFeedback.Instance?.Notify(GameLanguage.Text("CHALLENGE COMPLETE · THRIFTY", "도전 완료 · 절약"),
                            GameLanguage.Text($"{ThriftyTarget} raids with {ThriftyShotLimit} shots or fewer", $"{ThriftyShotLimit}발 이하로 {ThriftyTarget}회 격퇴"), 2f);
                }
                bool final = RaidsCleared >= TotalRaids;
                if (EndsChapter(RaidNumber))
                {
                    Score += 500;
                    if (!final)
                        GameFeedback.Instance?.Notify(GameManager.ChapterClearTitle(ChapterOf(RaidNumber)),
                            GameLanguage.Text("Chapter bonus +500", "장 보너스 +500"), 2.2f);
                }
                State = final ? GameState.Victory : GameState.Intermission;
                Archer?.CancelAim();
                ClearProjectiles();
                Telemetry.RaidCleared(RaidNumber, ShotsFired - _raidShotsAtStart, ShotsHit - _raidHitsAtStart);
                if (final) { Telemetry.RunEnded(true, RaidNumber); SubmitRecords(); }
                if (!final) RollOffers();
                GameFeedback.Instance?.Play(FeedbackSound.Clear);
            }
        }

        public void DamageArcher(float amount, MonsterRole? source = null)
        {
            if (State != GameState.Playing) return;
            if (amount <= 0f) return;
            HitStreak = 0;
            LastDamageSource = source;
            DamageTaken += Mathf.Min(Health, amount);
            Health -= amount;
            GameFeedback.Instance?.Hurt();
            if (Health <= 0f)
            {
                Health = 0f;
                State = GameState.GameOver;
                DefeatCause = CauseFor(source, RaidNumber);
                DefeatTip = TipFor(source);
                Archer?.CancelAim();
                ClearProjectiles();
                Telemetry.RunEnded(false, RaidNumber);
                SubmitRecords();
            }
        }

        static string CauseFor(MonsterRole? source, int raid)
        {
            return source switch
            {
                MonsterRole.Ogre => GameLanguage.Text($"The brute's final swing broke the gate in raid {raid}.",
                    $"{raid}차 습격에서 오우거의 마지막 공격에 성문이 무너졌습니다."),
                MonsterRole.Runner => GameLanguage.Text($"A runner slipped through in raid {raid}.",
                    $"{raid}차 습격에서 질주병이 방어선을 통과했습니다."),
                MonsterRole.Shield => GameLanguage.Text($"A shield wall reached the gate in raid {raid}.",
                    $"{raid}차 습격에서 방패병이 성문 앞까지 밀고 들어왔습니다."),
                MonsterRole.Raider => GameLanguage.Text($"A raider reached the gate in raid {raid}.",
                    $"{raid}차 습격에서 약탈자가 성문을 공격했습니다."),
                _ => GameLanguage.Text("The defense fell.", "방어선이 무너졌습니다.")
            };
        }

        static string TipFor(MonsterRole? source) => source switch
        {
            MonsterRole.Ogre => GameLanguage.Text("Interrupt the brute's wind-up with a headshot.",
                "헤드샷으로 오우거의 공격 준비를 끊으세요."),
            MonsterRole.Runner => GameLanguage.Text("Lead runners and shoot before they reach the gate.",
                "질주병의 앞을 겨누어 성문에 닿기 전에 쏘세요."),
            MonsterRole.Shield => GameLanguage.Text("Aim above the shield; body shots are blocked.",
                "방패 위쪽 머리를 노리세요. 몸통은 막힙니다."),
            MonsterRole.Raider => GameLanguage.Text("Reach full draw and release before enemies reach the gate.",
                "최대로 당긴 뒤 적이 성문에 닿기 전에 쏘세요."),
            _ => GameLanguage.Text("Hold your aim and keep the lane clear.",
                "조준을 유지하고 숲길을 비우세요.")
        };

        void SubmitRecords()
        {
            if (RecordsSubmitted) return;
            RecordsSubmitted = true;
            LastRunRecords = PlayerRecords.SubmitRun(this);
            HasRunRecords = true;
        }

        public void Restart()
        {
            ResetRun();
            Telemetry.RunStarted();
            StartRaid();
        }

        /// <summary>
        /// Abandon the current run and show the opening title card again. Works
        /// from play, practice, pause and the results screen; time is restored
        /// so a paused simulation never stays frozen behind the title.
        /// </summary>
        public void ReturnToTitle()
        {
            // Already on the title card: nothing to tear down.
            if (State == GameState.Intermission && RaidNumber == 0 && _offers.Count == 0) return;
            Telemetry.Quit(State.ToString(), RaidNumber);
            ResetRun();
        }

        /// <summary>Clear every trace of the run without starting a new raid.</summary>
        void ResetRun()
        {
            // Clean the whole run: enemies AND any arrows still in flight, so a
            // fresh game cannot inherit hits or rewards.
            Time.timeScale = 1f;
            Archer?.CancelAim();
            ClearProjectiles();
            ClearMonsters();
            Tutorial?.Cancel();

            // New game: clear upgrades, run stats and the day/night clock.
            if (Archer != null) { Archer.ArrowDamage = _baseArrowDamage; Archer.ChargeTime = _baseChargeTime; }
            Piercing = SlowingShot = false;
            Deadeye = DeepPierce = CripplingShot = false;
            HeadshotDamageMultiplier = _baseHeadshotMultiplier;
            PierceCapacity = _basePierceCapacity;
            Boss = null;
            Score = Headshots = HitStreak = BestStreak = 0;
            Blocked = FellShort = WideMisses = 0;
            CombatEffects.Instance?.Clear();
            GameFeedback.Instance?.ResetFeedback();
            MaxHealth = _baseMaxHealth;
            _offers.Clear();
            Kills = 0;
            ShotsFired = 0;
            ShotsHit = 0;
            RaidsCleared = 0;
            DamageTaken = 0f;
            ObjectiveKind = RaidObjectiveKind.None;
            ObjectiveTarget = ObjectiveProgress = ObjectivesCompleted = ObjectivesAttempted = 0;
            ObjectiveComplete = ObjectiveFailed = false;
            DefeatCause = DefeatTip = "";
            LastDamageSource = null;
            HasRunRecords = false;
            RecordsSubmitted = false;
            LastRunRecords = default;
            UntouchedRaids = 0;
            ThriftyRaids = 0;
            _headhunterAnnounced = false;
            TimeOfDay.Instance?.ResetToStart();

            Health = MaxHealth;
            RaidNumber = 0;
            MonstersRemaining = 0;
            MonstersThisRaid = 0;
            _spawnIndex = 0;
            _spawnTimer = 0f;
            State = GameState.Intermission;
        }

        void ClearMonsters()
        {
            // Only the enemies this manager spawned: the practice targets and
            // the raid monsters both live under the monster parent. Anything
            // else in the scene (a test rig, a preview) is not ours to delete.
            foreach (var m in FindObjectsByType<Monster>(FindObjectsSortMode.None))
            {
                if (m == null) continue;
                if (MonsterParent != null && !m.transform.IsChildOf(MonsterParent)) continue;
                m.gameObject.SetActive(false);
                Destroy(m.gameObject);
            }
            _monsters.Clear();
            _spawnQueue.Clear();
        }

        public static string RaidTitle(int raid) => raid switch
        {
            1 => GameLanguage.Text("FIRST SIGNS", "첫 징조"),
            2 => GameLanguage.Text("THE RUSH", "몰려오는 적"),
            3 => GameLanguage.Text("WOODEN SHIELDS", "나무 방패"),
            4 => GameLanguage.Text("THE AMBUSH", "매복"),
            5 => GameLanguage.Text("THE BRUTE", "오우거"),
            6 => GameLanguage.Text("THE SECOND WAVE", "두 번째 물결"),
            7 => GameLanguage.Text("IRON RANKS", "철벽"),
            8 => GameLanguage.Text("THE HUNT", "사냥"),
            9 => GameLanguage.Text("THE BRUTE'S GUARD", "오우거의 호위"),
            10 => GameLanguage.Text("THE WARDEN", "감시자"),
            11 => GameLanguage.Text("THE FIRST FROST", "첫 서리"),
            12 => GameLanguage.Text("IRON AND ASH", "철과 재"),
            13 => GameLanguage.Text("THE NIGHT SIEGE", "밤의 공성"),
            14 => GameLanguage.Text("THE WAR BAND", "전쟁 무리"),
            15 => GameLanguage.Text("THE LAST DAWN", "마지막 새벽"),
            _ => raid % 5 == 0 ? GameLanguage.Text("THE BRUTE RETURNS", "돌아온 오우거") : GameLanguage.Text($"FOREST RAID {raid}", $"숲의 습격 {raid}")
        };

        GameState _pausedFrom = GameState.Playing;

        public void TogglePause()
        {
            if (State == GameState.Paused)
            {
                State = _pausedFrom;
                Time.timeScale = 1f;
                return;
            }
            if (State != GameState.Playing && State != GameState.Tutorial) return;
            _pausedFrom = State;
            Archer?.CancelAim();
            State = GameState.Paused;
            Time.timeScale = 0f;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && (State == GameState.Playing || State == GameState.Tutorial)) TogglePause();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && (State == GameState.Playing || State == GameState.Tutorial)) TogglePause();
        }

        void OnApplicationQuit()
        {
            Telemetry.Quit(State.ToString(), RaidNumber);
        }

        public void ClearProjectiles()
        {
            foreach (var arrow in FindObjectsByType<Arrow>(FindObjectsSortMode.None))
                if (arrow != null) { arrow.gameObject.SetActive(false); Destroy(arrow.gameObject); }
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            if (Instance == this) Instance = null;
        }
    }
}
