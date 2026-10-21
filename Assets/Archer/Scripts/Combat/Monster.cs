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
        float _barWidth = 0.66f;
        float _barHeight = 0.10f;
        Transform _camera;
        float _headBottomLocal;
        float _headTopLocal;
        bool _headInModelSpace;
        Transform _leftLeg, _rightLeg, _weapon;
        Transform _meshModelRoot;
        Transform _headBone;
        float _visualScale = 1.75f;
        bool _importedVisual;
        readonly List<SkinnedMeshRenderer> _lodRenderers = new();
        int _lodIndex;
        // Distance to the camera at which each level takes over. The bands are
        // wide so a goblin walking towards the archer does not flip between
        // levels every few steps.
        const float Lod0Far = 12f, Lod1Near = 26f, Lod1Far = 33f;
        const float Lod0Near = 9f;
        Transform _gearRoot;
        GoblinMotion _motion;
        float _attackRecovery;
        const float RecoveryDuration = 0.3f;
        float _walkPhase;
        float _deathTime;
        Vector3 _deathPosition;
        Quaternion _deathRotation;
        LineRenderer _attackRing;
        Material _attackRingMaterial;
        Transform _runnerTail;
        Transform _shieldRig;
        Renderer _shieldRenderer;
        Vector3 _shieldBaseLocalPosition;
        Quaternion _shieldBaseLocalRotation;
        Color _shieldBaseColor = new Color(0.36f, 0.26f, 0.16f);
        Color _shieldColor = new Color(0.36f, 0.26f, 0.16f);
        Vector3 _shieldKickDirection;
        float _shieldKickTimer;
        const float ShieldKickDuration = 0.24f;
        float _shieldFlash;
        float _flinchTimer;
        float _flinchDuration;
        float _flinchStrength;
        Vector3 _flinchDirection;
        float _interruptTimer;
        bool _reportedGate;
        float _roleScale = 1f;
        float _meshBaseY;
        Quaternion _meshBaseRotation = Quaternion.identity;
        public bool IsDead { get; private set; }
        public bool IsWinding => _winding;
        public float WindupProgress => _winding ? 1f-Mathf.Clamp01(_windUpTimer / Mathf.Max(0.01f, WindUp)) : 0f;
        /// <summary>Seconds left before the telegraphed blow lands (0 when not winding).</summary>
        public float WindupRemaining => _winding ? Mathf.Max(0f, _windUpTimer) : 0f;
        /// <summary>True while a brute is visibly recovering from a broken swing.</summary>
        public bool IsInterrupted => _interruptTimer > 0f;
        public float InterruptRemaining => Mathf.Max(0f, _interruptTimer);
        /// <summary>0 when settled, 1 at the moment a shield absorbs an arrow.</summary>
        public float ShieldReaction01 => _shieldKickTimer > 0f ? _shieldKickTimer / ShieldKickDuration : 0f;
        /// <summary>Relative walk cadence that gives each role its own rhythm.</summary>
        public float GaitRate { get; private set; } = 1f;

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
            root.AddComponent<RuntimeMaterials>().Capture(root.transform,
                monster._importedVisual ? monster._meshModelRoot : null,
                monster._gearRoot);
            return monster;
        }

        void Build(MonsterRole role, Color color)
        {
            _baseColor = color;
            _roleScale = role switch
            {
                MonsterRole.Runner => 0.8f,
                MonsterRole.Shield => 1.05f,
                MonsterRole.Ogre => 1.75f,
                _ => 1f
            };
            float s = _roleScale;
            GaitRate = role switch
            {
                MonsterRole.Runner => 1.5f,
                MonsterRole.Shield => 0.78f,
                MonsterRole.Ogre => 0.62f,
                _ => 1f
            };
            // The shield lives on a separate gear root so it can flinch with
            // the body while still kicking back independently when it blocks.
            _gearRoot = new GameObject("Gear").transform;
            _gearRoot.SetParent(transform, false);

            // Per-role silhouette: scale plus accents so roles read at a glance.
            _meshBaseRotation = role switch
            {
                MonsterRole.Runner => Quaternion.Euler(7f, 0f, 0f),    // forward-leaning scout
                MonsterRole.Shield => Quaternion.Euler(-2f, 0f, 0f),   // braced behind the shield
                MonsterRole.Ogre => Quaternion.Euler(3f, 0f, 0f),      // heavy, chest-forward brute
                _ => Quaternion.identity
            };

            // The shipped prefab carries three decimated levels of detail; the
            // source model is far too heavy to draw on a phone.
            var visualPrefab = Resources.Load<GameObject>("Prefabs/Goblin_Visual");
            if (visualPrefab != null)
            {
                // The imported goblin already carries its own skin and cloth
                // colours. Multiplying the texture with the full role colour
                // turned the rust tunic into mud, so the role is only a hint.
                _baseColor = Color.Lerp(Color.white, color, 0.45f);
                var visual = Instantiate(visualPrefab, transform);
                visual.name = "Goblin_3D_Mesh";
                _meshBaseY = 0.95f * s;
                visual.transform.localPosition = new Vector3(0f, _meshBaseY, 0f);
                visual.transform.localRotation = _meshBaseRotation;
                visual.transform.localScale = Vector3.one * (1.75f * s);
                _meshModelRoot = visual.transform;
                _importedVisual = true;
                _visualScale = 1.75f * s;
                _headBone = FindBone(visual.transform, "Head");
                _motion = visual.GetComponentInChildren<GoblinMotion>();
                if (_motion != null) _motion.WalkRate = GaitRate;

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
                CollectLodRenderers(visual.transform);

                var dummyLeg = new GameObject("DummyLeg").transform;
                dummyLeg.SetParent(transform, false);
                _leftLeg = dummyLeg; _rightLeg = dummyLeg;
                _weapon = dummyLeg;
            }
            else
            {
                // Keep every fallback part under one visual root so the flinch
                // can move the silhouette without moving its hitbox.
                var visual = new GameObject("Goblin_3D_Mesh").transform;
                visual.SetParent(transform, false);
                visual.localRotation = _meshBaseRotation;
                _meshModelRoot = visual;
                _meshBaseY = 0f;

                var bodyMat = Prim.Material(color, 0.2f);
                bodyMat.EnableKeyword("_EMISSION");
                bodyMat.SetColor("_EmissionColor", color * 0.12f);
                Color eyeColor = role == MonsterRole.Ogre ? new Color(1f, 0.35f, 0.2f)
                    : role == MonsterRole.Runner ? new Color(0.65f, 1f, 0.4f)
                    : new Color(1f, 0.85f, 0.2f);
                var eyeMat = Prim.Emissive(eyeColor, 2.2f);

                var leather = Prim.Material(new Color(0.27f,0.18f,0.12f));
                var iron = Prim.Material(new Color(0.35f,0.39f,0.41f),0.3f,0.45f);
                var body = Prim.Create(PrimitiveType.Capsule, "Body", visual, new Vector3(0f, 1.0f * s, 0f),
                    new Vector3(0.85f * s, 0.65f * s, 0.65f * s), bodyMat);
                Prim.Create(PrimitiveType.Cube,"Tunic",visual,new Vector3(0,0.85f*s,0),new Vector3(0.82f*s,0.55f*s,0.67f*s),leather,false);
                Prim.Create(PrimitiveType.Cube,"Belt",visual,new Vector3(0,0.9f*s,0.36f*s),new Vector3(0.85f*s,0.09f*s,0.06f),iron,false);
                _leftLeg=Prim.Create(PrimitiveType.Capsule,"Left boot",visual,new Vector3(-0.24f*s,0.32f*s,0),new Vector3(0.24f*s,0.32f*s,0.28f*s),leather,false).transform;
                _rightLeg=Prim.Create(PrimitiveType.Capsule,"Right boot",visual,new Vector3(0.24f*s,0.32f*s,0),new Vector3(0.24f*s,0.32f*s,0.28f*s),leather,false).transform;
                for(int side=-1;side<=1;side+=2)
                    Prim.Create(PrimitiveType.Capsule,"Arm",visual,new Vector3(side*0.58f*s,1.0f*s,0.06f),new Vector3(0.24f*s,0.45f*s,0.24f*s),bodyMat,false);
                _weapon=new GameObject("Weapon pivot").transform; _weapon.SetParent(visual,false); _weapon.localPosition=new Vector3(-0.64f*s,1.0f*s,0.25f);
                Prim.Create(PrimitiveType.Cylinder,"Club handle",_weapon,new Vector3(0,0.15f*s,0),new Vector3(0.12f*s,0.5f*s,0.12f*s),leather,false);
                Prim.Create(PrimitiveType.Cube,"Club head",_weapon,new Vector3(0,0.6f*s,0),new Vector3(0.28f*s,0.4f*s,0.28f*s),role==MonsterRole.Ogre?iron:leather,false);
                var head = Prim.Create(PrimitiveType.Sphere, "Head", visual, new Vector3(0f, 1.85f * s, 0f),
                    new Vector3(0.78f * s, 0.65f * s, 0.64f * s), bodyMat);
                Prim.Create(PrimitiveType.Cube,"Nose",visual,new Vector3(0,1.79f*s,0.37f*s),new Vector3(0.16f*s,0.17f*s,0.19f*s),bodyMat,false);
                Prim.Create(PrimitiveType.Cube,"Brow",visual,new Vector3(0,1.99f*s,0.30f*s),new Vector3(0.65f*s,0.09f*s,0.13f*s),leather,false);
                _renderers = new[] { body.GetComponent<Renderer>(), head.GetComponent<Renderer>() };

                var eyeL = Prim.Create(PrimitiveType.Sphere, "EyeL", visual, new Vector3(-0.14f * s, 1.9f * s, 0.26f * s),
                    new Vector3(0.1f * s, 0.1f * s, 0.1f * s), eyeMat, false);
                var eyeR = Prim.Create(PrimitiveType.Sphere, "EyeR", visual, new Vector3(0.14f * s, 1.9f * s, 0.26f * s),
                    new Vector3(0.1f * s, 0.1f * s, 0.1f * s), eyeMat, false);
                eyeL.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                eyeR.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                if (role != MonsterRole.Ogre)
                {
                    // Goblin ears make the small goblin silhouette unmistakable.
                    var earL = Prim.Create(PrimitiveType.Sphere, "EarL", visual, new Vector3(-0.46f * s, 1.95f * s, 0f),
                        new Vector3(0.3f * s, 0.16f * s, 0.12f * s), bodyMat, false);
                    earL.transform.localRotation = Quaternion.Euler(0f, 0f, 22f);
                    var earR = Prim.Create(PrimitiveType.Sphere, "EarR", visual, new Vector3(0.46f * s, 1.95f * s, 0f),
                        new Vector3(0.3f * s, 0.16f * s, 0.12f * s), bodyMat, false);
                    earR.transform.localRotation = Quaternion.Euler(0f, 0f, -22f);
                }
            }

            if (role == MonsterRole.Runner)
            {
                // The imported goblin is one skinned mesh, so a scout's identity
                // has to be added geometry. Attach it to the animated bones so it
                // moves with the body instead of floating as a block, and keep
                // every piece thin and small: a flat plate at torso height reads as
                // a slab, not clothing. Bandit-scout kit: bare green head with a
                // red bandana, red bandolier with stowed daggers, hip pouch,
                // and a red scarf streaming behind on the run.
                var redMat = GoblinGear.Red;
                var cowlMat = GoblinGear.Dark;
                var strapMat = GoblinGear.Leather;
                var bladeMat = GoblinGear.Metal;
                var boneRoot = _meshModelRoot != null ? _meshModelRoot : transform;

                // Hat-free head: a bare green face with only the red bandana
                // knot at the back of the skull marking the runner vs the
                // bare-headed raiders.
                var head = FindBone(boneRoot, "Head");
                if (head != null)
                {
                    // Round bandana knot, tails splayed down. Round profiles keep
                    // the accents from reading as hard slabs on the goblin.
                    // The knot keeps one red spot on the head as the aim marker.
                    var knot = Prim.Create(PrimitiveType.Sphere, "Runner bandana knot", head,
                        new Vector3(0f, 0.03f, -0.17f),
                        new Vector3(0.07f, 0.07f, 0.07f), redMat, false);
                    var tailL = Prim.Create(PrimitiveType.Capsule, "Runner bandana tail L", head,
                        new Vector3(-0.04f, -0.055f, -0.19f),
                        new Vector3(0.018f, 0.075f, 0.018f), redMat, false);
                    tailL.transform.localRotation = Quaternion.Euler(14f, 0f, 16f);
                    var tailR = Prim.Create(PrimitiveType.Capsule, "Runner bandana tail R", head,
                        new Vector3(0.04f, -0.06f, -0.19f),
                        new Vector3(0.018f, 0.085f, 0.018f), redMat, false);
                    tailR.transform.localRotation = Quaternion.Euler(6f, 0f, -20f);
                    // One mesh for the whole bandana instead of three objects.
                    GoblinGear.Merge("Runner bandana", head, redMat,
                        new[] { knot, tailL, tailR });
                }

                // Bandolier: one thin red cord slung over the shoulder — a
                // cylinder reads as a wrapped cord where it crosses the chest,
                // where a flat plate read as a slab.
                var spine = FindBone(boneRoot, "Spine02");
                if (spine != null)
                {
                    var strap = Prim.Create(PrimitiveType.Cylinder, "Runner bandolier", spine,
                        new Vector3(0f, 0.045f, 0.095f),
                        new Vector3(0.024f, 0.17f, 0.024f), redMat, false);
                    strap.transform.localRotation = Quaternion.Euler(-14f, 0f, 38f);

                    // Dagger handles ride the cord, each tipped with a small
                    // pommel; parented to the spine so nothing inherits skew.
                    var blades = new List<GameObject>();
                    for (int d = 0; d < 2; d++)
                    {
                        float t = d == 0 ? 0.055f : -0.055f;    // along the cord: y = 0.045 + t·tan(38°)
                        var handle = Prim.Create(PrimitiveType.Cylinder, "Runner dagger handle", spine,
                            new Vector3(t, 0.045f + t * 0.78f, 0.125f),
                            new Vector3(0.017f, 0.042f, 0.017f), bladeMat, false);
                        handle.transform.localRotation = Quaternion.Euler(-14f, 0f, 38f);
                        blades.Add(handle);
                        var pommel = Prim.Create(PrimitiveType.Sphere, "Runner dagger pommel", spine,
                            new Vector3(t - 0.029f, 0.022f + t * 0.78f, 0.125f),
                            new Vector3(0.03f, 0.03f, 0.03f), bladeMat, false);
                        blades.Add(pommel);
                    }
                    GoblinGear.Merge("Runner daggers", spine, bladeMat, blades);

                    // Red scarf from the upper back: a directional tell while
                    // running. Update() animates it with the flutter baseline.
                    var scarf = Prim.Create(PrimitiveType.Capsule, "Runner scarf tail", spine,
                        new Vector3(0.05f, -0.04f, -0.105f),
                        new Vector3(0.02f, 0.105f, 0.02f), redMat, false);
                    _runnerTail = scarf.transform;
                }

                if (_runnerTail == null)
                {
                    // No rig (fallback goblin): pin the scarf in body space instead.
                    var scarf = Prim.Create(PrimitiveType.Capsule, "Runner scarf tail", _meshModelRoot,
                        new Vector3(0.09f * s, 1.0f * s, -0.2f * s),
                        new Vector3(0.025f * s, 0.12f * s, 0.025f * s), redMat, false);
                    scarf.transform.localRotation = Quaternion.Euler(30f, 0f, 16f);
                    _runnerTail = scarf.transform;
                }

                // Hip pouch on the back-left hip, closed by a dark flap.
                var pelvis = FindBone(boneRoot, "Pelvis");
                if (pelvis != null)
                {
                    var pouch = Prim.Create(PrimitiveType.Cube, "Runner pouch", pelvis,
                        new Vector3(-0.1f, -0.04f, -0.095f),
                        new Vector3(0.105f, 0.125f, 0.075f), strapMat, false);
                    pouch.transform.localRotation = Quaternion.Euler(0f, -15f, 0f);
                    var flap = Prim.Create(PrimitiveType.Cube, "Runner pouch flap", pelvis,
                        new Vector3(-0.1f, 0.035f, -0.095f),
                        new Vector3(0.115f, 0.055f, 0.085f), cowlMat, false);
                    flap.transform.localRotation = Quaternion.Euler(0f, -15f, 0f);
                }
            }

            if (role == MonsterRole.Shield)
            {
                // Broad wooden shield covers the body; the head stays exposed above.
                _shieldBaseColor = new Color(0.36f, 0.26f, 0.16f);
                var woodMat = GoblinGear.Wood;
                var ironMat = GoblinGear.Iron;
                // The whole shield assembly moves as one so a blocked arrow
                // produces a readable kick instead of a dead stop.
                var rig = new GameObject("Shield rig").transform;
                rig.SetParent(_gearRoot, false);
                _shieldRig = rig;
                _shieldBaseLocalPosition = Vector3.zero;
                _shieldBaseLocalRotation = Quaternion.identity;

                // The board used to be a plain slab, which read as a box
                // parked in front of the goblin. A rounded top, plank seams
                // and an iron rim give it a silhouette of its own.
                var planks = new List<GameObject>();
                // Thick and clearly in front so fast arrows can't tunnel into the body.
                var shield = Prim.Create(PrimitiveType.Cube, "Shield", rig, new Vector3(0f, 0.70f * s, 0.70f * s),
                    new Vector3(1.25f * s, 1.05f * s, 0.18f), woodMat);   // collider blocks arrows
                planks.Add(shield);
                // Rounded head of the shield, so the top is not a flat edge.
                // A Unity cylinder is 2 units tall, so the Y scale carries the
                // thickness and X/Z carry the ellipse.
                var cap = Prim.Create(PrimitiveType.Cylinder, "Shield cap", rig,
                    new Vector3(0f, 1.025f * s, 0.70f * s),
                    new Vector3(1.25f * s, 0.09f * s, 0.40f * s), woodMat, false);
                cap.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                planks.Add(cap);
                var board = GoblinGear.Merge("Shield board", rig, woodMat,
                    new[] { shield, cap });
                _shieldRenderer = board != null ? board.GetComponent<Renderer>()
                    : shield.GetComponent<Renderer>();

                // Iron rim, centre strap, plank seams and a boss that points
                // at the archer.
                var iron = new List<GameObject>();
                // Two vertical seams break the flat board into planks.
                for (int p = -1; p <= 1; p += 2)
                {
                    var seam = Prim.Create(PrimitiveType.Cube, "Shield seam", rig,
                        new Vector3(p * 0.31f * s, 0.70f * s, 0.80f * s),
                        new Vector3(0.05f * s, 1.00f * s, 0.05f * s), ironMat, false);
                    iron.Add(seam);
                }
                iron.Add(Prim.Create(PrimitiveType.Cube, "Shield rim top", rig,
                    new Vector3(0f, 1.19f * s, 0.80f * s),
                    new Vector3(1.28f * s, 0.11f * s, 0.06f * s), ironMat, false));
                iron.Add(Prim.Create(PrimitiveType.Cube, "Shield rim bottom", rig,
                    new Vector3(0f, 0.22f * s, 0.80f * s),
                    new Vector3(1.28f * s, 0.11f * s, 0.06f * s), ironMat, false));
                for (int p = -1; p <= 1; p += 2)
                    iron.Add(Prim.Create(PrimitiveType.Cube, "Shield rim side", rig,
                        new Vector3(p * 0.60f * s, 0.70f * s, 0.80f * s),
                        new Vector3(0.11f * s, 1.08f * s, 0.06f * s), ironMat, false));
                iron.Add(Prim.Create(PrimitiveType.Cube, "Shield strap", rig,
                    new Vector3(0f, 0.70f * s, 0.81f * s),
                    new Vector3(0.11f * s, 1.10f * s, 0.05f * s), ironMat, false));
                var boss = Prim.Create(PrimitiveType.Sphere, "Shield boss", rig,
                    new Vector3(0f, 0.70f * s, 0.85f * s),
                    new Vector3(0.25f * s, 0.25f * s, 0.16f * s), ironMat, false);
                iron.Add(boss);
                var spike = Prim.Create(PrimitiveType.Cylinder, "Shield spike", rig,
                    new Vector3(0f, 0.70f * s, 0.92f * s),
                    new Vector3(0.13f * s, 0.13f * s, 0.09f * s), ironMat, false);
                spike.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                iron.Add(spike);
                GoblinGear.Merge("Shield ironwork", rig, ironMat, iron);
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
            // The bar used to sit on a hard-coded 2.5 units, which floated a
            // good half body height above the goblin's actual head. Anchor it
            // to the head bone instead.
            BuildHealthBar(HealthBarHeight());
            var ring = new GameObject("Attack warning"); ring.transform.SetParent(transform,false);
            ring.transform.localPosition = new Vector3(0,0.06f,0);
            _attackRing = ring.AddComponent<LineRenderer>();
            _attackRingMaterial = Prim.Unlit(new Color(1f,0.43f,0.17f));
            _attackRing.sharedMaterial = _attackRingMaterial;
            _attackRing.useWorldSpace = false; _attackRing.loop = true; _attackRing.positionCount = 32;
            _attackRing.widthMultiplier = 0.06f;
            for(int i=0;i<32;i++) { float angle=i*Mathf.PI*2f/32; _attackRing.SetPosition(i,new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*s*0.65f); }
            _attackRing.enabled = false;

            _camera = Camera.main != null ? Camera.main.transform : null;
        }

        /// <summary>
        /// Picks the level of detail that matches how much of the screen this
        /// goblin covers. The prefab ships three decimated meshes; only one is
        /// ever drawn, so a raid of five enemies costs a fraction of the
        /// original 1.4M triangles each.
        /// </summary>
        void CollectLodRenderers(Transform visual)
        {
            _lodRenderers.Clear();
            foreach (var r in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
                _lodRenderers.Add(r);
            _lodRenderers.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            _lodIndex = 0;
            for (int i = 0; i < _lodRenderers.Count; i++)
                _lodRenderers[i].enabled = i == 0;
        }

        void UpdateLod()
        {
            if (_lodRenderers.Count < 2 || _camera == null) return;
            // Bigger roles switch later, because they cover more of the screen.
            float distance = Vector3.Distance(transform.position, _camera.position)
                / Mathf.Max(0.01f, _roleScale);
            int target = _lodIndex;
            if (_lodIndex == 0)
            {
                if (distance > Lod0Far) target = 1;
            }
            else if (_lodIndex == 1)
            {
                if (distance > Lod1Far) target = 2;
                else if (distance < Lod0Near) target = 0;
            }
            else if (distance < Lod1Near) target = 1;

            if (target == _lodIndex) return;
            _lodRenderers[_lodIndex].enabled = false;
            _lodRenderers[target].enabled = true;
            _lodIndex = target;
        }

        /// <summary>Height of the health bar above the monster's feet.</summary>
        float HealthBarHeight()
        {
            if (_headBone != null)
                return transform.InverseTransformPoint(_headBone.position).y + 0.24f * _visualScale;
            return SilhouetteTop() + 0.26f * _roleScale;
        }

        /// <summary>World height of the top of this monster's silhouette.</summary>
        float SilhouetteTop()
        {
            float top = 0f;
            if (_renderers == null) return 0f;
            foreach (var r in _renderers)
            {
                if (r == null || !r.enabled) continue;
                // Costume pieces ride the bones and never break the outline.
                if (r.name.StartsWith("Runner ")) continue;
                // A skinned renderer only knows its deformed bounds once the
                // skinning has run, so use the mesh bounds directly.
                if (r is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                    top = Mathf.Max(top, skin.transform.TransformPoint(skin.sharedMesh.bounds.max).y);
                else
                    top = Mathf.Max(top, r.bounds.max.y);
            }
            return top;
        }

        void BuildHealthBar(float topY)
        {
            var barRoot = new GameObject("HealthBar");
            barRoot.transform.SetParent(transform, false);
            barRoot.transform.localPosition = new Vector3(0f, topY, 0f);
            _healthBar = barRoot.transform;
            // The bar used to be a unit wide, which is wider than a goblin.
            _barWidth = 0.66f * _roleScale;
            _barHeight = 0.10f * _roleScale;

            Prim.Create(PrimitiveType.Cube, "Back", barRoot.transform, Vector3.zero,
                new Vector3(_barWidth, _barHeight, 0.02f), Prim.Material(new Color(0.06f, 0.06f, 0.08f)), false);

            var fill = Prim.Create(PrimitiveType.Cube, "Fill", barRoot.transform, new Vector3(0f, 0f, -0.02f),
                new Vector3(_barWidth, _barHeight, 0.02f), Prim.Material(new Color(0.85f, 0.2f, 0.2f)), false);
            _healthFill = fill.transform;
        }

        /// <summary>
        /// Finds the head band of the built silhouette so arrows can award
        /// headshot damage without relying on collider names.
        ///
        /// The band is anchored to the head bone and the top of the skull in
        /// the mesh. Renderer bounds were the old source, but they only settle
        /// after the first skinning pass, so a band measured while the rig was
        /// still in its bind pose landed below the neck.
        /// </summary>
        void MeasureHeadHitbox()
        {
            _headInModelSpace = false;
            _headBottomLocal = 1.5f;
            _headTopLocal = 1.9f;
            if (_meshModelRoot == null) return;

            _headInModelSpace = true;
            var skin = _meshModelRoot.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin != null && skin.sharedMesh != null)
            {
                // The top of the skull comes from the mesh, which never moves.
                Vector3 world = skin.transform.TransformPoint(skin.sharedMesh.bounds.max);
                float topLocal = _meshModelRoot.InverseTransformPoint(world).y;
                if (_headBone != null)
                {
                    Vector3 headLocal = _meshModelRoot.InverseTransformPoint(_headBone.position);
                    _headBottomLocal = headLocal.y + 0.01f;
                    _headTopLocal = Mathf.Max(topLocal + 0.04f, headLocal.y + 0.05f);
                    return;
                }
            }

            // No head bone: fall back to the top of the silhouette.
            var source = _meshModelRoot.GetComponentsInChildren<Renderer>();
            if (source == null || source.Length == 0) return;

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
            if (!hasBounds) return;

            // The head sits in the top ~22% of every role's silhouette. Cap the
            // band at the silhouette itself so shots sailing overhead do not count.
            float headBottomWorld = bounds.max.y - bounds.size.y * 0.22f;
            float headTopWorld = bounds.max.y;
            Transform space = _meshModelRoot;
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
                float fall = Mathf.SmoothStep(0f,1f,_deathTime/0.42f);
                transform.rotation = _deathRotation * Quaternion.Euler(fall*85f,0,fall*12f);
                // Clear the lane sooner: colliders are already off, and the
                // body now sinks away instead of lingering in the next shot.
                transform.position = _deathPosition - Vector3.up * Mathf.Max(0,_deathTime-0.22f)*2.4f;
                if (_deathTime >= 0.68f) Destroy(gameObject);
                return;
            }
            if (_target == null) return;

            UpdateLod();

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

            // Reaction timers decay without pausing the attack clock. A body
            // hit must not silently buy the enemy a longer wind-up.
            if (_flinchTimer > 0f) _flinchTimer = Mathf.Max(0f, _flinchTimer - Time.deltaTime);
            if (_interruptTimer > 0f) _interruptTimer = Mathf.Max(0f, _interruptTimer - Time.deltaTime);
            UpdateShieldReaction();

            // Fan into separate melee positions instead of stacking on the same point.
            float lane = Mathf.Clamp(transform.position.x,-0.8f,0.8f);
            var toTarget = _target.position + Vector3.right*lane - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            bool moving = distance > _attackRange;

            _attackRecovery = Mathf.Max(0f, _attackRecovery - Time.deltaTime);
            float moveSpeed = Speed * _slowFactor;
            // A broken brute swing visibly stalls him for a moment; body hits
            // never touch movement or attack timing.
            if (Role == MonsterRole.Ogre && _interruptTimer > 0f) moveSpeed *= 0.15f;

            if (moving)
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

                if (Role == MonsterRole.Runner && !_reportedGate)
                {
                    _reportedGate = true;
                    GameManager.Instance?.NotifyRunnerReachedGate(this);
                }

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
                        GameManager.Instance?.DamageArcher(_attackDamage, Role);
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
            if (_winding)
            {
                float progress = WindupProgress;
                _attackRing.widthMultiplier = Mathf.Lerp(0.04f, 0.13f, progress);
                _attackRing.transform.localScale = Vector3.one * Mathf.Lerp(0.82f, 1.65f, progress);
                if (_attackRingMaterial != null)
                    Prim.SetColor(_attackRingMaterial, Color.Lerp(new Color(1f, 0.58f, 0.18f),
                        new Color(1f, 0.20f, 0.10f), progress));
            }
            else
            {
                _attackRing.transform.localScale = Vector3.one;
            }

            _walkPhase += Time.deltaTime * moveSpeed * 5f * GaitRate;
            float strideAmplitude = Role switch
            {
                MonsterRole.Runner => 33f,
                MonsterRole.Shield => 15f,
                MonsterRole.Ogre => 19f,
                _ => 24f
            };
            float stride = moving ? Mathf.Sin(_walkPhase) * strideAmplitude : 0f;
            _leftLeg.localRotation = Quaternion.Euler(stride,0,0);
            _rightLeg.localRotation = Quaternion.Euler(-stride,0,0);
            _weapon.localRotation = Quaternion.Euler(_winding ? -70f * (1f-_windUpTimer/Mathf.Max(WindUp,0.01f)) : stride*0.4f,0,-12f);

            if (_motion != null && _motion.enabled)
            {
                _motion.Sample(moving, moveSpeed, _winding, WindupProgress,
                    _attackRecovery > 0f ? Mathf.Max(0.0001f, 1f - _attackRecovery / RecoveryDuration) : 0f);
            }

            UpdateVisualPose(moving);

            if (_runnerTail != null)
            {
                // The scarf streams back while the scout is moving.
                float flutter = moving ? Mathf.Sin(_walkPhase * 1.7f) * 10f : 0f;
                _runnerTail.localRotation = Quaternion.Euler(30f + flutter, 0f, 16f);
            }

            UpdateTint();
        }

        /// <summary>
        /// Applies a short visual flinch along the arrow's travel direction.
        /// The monster root, lane position and attack range never move, so a
        /// body hit cannot push an enemy out of range or reset its attack.
        /// </summary>
        void UpdateVisualPose(bool moving)
        {
            float envelope = _flinchTimer > 0f
                ? Mathf.Sin(Mathf.Clamp01(_flinchTimer / Mathf.Max(0.01f, _flinchDuration)) * Mathf.PI * 0.5f)
                : 0f;
            Vector3 localDirection = transform.InverseTransformDirection(_flinchDirection);
            localDirection.y = 0f;
            if (localDirection.sqrMagnitude < 0.0001f) localDirection = Vector3.back;
            localDirection.Normalize();

            float amplitude = 0.055f * _flinchStrength * envelope * Mathf.Max(0.65f, _roleScale);
            Vector3 flinchOffset = localDirection * amplitude;
            float angle = 10f * _flinchStrength * envelope;
            Quaternion flinchRotation = Quaternion.Euler(localDirection.z * angle, 0f, -localDirection.x * angle);

            Vector3 position = new Vector3(0f, _meshBaseY, 0f);
            Quaternion rotation = _meshBaseRotation * flinchRotation;
            float lungeZ = 0f;

            if (_motion == null || !_motion.enabled)
            {
                // Placeholder rig: add locomotion bob, sway and the wind-up
                // lunge on top of the role's base stance.
                float bob = moving ? Mathf.Abs(Mathf.Sin(_walkPhase)) * 0.08f : 0f;
                float tilt = moving ? Mathf.Cos(_walkPhase) * 4.5f : 0f;
                float progress = _winding ? 1f - _windUpTimer / Mathf.Max(WindUp, 0.01f) : 0f;
                lungeZ = _winding ? Mathf.Sin(progress * Mathf.PI * 0.5f) * 0.25f : 0f;
                float lungeRotX = _winding ? -18f * progress : 0f;
                position += new Vector3(0f, bob, lungeZ);
                rotation = _meshBaseRotation * Quaternion.Euler(lungeRotX, 0f, tilt) * flinchRotation;
            }

            position += flinchOffset;
            if (_meshModelRoot != null)
            {
                _meshModelRoot.localPosition = position;
                _meshModelRoot.localRotation = rotation;
            }

            // The shield shares the body's flinch but can also kick on a block.
            if (_gearRoot != null)
            {
                _gearRoot.localPosition = new Vector3(0f, 0f, lungeZ) + flinchOffset;
                _gearRoot.localRotation = flinchRotation;
            }
        }

        void UpdateShieldReaction()
        {
            if (_shieldRig == null) return;

            if (_shieldKickTimer > 0f)
            {
                _shieldKickTimer = Mathf.Max(0f, _shieldKickTimer - Time.deltaTime);
                float t = 1f - _shieldKickTimer / ShieldKickDuration;
                float envelope = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                Vector3 offset = _shieldKickDirection * (0.10f * envelope * _roleScale);
                _shieldRig.localPosition = _shieldBaseLocalPosition + offset;
                _shieldRig.localRotation = _shieldBaseLocalRotation * Quaternion.Euler(
                    _shieldKickDirection.z * 12f * envelope, 0f, -_shieldKickDirection.x * 12f * envelope);
            }
            else
            {
                _shieldRig.localPosition = _shieldBaseLocalPosition;
                _shieldRig.localRotation = _shieldBaseLocalRotation;
            }

            if (_shieldRenderer == null) return;
            if (_tintBlock == null) _tintBlock = new MaterialPropertyBlock();
            if (_shieldFlash > 0f)
            {
                _shieldFlash = Mathf.Max(0f, _shieldFlash - Time.deltaTime * 5f);
                _shieldColor = Color.Lerp(_shieldBaseColor, new Color(0.82f, 0.90f, 1f), _shieldFlash);
                _tintBlock.SetColor("_BaseColor", _shieldColor);
                _tintBlock.SetColor("_Color", _shieldColor);
                _shieldRenderer.SetPropertyBlock(_tintBlock);
            }
            else if (_shieldColor != _shieldBaseColor)
            {
                _shieldColor = _shieldBaseColor;
                _tintBlock.SetColor("_BaseColor", _shieldColor);
                _tintBlock.SetColor("_Color", _shieldColor);
                _shieldRenderer.SetPropertyBlock(_tintBlock);
            }
        }

        /// <summary>Called by an arrow that was stopped by the shield.</summary>
        public void BlockHit(Vector3 point, Vector3 direction)
        {
            if (IsDead || _shieldRig == null) return;
            _shieldKickTimer = ShieldKickDuration;
            _shieldFlash = 1f;
            Vector3 local = transform.InverseTransformDirection(direction);
            local.y = 0f;
            if (local.sqrMagnitude < 0.0001f) local = Vector3.back;
            _shieldKickDirection = local.normalized;
        }

        void ApplyFlinch(Vector3 worldDirection, bool precision)
        {
            Vector3 direction = worldDirection;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = -transform.forward;
            _flinchDirection = direction.normalized;
            _flinchDuration = precision ? 0.28f : 0.16f;
            _flinchTimer = _flinchDuration;
            float roleWeight = Role == MonsterRole.Ogre ? 0.55f : Role == MonsterRole.Runner ? 1.15f : 1f;
            float motionScale = GameSettings.ReducedMotion ? 0.35f : 1f;
            _flinchStrength = (precision ? 1.7f : 1f) * roleWeight * motionScale;
        }

        MaterialPropertyBlock _tintBlock;
        Color _lastTint;
        bool _hasTint;

        void UpdateTint()
        {
            Color c = _baseColor;
            if (_winding)
            {
                float p = 1f - Mathf.Clamp01(_windUpTimer / Mathf.Max(WindUp, 0.01f));
                c = Color.Lerp(_baseColor, new Color(1f, 0.5f, 0.15f), 0.4f + 0.5f * p);
            }
            if (_interruptTimer > 0f)
            {
                // A broken swing reads as a pale, vulnerable stagger.
                float p = Mathf.Clamp01(_interruptTimer / 1.1f);
                c = Color.Lerp(c, new Color(0.76f, 0.88f, 1f), 0.55f * p);
            }
            if (_hitFlash > 0f)
            {
                _hitFlash -= Time.deltaTime * 4f;
                c = Color.Lerp(c, Color.white, Mathf.Clamp01(_hitFlash));
            }
            ApplyTint(c);
        }

        void UpdateHitFlash()
        {
            if (_hitFlash <= 0f) return;
            _hitFlash -= Time.deltaTime * 4f;
            ApplyTint(Color.Lerp(_baseColor, Color.white, Mathf.Clamp01(_hitFlash)));
        }

        /// <summary>
        /// Property-block writes are not free, so idle enemies skip the update
        /// when their tint has not changed. Hit/wind-up frames still animate.
        /// </summary>
        void ApplyTint(Color c)
        {
            if (_hasTint
                && Mathf.Abs(c.r - _lastTint.r) < 0.002f
                && Mathf.Abs(c.g - _lastTint.g) < 0.002f
                && Mathf.Abs(c.b - _lastTint.b) < 0.002f)
                return;
            _hasTint = true;
            _lastTint = c;

            if (_tintBlock == null) _tintBlock = new MaterialPropertyBlock();
            _tintBlock.SetColor("_BaseColor", c);
            _tintBlock.SetColor("_Color", c);
            foreach (var r in _renderers)
            {
                if (r != null) r.SetPropertyBlock(_tintBlock);
            }
        }

        void LateUpdate()
        {
            if (_healthBar == null) return;
            if (_headBone != null)
            {
                // Follow the head so the bar never drifts off the silhouette.
                _healthBar.position = _headBone.position + Vector3.up * (0.24f * _visualScale);
            }
            if (_camera != null)
                _healthBar.rotation = Quaternion.LookRotation(_healthBar.position - _camera.position, Vector3.up);
        }

        /// <summary>A slowing shot reduces movement speed for a while; re-applying refreshes it.</summary>
        public void ApplySlow(float factor, float duration)
        {
            if (factor <= 0f || factor >= 1f || duration <= 0f) return;
            _slowFactor = Mathf.Min(_slowFactor, factor);
            _slowTimer = Mathf.Max(_slowTimer, duration);
        }

        public void TakeDamage(float amount, bool precision = false, Vector3 hitDirection = default)
        {
            if (Health <= 0f || amount <= 0f || GameManager.Instance == null
                || (GameManager.Instance.State != GameState.Playing && GameManager.Instance.State != GameState.Tutorial)) return;
            Health -= amount;
            _hitFlash = 1f;
            ApplyFlinch(hitDirection, precision);

            if (precision)
            {
                // A headshot is the brute's interruption window: the swing stops
                // and he visibly recovers. Body hits never touch this clock.
                bool interrupted = _winding;
                if (interrupted)
                {
                    _winding = false;
                    _attackRing.enabled = false;
                    _attackTimer = Mathf.Max(_attackInterval, RecoveryDuration);
                    _attackRecovery = Role == MonsterRole.Ogre ? 0.65f : 0.38f;
                    _interruptTimer = Role == MonsterRole.Ogre ? 1.1f : 0.55f;
                    _weapon.localRotation = Quaternion.Euler(12f,0,-12f);
                    GameFeedback.Instance?.Interrupted(
                        transform.position + Vector3.up * (1.1f * _roleScale), _roleScale,
                        Role == MonsterRole.Ogre);
                    GameManager.Instance?.NotifyMonsterInterrupted(this);
                }
            }

            if (_healthFill != null)
            {
                float ratio = Mathf.Clamp01(Health / MaxHealth);
                var s = _healthFill.localScale;
                _healthFill.localScale = new Vector3(ratio, s.y, s.z);
                _healthFill.localPosition = new Vector3(-(1f - ratio) * _barWidth * 0.5f, 0f, -0.02f);
            }

            if (Health <= 0f) { DiedFromHeadshot = precision; Die(); }
        }

        void Die()
        {
            if (IsDead) return;
            IsDead = true;
            _winding = false;
            _interruptTimer = 0f;
            _flinchTimer = 0f;
            _attackRing.enabled = false;
            _healthBar.gameObject.SetActive(false);
            foreach(var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            _deathPosition = transform.position;
            _deathRotation = transform.rotation;
            GameFeedback.Instance?.Defeated(transform.position + Vector3.up * (0.8f * _roleScale),
                _roleScale, DiedFromHeadshot, Role == MonsterRole.Ogre);
            Died?.Invoke(this);
        }
    }
}
