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

        Rigidbody _rb;
        Material _headMaterial, _featherMaterial, _trailMaterial;
        TrailRenderer _trail;
        bool _spent;
        float _lifeTimer;

        public static Arrow Create(Transform parent, Vector3 position, Vector3 velocity,
            float damage, Material shaftMaterial)
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
            arrow._rb = rb;
            arrow._rb.linearVelocity = velocity;

            var flightClip = Resources.Load<AudioClip>("Audio/Arrow_Flight");
            if (flightClip != null && PlayerPrefs.GetInt("LastRanger.Sound", 1) != 0)
            {
                var audio = root.AddComponent<AudioSource>();
                audio.clip = flightClip;
                audio.spatialBlend = 0.65f;
                audio.minDistance = 2f;
                audio.maxDistance = 35f;
                audio.volume = 0.65f;
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
        }

        void Update()
        {
            _lifeTimer += Time.deltaTime;
            if (_lifeTimer > 10f && !_spent) { GameManager.Instance?.RegisterMiss(); Expire(); }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (_spent) return;
            if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing)
            { Expire(); return; }

            // A shield goblin's shield blocks the shot (aim over it, at the head).
            if (collision.collider.name == "Shield")
            {
                GameManager.Instance?.RegisterMiss();
                GameFeedback.Instance?.Hit(collision.GetContact(0).point, 0, false, true);
                Expire(collision.collider.transform);
                return;
            }

            var monster = collision.collider.GetComponentInParent<Monster>();
            if (monster != null)
            {
                if (monster.IsDead) { Expire(); return; }
                bool headshot = collision.collider.name == "Head";
                float damage = Damage * (headshot ? 1.75f : 1f);
                GameManager.Instance?.RegisterHit(headshot);
                GameFeedback.Instance?.Hit(collision.GetContact(0).point, damage, headshot, false);
                monster.TakeDamage(damage, headshot);
                Expire(monster.transform);
                return;
            }

            // A resolved miss breaks the precision streak exactly once.
            GameManager.Instance?.RegisterMiss();
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
