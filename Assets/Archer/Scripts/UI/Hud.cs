using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Archer
{
    /// <summary>Responsive safe-area Canvas. All gameplay actions remain guarded by GameManager.</summary>
    public class Hud : MonoBehaviour
    {
        static readonly Color Ink = new(0.035f, 0.075f, 0.09f, 0.96f);
        static readonly Color Gold = new(0.94f, 0.73f, 0.39f);
        static readonly Color Cream = new(0.94f, 0.93f, 0.85f);
        static readonly Color Muted = new(0.61f, 0.72f, 0.72f);
        public static Hud Instance { get; private set; }
        RectTransform _safe, _panel, _pauseRect, _cancelRect, _skipRect;
        Text _hp, _raid, _enemies, _ammo, _drawLabel, _title, _subtitle, _stats, _toast, _toastDetail, _hint, _brand;
        Text _soundLabel, _guideLabel, _score, _bossLabel, _languageLabel, _objective, _objectiveSub;
        bool _lastKorean;
        Image _bossFill;
        GameObject _bossPanel;
        GameFeedback _feedback;
        readonly Text[] _numbers = new Text[10];
        readonly Vector3[] _positions = new Vector3[10];
        readonly float[] _expires = new float[10];
        int _nextNumber;
        Image _healthFill, _chargeFill, _regenFill, _reticle, _hit, _hurt;
        Image[] _quiver;
        GameObject _overlay, _combat, _charge, _regen, _pause, _cancel;
        Button _action, _offerA, _offerB, _sound, _guide, _practice, _skip;
        Font _font;
        GameState _lastState = (GameState)(-1);
        int _lastRaid = -1, _lastOffers = -1;
        Rect _lastSafe;

        void Awake()
        {
            Instance = this;
            _font = Resources.Load<Font>("Fonts/NotoSansCJKkr-Regular");
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scale = gameObject.AddComponent<CanvasScaler>();
            scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(480, 854);
            // Expand fits the whole reference canvas on any aspect. Matching only
            // width cropped the bottom row on tall phones (e.g. 1200x2000).
            scale.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("UI Input", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
            _safe = Node("Safe Area", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            BuildCombat();
            BuildOverlay();
            _hurt = Box("Damage edge", _safe, new Color(0.75f, 0.12f, 0.08f, 0f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _hurt.transform.SetAsFirstSibling();
            ApplySafeArea();
        }

        static RectTransform Node(string name, Transform parent, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false); r.anchorMin = min; r.anchorMax = max;
            r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = position; r.sizeDelta = size;
            return r;
        }

        Image Box(string name, Transform parent, Color color, Vector2 min, Vector2 max, Vector2 pos, Vector2 size)
        {
            var image = Node(name, parent, min, max, pos, size).gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false; return image;
        }

        Text Label(string name, Transform parent, string value, int fontSize, Color color,
            Vector2 min, Vector2 max, Vector2 pos, Vector2 size, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var text = Node(name, parent, min, max, pos, size).gameObject.AddComponent<Text>();
            text.font = _font; text.text = value; text.fontSize = Mathf.Max(26, fontSize); text.color = color;
            text.alignment = align; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        Button Button(string name, Transform parent, string title, Vector2 min, Vector2 max, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction action, bool primary = false)
        {
            var image = Box(name, parent, primary ? Gold : new Color(0.12f, 0.20f, 0.22f), min, max, pos, size);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.7f, 0.75f, 0.75f); colors.disabledColor = new Color(0.55f, 0.55f, 0.55f);
            button.colors = colors;
            var nav = button.navigation; nav.mode = Navigation.Mode.None; button.navigation = nav;
            Label("Label", image.transform, title, primary ? 30 : 28, primary ? Ink : Cream, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-12, -4));
            button.onClick.AddListener(action); return button;
        }

        Image Bar(string name, Transform parent, Color color, Vector2 min, Vector2 max, Vector2 pos, Vector2 size)
        {
            var back = Box(name, parent, new Color(0.02f, 0.04f, 0.05f, 0.9f), min, max, pos, size);
            var fill = Box("Fill", back.transform, color, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-4, -4));
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.anchorMin = new Vector2(0, 0);
            fill.rectTransform.anchorMax = new Vector2(1, 1);
            return fill;
        }

        void BuildCombat()
        {
            _combat = Node("Combat HUD", _safe, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            var top = Box("Status", _combat.transform, new Color(0.035f, 0.07f, 0.08f, 0.88f), new Vector2(0, 1), Vector2.one, new Vector2(0, -66), new Vector2(-32, 104));
            _hp = Label("Health", top.transform, "100 / 100", 16, Cream, new Vector2(0, 0.5f), new Vector2(0.45f, 0.5f), new Vector2(15, 24), new Vector2(-14, 44), TextAnchor.MiddleLeft);
            _healthFill = Bar("Health bar", top.transform, new Color(0.82f, 0.29f, 0.23f), new Vector2(0, 0.5f), new Vector2(0.45f, 0.5f), new Vector2(14, -10), new Vector2(-16, 8));
            _raid = Label("Raid", top.transform, "RAID 1/20", 15, Gold, new Vector2(0.48f, 0.5f), new Vector2(1, 0.5f), new Vector2(-45, 24), new Vector2(-65, 44));
            _enemies = Label("Remaining", top.transform, "4 incoming", 13, Muted, new Vector2(0.48f, 0.5f), new Vector2(1, 0.5f), new Vector2(-45, -18), new Vector2(-65, 44));
            _pause = Button("Pause", top.transform, "II", Vector2.one, Vector2.one, new Vector2(-29, -52), new Vector2(52, 56), () => GameManager.Instance?.TogglePause()).gameObject;
            _pauseRect = _pause.GetComponent<RectTransform>();
            var bottom = Box("Quiver panel", _combat.transform, new Color(0.035f, 0.07f, 0.08f, 0.9f), Vector2.zero, new Vector2(1, 0), new Vector2(0, 63), new Vector2(-32, 86));
            _ammo = Label("Ammunition", bottom.transform, "QUIVER  6 / 6", 13, Cream, new Vector2(0, 1), Vector2.one, new Vector2(0, -22), new Vector2(0, 44));
            _quiver = new Image[14];
            for (int i = 0; i < _quiver.Length; i++)
                _quiver[i] = Box("Arrow "+i, bottom.transform, Gold, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(10, 20));
            _regenFill = Bar("Arrow recovery", bottom.transform, Gold, Vector2.zero, new Vector2(1, 0), new Vector2(0, 12), new Vector2(-40, 5));
            _regen = _regenFill.transform.parent.gameObject;
            _chargeFill = Bar("Draw charge", _combat.transform, Gold, new Vector2(0.2f, 0), new Vector2(0.8f, 0), new Vector2(0, 148), new Vector2(0, 8));
            _charge = _chargeFill.transform.parent.gameObject;
            _drawLabel = Label("Draw label", _combat.transform, GameLanguage.Text("HOLD TO DRAW", "길게 눌러 당기기"), 13, Gold, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 178), new Vector2(0, 44));
            _hint = Label("Controls", _combat.transform, GameLanguage.Text("Hold · Aim · Release", "당기고 · 조준하고 · 놓기"), 12, Cream, Vector2.zero, new Vector2(1, 0), new Vector2(0, 128), new Vector2(-30, 44));
            _cancel = Button("Cancel shot", _combat.transform, GameLanguage.Text("Cancel", "취소"), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-76, 230), new Vector2(120, 60), () => ArcherController.Instance?.CancelAim()).gameObject;
            _cancelRect = _cancel.GetComponent<RectTransform>();
            _skip = Button("Skip practice", _combat.transform, GameLanguage.Text("Skip practice", "연습 건너뛰기"), Vector2.zero, Vector2.zero, new Vector2(96, 230), new Vector2(160, 48), () => GameManager.Instance?.SkipTutorial());
            _skipRect = _skip.GetComponent<RectTransform>();
            _reticle = Box("Predicted impact", _combat.transform, Cream, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6, 6));
            _hit = Box("Hit marker", _combat.transform, Gold, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18, 2));
            _hit.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var hit2 = Box("Cross", _hit.transform, Gold, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2, 18));
            _score = Label("Score", _combat.transform, "", 14, Gold, new Vector2(0,1), Vector2.one, new Vector2(0,-142), new Vector2(-36,44));
            _bossPanel = Box("Brute status", _combat.transform, Ink, new Vector2(0,1), Vector2.one, new Vector2(0,-210), new Vector2(-40,64)).gameObject;
            _bossLabel = Label("Brute warning", _bossPanel.transform, "", 12, Cream, Vector2.zero, Vector2.one, new Vector2(0,8), new Vector2(-12,-12));
            _bossFill = Bar("Brute health", _bossPanel.transform, new Color(0.85f,0.35f,0.2f), Vector2.zero, new Vector2(1,0), new Vector2(0,10), new Vector2(-20,8));
            for (int i = 0; i < _numbers.Length; i++)
            {
                _numbers[i] = Label("Damage " + i, _combat.transform, "", 22, Gold, new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(100,40));
                _numbers[i].fontStyle = FontStyle.Bold;
                _numbers[i].gameObject.SetActive(false);
            }
            _toast = Label("Callout", _combat.transform, "", 24, Gold, new Vector2(0, 0.66f), new Vector2(1, 0.66f), Vector2.zero, new Vector2(-32, 40));
            _toastDetail = Label("Callout detail", _combat.transform, "", 14, Cream, new Vector2(0, 0.66f), new Vector2(1, 0.66f), new Vector2(0, -40), new Vector2(-32, 40));
            _objective = Label("Objective", _combat.transform, "", 17, Gold, new Vector2(0, 1), Vector2.one, new Vector2(0, -152), new Vector2(-44, 44));
            _objective.fontStyle = FontStyle.Bold;
            _objectiveSub = Label("Objective detail", _combat.transform, "", 14, Cream, new Vector2(0, 1), Vector2.one, new Vector2(0, -214), new Vector2(-44, 80));
        }

        void Start()
        {
            _feedback = GameFeedback.Instance;
            if (_feedback != null) { _feedback.Impact += ShowImpact; _feedback.Cleared += ClearImpacts; }
        }

        void ClearImpacts()
        {
            for (int i = 0; i < _numbers.Length; i++) { _expires[i] = 0; _numbers[i].gameObject.SetActive(false); }
        }

        void ShowImpact(Vector3 position, string value, Color color)
        {
            int i = _nextNumber++ % _numbers.Length;
            _positions[i] = position; _expires[i] = Time.time + 0.7f;
            _numbers[i].text = value; _numbers[i].color = color;
        }

        void BuildOverlay()
        {
            _overlay = Box("Overlay", _safe, new Color(0.01f, 0.025f, 0.035f, 0.7f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            _overlay.GetComponent<Image>().raycastTarget = true;
            _panel = Box("Supply card", _overlay.transform, Ink, new Vector2(0.05f, 0.17f), new Vector2(0.95f, 0.84f), Vector2.zero, Vector2.zero).rectTransform;
            Box("Gold rule", _panel, Gold, new Vector2(0, 1), Vector2.one, new Vector2(0, -2), new Vector2(0, 3));
            _brand = Label("Eyebrow", _panel, GameLanguage.Text("THE FOREST WATCH", "숲의 수호자"), 12, Gold, new Vector2(0, 1), Vector2.one, new Vector2(0, -33), new Vector2(-32, 44));
            _title = Label("Title", _panel, GameLanguage.Text("LAST RANGER", "라스트 레인저"), 38, Cream, new Vector2(0, 0.78f), new Vector2(1, 0.95f), Vector2.zero, new Vector2(-32, 0));
            _title.fontStyle = FontStyle.Bold;
            _subtitle = Label("Subtitle", _panel, "", 16, Muted, new Vector2(0, 0.62f), new Vector2(1, 0.77f), Vector2.zero, new Vector2(-40, 0));
            _stats = Label("Description", _panel, "", 16, Cream, new Vector2(0, 0.31f), new Vector2(1, 0.61f), Vector2.zero, new Vector2(-40, 0));
            _offerA = Button("Upgrade A", _panel, "", new Vector2(0, 0.49f), new Vector2(1, 0.49f), Vector2.zero, new Vector2(-36, 96), () => Choose(0));
            _offerB = Button("Upgrade B", _panel, "", new Vector2(0, 0.33f), new Vector2(1, 0.33f), Vector2.zero, new Vector2(-36, 96), () => Choose(1));
            _action = Button("Primary action", _panel, "Begin Raid 1", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 55), new Vector2(-36, 68), Act, true);
            _practice = Button("Practice", _panel, "Practice", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 140), new Vector2(-36, 56), () => GameManager.Instance?.StartTutorial());
            _sound = Button("Sound toggle", _overlay.transform, "", new Vector2(0.08f, 0.125f), new Vector2(0.48f, 0.125f), Vector2.zero, new Vector2(0, 54), () => { PlayerPrefs.SetInt("LastRanger.Sound", 1-PlayerPrefs.GetInt("LastRanger.Sound", 1)); PlayerPrefs.Save(); });
            _guide = Button("Guide toggle", _overlay.transform, "", new Vector2(0.52f, 0.125f), new Vector2(0.92f, 0.125f), Vector2.zero, new Vector2(0, 54), () => { var a = ArcherController.Instance; if (a != null) { a.ShowAimGuide = !a.ShowAimGuide; PlayerPrefs.SetInt("LastRanger.Guide", a.ShowAimGuide ? 1 : 0); PlayerPrefs.Save(); } });
            _soundLabel = _sound.GetComponentInChildren<Text>();
            _guideLabel = _guide.GetComponentInChildren<Text>();
            var language = Button("Language toggle", _overlay.transform, "", new Vector2(.15f,.055f), new Vector2(.85f,.055f), Vector2.zero, new Vector2(0,46), () => GameLanguage.Toggle());
            _languageLabel = language.GetComponentInChildren<Text>();
        }

        void Choose(int index)
        {
            var gm = GameManager.Instance;
            if (gm != null && index < gm.Offers.Count) gm.ChooseUpgrade(gm.Offers[index]);
        }

        void Act()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            if (gm.State == GameState.Paused) gm.TogglePause();
            else if (gm.State == GameState.Intermission) gm.BeginRun();
            else gm.Restart();
        }

        void ApplySafeArea()
        {
            _lastSafe = Screen.safeArea;
            _safe.anchorMin = new Vector2(_lastSafe.xMin / Screen.width, _lastSafe.yMin / Screen.height);
            _safe.anchorMax = new Vector2(_lastSafe.xMax / Screen.width, _lastSafe.yMax / Screen.height);
        }

        public static bool BlocksPointer(Vector2 screen)
        {
            var h = Instance;
            if (h == null) return false;
            if (h._overlay.activeSelf) return true;
            return (h._pause.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(h._pauseRect, screen))
                || (h._cancel.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(h._cancelRect, screen))
                || (h._skip.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(h._skipRect, screen));
        }

        static void Fill(Image image, float value)
        {
            var max = image.rectTransform.anchorMax; max.x = Mathf.Clamp01(value); image.rectTransform.anchorMax = max;
        }

        void Update()
        {
            var gm = GameManager.Instance;
            var archer = ArcherController.Instance;
            if (gm == null || archer == null) return;
            if (_lastSafe != Screen.safeArea) ApplySafeArea();
            bool playing = gm.State == GameState.Playing;
            bool tutorial = gm.State == GameState.Tutorial;
            _combat.SetActive(playing || tutorial);
            _overlay.SetActive(!playing && !tutorial);
            _hp.text = GameLanguage.Text($"{Mathf.CeilToInt(gm.Health)}/{Mathf.CeilToInt(gm.MaxHealth)} HP", $"체력 {Mathf.CeilToInt(gm.Health)}/{Mathf.CeilToInt(gm.MaxHealth)}");
            Fill(_healthFill, gm.Health / Mathf.Max(1, gm.MaxHealth));
            _raid.text = tutorial
                ? GameLanguage.Text("PRACTICE", "연습")
                : GameLanguage.Text($"RAID {gm.RaidNumber}/{GameManager.TotalRaids}", $"습격 {gm.RaidNumber}/{GameManager.TotalRaids}");
            _enemies.text = tutorial
                ? GameLanguage.Text("free arrows", "화살 무제한")
                : GameLanguage.Text($"{gm.MonstersRemaining} enemies", $"남은 적 {gm.MonstersRemaining}");
            _score.gameObject.SetActive(!tutorial);
            _score.text = GameLanguage.Text($"{gm.Score} PTS · ×{gm.ScoreMultiplier} · {gm.HitStreak} HITS", $"{gm.Score}점 · ×{gm.ScoreMultiplier} · {gm.HitStreak}연속");
            _bossPanel.SetActive(gm.Boss != null && !gm.Boss.IsDead);
            if (gm.Boss != null)
            {
                _bossLabel.text = gm.Boss.IsWinding
                    ? GameLanguage.Text($"ATTACK IN {gm.Boss.WindupRemaining:0.0}s · HEADSHOT TO INTERRUPT", $"공격 {gm.Boss.WindupRemaining:0.0}초 전 · 머리를 맞혀 저지")
                    : GameLanguage.Text("THE BRUTE", "오우거");
                Fill(_bossFill, gm.Boss.Health / gm.Boss.MaxHealth);
            }
            _ammo.text = gm.Arrows == 0 ? GameLanguage.Text("EMPTY · RECOVERING", "화살 보충 중") : GameLanguage.Text($"QUIVER  {gm.Arrows} / {gm.MaxArrows}", $"화살 {gm.Arrows} / {gm.MaxArrows}");
            int shown = Mathf.Min(gm.MaxArrows, _quiver.Length);
            for (int i=0; i<_quiver.Length; i++)
            {
                _quiver[i].gameObject.SetActive(i < shown);
                _quiver[i].rectTransform.anchoredPosition = new Vector2((i-(shown-1)*0.5f)*19, 34);
                _quiver[i].color = i < gm.Arrows ? Gold : new Color(0.22f, 0.31f, 0.32f);
            }
            _regen.SetActive(gm.Arrows < gm.MaxArrows); Fill(_regenFill, gm.ArrowRegenProgress);
            _charge.SetActive(archer.IsAiming); _drawLabel.gameObject.SetActive(archer.IsAiming);
            _cancel.SetActive(archer.IsAiming);
            Fill(_chargeFill, archer.Charge01);
            _drawLabel.text = archer.Charge01 >= 0.99f ? GameLanguage.Text("FULL DRAW", "최대 장력") : GameLanguage.Text($"DRAW  {Mathf.RoundToInt(archer.Charge01*100)}%", $"장력 {Mathf.RoundToInt(archer.Charge01*100)}%");
            _hint.gameObject.SetActive(!archer.IsAiming && playing && gm.RaidNumber <= 2);
            var tut = tutorial ? gm.Tutorial : null;
            bool hasTut = tut != null && tut.Active;
            _objective.gameObject.SetActive(tutorial && hasTut);
            _objectiveSub.gameObject.SetActive(tutorial && hasTut);
            if (tutorial && hasTut)
            {
                string progress = tut.HitsNeeded > 1 ? $"  {tut.HitsThisStep}/{tut.HitsNeeded}" : "";
                _objective.text = tut.ObjectiveTitle() + progress;
                _objectiveSub.text = tut.ObjectiveHint();
            }
            Vector2 reticle = archer.IsAiming ? archer.ReticlePosition : new Vector2(Screen.width*0.5f, Screen.height*0.5f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_combat.GetComponent<RectTransform>(), reticle, null, out var point);
            _reticle.rectTransform.anchoredPosition = point;
            _reticle.gameObject.SetActive(archer.ShowAimGuide);
            var feedback = GameFeedback.Instance;
            bool hit = feedback != null && Time.time < feedback.HitUntil;
            _hit.gameObject.SetActive(hit);
            var camera = Camera.main;
            if (hit && camera != null)
            {
                var screen = camera.WorldToScreenPoint(feedback.LastHitPosition);
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_combat.transform, screen, null, out var local);
                _hit.rectTransform.anchoredPosition = local;
                _hit.gameObject.SetActive(screen.z > 0);
            }
            for (int i = 0; i < _numbers.Length; i++)
            {
                float remaining = _expires[i] - Time.time;
                bool visible = remaining > 0 && camera != null;
                _numbers[i].gameObject.SetActive(visible);
                if (!visible) continue;
                var screen = camera.WorldToScreenPoint(_positions[i] + Vector3.up * (0.7f - remaining));
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_combat.transform, screen, null, out var local);
                _numbers[i].rectTransform.anchoredPosition = local + Vector2.up * 20;
                var color = _numbers[i].color; color.a = Mathf.Clamp01(remaining / .25f); _numbers[i].color = color;
                _numbers[i].gameObject.SetActive(screen.z > 0);
            }
            bool toast = feedback != null && Time.unscaledTime < feedback.MessageUntil;
            _toast.text = toast ? feedback.Message : "";
            _toastDetail.text = toast ? feedback.Detail : "";
            _hurt.color = new Color(0.75f, 0.12f, 0.08f, feedback == null ? 0f : Mathf.Clamp01((feedback.HurtUntil-Time.unscaledTime)/0.3f)*0.2f);
            _soundLabel.text = PlayerPrefs.GetInt("LastRanger.Sound", 1) == 1 ? GameLanguage.Text("Sound: On", "소리: 켜짐") : GameLanguage.Text("Sound: Off", "소리: 꺼짐");
            _guideLabel.text = archer.ShowAimGuide ? GameLanguage.Text("Guide: On", "조준선: 켜짐") : GameLanguage.Text("Guide: Off", "조준선: 꺼짐");
            _languageLabel.text = GameLanguage.Text("Language: English", "언어: 한국어");
            _hint.text = GameLanguage.Text("Hold · Aim · Release", "당기고 · 조준하고 · 놓기");
            _cancel.GetComponentInChildren<Text>().text = GameLanguage.Text("Cancel", "취소");
            if (_lastKorean != GameLanguage.Korean || _lastState != gm.State || _lastRaid != gm.RaidNumber || _lastOffers != gm.Offers.Count)
            {
                _lastKorean=GameLanguage.Korean; _lastState=gm.State; _lastRaid=gm.RaidNumber; _lastOffers=gm.Offers.Count;
                RefreshOverlay(gm);
            }
        }

        void RefreshOverlay(GameManager gm)
        {
            bool intro = gm.State == GameState.Intermission && gm.RaidNumber == 0;
            bool supply = gm.State == GameState.Intermission && !intro;
            bool pause = gm.State == GameState.Paused;
            bool victory = gm.State == GameState.Victory;
            _brand.text = intro ? GameLanguage.Text("THE FOREST WATCH", "숲의 수호자") : supply ? GameLanguage.Text("VILLAGE SUPPLY POST", "마을 보급소") : pause ? GameLanguage.Text("TAKE A BREATH", "잠시 쉬어 가세요") : GameLanguage.Text("THE FOREST WATCH", "숲의 수호자");
            _title.text = intro ? GameLanguage.Text("LAST RANGER", "라스트 레인저") : supply ? GameLanguage.Text($"RAID {gm.RaidNumber} CLEARED", $"습격 {gm.RaidNumber} 격퇴") : pause ? GameLanguage.Text("WATCH PAUSED", "일시 정지") : victory ? GameLanguage.Text("CHAPTER ONE CLEARED", "1장 완료") : GameLanguage.Text("THE DEFENSE FELL", "방어 실패");
            _title.fontSize = 38;
            _subtitle.text = intro ? GameLanguage.Text("One bow. Five raids.\nA village counting on you.", "하나의 활로 다섯 습격을.\n당신의 마을을 지키세요.") : supply ? GameLanguage.Text($"Next: {GameManager.RaidTitle(gm.RaidNumber+1)}", $"다음: {GameManager.RaidTitle(gm.RaidNumber+1)}") : pause ? GameLanguage.Text("Your shot has been safely cancelled.", "당기던 활을 내렸습니다.") : victory ? GameLanguage.Text("Five raids held. The forest watch stands.", "다섯 번의 습격을 막아냈습니다.\n숲의 수호자는 건재합니다.") : GameLanguage.Text("Return to the gate. Try a new approach.", "다시 활을 들고\n마을을 지켜 주세요.");
            _stats.text = intro ? GameLanguage.Text("Hold to draw.\nAim, then release.\nAim higher at distance.", "길게 눌러 활을 당기세요.\n조준한 뒤 손을 놓으세요.\n멀수록 높게 조준하세요.") : supply ? (gm.Offers.Count == 0 ? GameLanguage.Text("Supplies taken.\nReady for the next watch.", "보급품을 받았습니다.\n다음 습격에 대비하세요.") : "") : pause ? GameLanguage.Text("Hold to draw.\nRelease to fire.\nCancel to lower your bow.", "길게 눌러 활을 당기세요.\n손을 놓으면 발사합니다.\n취소하면 활을 내립니다.") : GameLanguage.Text($"SCORE {gm.Score:00000}\n{gm.RaidsCleared} raids · {gm.Kills} kills\n{Mathf.RoundToInt(gm.Accuracy*100)}% hit · {gm.Headshots} headshots\nBlocked {gm.Blocked} · short {gm.FellShort} · wide {gm.WideMisses}", $"점수 {gm.Score:00000}\n{gm.RaidsCleared}회 격퇴 · {gm.Kills}마리 처치\n명중률 {Mathf.RoundToInt(gm.Accuracy*100)}% · 머리 {gm.Headshots}회\n방패 {gm.Blocked} · 짧음 {gm.FellShort} · 빗나감 {gm.WideMisses}");
            _practice.gameObject.SetActive(intro);
            _skip.gameObject.SetActive(gm.State == GameState.Tutorial);
            _offerA.gameObject.SetActive(supply && gm.Offers.Count>0);
            _offerB.gameObject.SetActive(supply && gm.Offers.Count>1);
            for(int i=0;i<gm.Offers.Count && i<2;i++)
            {
                var button = i == 0 ? _offerA : _offerB;
                button.GetComponentInChildren<Text>().text = gm.UpgradeLabel(gm.Offers[i]);
            }
            _action.interactable = !supply || gm.Offers.Count==0;
            _action.GetComponentInChildren<Text>().text = intro ? GameLanguage.Text("Begin the Watch", "방어 시작") : supply ? (gm.Offers.Count>0 ? GameLanguage.Text("Choose supplies", "보급품 선택") : GameLanguage.Text($"Begin Raid {gm.RaidNumber+1}", $"습격 {gm.RaidNumber+1} 시작")) : pause ? GameLanguage.Text("Resume Watch", "계속하기") : GameLanguage.Text("Defend Again", "다시 도전");
            _practice.GetComponentInChildren<Text>().text = PlayerPrefs.GetInt("LastRanger.Practiced", 0) == 1
                ? GameLanguage.Text("Practice again", "연습 다시 하기")
                : GameLanguage.Text("Practice first", "먼저 연습하기");
        }

        void OnDestroy() { if (_feedback != null) { _feedback.Impact -= ShowImpact; _feedback.Cleared -= ClearImpacts; } if (Instance == this) Instance = null; }
    }
}
