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
        Arrows,
        QuickDraw,
        Recovery,
        Piercing,
        Slowing,
        HeadshotRefund
    }

    /// <summary>Why a shot failed to damage an enemy — distinct feedback for each.</summary>
    public enum MissReason
    {
        Blocked,   // stopped by a shield
        Short,     // hit the ground / scenery before reaching the enemy line
        Missed     // travelled past or wide of the enemy
    }

    /// <summary>
    /// Owns the raid loop: spawning waves, tracking archer health and
    /// arrows, and switching between play / between-raid / game-over states.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>Length of the first chapter. The loop extends past this if playtests want more.</summary>
        public const int TotalRaids = 5;

        [Header("Archer")]
        public float MaxHealth = 100f;
        public float Health = 100f;

        [Header("Quiver")]
        public int MaxArrows = 6;
        public int Arrows = 12;
        public float ArrowRegenInterval = 1.35f;

        [Header("Spawn lane")]
        public float SpawnHalfWidth = 8f;
        public float SpawnNearZ = 24f;
        public float SpawnFarZ = 42f;

        [Header("Upgrades")]
        public ArcherController Archer;
        public float HealAmount = 35f;
        public float MaxHealthStep = 25f;
        public float ArrowPowerStep = 8f;
        public int ExtraArrowsStep = 2;

        [Header("Shot-changing upgrades")]
        [Tooltip("How many enemies a piercing arrow passes through after the first.")]
        public int PierceCapacity = 1;
        [Tooltip("Movement multiplier applied by a slowing shot.")]
        public float SlowFactor = 0.45f;
        [Tooltip("Seconds a slowing shot lingers on its target.")]
        public float SlowDuration = 2.2f;
        public bool Piercing;
        public bool SlowingShot;
        public bool RefundHeadshotKills;

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
            if (headshot) Headshots++;
            Score += (headshot ? 75 : 25) * ScoreMultiplier;
            if (ShotsHit == 1) Telemetry.FirstHit();
        }

        public void NotifyMonsterHit(Monster monster, bool headshot)
        {
            if (monster == null) return;
            MonsterHit?.Invoke(monster, headshot);
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

        readonly List<Monster> _monsters = new List<Monster>();
        readonly List<SpawnOrder> _spawnQueue = new List<SpawnOrder>();
        int _spawnIndex;
        float _spawnTimer;
        float _arrowTimer;

        [Header("Spawning")]
        public float SpawnInterval = 0.9f;

        /// <summary>0..1 progress toward the next regenerated arrow.</summary>
        public float ArrowRegenProgress =>
            ArrowRegenInterval > 0f ? Mathf.Clamp01(_arrowTimer / ArrowRegenInterval) : 0f;
        float _baseMaxHealth;
        int _baseMaxArrows;
        float _baseArrowDamage, _baseChargeTime, _baseRegenInterval;

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
            _baseMaxArrows = MaxArrows;
            _baseArrowDamage = Archer != null ? Archer.ArrowDamage : 0f;
            _baseChargeTime = Archer != null ? Archer.ChargeTime : 0.85f;
            _baseRegenInterval = ArrowRegenInterval;
            Tutorial = gameObject.AddComponent<TutorialDirector>();
        }

        void RollOffers()
        {
            _offers.Clear();
            var pool = new List<UpgradeKind> { UpgradeKind.MaxHealth, UpgradeKind.Damage, UpgradeKind.Arrows };
            if (Archer != null && Archer.ChargeTime > 0.46f) pool.Add(UpgradeKind.QuickDraw);
            if (ArrowRegenInterval > 0.51f) pool.Add(UpgradeKind.Recovery);
            if (Health < MaxHealth) pool.Add(UpgradeKind.Heal);   // don't offer a useless heal

            // Surface one shot-changing upgrade whenever one is still available, so
            // the choice is about how you shoot, not only how big the numbers are.
            var special = new List<UpgradeKind>();
            if (!Piercing) special.Add(UpgradeKind.Piercing);
            if (!SlowingShot) special.Add(UpgradeKind.Slowing);
            if (!RefundHeadshotKills) special.Add(UpgradeKind.HeadshotRefund);

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
            UpgradeKind.Heal => GameLanguage.Text($"Field dressing\n{Mathf.CeilToInt(Health)} → {Mathf.CeilToInt(Mathf.Min(MaxHealth,Health+HealAmount))} HP", $"응급 치료\n체력 {Mathf.CeilToInt(Health)} → {Mathf.CeilToInt(Mathf.Min(MaxHealth,Health+HealAmount))}"),
            UpgradeKind.MaxHealth => GameLanguage.Text($"Fortitude\n{MaxHealth:0} → {MaxHealth+MaxHealthStep:0} max HP", $"강인함\n최대 체력 {MaxHealth:0} → {MaxHealth+MaxHealthStep:0}"),
            UpgradeKind.Damage => GameLanguage.Text($"Broadheads\n{Archer.ArrowDamage:0} → {Archer.ArrowDamage+ArrowPowerStep:0} damage", $"강철 화살촉\n공격력 {Archer.ArrowDamage:0} → {Archer.ArrowDamage+ArrowPowerStep:0}"),
            UpgradeKind.Arrows => GameLanguage.Text($"Deep quiver\n{MaxArrows} → {MaxArrows+ExtraArrowsStep} arrows", $"큰 화살통\n화살 {MaxArrows} → {MaxArrows+ExtraArrowsStep}개"),
            UpgradeKind.QuickDraw => GameLanguage.Text($"Quickdraw\n{Archer.ChargeTime:0.00}s → {Mathf.Max(0.45f,Archer.ChargeTime*0.8f):0.00}s draw", $"빠른 시위\n당기기 {Archer.ChargeTime:0.00} → {Mathf.Max(0.45f,Archer.ChargeTime*0.8f):0.00}초"),
            UpgradeKind.Recovery => GameLanguage.Text($"Fletcher\n{ArrowRegenInterval:0.00}s → {Mathf.Max(0.5f,ArrowRegenInterval*0.8f):0.00}s recovery", $"화살 보급\n보충 {ArrowRegenInterval:0.00} → {Mathf.Max(0.5f,ArrowRegenInterval*0.8f):0.00}초"),
            UpgradeKind.Piercing => GameLanguage.Text("Bodkin points\nArrows punch through\nthe first enemy", $"관통 촉\n화살이 첫 적을\n꿰뚫고 지나갑니다"),
            UpgradeKind.Slowing => GameLanguage.Text("Hamstring shot\nHits slow the enemy\nfor a moment", $"급소 사격\n명중한 적의\n이동 속도를 늦춥니다"),
            UpgradeKind.HeadshotRefund => GameLanguage.Text("Recover\nHeadshot kills\nreturn one arrow", $"회수\n머리로 처치하면\n화살 1개를 되찾습니다"),
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
                case UpgradeKind.Recovery:
                    ArrowRegenInterval = Mathf.Max(0.5f, ArrowRegenInterval * 0.8f);
                    break;
                case UpgradeKind.Arrows:
                    MaxArrows += ExtraArrowsStep;
                    Arrows = MaxArrows;
                    break;
                case UpgradeKind.Piercing:
                    Piercing = true;
                    break;
                case UpgradeKind.Slowing:
                    SlowingShot = true;
                    break;
                case UpgradeKind.HeadshotRefund:
                    RefundHeadshotKills = true;
                    break;
            }
        }

        void Update()
        {
            if (State != GameState.Playing) return;

            SpawnStep();

            if (Arrows < MaxArrows)
            {
                _arrowTimer += Time.deltaTime;
                if (_arrowTimer >= ArrowRegenInterval)
                {
                    _arrowTimer = 0f;
                    Arrows++;
                }
            }
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
                GameFeedback.Instance?.Notify(GameLanguage.Text("THE BRUTE APPROACHES", "오우거가 다가옵니다"), GameLanguage.Text("Headshots interrupt his swing", "머리를 맞히면 공격을 막습니다"), 3f);
            }
        }

        // Authored per-role baselines with only a gentle late-raid scale.
        static void RoleStats(MonsterRole role, int raid, out float health, out float speed, out float damage)
        {
            switch (role)
            {
                case MonsterRole.Runner: health = 22f; speed = 4.1f; damage = 5f; break;
                case MonsterRole.Shield: health = 72f; speed = 1.75f; damage = 9f; break;
                case MonsterRole.Ogre: health = 300f; speed = 1.5f; damage = 20f; break;
                default: health = 40f; speed = 2.3f; damage = 8f; break;   // Raider
            }
            int extra = Mathf.Max(0, raid - TotalRaids);
            health *= 1f + 0.08f * extra;
            speed *= 1f + 0.02f * extra;
            damage *= 1f + 0.04f * extra;
        }

        // Bright, saturated values so enemies read against the cool forest.
        static Color RoleColor(MonsterRole role) => role switch
        {
            MonsterRole.Runner => new Color(0.62f, 0.80f, 0.42f),   // lighter goblin
            MonsterRole.Shield => new Color(0.54f, 0.52f, 0.32f),   // dull, armored
            MonsterRole.Ogre => new Color(0.54f, 0.42f, 0.46f),     // ogre
            _ => new Color(0.46f, 0.64f, 0.36f)                     // goblin raider (green)
        };

        public bool TryConsumeArrow()
        {
            if (State != GameState.Playing && State != GameState.Tutorial) return false;
            if (State == GameState.Tutorial) return true;   // practice fires freely
            if (Arrows <= 0) return false;
            Arrows--;
            return true;
        }

        /// <summary>Start the opening practice: still target, moving goblin, shielded head.</summary>
        public void StartTutorial()
        {
            if (State != GameState.Intermission || _offers.Count > 0) return;
            Archer?.CancelAim();
            ClearProjectiles();
            ClearMonsters();
            Arrows = MaxArrows;
            _arrowTimer = 0f;
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
            Arrows = MaxArrows;
            _arrowTimer = 0f;
            _offers.Clear();
            Boss = null;

            RaidPlan.Build(RaidNumber, _spawnQueue);
            MonstersThisRaid = _spawnQueue.Count;
            MonstersRemaining = MonstersThisRaid;
            _spawnIndex = 0;
            _spawnTimer = 0.8f;
            _raidShotsAtStart = ShotsFired;
            _raidHitsAtStart = ShotsHit;
            Telemetry.RaidStarted(RaidNumber);
            GameFeedback.Instance?.Notify(RaidTitle(RaidNumber), RaidSubtitle(RaidNumber), 2.5f);
        }

        int _raidShotsAtStart, _raidHitsAtStart;

        static string RaidSubtitle(int raid) => raid switch
        {
            2 => GameLanguage.Text("Lead the runners", "달려오는 적을 앞서 겨누세요"),
            3 => GameLanguage.Text("Aim above the shields", "방패 위를 노리세요"),
            4 => GameLanguage.Text("Two lines, staggered", "두 갈래로 나뉘어 옵니다"),
            5 => GameLanguage.Text("Break the brute's swing", "오우거의 공격을 끊으세요"),
            _ => GameLanguage.Text("Hold the path", "숲길을 지키세요")
        };

        /// <summary>Short description of the upcoming raid for the shop panel.</summary>
        public string UpcomingRaidSummary()
        {
            RaidPlan.Tally(RaidNumber + 1, out int r, out int run, out int sh, out int og);
            string s = GameLanguage.Text($"{r} Raider", $"전사 {r}");
            if (run > 0) s += GameLanguage.Text($", {run} Runner", $", 돌격병 {run}");
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

            // A headshot kill returns an arrow once the upgrade is taken.
            if (RefundHeadshotKills && monster.DiedFromHeadshot && Arrows < MaxArrows)
            {
                Arrows++;
                GameFeedback.Instance?.Notify(GameLanguage.Text("ARROW RECOVERED", "화살 회수"), GameLanguage.Text("Headshot kill", "머리 처치"), 0.8f);
            }

            if (MonstersRemaining <= 0)
            {
                RaidsCleared++;
                Score += 200;
                bool final = RaidsCleared >= TotalRaids;
                State = final ? GameState.Victory : GameState.Intermission;
                Archer?.CancelAim();
                ClearProjectiles();
                Telemetry.RaidCleared(RaidNumber, ShotsFired - _raidShotsAtStart, ShotsHit - _raidHitsAtStart);
                if (final) Telemetry.RunEnded(true, RaidNumber);
                if (!final) RollOffers();
                GameFeedback.Instance?.Play(FeedbackSound.Clear);
            }
        }

        public void DamageArcher(float amount)
        {
            if (State != GameState.Playing) return;
            if (amount <= 0f) return;
            HitStreak = 0;
            DamageTaken += Mathf.Min(Health, amount);
            Health -= amount;
            GameFeedback.Instance?.Hurt();
            if (Health <= 0f)
            {
                Health = 0f;
                State = GameState.GameOver;
                Archer?.CancelAim();
                ClearProjectiles();
                Telemetry.RunEnded(false, RaidNumber);
            }
        }

        public void Restart()
        {
            // Clean the whole run: enemies AND any arrows still in flight, so a
            // fresh game cannot inherit hits or rewards.
            Time.timeScale = 1f;
            Archer?.CancelAim();
            ClearProjectiles();
            ClearMonsters();
            _arrowTimer = 0f;

            // New game: clear upgrades, run stats and the day/night clock.
            if (Archer != null) { Archer.ArrowDamage = _baseArrowDamage; Archer.ChargeTime = _baseChargeTime; }
            ArrowRegenInterval = _baseRegenInterval;
            Piercing = SlowingShot = RefundHeadshotKills = false;
            Boss = null;
            Score = Headshots = HitStreak = BestStreak = 0;
            Blocked = FellShort = WideMisses = 0;
            CombatEffects.Instance?.Clear();
            GameFeedback.Instance?.ResetFeedback();
            MaxHealth = _baseMaxHealth;
            MaxArrows = _baseMaxArrows;
            _offers.Clear();
            Kills = 0;
            ShotsFired = 0;
            ShotsHit = 0;
            RaidsCleared = 0;
            DamageTaken = 0f;
            TimeOfDay.Instance?.ResetToStart();

            Health = MaxHealth;
            RaidNumber = 0;
            Arrows = MaxArrows;
            State = GameState.Intermission;
            Telemetry.RunStarted();
            StartRaid();
        }

        void ClearMonsters()
        {
            foreach (var m in FindObjectsByType<Monster>(FindObjectsSortMode.None))
                if (m != null) { m.gameObject.SetActive(false); Destroy(m.gameObject); }
            _monsters.Clear();
            _spawnQueue.Clear();
        }

        public static string RaidTitle(int raid) => raid switch
        {
            1 => GameLanguage.Text("FIRST SIGNS", "첫 번째 징조"),
            2 => GameLanguage.Text("THE RUSH", "몰려오는 적"),
            3 => GameLanguage.Text("WOODEN SHIELDS", "나무 방패"),
            4 => GameLanguage.Text("THE AMBUSH", "매복"),
            5 => GameLanguage.Text("THE BRUTE", "오우거"),
            _ => raid % 5 == 0 ? GameLanguage.Text("THE BRUTE RETURNS", "돌아온 오우거") : GameLanguage.Text($"FOREST RAID {raid}", $"숲의 습격 {raid}")
        };

        public void TogglePause()
        {
            if (State != GameState.Playing && State != GameState.Paused) return;
            Archer?.CancelAim();
            State = State == GameState.Playing ? GameState.Paused : GameState.Playing;
            Time.timeScale = State == GameState.Paused ? 0f : 1f;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && State == GameState.Playing) TogglePause();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && State == GameState.Playing) TogglePause();
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
