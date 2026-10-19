using System.Collections.Generic;
using UnityEngine;

namespace Archer
{
    public enum GameState
    {
        Playing,
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
        Recovery
    }

    /// <summary>
    /// Owns the raid loop: spawning waves, tracking archer health and
    /// arrows, and switching between play / between-raid / game-over states.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }
        public const int TotalRaids = 20;

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
        public int ScoreMultiplier => 1 + Mathf.Min(2, HitStreak / 3);
        public Monster Boss { get; private set; }
        public int SpawnedThisRaid => _spawnIndex;
        public float Accuracy => ShotsFired > 0 ? (float)ShotsHit / ShotsFired : 0f;

        public void RegisterShot() { if (State == GameState.Playing) ShotsFired++; }
        public void RegisterHit(bool headshot = false)
        {
            if (State != GameState.Playing) return;
            ShotsHit++;
            HitStreak++;
            BestStreak = Mathf.Max(BestStreak, HitStreak);
            if (headshot) Headshots++;
            Score += (headshot ? 75 : 25) * ScoreMultiplier;
        }

        public void RegisterMiss()
        {
            if (State == GameState.Playing) HitStreak = 0;
        }

        public Transform MonsterParent;
        public Transform ArcherTarget;
        public Monster[] ActiveMonsters => _monsters.ToArray();

        readonly List<Monster> _monsters = new List<Monster>();
        readonly List<MonsterRole> _spawnQueue = new List<MonsterRole>();
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
        }

        void RollOffers()
        {
            _offers.Clear();
            var pool = new List<UpgradeKind> { UpgradeKind.MaxHealth, UpgradeKind.Damage, UpgradeKind.Arrows };
            if (Archer != null && Archer.ChargeTime > 0.46f) pool.Add(UpgradeKind.QuickDraw);
            if (ArrowRegenInterval > 0.51f) pool.Add(UpgradeKind.Recovery);
            if (Health < MaxHealth) pool.Add(UpgradeKind.Heal);   // don't offer a useless heal

            if (Health < MaxHealth * 0.4f)
            {
                _offers.Add(UpgradeKind.Heal);
                pool.Remove(UpgradeKind.Heal);
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
            _ => kind.ToString()
        };

        /// <summary>Pick one of the two offered upgrades (free).</summary>
        public void ChooseUpgrade(UpgradeKind kind)
        {
            if (State != GameState.Intermission || !_offers.Contains(kind)) return;
            ApplyUpgrade(kind);
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

            // Small groups create a short breathing space before the next approach.
            _spawnTimer = SpawnInterval * (_spawnIndex % 2 == 1 ? 2.1f : 1f);
            SpawnMonster(_spawnQueue[_spawnIndex], _spawnIndex);
            _spawnIndex++;
        }

        void SpawnMonster(MonsterRole role, int index)
        {
            // Alternate lanes to separate successive spawns without marching left to right.
            float[] lanes = { -0.6f, 0.6f, 0f, -0.3f, 0.3f };
            float x = lanes[index % lanes.Length] * SpawnHalfWidth + Random.Range(-0.25f, 0.25f);
            float z = Random.Range(SpawnNearZ, SpawnFarZ);

            RoleStats(role, RaidNumber, out float health, out float speed, out float damage);
            Color color = RoleColor(role);

            var m = Monster.Create(MonsterParent, new Vector3(x, 0f, z), ArcherTarget, role,
                health, speed, damage, 1.0f, color);
            m.Died += OnMonsterDied;
            _monsters.Add(m);
            if (role == MonsterRole.Ogre)
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
            int extra = Mathf.Max(0, raid - 1);
            health *= 1f + 0.06f * extra;
            speed *= 1f + 0.02f * extra;
            damage *= 1f + 0.03f * extra;
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
            if (State != GameState.Playing) return false;
            if (Arrows <= 0) return false;
            Arrows--;
            return true;
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

            ComposeRaid(RaidNumber, _spawnQueue);
            MonstersThisRaid = _spawnQueue.Count;
            MonstersRemaining = MonstersThisRaid;
            _spawnIndex = 0;
            _spawnTimer = 0.8f;
            GameFeedback.Instance?.Notify(RaidTitle(RaidNumber), GameLanguage.Text("Hold the path", "숲길을 지키세요"), 2.5f);
        }

        /// <summary>Twenty-raid defense, with an ogre every fifth raid.</summary>
        static void ComposeRaid(int raid, List<MonsterRole> into, bool shuffle = true)
        {
            into.Clear();
            int raiders, runners, shields, ogres;
            switch (raid)
            {
                case 1: raiders = 5; runners = 0; shields = 0; ogres = 0; break;  // first signs
                case 2: raiders = 4; runners = 2; shields = 0; ogres = 0; break;  // the rush
                case 3: raiders = 4; runners = 1; shields = 2; ogres = 0; break;  // wooden shields
                case 4: raiders = 4; runners = 3; shields = 2; ogres = 0; break;  // hold the path
                case 5: raiders = 3; runners = 2; shields = 1; ogres = 1; break;  // the brute
                default:
                    raiders = 4 + Mathf.Max(0, raid - 6) / 4;
                    runners = 2 + Mathf.Max(0, raid - 6) / 4;
                    shields = 2 + Mathf.Max(0, raid - 6) / 5;
                    ogres = raid % 5 == 0 ? 1 : 0;
                    break;
            }

            var pool = new List<MonsterRole>(raiders + runners + shields + ogres);
            for (int i = 0; i < ogres; i++) pool.Add(MonsterRole.Ogre);
            for (int i = 0; i < raiders; i++) pool.Add(MonsterRole.Raider);
            for (int i = 0; i < runners; i++) pool.Add(MonsterRole.Runner);
            for (int i = 0; i < shields; i++) pool.Add(MonsterRole.Shield);

            // Interleave everything but keep any ogre(s) at the front (headline).
            for (int i = pool.Count - 1; shuffle && i > ogres; i--)
            {
                int j = Random.Range(ogres, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            into.AddRange(pool);
        }

        /// <summary>Short description of the upcoming raid for the shop panel.</summary>
        public string UpcomingRaidSummary()
        {
            var list = new List<MonsterRole>();
            ComposeRaid(RaidNumber + 1, list, false);
            int r = 0, run = 0, sh = 0, og = 0;
            foreach (var role in list)
            {
                if (role == MonsterRole.Runner) run++;
                else if (role == MonsterRole.Shield) sh++;
                else if (role == MonsterRole.Ogre) og++;
                else r++;
            }
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

            if (MonstersRemaining <= 0)
            {
                RaidsCleared++;
                Score += 200;
                State = RaidsCleared >= TotalRaids ? GameState.Victory : GameState.Intermission;
                Archer?.CancelAim();
                ClearProjectiles();
                if (State == GameState.Intermission) RollOffers();
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
            }
        }

        public void Restart()
        {
            // Clean the whole run: enemies AND any arrows still in flight, so a
            // fresh game cannot inherit hits or rewards.
            Time.timeScale = 1f;
            Archer?.CancelAim();
            ClearProjectiles();
            foreach (var m in FindObjectsByType<Monster>(FindObjectsSortMode.None))
                if (m != null) { m.gameObject.SetActive(false); Destroy(m.gameObject); }
            _monsters.Clear();
            _arrowTimer = 0f;

            // New game: clear upgrades, run stats and the day/night clock.
            if (Archer != null) { Archer.ArrowDamage = _baseArrowDamage; Archer.ChargeTime = _baseChargeTime; }
            ArrowRegenInterval = _baseRegenInterval;
            Boss = null;
            Score = Headshots = HitStreak = BestStreak = 0;
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
            StartRaid();
        }

        public static string RaidTitle(int raid) => raid switch
        {
            1 => GameLanguage.Text("FIRST SIGNS", "첫 번째 징조"), 2 => GameLanguage.Text("THE RUSH", "몰려오는 적"), 3 => GameLanguage.Text("WOODEN SHIELDS", "나무 방패"),
            4 => GameLanguage.Text("HOLD THE PATH", "숲길 사수"), 5 => GameLanguage.Text("THE BRUTE", "오우거"), 20 => GameLanguage.Text("THE LAST STAND", "최후의 결전"),
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
