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
        static readonly Color Parchment = new(0.84f, 0.77f, 0.64f);
        public static Hud Instance { get; private set; }
        public bool OverlayVisible => _overlay != null && _overlay.activeSelf;
        RectTransform _safe, _panel, _pauseRect, _cancelRect, _skipRect;
        Text _hp, _raid, _enemies, _drawLabel, _title, _subtitle, _stats, _toast, _toastDetail, _hint, _brand;
        Text _soundLabel, _guideLabel, _score, _bossLabel, _languageLabel, _objective, _objectiveSub, _threat, _objectiveBadge;
        Text _musicLabel, _ambienceLabel, _effectsLabel, _sensitivityLabel, _reducedMotionLabel, _toTitleLabel;
        Image _hitCross;
        Camera _camera;
        bool _lastKorean;
        Image _bossFill;
        GameObject _bossPanel;
        GameFeedback _feedback;
        readonly Text[] _numbers = new Text[10];
        readonly Vector3[] _positions = new Vector3[10];
        readonly float[] _expires = new float[10];
        int _nextNumber;
        Image _healthFill, _chargeFill, _reticle, _hit, _hurt;
        GameObject _overlay, _combat, _charge, _pause, _cancel;
        Button _action, _offerA, _offerB, _sound, _guide, _language, _practice, _skip, _toTitle;
        Button _music, _ambience, _effects, _sensitivity, _reducedMotion, _resetDefaults;
        Sprite _stonePanel, _stoneButton, _stoneButtonPrimary, _woodPanel, _woodButton;
        Font _font;
        GameState _lastState = (GameState)(-1);
        int _lastRaid = -1, _lastOffers = -1;
        float _nextTextRefresh;
        float _clearHold;
        Rect _lastSafe;
        const float CardPadX = 26f, CardPadY = 20f;
        // Settings page: drawn on the existing stone card so no second opaque
        // panel is stacked over it. The normal card content slides aside.
        RectTransform _cardContent, _settingsPage;
        CanvasGroup _cardContentGroup, _settingsGroup;
        Text _menuTitle, _menuCloseLabel, _menuHint;
        bool _menuOpen;
        float _menuEase;

        void Awake()
        {
            Instance = this;
            _font = Resources.Load<Font>("Fonts/NotoSansCJKkr-Regular");
            _stonePanel = Resources.Load<Sprite>("UI/Stone/panel");
            _stoneButton = Resources.Load<Sprite>("UI/Stone/button");
            _stoneButtonPrimary = Resources.Load<Sprite>("UI/Stone/button_primary");
            _woodPanel = Resources.Load<Sprite>("UI/Wood/panel");
            _woodButton = Resources.Load<Sprite>("UI/Wood/button");
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

        void ApplyWood(Image image)
        {
            if (_woodPanel == null) { ApplyStone(image, _stonePanel, 1.5f); return; }
            image.sprite = _woodPanel;
            image.type = Image.Type.Simple;
            image.color = Color.white;
        }

        static void ApplyStone(Image image, Sprite sprite, float pixelsPerUnitMultiplier, Color? tint = null)
        {
            if (sprite == null) return;
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
            image.color = tint ?? Color.white;
        }

        Button Button(string name, Transform parent, string title, Vector2 min, Vector2 max, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction action, bool primary = false, bool stone = false)
        {
            var image = Box(name, parent, primary ? Gold : new Color(0.12f, 0.20f, 0.22f), min, max, pos, size);
            var stoneSprite = primary ? _stoneButtonPrimary : _stoneButton;
            bool woodApplied = stone && !primary && _woodButton != null;
            bool stoneApplied = stone && !woodApplied && stoneSprite != null;
            if (woodApplied)
            {
                image.sprite = _woodButton;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 1f;
                image.color = Color.white;
            }
            else if (stoneApplied) ApplyStone(image, stoneSprite, 2.5f);
            bool decorated = woodApplied || stoneApplied;
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.7f, 0.75f, 0.75f); colors.disabledColor = new Color(0.55f, 0.55f, 0.55f);
            button.colors = colors;
            var nav = button.navigation; nav.mode = Navigation.Mode.None; button.navigation = nav;
            Label("Label", image.transform, title, primary ? 30 : 28, decorated ? Ink : primary ? Ink : Cream, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-12, -4));
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
            _pause = Button("Pause", top.transform, "II", Vector2.one, Vector2.one, new Vector2(-34, -52), new Vector2(60, 60), () => GameManager.Instance?.TogglePause()).gameObject;
            _pauseRect = _pause.GetComponent<RectTransform>();
            _chargeFill = Bar("Draw charge", _combat.transform, Gold, new Vector2(0.2f, 0), new Vector2(0.8f, 0), new Vector2(0, 62), new Vector2(0, 8));
            _charge = _chargeFill.transform.parent.gameObject;
            _drawLabel = Label("Draw label", _combat.transform, GameLanguage.Text("HOLD TO DRAW", "길게 눌러 활 당기기"), 13, Gold, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 92), new Vector2(0, 44));
            _hint = Label("Controls", _combat.transform, GameLanguage.Text("Hold · Aim · Release", "당기기 · 조준 · 발사"), 12, Cream, Vector2.zero, new Vector2(1, 0), new Vector2(0, 62), new Vector2(-30, 44));
            _cancel = Button("Cancel shot", _combat.transform, GameLanguage.Text("Cancel", "취소"), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-76, 230), new Vector2(120, 60), () => ArcherController.Instance?.CancelAim()).gameObject;
            _cancelRect = _cancel.GetComponent<RectTransform>();
            _skip = Button("Skip practice", _combat.transform, GameLanguage.Text("Skip practice", "연습 건너뛰기"), Vector2.zero, Vector2.zero, new Vector2(96, 230), new Vector2(160, 48), () => GameManager.Instance?.SkipTutorial());
            _skipRect = _skip.GetComponent<RectTransform>();
            _reticle = Box("Predicted impact", _combat.transform, Cream, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6, 6));
            _hit = Box("Hit marker", _combat.transform, Gold, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18, 2));
            _hit.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            _hitCross = Box("Cross", _hit.transform, Gold, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2, 18));
            _score = Label("Score", _combat.transform, "", 14, Gold, new Vector2(0,1), Vector2.one, new Vector2(0,-142), new Vector2(-36,44));
            _bossPanel = Box("Brute status", _combat.transform, Ink, new Vector2(0,1), Vector2.one, new Vector2(0,-258), new Vector2(-40,64)).gameObject;
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
            _objectiveBadge = Label("Raid objective", _combat.transform, "", 14, Gold,
                new Vector2(0, 1), Vector2.one, new Vector2(0, -190), new Vector2(-40, 44));
            _objectiveBadge.fontStyle = FontStyle.Bold;
            // Non-boss attack cues. Bosses keep their dedicated panel; this
            // line gives normal wind-ups the same readable countdown.
            _threat = Label("Threat", _combat.transform, "", 14, new Color(1f, 0.66f, 0.32f),
                new Vector2(0, 1), Vector2.one, new Vector2(0, -258), new Vector2(-44, 44));
            _threat.fontStyle = FontStyle.Bold;
        }

        void Start()
        {
            _camera = Camera.main;
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
            var feedback = GameFeedback.Instance;
            bool critical = feedback != null && feedback.Critical;
            bool blocked = feedback != null && feedback.Blocked;
            _numbers[i].text = value;
            _numbers[i].color = color;
            _numbers[i].fontSize = critical ? 26 : blocked ? 19 : 22;
            _numbers[i].fontStyle = critical ? FontStyle.Bold : FontStyle.Normal;
        }

        void BuildOverlay()
        {
            // Invisible input catcher only. The scene stays visible behind the
            // stone card: no full-screen black panel and no dark card shadow.
            _overlay = Box("Overlay", _safe, Color.clear, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            _overlay.GetComponent<Image>().raycastTarget = true;
            var card = Box("Supply card", _overlay.transform, Ink, new Vector2(0.05f, 0.17f), new Vector2(0.95f, 0.84f), Vector2.zero, Vector2.zero);
            ApplyWood(card);
            _panel = card.rectTransform;
            // The card double as the settings page; clip the page slide at the
            // slab's edge instead of letting content spill over the scene.
            card.gameObject.AddComponent<RectMask2D>();
            _cardContent = Node("Card content", _panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _cardContent.offsetMin = new Vector2(CardPadX, CardPadY);
            _cardContent.offsetMax = new Vector2(-CardPadX, -CardPadY);
            _cardContentGroup = _cardContent.gameObject.AddComponent<CanvasGroup>();
            _brand = Label("Eyebrow", _cardContent, GameLanguage.Text("THE FOREST WATCH", "숲의 수호자"), 12, Gold, new Vector2(0, 1), Vector2.one, new Vector2(0, -33), new Vector2(-32, 44));
            _title = Label("Title", _cardContent, GameLanguage.Text("LAST RANGER", "라스트 레인저"), 38, Cream, new Vector2(0, 0.78f), new Vector2(1, 0.95f), Vector2.zero, new Vector2(-32, 0));
            _title.fontStyle = FontStyle.Bold;
            _subtitle = Label("Subtitle", _cardContent, "", 16, Parchment, new Vector2(0, 0.62f), new Vector2(1, 0.77f), Vector2.zero, new Vector2(-40, 0));
            _stats = Label("Description", _cardContent, "", 16, Cream, new Vector2(0, 0.17f), new Vector2(1, 0.61f), Vector2.zero, new Vector2(-40, 0));
            _offerA = Button("Upgrade A", _cardContent, "", new Vector2(0, 0.54f), new Vector2(1, 0.54f), Vector2.zero, new Vector2(-36, 116), () => Choose(0), stone: true);
            _offerB = Button("Upgrade B", _cardContent, "", new Vector2(0, 0.32f), new Vector2(1, 0.32f), Vector2.zero, new Vector2(-36, 116), () => Choose(1), stone: true);
            // Upgrade cards need three lines (title, effect, synergy); keep
            // the copy compact enough to fit both the stone slab and the gap
            // between the two offer rows.
            _offerA.GetComponentInChildren<Text>().fontSize = 22;
            _offerB.GetComponentInChildren<Text>().fontSize = 22;
            _action = Button("Primary action", _cardContent, "Begin Raid 1", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 55), new Vector2(-36, 68), Act, true, true);
            _practice = Button("Practice", _cardContent, "Practice", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 140), new Vector2(-36, 56), () => GameManager.Instance?.StartTutorial(), stone: true);
            BuildMenu();
        }

        /// <summary>
        /// One stone tab above the card opens settings on the card's own stone
        /// slab. There is deliberately no extra panel or scrim: the normal
        /// card content slides out and the settings rows slide in, so no
        /// opaque slab ever overlaps the stone panel.
        /// </summary>
        void BuildMenu()
        {
            Button menuButton = Button("Menu", _overlay.transform, "", Vector2.one, Vector2.one,
                new Vector2(-52, -34), new Vector2(92, 46), ToggleMenu, stone: true);
            menuButton.GetComponentInChildren<Text>().text = "";
            for (int i = -1; i <= 1; i++)
                Box("Menu bar " + (i + 2), menuButton.transform, Ink,
                    new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, i * 8), new Vector2(26, 3));

            // The counterpart tab on the left: leave the run and go back to the
            // title card. It lives outside the card so it is reachable from
            // pause, the supply screen and the results screen alike.
            _toTitle = Button("Main title", _overlay.transform, "", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(58, -34), new Vector2(104, 46), () => GameManager.Instance?.ReturnToTitle(), stone: true);
            _toTitleLabel = _toTitle.GetComponentInChildren<Text>();
            _toTitleLabel.fontSize = 22;

            _settingsPage = Node("Settings page", _panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _settingsPage.offsetMin = new Vector2(CardPadX, CardPadY);
            _settingsPage.offsetMax = new Vector2(-CardPadX, -CardPadY);
            _settingsGroup = _settingsPage.gameObject.AddComponent<CanvasGroup>();
            _settingsGroup.alpha = 1f; _settingsGroup.interactable = false; _settingsGroup.blocksRaycasts = false;
            _settingsPage.anchoredPosition = new Vector2(432f, 0f);

            _menuTitle = Label("Menu title", _settingsPage, GameLanguage.Text("SETTINGS", "설정"), 24, Gold,
                new Vector2(0, 1), Vector2.one, new Vector2(30, -44), new Vector2(-96, 44), TextAnchor.MiddleLeft);
            _menuTitle.fontStyle = FontStyle.Bold;
            Button("Menu close", _settingsPage, "×", new Vector2(1, 1), Vector2.one,
                new Vector2(-33, -42), new Vector2(48, 48), () => SetMenu(false), stone: true);
            Box("Menu rule", _settingsPage, new Color(0.94f, 0.73f, 0.39f, 0.55f),
                new Vector2(0, 1), Vector2.one, new Vector2(0, -78), new Vector2(-40, 2));

            _sound = MenuRow(_settingsPage, "Sound toggle", -120f, ToggleSound);
            _music = MenuRow(_settingsPage, "Music volume", -120f, CycleMusic, true);
            _ambience = MenuRow(_settingsPage, "Ambience volume", -180f, CycleAmbience);
            _effects = MenuRow(_settingsPage, "Effects volume", -180f, CycleEffects, true);
            _guide = MenuRow(_settingsPage, "Guide toggle", -240f, ToggleGuide);
            _sensitivity = MenuRow(_settingsPage, "Aim sensitivity", -240f, CycleSensitivity, true);
            _language = MenuRow(_settingsPage, "Language toggle", -300f, () => GameLanguage.Toggle());
            _reducedMotion = MenuRow(_settingsPage, "Reduced motion", -300f, ToggleReducedMotion, true);
            _soundLabel = _sound.GetComponentInChildren<Text>();
            _musicLabel = _music.GetComponentInChildren<Text>();
            _ambienceLabel = _ambience.GetComponentInChildren<Text>();
            _effectsLabel = _effects.GetComponentInChildren<Text>();
            _guideLabel = _guide.GetComponentInChildren<Text>();
            _sensitivityLabel = _sensitivity.GetComponentInChildren<Text>();
            _languageLabel = _language.GetComponentInChildren<Text>();
            _reducedMotionLabel = _reducedMotion.GetComponentInChildren<Text>();
            _resetDefaults = Button("Restore defaults", _settingsPage, "", new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -368), new Vector2(-40, 48), RestoreDefaults, stone: true);

            _menuHint = Label("Menu hint", _settingsPage, "", 14, Parchment,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 108), new Vector2(-60, 34));
            var close = Button("Menu close primary", _settingsPage, "", new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 55), new Vector2(-36, 64), () => SetMenu(false), true, true);
            _menuCloseLabel = close.GetComponentInChildren<Text>();
        }

        Button MenuRow(Transform parent, string name, float top, UnityEngine.Events.UnityAction action, bool right = false)
        {
            return Button(name, parent, "", right ? new Vector2(0.5f, 1) : new Vector2(0, 1),
                right ? new Vector2(1, 1) : new Vector2(0.5f, 1),
                new Vector2(right ? 8f : -8f, top), new Vector2(-10, 48), action, stone: true);
        }

        void ApplyAudioSettings() => GameFeedback.Instance?.ApplySettings();

        void ToggleSound()
        {
            GameSettings.SoundEnabled = !GameSettings.SoundEnabled;
            ApplyAudioSettings();
        }

        void CycleMusic()
        {
            GameSettings.MusicVolume = GameSettings.NextVolume(GameSettings.MusicVolume);
            ApplyAudioSettings();
        }

        void CycleAmbience()
        {
            GameSettings.AmbienceVolume = GameSettings.NextVolume(GameSettings.AmbienceVolume);
            ApplyAudioSettings();
        }

        void CycleEffects()
        {
            GameSettings.EffectsVolume = GameSettings.NextVolume(GameSettings.EffectsVolume);
            ApplyAudioSettings();
        }

        void CycleSensitivity()
        {
            GameSettings.AimSensitivity = GameSettings.NextSensitivity(GameSettings.AimSensitivity, 1);
            ArcherController.Instance?.ApplySettings();
        }

        void ToggleReducedMotion() => GameSettings.ReducedMotion = !GameSettings.ReducedMotion;

        void RestoreDefaults()
        {
            GameSettings.RestoreDefaults();
            ArcherController.Instance?.ApplySettings();
            ApplyAudioSettings();
        }

        void ToggleGuide()
        {
            var archer = ArcherController.Instance;
            if (archer == null) return;
            archer.ShowAimGuide = !archer.ShowAimGuide;
            GameSettings.ShowAimGuide = archer.ShowAimGuide;
        }

        void ToggleMenu() => SetMenu(!_menuOpen);

        void SetMenu(bool open)
        {
            _menuOpen = open;
            _settingsGroup.interactable = open;
            _settingsGroup.blocksRaycasts = open;
            _cardContentGroup.interactable = !open;
            _cardContentGroup.blocksRaycasts = !open;
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
            // Let the last kill, hit marker and callout breathe before the
            // intermission card slides over the battlefield.
            bool justCleared = _lastState == GameState.Playing
                && (gm.State == GameState.Intermission || gm.State == GameState.Victory);
            if (justCleared && _clearHold <= 0f) _clearHold = 1.2f;
            if (gm.State == GameState.Playing) _clearHold = 0f;
            if (_clearHold > 0f) _clearHold = Mathf.Max(0f, _clearHold - Time.unscaledDeltaTime);
            bool holdingClear = _clearHold > 0f;
            _combat.SetActive(playing || tutorial || holdingClear);
            _overlay.SetActive(!playing && !tutorial && !holdingClear);
            UpdateMenu();
            Fill(_healthFill, gm.Health / Mathf.Max(1, gm.MaxHealth));
            _score.gameObject.SetActive(!tutorial);
            _bossPanel.SetActive(gm.Boss != null && !gm.Boss.IsDead);
            if (gm.Boss != null) Fill(_bossFill, gm.Boss.Health / gm.Boss.MaxHealth);
            bool showObjective = playing && gm.ObjectiveKind != RaidObjectiveKind.None;
            _objectiveBadge.gameObject.SetActive(showObjective);
            // Text formatting allocates; refresh it at 10 Hz while the bars,
            // charge and reticle keep updating every frame. The winding scan
            // rides along with the text refresh instead of every frame.
            bool refreshText = Time.unscaledTime >= _nextTextRefresh;
            if (refreshText)
            {
                _nextTextRefresh = Time.unscaledTime + 0.1f;
                // One readable countdown for ordinary wind-ups. The brute keeps
                // his own larger panel, so the two cues share the same slot and
                // are mutually exclusive.
                Monster threat = tutorial || (gm.Boss != null && !gm.Boss.IsDead)
                    ? null : gm.NearestWinding();
                _threat.gameObject.SetActive(threat != null);
                _hp.text = GameLanguage.Text($"{Mathf.CeilToInt(gm.Health)}/{Mathf.CeilToInt(gm.MaxHealth)} HP", $"체력 {Mathf.CeilToInt(gm.Health)}/{Mathf.CeilToInt(gm.MaxHealth)}");
                int shownRaid = Mathf.Max(1, gm.RaidNumber);
                int chapter = GameManager.ChapterOf(shownRaid);
                int raidInChapter = GameManager.RaidInChapter(shownRaid);
                _raid.text = tutorial
                    ? GameLanguage.Text("PRACTICE", "연습")
                    : GameLanguage.Text($"CH {chapter} {raidInChapter}/{GameManager.RaidsPerChapter}", $"{chapter}장 {raidInChapter}/{GameManager.RaidsPerChapter}");
                _enemies.text = tutorial
                    ? GameLanguage.Text("free arrows", "화살 무제한")
                    : GameLanguage.Text($"{gm.MonstersRemaining} enemies", $"남은 적 {gm.MonstersRemaining}");
                _score.text = GameLanguage.Text($"{gm.Score} PTS · ×{gm.ScoreMultiplier} · {gm.HitStreak} HITS", $"{gm.Score}점 · ×{gm.ScoreMultiplier} · {gm.HitStreak}연속 명중");
                if (gm.Boss != null)
                    _bossLabel.text = gm.Boss.IsInterrupted
                        ? GameLanguage.Text($"STAGGERED · RECOVERING {gm.Boss.InterruptRemaining:0.0}s", $"경직 · 회복 {gm.Boss.InterruptRemaining:0.0}초")
                        : gm.Boss.IsWinding
                            ? GameLanguage.Text($"ATTACK IN {gm.Boss.WindupRemaining:0.0}s · HEADSHOT TO INTERRUPT", $"{gm.Boss.WindupRemaining:0.0}초 후 공격 · 헤드샷으로 저지")
                            : GameLanguage.Text("THE BRUTE", "오우거");
                if (threat != null)
                    _threat.text = GameLanguage.Text($"{RoleName(threat.Role)} · ATTACK IN {threat.WindupRemaining:0.0}s",
                        $"{RoleName(threat.Role)} · {threat.WindupRemaining:0.0}초 후 공격");
                if (showObjective)
                {
                    _objectiveBadge.text = GameLanguage.Text(
                        $"{gm.ObjectiveTitle()} · {gm.ObjectiveProgressText()}",
                        $"{gm.ObjectiveTitle()} · {gm.ObjectiveProgressText()}");
                    _objectiveBadge.color = gm.ObjectiveFailed ? new Color(0.95f, 0.45f, 0.34f)
                        : gm.ObjectiveComplete ? new Color(0.55f, 0.95f, 0.58f)
                        : Gold;
                }
            }
            _charge.SetActive(archer.IsAiming); _drawLabel.gameObject.SetActive(archer.IsAiming);
            _cancel.SetActive(archer.IsAiming);
            Fill(_chargeFill, archer.Charge01);
            if (refreshText)
                _drawLabel.text = archer.Charge01 >= 0.99f ? GameLanguage.Text("FULL DRAW", "최대 당김") : GameLanguage.Text($"DRAW  {Mathf.RoundToInt(archer.Charge01*100)}%", $"당김 {Mathf.RoundToInt(archer.Charge01*100)}%");
            _hint.gameObject.SetActive(!archer.IsAiming && playing && gm.RaidNumber <= 2);
            var tut = tutorial ? gm.Tutorial : null;
            bool hasTut = tut != null && tut.Active;
            _objective.gameObject.SetActive(tutorial && hasTut);
            _objectiveSub.gameObject.SetActive(tutorial && hasTut);
            if (tutorial && hasTut && refreshText)
            {
                string progress = tut.HitsNeeded > 1 ? $"  {tut.HitsThisStep}/{tut.HitsNeeded}" : "";
                _objective.text = tut.ObjectiveTitle() + progress;
                _objectiveSub.text = tut.ObjectiveHint();
            }
            var feedback = GameFeedback.Instance;
            bool hit = feedback != null && Time.time < feedback.HitUntil;
            _hit.gameObject.SetActive(hit);
            var camera = _camera != null ? _camera : (_camera = Camera.main);
            if (hit && camera != null)
            {
                bool blocked = feedback.Blocked;
                bool critical = feedback.Critical;
                Color marker = blocked ? new Color(0.66f, 0.84f, 1f)
                    : critical ? new Color(1f, 0.86f, 0.44f)
                    : Cream;
                _hit.color = marker;
                _hitCross.color = marker;
                float scale = critical ? 1.45f : blocked ? 1.15f : 0.95f;
                _hit.rectTransform.localScale = Vector3.one * scale;
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
                // Keep numbers off the silhouette: push them away from screen
                // centre and fan overlapping hits instead of stacking on a head.
                float side = local.x >= 0f ? 1f : -1f;
                float lane = ((i % 3) - 1) * 9f;
                _numbers[i].rectTransform.anchoredPosition = local + new Vector2(side * 26f + lane, 24f);
                var color = _numbers[i].color; color.a = Mathf.Clamp01(remaining / .25f); _numbers[i].color = color;
                _numbers[i].gameObject.SetActive(screen.z > 0);
            }
            bool toast = feedback != null && Time.unscaledTime < feedback.MessageUntil;
            float hurtAlpha = feedback == null ? 0f : Mathf.Clamp01((feedback.HurtUntil - Time.unscaledTime) / 0.3f) * 0.2f;
            if (GameSettings.ReducedMotion) hurtAlpha *= 0.5f;
            _hurt.color = new Color(0.75f, 0.12f, 0.08f, hurtAlpha);
            if (refreshText)
            {
                _toast.text = toast ? feedback.Message : "";
                _toastDetail.text = toast ? feedback.Detail : "";
                // Settings copy only needs formatting while the page is open.
                if (_menuOpen)
                {
                    _soundLabel.text = GameSettings.SoundEnabled ? GameLanguage.Text("Sound: On", "소리: 켜짐") : GameLanguage.Text("Sound: Off", "소리: 꺼짐");
                    _musicLabel.text = GameLanguage.Text($"Music {Mathf.RoundToInt(GameSettings.MusicVolume * 100)}%", $"음악 {Mathf.RoundToInt(GameSettings.MusicVolume * 100)}%");
                    _ambienceLabel.text = GameLanguage.Text($"Amb {Mathf.RoundToInt(GameSettings.AmbienceVolume * 100)}%", $"환경 {Mathf.RoundToInt(GameSettings.AmbienceVolume * 100)}%");
                    _effectsLabel.text = GameLanguage.Text($"Effects {Mathf.RoundToInt(GameSettings.EffectsVolume * 100)}%", $"효과음 {Mathf.RoundToInt(GameSettings.EffectsVolume * 100)}%");
                    _guideLabel.text = archer.ShowAimGuide ? GameLanguage.Text("Guide: On", "조준선 켜짐") : GameLanguage.Text("Guide: Off", "조준선 꺼짐");
                    _sensitivityLabel.text = GameLanguage.Text($"Aim {GameSettings.AimSensitivity:0.0}×", $"조준 {GameSettings.AimSensitivity:0.0}×");
                    _languageLabel.text = GameLanguage.Text("Language", "언어");
                    _reducedMotionLabel.text = GameSettings.ReducedMotion ? GameLanguage.Text("Motion: Low", "모션: 줄임") : GameLanguage.Text("Motion: Full", "모션: 전체");
                    _resetDefaults.GetComponentInChildren<Text>().text = GameLanguage.Text("Restore defaults", "기본값 복원");
                    _menuTitle.text = GameLanguage.Text("SETTINGS", "설정");
                    _menuCloseLabel.text = GameLanguage.Text("Close", "닫기");
                    _menuHint.text = GameLanguage.Text("Saved automatically.", "설정은 자동 저장됩니다.");
                }
                _hint.text = GameLanguage.Text("Hold · Aim · Release", "당기기 · 조준 · 발사");
                _cancel.GetComponentInChildren<Text>().text = GameLanguage.Text("Cancel", "취소");
                if (_toTitleLabel != null) _toTitleLabel.text = GameLanguage.Text("TITLE", "메인");
            }
            if (_lastKorean != GameLanguage.Korean || _lastState != gm.State || _lastRaid != gm.RaidNumber || _lastOffers != gm.Offers.Count)
            {
                if (_lastState != gm.State) SetMenu(false);
                _lastKorean=GameLanguage.Korean; _lastState=gm.State; _lastRaid=gm.RaidNumber; _lastOffers=gm.Offers.Count;
                RefreshOverlay(gm);
            }
        }

        static string RoleName(MonsterRole role) => role switch
        {
            MonsterRole.Runner => GameLanguage.Text("RUNNER", "질주병"),
            MonsterRole.Shield => GameLanguage.Text("SHIELD", "방패병"),
            MonsterRole.Ogre => GameLanguage.Text("BRUTE", "오우거"),
            _ => GameLanguage.Text("RAIDER", "약탈자")
        };

        void UpdateMenu()
        {
            // Gameplay and tutorial hide the whole overlay; never leave the
            // settings page logically open behind them.
            var gm = GameManager.Instance;
            bool menuState = gm != null && _overlay.activeSelf
                && gm.State != GameState.Playing && gm.State != GameState.Tutorial;
            if (!menuState && _menuOpen) SetMenu(false);
            _menuEase = Mathf.MoveTowards(_menuEase, _menuOpen ? 1f : 0f, Time.unscaledDeltaTime * 6.5f);
            float eased = _menuEase * _menuEase * (3f - 2f * _menuEase);
            // Two pages on the same stone slab: the card content slides out
            // while the settings page slides in from the card's right edge.
            // Nothing opaque is stacked over the stone panel.
            float width = _panel.rect.width > 1f ? _panel.rect.width : 432f;
            _cardContent.anchoredPosition = new Vector2(-width * eased, 0f);
            _settingsPage.anchoredPosition = new Vector2(width * (1f - eased), 0f);
        }

        void LateUpdate()
        {
            // The reticle must reflect the aim/trajectory produced by this
            // frame's archer update, not the previous frame. Updating it here
            // removes a one-frame lag that made shots look high on release.
            var archer = ArcherController.Instance;
            if (archer == null || _combat == null) return;
            Vector2 reticle = archer.IsAiming ? archer.ReticlePosition : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_combat.GetComponent<RectTransform>(), reticle, null, out var point);
            _reticle.rectTransform.anchoredPosition = point;
            _reticle.gameObject.SetActive(archer.IsAiming && _combat.activeSelf);
            bool enemy = archer.Prediction == AimPrediction.Enemy;
            bool blocked = archer.Prediction == AimPrediction.Shield;
            Color aimColor = blocked ? new Color(1f, 0.35f, 0.25f)
                : enemy ? new Color(0.45f, 1f, 0.5f) : Cream;
            _reticle.color = aimColor;
            _reticle.rectTransform.sizeDelta = new Vector2(enemy || blocked ? 10 : 6, blocked ? 4 : enemy ? 10 : 6);
        }

        static string BestLine()
        {
            if (!PlayerRecords.HasRecords)
                return GameLanguage.Text("BEST · no record yet", "최고 기록 · 아직 없음");
            string accuracy = PlayerRecords.BestAccuracy > 0f
                ? Mathf.RoundToInt(PlayerRecords.BestAccuracy * 100) + "%" : "--";
            return GameLanguage.Text(
                $"BEST {PlayerRecords.BestScore:00000} · {accuracy} · {PlayerRecords.BestRaids} raids",
                $"최고 {PlayerRecords.BestScore:00000} · {accuracy} · {PlayerRecords.BestRaids}회 격퇴");
        }

        static string BestLine(RunRecordSummary records)
        {
            string accuracy = records.BestAccuracy > 0f
                ? Mathf.RoundToInt(records.BestAccuracy * 100) + "%" : "--";
            return GameLanguage.Text(
                $"BEST {records.BestScore:00000} · {accuracy} · {records.BestRaids} raids",
                $"최고 {records.BestScore:00000} · {accuracy} · {records.BestRaids}회 격퇴");
        }

        static string NewBestLine(RunRecordSummary records)
        {
            string bits = "";
            if (records.NewScore) bits += GameLanguage.Text(" SCORE", " 점수");
            if (records.NewAccuracy) bits += GameLanguage.Text(" ACCURACY", " 명중률");
            if (records.NewHeadshots) bits += GameLanguage.Text(" HEADSHOTS", " 헤드샷");
            if (records.NewRaids) bits += GameLanguage.Text(" RAIDS", " 격퇴");
            return bits.Length == 0 ? "" : GameLanguage.Text("NEW BEST ·" + bits, "신기록 ·" + bits);
        }

        static string ChallengeLine(GameManager gm, bool detailed)
        {
            int headhunter = Mathf.Min(gm.Headshots, GameManager.HeadhunterTarget);
            int untouched = Mathf.Min(gm.UntouchedRaids, GameManager.UntouchedTarget);
            int thrifty = Mathf.Min(gm.ThriftyRaids, GameManager.ThriftyTarget);
            string compact = GameLanguage.Text(
                $"CHALLENGES {gm.ChallengesCompleted}/3 · HEAD {headhunter}/{GameManager.HeadhunterTarget}\n" +
                $"CLEAN {untouched}/{GameManager.UntouchedTarget} · THRIFTY {thrifty}/{GameManager.ThriftyTarget}",
                $"도전 {gm.ChallengesCompleted}/3 · 헤드 {headhunter}/{GameManager.HeadhunterTarget}\n" +
                $"무결 {untouched}/{GameManager.UntouchedTarget} · 절약 {thrifty}/{GameManager.ThriftyTarget}");
            if (!detailed) return compact;
            return compact + GameLanguage.Text(
                $"\n{GameManager.HeadhunterTarget} headshots · {GameManager.UntouchedTarget} no-damage raids · {GameManager.ThriftyTarget} raids in {GameManager.ThriftyShotLimit} shots",
                $"\n헤드샷 {GameManager.HeadhunterTarget}회 · 피해 없이 {GameManager.UntouchedTarget}회 · {GameManager.ThriftyShotLimit}발 이하 {GameManager.ThriftyTarget}회");
        }

        void RefreshOverlay(GameManager gm)
        {
            bool intro = gm.State == GameState.Intermission && gm.RaidNumber == 0;
            bool supply = gm.State == GameState.Intermission && !intro;
            bool pause = gm.State == GameState.Paused;
            bool victory = gm.State == GameState.Victory;
            bool chapterBreak = supply && GameManager.EndsChapter(gm.RaidNumber);
            bool hasOffers = gm.Offers.Count > 0;
            int chapter = GameManager.ChapterOf(Mathf.Max(1, gm.RaidNumber));
            int nextChapter = Mathf.Min(GameManager.TotalChapters, chapter + 1);
            _brand.text = intro
                ? GameLanguage.Text("THE FOREST WATCH", "숲의 수호자")
                : chapterBreak ? GameManager.ChapterName(nextChapter)
                : supply ? GameLanguage.Text("VILLAGE SUPPLY POST", "마을 보급소")
                : pause ? GameLanguage.Text("TAKE A BREATH", "잠시 휴식")
                : victory ? GameManager.ChapterName(GameManager.TotalChapters)
                : GameManager.ChapterName(chapter);
            _title.text = intro
                ? GameLanguage.Text("LAST RANGER", "라스트 레인저")
                : chapterBreak ? GameManager.ChapterClearTitle(chapter)
                : supply ? GameLanguage.Text($"RAID {gm.RaidNumber} CLEARED", $"제{gm.RaidNumber}차 습격 격퇴")
                : pause ? GameLanguage.Text("WATCH PAUSED", "일시 정지")
                : victory ? GameManager.ChapterClearTitle(GameManager.TotalChapters)
                : GameLanguage.Text("THE DEFENSE FELL", "방어선 붕괴");
            _title.fontSize = 38;
            bool defeat = !intro && !supply && !pause && !victory;
            _subtitle.text = intro
                ? GameLanguage.Text("One bow. Three chapters.\nFifteen raids. One village.", "활 하나로 세 개의 장, 열다섯 번의 습격.\n마을은 당신을 기다립니다.")
                : chapterBreak ? (hasOffers
                    ? GameManager.ChapterStartLine(nextChapter)
                    : GameLanguage.Text($"Chapter {nextChapter}: {GameManager.ChapterName(nextChapter)}", $"{nextChapter}장: {GameManager.ChapterName(nextChapter)}"))
                : supply ? GameLanguage.Text($"Next: {GameManager.RaidTitle(gm.RaidNumber+1)}", $"다음: {GameManager.RaidTitle(gm.RaidNumber+1)}")
                : pause ? GameLanguage.Text("Your shot has been safely cancelled.", "당기던 활을 내렸습니다.")
                : victory ? GameLanguage.Text("Fifteen raids held. The dawn finally breaks.", "열다섯 번의 습격을 막아냈습니다.\n마침내 새벽이 밝아옵니다.")
                : gm.DefeatCause;
            string challengeProgress = ChallengeLine(gm, pause || supply);
            string recordLine = gm.HasRunRecords ? BestLine(gm.LastRunRecords) : BestLine();
            string newBest = gm.HasRunRecords ? NewBestLine(gm.LastRunRecords) : "";
            string recordsBlock = string.IsNullOrEmpty(newBest) ? recordLine : newBest + "\n" + recordLine;
            // Results cards carry more copy; a slightly smaller size keeps
            // cause, tip, records and challenges on the slab.
            _stats.fontSize = (defeat || victory) ? 21 : 26;
            _stats.rectTransform.anchorMin = new Vector2(0f,
                (defeat || victory) ? 0.17f : intro ? 0.27f : 0.21f);
            _stats.text = intro
                ? GameLanguage.Text(
                    "Hold to draw.\nDrag to aim, then release.\nAim higher at distance.\n" + recordLine,
                    "길게 눌러 활을 당기세요.\n끌어서 조준한 뒤 손을 놓으세요.\n멀수록 높게 조준하세요.\n" + recordLine)
                : supply ? (hasOffers ? "" : GameLanguage.Text("Supplies taken.\n" + challengeProgress,
                    "보급품을 받았습니다.\n" + challengeProgress))
                : pause ? GameLanguage.Text("Hold · Aim · Release\n" + challengeProgress + "\nAccuracy records need 20+ shots.",
                    "당기기 · 조준 · 발사\n" + challengeProgress + "\n명중률 기록은 20발 이상부터 집계됩니다.")
                : victory ? GameLanguage.Text(
                    $"SCORE {gm.Score:00000} · {gm.Kills} kills\n{Mathf.RoundToInt(gm.Accuracy*100)}% hit · {gm.Headshots} headshots · {gm.RaidsCleared} raids\n" +
                    recordsBlock + "\n" + challengeProgress,
                    $"점수 {gm.Score:00000} · {gm.Kills}마리 처치\n명중률 {Mathf.RoundToInt(gm.Accuracy*100)}% · 헤드샷 {gm.Headshots}회 · {gm.RaidsCleared}회 격퇴\n" +
                    recordsBlock + "\n" + challengeProgress)
                : GameLanguage.Text(
                    $"TIP · {gm.DefeatTip}\nSCORE {gm.Score:00000} · {gm.Kills} kills · {Mathf.RoundToInt(gm.Accuracy*100)}%\n" +
                    $"{gm.Headshots} headshots · {gm.RaidsCleared} raids\n" + recordsBlock + "\n" + challengeProgress,
                    $"조언 · {gm.DefeatTip}\n점수 {gm.Score:00000} · {gm.Kills}마리 처치 · {Mathf.RoundToInt(gm.Accuracy*100)}%\n" +
                    $"헤드샷 {gm.Headshots}회 · {gm.RaidsCleared}회 격퇴\n" + recordsBlock + "\n" + challengeProgress);
            _practice.gameObject.SetActive(intro);
            // The title tab is redundant on the title card itself.
            if (_toTitle != null) _toTitle.gameObject.SetActive(!intro);
            _skip.gameObject.SetActive(gm.State == GameState.Tutorial);
            _offerA.gameObject.SetActive(supply && gm.Offers.Count>0);
            _offerB.gameObject.SetActive(supply && gm.Offers.Count>1);
            for(int i=0;i<gm.Offers.Count && i<2;i++)
            {
                var button = i == 0 ? _offerA : _offerB;
                button.GetComponentInChildren<Text>().text = gm.UpgradeLabel(gm.Offers[i]);
            }
            _action.interactable = !supply || !hasOffers;
            _action.GetComponentInChildren<Text>().text = intro
                ? GameLanguage.Text("Begin the Watch", "방어 시작")
                : supply ? (hasOffers
                    ? GameLanguage.Text("Choose supplies", "보급품 선택")
                    : chapterBreak ? GameLanguage.Text($"Begin Chapter {GameManager.ChapterNumberWord(nextChapter)}", $"{nextChapter}장 시작")
                    : GameLanguage.Text($"Begin Raid {gm.RaidNumber+1}", $"제{gm.RaidNumber+1}차 습격 시작"))
                : pause ? GameLanguage.Text("Resume Watch", "계속하기")
                : GameLanguage.Text("Defend Again", "다시 도전");
            _practice.GetComponentInChildren<Text>().text = PlayerPrefs.GetInt("LastRanger.Practiced", 0) == 1
                ? GameLanguage.Text("Practice again", "다시 연습하기")
                : GameLanguage.Text("Practice first", "먼저 연습하기");
        }

        void OnDestroy() { if (_feedback != null) { _feedback.Impact -= ShowImpact; _feedback.Cleared -= ClearImpacts; } if (Instance == this) Instance = null; }
    }
}
