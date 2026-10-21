using UnityEngine;

namespace Archer
{
    /// <summary>
    /// Ballistic projectile. Flies under gravity, orients along its velocity
    /// and deals damage to the first monster it hits.
    /// </summary>
    public class Arrow : MonoBehaviour
    {
        public float Damage = 25f;

        [Tooltip("Damage multiplier when the arrow lands on a monster's head.")]
        public float HeadshotMultiplier = 1.75f;

        public bool Piercing;
        int _pierceRemaining;
        float _slowFactor = 1f, _slowDuration;
        readonly System.Collections.Generic.HashSet<Monster> _pierced = new();

        Rigidbody _rb;
        Vector3 _flightVelocity;
        Material _headMaterial, _featherMaterial, _trailMaterial;
        TrailRenderer _trail;
        bool _spent;
        float _lifeTimer;

        public static Arrow Create(Transform parent, Vector3 position, Vector3 velocity,
            float damage, Material shaftMaterial, bool piercing = false, int pierceCapacity = 1,
            float slowFactor = 1f, float slowDuration = 0f, float headshotMultiplier = 1.75f)
        {
            var root = new GameObject("Arrow");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            if (velocity.sqrMagnitude > 0.0001f)
                root.transform.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);

            var shaft = Prim.Create(PrimitiveType.Cylinder, "Shaft", root.transform, Vector3.zero,
                new Vector3(0.035f, 0.42f, 0.035f), shaftMaterial, false);
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var headMaterial = Prim.Material(new Color(0.8f, 0.82f, 0.88f), 0.6f, 0.9f);
            var featherMaterial = Prim.Material(new Color(0.85f, 0.3f, 0.25f));
            var head = Prim.Create(PrimitiveType.Cube, "Head", root.transform, new Vector3(0f, 0f, 0.46f),
                new Vector3(0.07f, 0.07f, 0.16f), headMaterial, false);
            head.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);

            Prim.Create(PrimitiveType.Cube, "Fletching", root.transform, new Vector3(0f, 0f, -0.4f),
                new Vector3(0.14f, 0.02f, 0.14f), featherMaterial, false);

            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 2; // Z axis
            col.radius = 0.06f;
            col.height = 1.0f;
            col.center = new Vector3(0f, 0f, 0.03f);

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 0.05f;
            rb.useGravity = true;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.05f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var arrow = root.AddComponent<Arrow>();
            arrow._headMaterial = headMaterial;
            arrow._featherMaterial = featherMaterial;
            arrow._trailMaterial = Prim.Unlit(new Color(1f, .83f, .48f));
            arrow._trail = root.AddComponent<TrailRenderer>();
            arrow._trail.sharedMaterial = arrow._trailMaterial;
            arrow._trail.time = 0.18f;
            arrow._trail.minVertexDistance = 0.08f;
            arrow._trail.startWidth = 0.035f; arrow._trail.endWidth = 0f;
            arrow._trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arrow._trail.receiveShadows = false;
            arrow._trail.numCapVertices = 2;
            arrow.Damage = damage;
            arrow.HeadshotMultiplier = headshotMultiplier;
            arrow.Piercing = piercing;
            arrow._pierceRemaining = piercing ? Mathf.Max(0, pierceCapacity) : 0;
            arrow._slowFactor = slowFactor;
            arrow._slowDuration = slowDuration;
            arrow._rb = rb;
            arrow._rb.linearVelocity = velocity;
            arrow._flightVelocity = velocity;

            var flightClip = Resources.Load<AudioClip>("Audio/Arrow_Flight");
            float flightVolume = 0.65f * GameSettings.EffectsVolume;
            if (flightClip != null && GameSettings.SoundEnabled && flightVolume > 0f)
            {
                var audio = root.AddComponent<AudioSource>();
                audio.clip = flightClip;
                audio.spatialBlend = 0.65f;
                audio.minDistance = 2f;
                audio.maxDistance = 35f;
                audio.volume = flightVolume;
                audio.Play();
            }

            return arrow;
        }

        void OnDestroy()
        {
            if (_trailMaterial != null) Destroy(_trailMaterial);
            if (_headMaterial != null) Destroy(_headMaterial);
            if (_featherMaterial != null) Destroy(_featherMaterial);
        }

        void FixedUpdate()
        {
            if (_rb == null) return;
            var v = _rb.linearVelocity;
            if (v.sqrMagnitude > 0.5f)
                transform.rotation = Quaternion.LookRotation(v.normalized, Vector3.up);
            if (v.sqrMagnitude > 0.5f) _flightVelocity = v;
        }

        void Update()
        {
            _lifeTimer += Time.deltaTime;
            if (_lifeTimer > 10f && !_spent)
            {
                GameManager.Instance?.ReportMiss(MissReason.Missed, transform.position);
                Expire();
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (_spent) return;
            var gm = GameManager.Instance;
            if (gm == null || (gm.State != GameState.Playing && gm.State != GameState.Tutorial))
            { Expire(); return; }

            Vector3 point = collision.GetContact(0).point;

            // A shield goblin's shield blocks the shot (aim over it, at the head).
            if (collision.collider.name == "Shield")
            {
                collision.collider.GetComponentInParent<Monster>()?.BlockHit(point, _flightVelocity);
                gm.ReportMiss(MissReason.Blocked, point);
                GameFeedback.Instance?.Hit(point, 0, false, true);
                Expire(collision.collider.transform);
                return;
            }

            var monster = collision.collider.GetComponentInParent<Monster>();
            if (monster != null)
            {
                // Already passed through this one: ignore further contacts with it.
                if (_pierced.Contains(monster)) return;
                if (monster.IsDead) { Expire(); return; }
                bool headshot = monster.IsHeadshot(point);
                float damage = Damage * (headshot ? HeadshotMultiplier : 1f);
                // Crippling shot rewards keeping enemies in the slowing field.
                if (gm.CripplingShot && monster.SlowFactor < 0.999f)
                    damage *= 1f + gm.CrippleDamageBonus;
                bool lethal = damage >= monster.Health - 0.0001f;
                gm.RegisterHit(headshot);
                GameFeedback.Instance?.Hit(point, damage, headshot, false, lethal);
                if (_slowDuration > 0f) monster.ApplySlow(_slowFactor, _slowDuration);

                // A piercing arrow passes through and keeps flying into the next enemy.
                if (_pierceRemaining > 0)
                {
                    _pierceRemaining--;
                    _pierced.Add(monster);
                    // The goblin has a kinematic body; without this the solver
                    // shoves the arrow backwards and it drops at the target's feet.
                    var arrowCollider = GetComponent<Collider>();
                    if (arrowCollider != null)
                        foreach (var other in monster.GetComponentsInChildren<Collider>())
                            Physics.IgnoreCollision(arrowCollider, other, true);
                    if (_rb != null)
                    {
                        _rb.linearVelocity = _flightVelocity;
                        _rb.angularVelocity = Vector3.zero;
                    }
                    monster.TakeDamage(damage, headshot, _flightVelocity);
                    gm.NotifyMonsterHit(monster, headshot);
                    return;
                }

                monster.TakeDamage(damage, headshot, _flightVelocity);
                gm.NotifyMonsterHit(monster, headshot);
                Expire(monster.transform);
                return;
            }

            // A resolved miss: tell short from wide so feedback can coach the shot.
            // In practice the tutorial director owns the coaching copy.
            var reason = gm.ClassifyEnvironmentMiss(point);
            gm.ReportMiss(reason, point);
            if (gm.State != GameState.Tutorial) GameFeedback.Instance?.Miss(reason, point);
            Expire();
        }

        void Expire(Transform attachTo = null)
        {
            _spent = true;
            if (_trail != null) _trail.emitting = false;

            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;

            if (attachTo != null)
            {
                // A Rigidbody owns its transform, so remove it or the arrow won't
                // follow the monster; then embed so it never floats in mid-air.
                if (_rb != null) Destroy(_rb);
                transform.SetParent(attachTo, true);
            }
            else if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.isKinematic = true;
            }

            Destroy(gameObject, 0.9f);
        }
    }
}
