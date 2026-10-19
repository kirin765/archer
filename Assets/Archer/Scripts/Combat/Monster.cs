using System;
using System.Collections.Generic;
using UnityEngine;

namespace Archer
{
    /// <summary>Distinct enemy archetypes with readable silhouettes.</summary>
    public enum MonsterRole
    {
        Raider,   // goblin raider: baseline
        Runner,   // goblin runner: fast, fragile
        Shield,   // shield goblin: blocks the body, exposed head
        Ogre      // final-raid miniboss: large, slow, heavy
    }

    /// <summary>
    /// A raider that walks toward the archer and attacks once it arrives.
    /// Builds its own placeholder visuals so it can be spawned from code.
    /// </summary>
    public class Monster : MonoBehaviour
    {
        public float MaxHealth { get; private set; }
        public float Health { get; private set; }
        public float Speed { get; private set; }
        public MonsterRole Role { get; private set; }

        public event Action<Monster> Died;

        Transform _target;
        float _attackDamage;
        float _attackInterval;
        float _attackRange;
        float _attackTimer;
        float _hitFlash;
        bool _winding;
        float _windUpTimer;

        [Tooltip("Seconds of visible wind-up before an attack lands.")]
        public float WindUp = 0.5f;

        Renderer[] _renderers;
        Color _baseColor;
        Transform _healthBar;
        Transform _healthFill;
        Transform _camera;
        float _headBottomLocal;
        float _headTopLocal;
        bool _headInModelSpace;
        Transform _leftLeg, _rightLeg, _weapon;
        Transform _meshModelRoot;
        GoblinMotion _motion;
        float _attackRecovery;
        const float RecoveryDuration = 0.3f;
        float _walkPhase;
        float _stagger, _deathTime;
        Vector3 _deathPosition;
        Quaternion _deathRotation;
        LineRenderer _attackRing;
        Transform _runnerTail;
        readonly List<Material> _runnerAccentMaterials = new();
        public bool IsDead { get; private set; }
        public bool IsWinding => _winding;
        public float WindupProgress => _winding ? 1f-Mathf.Clamp01(_windUpTimer / WindUp) : 0f;
        /// <summary>Seconds left before the telegraphed blow lands (0 when not winding).</summary>
        public float WindupRemaining => _winding ? Mathf.Max(0f, _windUpTimer) : 0f;

        /// <summary>A tutorial dummy: it may walk, but it never winds up or deals damage.</summary>
        public bool IsPractice { get; private set; }
        /// <summary>True when the blow that killed this enemy was a headshot.</summary>
        public bool DiedFromHeadshot { get; private set; }
        /// <summary>1 when unslowed; a slowing shot lowers this toward 0 while the effect lasts.</summary>
        public float SlowFactor => _slowFactor;
        float _slowFactor = 1f, _slowTimer;

        public static Monster Create(Transform parent, Vector3 position, Transform target, MonsterRole role,
            float health, float speed, float damage, float attackInterval, Color color, bool practice = false)
        {
            var root = new GameObject("Monster_" + role);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            var monster = root.AddComponent<Monster>();
            monster.Role = role;
            monster.IsPractice = practice;
            monster.WindUp = role == MonsterRole.Ogre ? 1.15f : role == MonsterRole.Runner ? 0.4f : 0.65f;
            monster.Build(role, color);
            monster._target = target;
            var facing = target.position - position; facing.y = 0;
            if(facing.sqrMagnitude > 0.001f) root.transform.rotation = Quaternion.LookRotation(facing);
            monster.MaxHealth = health;
            monster.Health = health;
            monster.Speed = speed;
            monster._attackDamage = damage;
            monster._attackInterval = attackInterval;
            monster._attackRange = role switch
            {
                MonsterRole.Ogre => 2.3f,
                MonsterRole.Shield => 1.8f,
                _ => 1.6f
            };
            root.AddComponent<RuntimeMaterials>().Capture(root.transform, monster._meshModelRoot);
            // Accents attached to imported bones are inside the excluded visual,
            // so hand their generated materials to the same owner.
            var owned = root.GetComponent<RuntimeMaterials>();
            if (monster._runnerAccentMaterials.Count > 0)
                owned.CaptureOwned(monster._runnerAccentMaterials.ToArray());
            return monster;
        }

        void Build(MonsterRole role, Color color)
        {
            _baseColor = color;

            // Per-role silhouette: scale plus accents so roles read at a glance.
            float s = role switch
            {
                MonsterRole.Runner => 0.8f,
                MonsterRole.Shield => 1.05f,
                MonsterRole.Ogre => 1.75f,
                _ => 1f
            };

            // Prefer the decimated rig: the source goblin is ~1.4M triangles,
            // which is unusable on mobile when several are on screen.
            var visualPrefab = Resources.Load<GameObject>("Prefabs/Goblin_Visual_Low");
            if (visualPrefab == null) visualPrefab = Resources.Load<GameObject>("Prefabs/Goblin_Visual");
            if (visualPrefab != null)
            {
                var visual = Instantiate(visualPrefab, transform);
                visual.name = "Goblin_3D_Mesh";
                visual.transform.localPosition = new Vector3(0f, 0.95f * s, 0f);
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one * (1.75f * s);
                _meshModelRoot = visual.transform;
                _motion = visual.GetComponentInChildren<GoblinMotion>();

                // Colliders for hit detection
                var bodyCollider = visual.AddComponent<CapsuleCollider>();
                bodyCollider.direction = 1; // Y
                bodyCollider.radius = 0.22f;
                bodyCollider.height = 0.9f;
                bodyCollider.center = new Vector3(0f, 0f, 0f);

                // Keep the head box on the actual head: a larger sphere floats
                // above the silhouette and turns near-misses overhead into free
                // headshots at close range.
                var headSphere = visual.AddComponent<SphereCollider>();
                headSphere.radius = 0.11f;
                headSphere.center = new Vector3(0f, 0.35f, 0.05f);

                _renderers = visual.GetComponentsInChildren<Renderer>();

                var dummyLeg = new GameObject("DummyLeg").transform;
                dummyLeg.SetParent(transform, false);
                _leftLeg = dummyLeg; _rightLeg = dummyLeg;
                _weapon = dummyLeg;
            }
            else
            {
                var bodyMat = Prim.Material(color, 0.2f);
                bodyMat.EnableKeyword("_EMISSION");
                bodyMat.SetColor("_EmissionColor", color * 0.12f);
                Color eyeColor = role == MonsterRole.Ogre ? new Color(1f, 0.35f, 0.2f)
                    : role == MonsterRole.Runner ? new Color(0.65f, 1f, 0.4f)
                    : new Color(1f, 0.85f, 0.2f);
                var eyeMat = Prim.Emissive(eyeColor, 2.2f);

                var leather = Prim.Material(new Color(0.27f,0.18f,0.12f));
                var iron = Prim.Material(new Color(0.35f,0.39f,0.41f),0.3f,0.45f);
                var body = Prim.Create(PrimitiveType.Capsule, "Body", transform, new Vector3(0f, 1.0f * s, 0f),
                    new Vector3(0.85f * s, 0.65f * s, 0.65f * s), bodyMat);
                Prim.Create(PrimitiveType.Cube,"Tunic",transform,new Vector3(0,0.85f*s,0),new Vector3(0.82f*s,0.55f*s,0.67f*s),leather,false);
                Prim.Create(PrimitiveType.Cube,"Belt",transform,new Vector3(0,0.9f*s,0.36f*s),new Vector3(0.85f*s,0.09f*s,0.06f),iron,false);
                _leftLeg=Prim.Create(PrimitiveType.Capsule,"Left boot",transform,new Vector3(-0.24f*s,0.32f*s,0),new Vector3(0.24f*s,0.32f*s,0.28f*s),leather,false).transform;
                _rightLeg=Prim.Create(PrimitiveType.Capsule,"Right boot",transform,new Vector3(0.24f*s,0.32f*s,0),new Vector3(0.24f*s,0.32f*s,0.28f*s),leather,false).transform;
                for(int side=-1;side<=1;side+=2)
                    Prim.Create(PrimitiveType.Capsule,"Arm",transform,new Vector3(side*0.58f*s,1.0f*s,0.06f),new Vector3(0.24f*s,0.45f*s,0.24f*s),bodyMat,false);
                _weapon=new GameObject("Weapon pivot").transform; _weapon.SetParent(transform,false); _weapon.localPosition=new Vector3(-0.64f*s,1.0f*s,0.25f);
                Prim.Create(PrimitiveType.Cylinder,"Club handle",_weapon,new Vector3(0,0.15f*s,0),new Vector3(0.12f*s,0.5f*s,0.12f*s),leather,false);
                Prim.Create(PrimitiveType.Cube,"Club head",_weapon,new Vector3(0,0.6f*s,0),new Vector3(0.28f*s,0.4f*s,0.28f*s),role==MonsterRole.Ogre?iron:leather,false);
                var head = Prim.Create(PrimitiveType.Sphere, "Head", transform, new Vector3(0f, 1.85f * s, 0f),
                    new Vector3(0.78f * s, 0.65f * s, 0.64f * s), bodyMat);
                Prim.Create(PrimitiveType.Cube,"Nose",transform,new Vector3(0,1.79f*s,0.37f*s),new Vector3(0.16f*s,0.17f*s,0.19f*s),bodyMat,false);
                Prim.Create(PrimitiveType.Cube,"Brow",transform,new Vector3(0,1.99f*s,0.30f*s),new Vector3(0.65f*s,0.09f*s,0.13f*s),leather,false);
                _renderers = new[] { body.GetComponent<Renderer>(), head.GetComponent<Renderer>() };

                var eyeL = Prim.Create(PrimitiveType.Sphere, "EyeL", transform, new Vector3(-0.14f * s, 1.9f * s, 0.26f * s),
                    new Vector3(0.1f * s, 0.1f * s, 0.1f * s), eyeMat, false);
                var eyeR = Prim.Create(PrimitiveType.Sphere, "EyeR", transform, new Vector3(0.14f * s, 1.9f * s, 0.26f * s),
                    new Vector3(0.1f * s, 0.1f * s, 0.1f * s), eyeMat, false);
                eyeL.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                eyeR.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                if (role != MonsterRole.Ogre)
                {
                    // Goblin ears make the small goblin silhouette unmistakable.
                    var earL = Prim.Create(PrimitiveType.Sphere, "EarL", transform, new Vector3(-0.46f * s, 1.95f * s, 0f),
                        new Vector3(0.3f * s, 0.16f * s, 0.12f * s), bodyMat, false);
                    earL.transform.localRotation = Quaternion.Euler(0f, 0f, 22f);
                    var earR = Prim.Create(PrimitiveType.Sphere, "EarR", transform, new Vector3(0.46f * s, 1.95f * s, 0f),
                        new Vector3(0.3f * s, 0.16f * s, 0.12f * s), bodyMat, false);
                    earR.transform.localRotation = Quaternion.Euler(0f, 0f, -22f);
                }
            }

            if (role == MonsterRole.Runner)
            {
                // The imported goblin is one skinned mesh, so a scout's identity
                // has to be added geometry. Attach it to the animated bones so it
                // moves with the body instead of floating as a red block, and keep
                // every piece thin and small: a flat plate at torso height reads as
                // a slab, not clothing.
                var scarfMat = Prim.Material(new Color(0.78f, 0.20f, 0.16f), 0.25f);
                var boneRoot = _meshModelRoot != null ? _meshModelRoot : transform;
                _runnerAccentMaterials.Add(scarfMat);

                // Hood: the strongest read at gameplay distance. A red head vs a
                // bare green one is unmistakable, and it also marks the head as the
                // place to aim without adding a hitbox.
                var head = FindBone(boneRoot, "Head");
                if (head != null)
                {
                    var hood = Prim.Create(PrimitiveType.Sphere, "Runner hood", head,
                        new Vector3(0f, 0.085f, -0.005f),
                        new Vector3(0.24f, 0.20f, 0.24f), scarfMat, false);
                    var peak = Prim.Create(PrimitiveType.Cube, "Runner hood peak", hood.transform,
                        new Vector3(0f, 0.62f, -0.25f),
                        new Vector3(0.28f, 0.35f, 0.5f), scarfMat, false);
                    peak.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
                }

                // Sash: a thin diagonal band across the chest. Cloth, not armour —
                // narrow enough to read as a stripe at gameplay distance.
                var spine = FindBone(boneRoot, "Spine02");
                if (spine != null)
                {
                    var sash = Prim.Create(PrimitiveType.Cube, "Runner sash", spine,
                        new Vector3(0f, 0.04f, 0.075f),
                        new Vector3(0.26f, 0.045f, 0.03f), scarfMat, false);
                    sash.transform.localRotation = Quaternion.Euler(0f, 0f, 38f);
                }

                // Scarf tail streaming behind: a directional tell while running.
                var tail = Prim.Create(PrimitiveType.Cube, "Runner scarf tail", transform,
                    new Vector3(0.09f * s, 1.00f * s, -0.20f * s),
                    new Vector3(0.055f * s, 0.24f * s, 0.03f * s), scarfMat, false);
                tail.transform.localRotation = Quaternion.Euler(30f, 0f, 16f);
                _runnerTail = tail.transform;
            }

            if (role == MonsterRole.Shield)
            {
                var ironMat = Prim.Material(new Color(0.35f, 0.39f, 0.41f), 0.3f, 0.45f);
                // Broad wooden shield covers the body; the head stays exposed above.
                var shieldMat = Prim.Material(new Color(0.36f, 0.26f, 0.16f), 0.2f);
                // Thick and clearly in front so fast arrows can't tunnel into the body.
                Prim.Create(PrimitiveType.Cube, "Shield", transform, new Vector3(0f, 0.80f * s, 0.70f * s),
                    new Vector3(1.25f * s, 1.25f * s, 0.22f), shieldMat);   // collider blocks arrows
                Prim.Create(PrimitiveType.Cube,"Shield band",transform,new Vector3(0,0.8f*s,0.84f*s),new Vector3(1.3f*s,0.12f*s,0.05f),ironMat,false);
                Prim.Create(PrimitiveType.Sphere,"Shield boss",transform,new Vector3(0,0.8f*s,0.88f*s),new Vector3(0.23f,0.23f,0.13f),ironMat,false);
            }

            // Generous floor only for the small runner so it stays a fair target.
            if (role == MonsterRole.Runner)
            {
                var hitbox = gameObject.AddComponent<CapsuleCollider>();
                hitbox.direction = 1; // Y
                hitbox.radius = 0.42f;
                hitbox.height = 1.7f;
                hitbox.center = new Vector3(0f, 0.85f * s, 0f);
            }

            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            MeasureHeadHitbox();
            BuildHealthBar(2.5f * s);
            var ring = new GameObject("Attack warning"); ring.transform.SetParent(transform,false);
            ring.transform.localPosition = new Vector3(0,0.06f,0);
            _attackRing = ring.AddComponent<LineRenderer>();
            _attackRing.sharedMaterial = Prim.Unlit(new Color(1f,0.43f,0.17f));
            _attackRing.useWorldSpace = false; _attackRing.loop = true; _attackRing.positionCount = 32;
            _attackRing.widthMultiplier = 0.06f;
            for(int i=0;i<32;i++) { float angle=i*Mathf.PI*2f/32; _attackRing.SetPosition(i,new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*s*0.65f); }
            _attackRing.enabled = false;

            _camera = Camera.main != null ? Camera.main.transform : null;
        }

        void BuildHealthBar(float topY)
        {
            var barRoot = new GameObject("HealthBar");
            barRoot.transform.SetParent(transform, false);
            barRoot.transform.localPosition = new Vector3(0f, topY, 0f);
            _healthBar = barRoot.transform;

            Prim.Create(PrimitiveType.Cube, "Back", barRoot.transform, Vector3.zero,
                new Vector3(1.0f, 0.12f, 0.02f), Prim.Material(new Color(0.06f, 0.06f, 0.08f)), false);

            var fill = Prim.Create(PrimitiveType.Cube, "Fill", barRoot.transform, new Vector3(0f, 0f, -0.02f),
                new Vector3(1.0f, 0.12f, 0.02f), Prim.Material(new Color(0.85f, 0.2f, 0.2f)), false);
            _healthFill = fill.transform;
        }

        /// <summary>
        /// Finds the head band of the built silhouette so arrows can award
        /// headshot damage without relying on collider names. The imported
        /// goblin keeps its colliders on a single shared object, so a contact
        /// point test is the reliable option.
        /// </summary>
        void MeasureHeadHitbox()
        {
            var source = _meshModelRoot != null
                ? _meshModelRoot.GetComponentsInChildren<Renderer>()
                : _renderers;
            if (source == null || source.Length == 0)
            {
                _headInModelSpace = false;
                _headBottomLocal = 1.5f;
                _headTopLocal = 1.9f;
                return;
            }

            bool hasBounds = false;
            Bounds bounds = default;
            foreach (var r in source)
            {
                if (r == null || !r.enabled) continue;
                // Costume pieces (hood, sash, scarf) are not part of the body
                // silhouette; including them would push the head band upward.
                if (r.name.StartsWith("Runner ")) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (!hasBounds) { _headInModelSpace = false; _headBottomLocal = 1.5f; _headTopLocal = 1.9f; return; }

            // The head sits in the top ~22% of every role's silhouette. Cap the
            // band at the silhouette itself so shots sailing overhead do not count.
            float headBottomWorld = bounds.max.y - bounds.size.y * 0.22f;
            float headTopWorld = bounds.max.y;
            Transform space = _meshModelRoot != null ? _meshModelRoot : transform;
            _headInModelSpace = _meshModelRoot != null;
            float scaleY = Mathf.Max(0.0001f, Mathf.Abs(space.lossyScale.y));
            _headBottomLocal = (headBottomWorld - space.position.y) / scaleY;
            _headTopLocal = (headTopWorld - space.position.y) / scaleY;
        }

        /// <summary>Find a named bone in the imported rig, if the prefab has one.</summary>
        static Transform FindBone(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name == name) return t;
            return null;
        }

        /// <summary>True when a hit point lands on the head band of this monster.</summary>
        public bool IsHeadshot(Vector3 worldPoint)
        {
            Transform space = _headInModelSpace && _meshModelRoot != null ? _meshModelRoot : transform;
            if (space == null) space = transform;
            float y = space.InverseTransformPoint(worldPoint).y;
            return y >= _headBottomLocal && y <= _headTopLocal;
        }

        void Update()
        {
            if (IsDead)
            {
                if (GameManager.Instance != null && GameManager.Instance.State == GameState.Paused) return;
                _deathTime += Time.deltaTime;
                float fall = Mathf.SmoothStep(0f,1f,_deathTime/0.5f);
                transform.rotation = _deathRotation * Quaternion.Euler(fall*85f,0,fall*12f);
                transform.position = _deathPosition - Vector3.up * Mathf.Max(0,_deathTime-0.35f)*1.2f;
                if (_deathTime >= 0.85f) Destroy(gameObject);
                return;
            }
            if (_target == null) return;

            var gm = GameManager.Instance;
            // Freeze movement and attacks outside active play (intermission,
            // game over) so ended runs can't keep dealing damage. Tutorial
            // practice targets still move so the player can learn to lead them.
            if (gm != null && gm.State != GameState.Playing && gm.State != GameState.Tutorial)
            {
                if (_hitFlash > 0f) UpdateHitFlash();
                return;
            }

            if (_slowTimer > 0f)
            {
                _slowTimer -= Time.deltaTime;
                if (_slowTimer <= 0f) _slowFactor = 1f;
            }

            if (_stagger > 0f)
            {
                _stagger -= Time.deltaTime;
                transform.position -= transform.forward * (Time.deltaTime * (Role == MonsterRole.Ogre ? 0.15f : 0.8f));
                UpdateTint();
                return;
            }
            // Fan into separate melee positions instead of stacking on the same point.
            float lane = Mathf.Clamp(transform.position.x,-0.8f,0.8f);
            var toTarget = _target.position + Vector3.right*lane - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            _attackRecovery = Mathf.Max(0f, _attackRecovery - Time.deltaTime);
            float moveSpeed = Speed * _slowFactor;
            if (distance > _attackRange)
            {
                _attackRecovery = 0f;
                _winding = false;   // left range: cancel any wind-up
                var dir = toTarget / Mathf.Max(distance, 0.0001f);
                transform.position += dir * (moveSpeed * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir, Vector3.up), 8f * Time.deltaTime);
            }
            else
            {
                if(toTarget.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(toTarget.normalized, Vector3.up), 8f * Time.deltaTime);

                if (IsPractice)
                {
                    _winding = false;
                }
                else if (_winding)
                {
                    // Telegraph: the enemy visibly winds up before the hit lands.
                    _windUpTimer -= Time.deltaTime;
                    if (_windUpTimer <= 0f)
                    {
                        _winding = false;
                        _attackTimer = Mathf.Max(_attackInterval, RecoveryDuration);
                        _attackRecovery = RecoveryDuration;
                        GameManager.Instance?.DamageArcher(_attackDamage);
                    }
                }
                else
                {
                    _attackTimer -= Time.deltaTime;
                    if (_attackTimer <= 0f)
                    {
                        _winding = true;
                        _windUpTimer = WindUp;
                        GameFeedback.Instance?.Play(FeedbackSound.EnemyWarning, Role == MonsterRole.Ogre ? 1.0f : 0.75f);
                    }
                }
            }

            _attackRing.enabled = _winding;
            if(_winding) _attackRing.widthMultiplier = Mathf.Lerp(0.04f,0.12f,WindupProgress);
            _walkPhase += Time.deltaTime * moveSpeed * 5f;
            float stride = distance > _attackRange ? Mathf.Sin(_walkPhase) * 24f : 0f;            _leftLeg.localRotation = Quaternion.Euler(stride,0,0);
            _rightLeg.localRotation = Quaternion.Euler(-stride,0,0);
            _weapon.localRotation = Quaternion.Euler(_winding ? -70f * (1f-_windUpTimer/Mathf.Max(WindUp,0.01f)) : stride*0.4f,0,-12f);

            if (_motion != null && _motion.enabled)
            {
                _motion.Sample(distance > _attackRange, moveSpeed, _winding, WindupProgress,
                    _attackRecovery > 0f ? Mathf.Max(0.0001f, 1f - _attackRecovery / RecoveryDuration) : 0f);
            }
            else if (_meshModelRoot != null)
            {
                // Dynamic creature locomotion bobbing, sway, and attack windup lunge
                float bob = distance > _attackRange ? Mathf.Abs(Mathf.Sin(_walkPhase)) * 0.08f : 0f;
                float tilt = distance > _attackRange ? Mathf.Cos(_walkPhase) * 4.5f : 0f;
                float lungeZ = _winding ? Mathf.Sin((1f - _windUpTimer / Mathf.Max(WindUp, 0.01f)) * Mathf.PI * 0.5f) * 0.25f : 0f;
                float lungeRotX = _winding ? -18f * (1f - _windUpTimer / Mathf.Max(WindUp, 0.01f)) : 0f;

                float s = Role switch
                {
                    MonsterRole.Runner => 0.8f,
                    MonsterRole.Shield => 1.05f,
                    MonsterRole.Ogre => 1.75f,
                    _ => 1f
                };

                _meshModelRoot.localPosition = new Vector3(0f, 0.95f * s + bob, lungeZ);
                _meshModelRoot.localRotation = Quaternion.Euler(lungeRotX, 0f, tilt);
            }

            if (_runnerTail != null)
            {
                // The scarf streams back while the scout is moving.
                float flutter = distance > _attackRange ? Mathf.Sin(_walkPhase * 1.7f) * 10f : 0f;
                _runnerTail.localRotation = Quaternion.Euler(30f + flutter, 0f, 16f);
            }

            UpdateTint();
        }

        MaterialPropertyBlock _tintBlock;

        void UpdateTint()
        {
            Color c = _baseColor;
            if (_winding)
            {
                float p = 1f - Mathf.Clamp01(_windUpTimer / Mathf.Max(WindUp, 0.01f));
                c = Color.Lerp(_baseColor, new Color(1f, 0.5f, 0.15f), 0.4f + 0.5f * p);
            }
            if (_hitFlash > 0f)
            {
                _hitFlash -= Time.deltaTime * 4f;
                c = Color.Lerp(c, Color.white, Mathf.Clamp01(_hitFlash));
            }

            if (_tintBlock == null) _tintBlock = new MaterialPropertyBlock();
            _tintBlock.SetColor("_BaseColor", c);
            _tintBlock.SetColor("_Color", c);
            foreach (var r in _renderers)
            {
                if (r != null)
                {
                    r.SetPropertyBlock(_tintBlock);
                }
            }
        }

        void UpdateHitFlash()
        {
            if (_hitFlash <= 0f) return;
            _hitFlash -= Time.deltaTime * 4f;
            var c = Color.Lerp(_baseColor, Color.white, Mathf.Clamp01(_hitFlash));

            if (_tintBlock == null) _tintBlock = new MaterialPropertyBlock();
            _tintBlock.SetColor("_BaseColor", c);
            _tintBlock.SetColor("_Color", c);
            foreach (var r in _renderers)
            {
                if (r != null)
                {
                    r.SetPropertyBlock(_tintBlock);
                }
            }
        }

        void LateUpdate()
        {
            if (_healthBar != null && _camera != null)
                _healthBar.rotation = Quaternion.LookRotation(_healthBar.position - _camera.position, Vector3.up);
        }

        /// <summary>A slowing shot reduces movement speed for a while; re-applying refreshes it.</summary>
        public void ApplySlow(float factor, float duration)
        {
            if (factor <= 0f || factor >= 1f || duration <= 0f) return;
            _slowFactor = Mathf.Min(_slowFactor, factor);
            _slowTimer = Mathf.Max(_slowTimer, duration);
        }

        public void TakeDamage(float amount, bool precision = false)
        {
            if (Health <= 0f || amount <= 0f || GameManager.Instance == null
                || (GameManager.Instance.State != GameState.Playing && GameManager.Instance.State != GameState.Tutorial)) return;
            Health -= amount;
            _hitFlash = 1f;
            _stagger = Role == MonsterRole.Ogre ? 0.08f : 0.18f;
            if (precision)
            {
                // A headshot is the brute's interruption window: the swing stops.
                bool interrupted = _winding;
                _winding = false;
                _attackRecovery = 0f;
                _attackRing.enabled = false;
                _attackTimer = 0.4f;
                _weapon.localRotation = Quaternion.Euler(12f,0,-12f);
                if (interrupted) GameFeedback.Instance?.Notify(
                    GameLanguage.Text("INTERRUPTED", "공격 저지"),
                    GameLanguage.Text("The swing is broken", "공격을 끊었습니다"), 0.9f);
            }

            if (_healthFill != null)
            {
                float ratio = Mathf.Clamp01(Health / MaxHealth);
                var s = _healthFill.localScale;
                _healthFill.localScale = new Vector3(ratio, s.y, s.z);
                _healthFill.localPosition = new Vector3(-(1f - ratio) * 0.5f, 0f, -0.02f);
            }

            if (Health <= 0f) { DiedFromHeadshot = precision; Die(); }
        }

        void Die()
        {
            if (IsDead) return;
            IsDead = true;
            _winding = false;
            _attackRing.enabled = false;
            _healthBar.gameObject.SetActive(false);
            foreach(var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            _deathPosition = transform.position;
            _deathRotation = transform.rotation;
            CombatEffects.Instance?.Burst(transform.position+Vector3.up, false, true);
            GameFeedback.Instance?.Play(FeedbackSound.Kill);
            Died?.Invoke(this);
        }
    }
}
