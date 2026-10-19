using System.Collections.Generic;
using UnityEngine;

namespace Archer
{
    public enum FeedbackSound { Shot, Hit, Block, Clear, Kill, EnemyWarning, PlayerHurt }

    /// <summary>Audio playback, ambient forest soundscape, dynamic combat music, and transient HUD feedback.</summary>
    public class GameFeedback : MonoBehaviour
    {
        public static GameFeedback Instance { get; private set; }
        public string Message { get; private set; }
        public string Detail { get; private set; }
        public float MessageUntil { get; private set; }
        public float HitUntil { get; private set; }
        public float HurtUntil { get; private set; }
        public bool Critical { get; private set; }
        public Vector3 LastHitPosition { get; private set; }
        public event System.Action<Vector3, string, Color> Impact;
        public event System.Action Cleared;

        AudioSource _source;
        AudioSource _tension;
        AudioSource _ambienceSource;
        AudioSource _musicSource;

        AudioClip _tensionClip;
        AudioClip _ambienceClip;
        AudioClip _musicClip;
        AudioClip _hurtClip;
        AudioClip _warningClip;

        readonly Dictionary<FeedbackSound, AudioClip> _clips = new();

        void Awake()
        {
            Instance = this;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            _tension = gameObject.AddComponent<AudioSource>();
            _tension.playOnAwake = false;
            _tension.loop = true;
            _tension.spatialBlend = 0f;

            _ambienceSource = gameObject.AddComponent<AudioSource>();
            _ambienceSource.playOnAwake = false;
            _ambienceSource.loop = true;
            _ambienceSource.spatialBlend = 0f;
            _ambienceSource.volume = 0.22f;

            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.spatialBlend = 0f;
            _musicSource.volume = 0.28f;

            // Load generated audio assets from Resources/Audio
            _tensionClip = Resources.Load<AudioClip>("Audio/Bow_Draw");
            if (_tensionClip != null)
            {
                _tension.clip = _tensionClip;
            }
            else
            {
                // Fallback synthetic tension
                const int rate = 22050;
                var tensionSamples = new float[rate];
                for (int i = 0; i < rate; i++)
                {
                    float t = (float)i / rate;
                    tensionSamples[i] = (Mathf.Sin(t * Mathf.PI * 2 * 113) * .6f + Mathf.Sin(t * Mathf.PI * 2 * 227) * .25f)
                        * (.7f + .3f * Mathf.Sin(t * Mathf.PI * 2 * 7));
                }
                _tensionClip = AudioClip.Create("Bow tension", rate, 1, rate, false);
                _tensionClip.SetData(tensionSamples, 0);
                _tension.clip = _tensionClip;
            }

            var releaseClip = Resources.Load<AudioClip>("Audio/Bow_Release");
            _clips[FeedbackSound.Shot] = releaseClip != null ? releaseClip : Tone("Bow release", 190f, 65f, 0.14f, 0.35f);

            var hitClip = Resources.Load<AudioClip>("Audio/Arrow_Hit");
            _clips[FeedbackSound.Hit] = hitClip != null ? hitClip : Tone("Arrow impact", 420f, 100f, 0.12f, 0.2f);

            var blockClip = Resources.Load<AudioClip>("Audio/Shield_Block");
            _clips[FeedbackSound.Block] = blockClip != null ? blockClip : Tone("Shield block", 760f, 310f, 0.16f, 0.4f);

            _clips[FeedbackSound.Kill] = Tone("Defeated", 520f, 210f, 0.22f, 0.12f);
            _clips[FeedbackSound.Clear] = Tone("Raid clear", 390f, 780f, 0.55f, 0.02f);

            _warningClip = Resources.Load<AudioClip>("Audio/Enemy_Warning");
            if (_warningClip != null) _clips[FeedbackSound.EnemyWarning] = _warningClip;

            _hurtClip = Resources.Load<AudioClip>("Audio/Player_Hurt");
            if (_hurtClip != null) _clips[FeedbackSound.PlayerHurt] = _hurtClip;

            _ambienceClip = Resources.Load<AudioClip>("Audio/Forest_Ambience");
            if (_ambienceClip != null)
            {
                _ambienceSource.clip = _ambienceClip;
                _ambienceSource.Play();
            }

            _musicClip = Resources.Load<AudioClip>("Audio/Combat_Music");
            if (_musicClip != null)
            {
                _musicSource.clip = _musicClip;
                _musicSource.Play();
            }
        }

        void Update()
        {
            // Restrained music balance: music stays quiet enough that the bow and enemy warnings remain clear.
            bool soundEnabled = PlayerPrefs.GetInt("LastRanger.Sound", 1) != 0;
            var gm = GameManager.Instance;
            bool isPlaying = gm != null && (gm.State == GameState.Playing || gm.State == GameState.Tutorial);

            if (_ambienceSource != null)
            {
                float targetAmbience = soundEnabled ? 0.20f : 0f;
                _ambienceSource.volume = Mathf.MoveTowards(_ambienceSource.volume, targetAmbience, Time.unscaledDeltaTime * 0.5f);
            }

            if (_musicSource != null)
            {
                // Music plays prominently during combat, and remains clearly audible during menus/intermissions.
                float targetMusic = (soundEnabled && isPlaying) ? 0.45f : (soundEnabled ? 0.28f : 0f);
                _musicSource.volume = Mathf.MoveTowards(_musicSource.volume, targetMusic, Time.unscaledDeltaTime * 0.4f);
            }
        }

        static AudioClip Tone(string title, float from, float to, float seconds, float noise)
        {
            const int rate = 22050;
            var samples = new float[Mathf.CeilToInt(seconds * rate)];
            float phase = 0f;
            var random = new System.Random(17);
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / samples.Length;
                phase += Mathf.Lerp(from, to, t) * 2f * Mathf.PI / rate;
                // Fast attack followed by a smooth decay.
                float envelope = Mathf.Min(t * 40f, 1f) * Mathf.Pow(1f - t, 2f);
                samples[i] = (Mathf.Sin(phase) * (1f - noise) + ((float)random.NextDouble() * 2f - 1f) * noise) * envelope * 0.3f;
            }
            var clip = AudioClip.Create(title, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public void SetBowTension(float draw)
        {
            if (_tension == null) return;
            bool audible = draw >= 0 && Time.timeScale > 0 && PlayerPrefs.GetInt("LastRanger.Sound", 1) != 0;
            if (!audible) { if (_tension.isPlaying) _tension.Stop(); return; }
            _tension.pitch = Mathf.Lerp(0.85f, 1.25f, draw);
            _tension.volume = Mathf.Lerp(0.12f, 0.40f, draw);
            if (!_tension.isPlaying) _tension.Play();
        }

        public void Play(FeedbackSound sound, float volumeScale = 1f)
        {
            if (PlayerPrefs.GetInt("LastRanger.Sound", 1) != 0 && _clips.TryGetValue(sound, out var clip))
                _source.PlayOneShot(clip, volumeScale);
        }

        public void Notify(string title, string detail, float duration = 1.5f)
        {
            Message = title; Detail = detail; MessageUntil = Time.unscaledTime + duration;
        }

        public void Hit(Vector3 position, float damage, bool critical, bool blocked)
        {
            Critical = critical;
            LastHitPosition = position;
            HitUntil = Time.time + 0.25f;
            Play(blocked ? FeedbackSound.Block : FeedbackSound.Hit, blocked ? 0.9f : critical ? 1.0f : 0.8f);
            Impact?.Invoke(position, blocked ? GameLanguage.Text("BLOCK", "막힘") : Mathf.CeilToInt(damage) + (critical ? "!" : ""),
                blocked ? new Color(0.65f,0.83f,1f) : critical ? new Color(1f,0.79f,0.33f) : Color.white);
            CombatEffects.Instance?.Burst(position, blocked, false);
            if (blocked)
            {
                // In practice the tutorial director owns the coaching copy.
                if (GameManager.Instance == null || GameManager.Instance.State != GameState.Tutorial)
                    Notify(GameLanguage.Text("SHIELD BLOCKED", "방패에 막혔습니다"), GameLanguage.Text("Aim above the shield", "방패 위를 조준하세요"), 1f);
            }
            else if (critical) Notify(GameLanguage.Text("HEADSHOT", "머리 명중"), GameLanguage.Text("Precision damage", "치명적인 명중"), 0.7f);
        }

        /// <summary>
        /// Coach the player after a shot that missed. Blocked shots are shown by
        /// <see cref="Hit"/> instead, so they are ignored here.
        /// </summary>
        public void Miss(MissReason reason, Vector3 position)
        {
            if (reason == MissReason.Blocked) return;
            LastHitPosition = position;
            if (reason == MissReason.Short)
                Notify(GameLanguage.Text("FELL SHORT", "짧았습니다"),
                    GameLanguage.Text("Draw longer, or aim a little higher", "더 당기거나 조금 더 높이 겨누세요"), 1.1f);
            else
                Notify(GameLanguage.Text("MISSED", "빗나갔습니다"),
                    GameLanguage.Text("Lead the target as it moves", "움직이는 적의 앞을 겨누세요"), 1.1f);
            Impact?.Invoke(position, GameLanguage.Text("MISS", "빗나감"), new Color(0.72f, 0.78f, 0.80f));
        }

        public void ResetFeedback()
        {
            HitUntil = HurtUntil = MessageUntil = 0;
            Message = Detail = "";
            Cleared?.Invoke();
        }

        public void Hurt()
        {
            HurtUntil = Time.unscaledTime + 0.3f;
            Play(FeedbackSound.PlayerHurt, 0.95f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
