using UnityEngine;
using UnityEngine.Rendering;

namespace Archer
{
    /// <summary>
    /// Drives a simple day/night cycle: sun and moon discs, sky colour,
    /// ambient light and fog. Time advances during a raid and jumps a step
    /// whenever a new raid begins, so the world visibly ages as you play.
    /// </summary>
    public class TimeOfDay : MonoBehaviour
    {
        [Tooltip("Fraction of a full day added each new raid across the fifteen-raid, three-chapter defense.")]
        public float AdvancePerRaid = 0.007f;
        [Tooltip("Fraction of a full day that passes per second of active play.")]
        public float DriftPerSecond = 0.00015f;
        [Range(0f, 1f)] public float StartTime = 0.65f;

        Camera _camera;
        Light _sun;
        Light _moonRim;
        Transform _sunDisc;
        float _time01;
        int _lastRaid;
        float _appliedTime = -1f;

        public static TimeOfDay Instance { get; private set; }
        public float Time01 => _time01;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Reset the clock to the configured start time (used on a new game).</summary>
        public void ResetToStart()
        {
            _time01 = StartTime;
            _lastRaid = 0;
        }

        static readonly Color Night = new Color(0.015f, 0.02f, 0.055f);
        static readonly Color Twilight = new Color(0.42f, 0.27f, 0.32f);
        static readonly Color Day = new Color(0.45f, 0.62f, 0.85f);

        public static TimeOfDay Create(Camera cam, Light sun, Light moonRim = null)
        {
            var go = new GameObject("TimeOfDay");
            var tod = go.AddComponent<TimeOfDay>();
            tod._camera = cam;
            tod._sun = sun;
            tod._moonRim = moonRim;
            tod._time01 = tod.StartTime;
            tod.BuildCelestialBodies();
            return tod;
        }

        void BuildCelestialBodies()
        {
            var pivot = new GameObject("CelestialBodies").transform;
            pivot.SetParent(transform, false);

            _sunDisc = Prim.Create(PrimitiveType.Sphere, "SunDisc", pivot, Vector3.zero,
                Vector3.one * 26f, Prim.Emissive(new Color(1f, 0.93f, 0.7f), 3f), false).transform;
            _sunDisc.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            // The moon is drawn by the skybox shader (disc + halo), not a world sphere.
        }

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm != null)
            {
                if (gm.RaidNumber != _lastRaid)
                {
                    if (_lastRaid != 0 || gm.RaidNumber == 1)
                        _time01 += AdvancePerRaid;
                    _lastRaid = gm.RaidNumber;
                }
                if (gm.State == GameState.Playing)
                    _time01 += DriftPerSecond * Time.deltaTime;
            }

            _time01 = Mathf.Clamp(_time01, 0.65f, 0.79f); // Hold night; long defenses must not wrap back to dusk.
            // Render settings only need to change when the clock does; touching
            // them every frame costs more than the slow day/night drift.
            if (Mathf.Abs(_time01 - _appliedTime) > 0.00005f)
            {
                _appliedTime = _time01;
                Apply();
            }
        }

        void Apply()
        {
            float night = Mathf.InverseLerp(0.65f, 0.79f, _time01);
            float sunAngle = Mathf.Lerp(22f, 7f, night);
            float sunHeight = 1f - night;
            float dayFactor = 1f - night;

            if (_sun != null)
            {
                _sun.transform.rotation = Quaternion.Euler(sunAngle, -35f, 0f);
                _sun.intensity = Mathf.Lerp(0.55f, 1.1f, dayFactor);
                _sun.color = Color.Lerp(new Color(0.56f, 0.68f, 0.9f), new Color(1f, 0.77f, 0.51f), dayFactor);
            }

            Color sky = Color.Lerp(new Color(0.18f,0.29f,0.43f), new Color(0.065f,0.12f,0.23f), night);
            if (_camera != null) _camera.backgroundColor = sky;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(new Color(0.34f,0.42f,0.54f), new Color(0.53f,0.57f,0.62f), dayFactor);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = sky;
            RenderSettings.fogDensity = Mathf.Lerp(0.009f, 0.006f, dayFactor);

            if (_sun != null)
            {
                Vector3 sunDir = _sun.transform.forward;
                if (_sunDisc != null)
                {
                    _sunDisc.position = -sunDir * 400f;
                    _sunDisc.gameObject.SetActive(false);
                }
            }

            // Cool moonlight fill strengthens as dusk settles into night so
            // enemy silhouettes stay readable against the dark forest.
            if (_moonRim != null)
                _moonRim.intensity = Mathf.Lerp(0.9f, 0.55f, dayFactor);
        }

        static Color SkyColor(float sunHeight)
        {
            if (sunHeight < 0f)
                return Color.Lerp(Night, Twilight, Mathf.Clamp01(sunHeight / 0.22f + 1f));
            return Color.Lerp(Twilight, Day, Mathf.Clamp01(sunHeight / 0.35f));
        }
    }
}
