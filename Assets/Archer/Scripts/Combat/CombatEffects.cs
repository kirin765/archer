using UnityEngine;

namespace Archer
{
    /// <summary>
    /// Fixed-size impact and ring pools. No GameObject or material creation
    /// during combat, so a busy wave cannot trigger a GC spike just because a
    /// shield was hit or a brute went down.
    /// </summary>
    public class CombatEffects : MonoBehaviour
    {
        struct Spark
        {
            public Transform Transform;
            public Renderer Renderer;
            public Vector3 Velocity;
            public float Life, Duration, Size;
        }

        struct Ring
        {
            public Transform Transform;
            public LineRenderer Line;
            public float Life, Duration, Radius;
            public Color Color;
        }

        readonly Spark[] _sparks = new Spark[64];
        readonly Ring[] _rings = new Ring[8];
        MaterialPropertyBlock _properties;
        Material _material;
        Material _ringMaterial;
        int _cursor;
        int _ringCursor;

        public static CombatEffects Instance { get; private set; }

        /// <summary>Number of shockwave rings currently animating (editor checks).</summary>
        public int ActiveRingCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _rings.Length; i++)
                    if (_rings[i].Life > 0f) count++;
                return count;
            }
        }

        void Awake()
        {
            Instance = this;
            _properties = new MaterialPropertyBlock();
            _material = Prim.Unlit(Color.white);
            _ringMaterial = Prim.Unlit(Color.white);

            for (int i = 0; i < _sparks.Length; i++)
            {
                var go = Prim.Create(PrimitiveType.Cube, "Impact mote", transform, Vector3.zero, Vector3.one, _material, false);
                var renderer = go.GetComponent<Renderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(false);
                _sparks[i] = new Spark { Transform = go.transform, Renderer = renderer };
            }

            for (int i = 0; i < _rings.Length; i++)
            {
                var go = new GameObject("Impact ring");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = _ringMaterial;
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = 24;
                line.widthMultiplier = 0.09f;
                line.numCapVertices = 0;
                line.alignment = LineAlignment.View;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                for (int p = 0; p < 24; p++)
                {
                    float angle = p * Mathf.PI * 2f / 24f;
                    line.SetPosition(p, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
                }
                go.SetActive(false);
                _rings[i] = new Ring { Transform = go.transform, Line = line };
            }
        }

        /// <summary>
        /// Emit a small directional impact burst. Headshots use a brighter,
        /// slightly larger spray than body hits; lethal bursts are the biggest.
        /// </summary>
        public void Burst(Vector3 position, bool blocked, bool lethal, bool headshot = false, float scale = 1f)
        {
            bool reduced = GameSettings.ReducedMotion;
            int count = lethal ? 16 : headshot ? 10 : 7;
            if (reduced) count = Mathf.Max(3, count / 2);
            if (reduced) scale *= 0.7f;
            Color color = blocked ? new Color(0.67f, 0.83f, 0.96f)
                : headshot || lethal ? new Color(1f, 0.83f, 0.40f)
                : new Color(1f, 0.74f, 0.28f);
            for (int i = 0; i < count; i++)
            {
                int index = _cursor++ % _sparks.Length;
                var spark = _sparks[index];
                spark.Life = spark.Duration = Random.Range(0.22f, 0.48f);
                spark.Size = Random.Range(0.035f, 0.075f) * (lethal ? 1.4f : headshot ? 1.15f : 1f) * scale;
                spark.Velocity = Random.onUnitSphere * Random.Range(1f, 3f) + Vector3.up * 1.2f;
                spark.Transform.position = position;
                spark.Transform.localScale = Vector3.one * spark.Size;
                spark.Transform.gameObject.SetActive(true);
                _properties.SetColor("_BaseColor", color);
                _properties.SetColor("_Color", color);
                spark.Renderer.SetPropertyBlock(_properties);
                _sparks[index] = spark;
            }
        }

        /// <summary>A tight ring accent for a headshot or a blocked shield.</summary>
        public void Headshot(Vector3 position)
            => RingAt(position, new Color(1f, 0.88f, 0.52f), 0.95f, 0.30f);

        public void ShieldBlock(Vector3 position)
            => RingAt(position, new Color(0.62f, 0.82f, 1f), 0.55f, 0.22f);

        /// <summary>A heavier ring that confirms the brute's swing was broken.</summary>
        public void Interrupt(Vector3 position, float scale)
        {
            Burst(position, false, false, true, 0.7f * scale);
            RingAt(position, new Color(1f, 0.94f, 0.68f), 1.15f * scale, 0.38f);
        }

        /// <summary>Death burst plus a role-scaled clearing ring.</summary>
        public void Defeat(Vector3 position, float scale, bool headshot)
        {
            Burst(position, false, true, headshot, scale);
            RingAt(position, headshot ? new Color(1f, 0.92f, 0.62f) : new Color(1f, 0.67f, 0.30f),
                1.35f * scale, 0.42f);
        }

        void RingAt(Vector3 position, Color color, float radius, float duration)
        {
            if (GameSettings.ReducedMotion)
            {
                radius *= 0.6f;
                duration *= 0.8f;
            }
            int index = _ringCursor++ % _rings.Length;
            var ring = _rings[index];
            ring.Life = ring.Duration = Mathf.Max(0.05f, duration);
            ring.Radius = Mathf.Max(0.1f, radius);
            ring.Color = color;
            ring.Transform.position = position;
            ring.Transform.localScale = Vector3.one * 0.18f;
            ring.Line.widthMultiplier = 0.09f;
            ring.Line.startColor = color;
            ring.Line.endColor = color;
            _properties.SetColor("_BaseColor", color);
            _properties.SetColor("_Color", color);
            ring.Line.SetPropertyBlock(_properties);
            ring.Transform.gameObject.SetActive(true);
            _rings[index] = ring;
        }

        void Update()
        {
            for (int i = 0; i < _sparks.Length; i++)
            {
                var spark = _sparks[i];
                if (spark.Life <= 0f) continue;
                spark.Life -= Time.deltaTime;
                spark.Velocity += Vector3.down * (3f * Time.deltaTime);
                spark.Transform.position += spark.Velocity * Time.deltaTime;
                spark.Transform.localScale = Vector3.one * (spark.Size * Mathf.Clamp01(spark.Life / spark.Duration));
                if (spark.Life <= 0f) spark.Transform.gameObject.SetActive(false);
                _sparks[i] = spark;
            }

            for (int i = 0; i < _rings.Length; i++)
            {
                var ring = _rings[i];
                if (ring.Life <= 0f) continue;
                ring.Life -= Time.deltaTime;
                float t = 1f - Mathf.Clamp01(ring.Life / ring.Duration);
                ring.Transform.localScale = Vector3.one * Mathf.Lerp(0.18f, ring.Radius, Mathf.SmoothStep(0f, 1f, t));
                ring.Line.widthMultiplier = Mathf.Lerp(0.09f, 0.015f, t);
                var color = ring.Color;
                color.a = Mathf.Clamp01(ring.Life / ring.Duration);
                ring.Line.startColor = color;
                ring.Line.endColor = color;
                if (ring.Life <= 0f) ring.Transform.gameObject.SetActive(false);
                _rings[i] = ring;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _sparks.Length; i++)
            {
                _sparks[i].Life = 0f;
                _sparks[i].Transform.gameObject.SetActive(false);
            }
            for (int i = 0; i < _rings.Length; i++)
            {
                _rings[i].Life = 0f;
                _rings[i].Transform.gameObject.SetActive(false);
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_material != null) Destroy(_material);
            if (_ringMaterial != null) Destroy(_ringMaterial);
        }
    }
}
