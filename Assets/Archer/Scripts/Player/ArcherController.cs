using UnityEngine;

namespace Archer
{
    /// <summary>
    /// The player. Control contract:
    /// tap to aim (sets the initial yaw/pitch toward the tapped point),
    /// hold to charge power over <see cref="ChargeTime"/>, drag to adjust the
    /// shot angle (horizontal = yaw, vertical = pitch), and release to fire.
    /// Aiming is only permitted during active play with ammunition available.
    /// </summary>
    public class ArcherController : MonoBehaviour
    {
        [Header("Aim limits")]
        public float MaxYaw = 34f;
        public float MinPitch = 4f;
        public float MaxPitch = 52f;

        [Header("Draw / power")]
        public float MinLaunchSpeed = 16f;
        public float MaxLaunchSpeed = 30f;
        public float MinPower = 0.28f;
        [Tooltip("Seconds of holding to charge from minimum power to full power.")]
        public float ChargeTime = 1.5f;
        [Tooltip("Fraction of the smaller screen dimension treated as a full aim drag.")]
        public float FullDrawScreenFraction = 0.34f;
        [Tooltip("Multiplier on up/down drag -> shot angle response. Below 1 = less sensitive.")]
        public float VerticalAimSensitivity = 0.5f;
        [Tooltip("On touch screens, raise the aim point above the finger by this fraction of screen height so the target stays visible.")]
        [Range(0f, 0.2f)] public float TouchAimLiftFraction = 0.08f;
        [Tooltip("Touch drag multiplier from the accessibility settings (1 = default).")]
        [Range(0.5f, 1.5f)] public float AimSensitivity = 1f;

        [Header("Arrow")]
        public float ArrowDamage = 34f;

        [Header("Placement")]
        [Tooltip("Bow position in camera-local space. The bow is parented to the camera so it stays fixed on screen.")]
        public Vector3 BowLocalOffset = new Vector3(0.43f, -0.62f, 1.65f);
        [Tooltip("Resting bow tilt as a fraction of the aim pitch. 0 = upright/vertical.")]
        [Range(0f, 1f)] public float RestPitchFraction = 0.05f;
        [Tooltip("Roll of the bow about the aim axis, in degrees. 90 lays the bow horizontal.")]
        public float BowRoll = 28f;
        [Tooltip("Draw the predicted arrow-path guide line while aiming.")]
        public bool ShowAimGuide = true;

        Camera _camera;
        readonly RaycastHit[] _aimHits = new RaycastHit[24];
        Transform _root;
        Transform _shoulder;
        Transform _bowPivot;
        Transform _bowRoll;
        Transform _nock;
        Transform _nockedArrow;
        Transform _arrowParent;
        LineRenderer _trajectory;
        LineRenderer _string;
        readonly Transform[] _limbs = new Transform[6];
        Transform _stringTop;
        Transform _stringBottom;

        Material _arrowMaterial;
        bool _aiming;
        bool _canAimLast;
        bool _suppressUntilRelease;
        Vector2 _anchor;
        float _anchorYaw;
        float _anchorPitch;
        float _yaw;
        float _pitch;
        float _power;
        float _recoil;
        Vector2 _aimScreen;
        public Vector3 PredictedImpact { get; private set; }
        public bool HasPrediction { get; private set; }
        public Vector2 ReticlePosition => _camera != null && HasPrediction
            ? (Vector2)_camera.WorldToScreenPoint(PredictedImpact) : _aimScreen;
        public Vector3 LaunchPosition => _nock.position + AimDirection * 0.3f;
        public Vector3 LaunchVelocity => AimDirection * CurrentSpeed;

        public static ArcherController Instance { get; private set; }
        public bool IsAiming => _aiming;
        public float Charge01 => Mathf.InverseLerp(MinPower, 1f, _power);

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static ArcherController Attach(GameObject archerRoot, Transform arrowParent)
        {
            var controller = archerRoot.AddComponent<ArcherController>();
            controller.Initialize(archerRoot.transform, arrowParent);
            return controller;
        }

        public void Initialize(Transform root, Transform arrowParent)
        {
            _root = root;
            _arrowParent = arrowParent;
            ApplySettings();
            Build();
        }

        /// <summary>Re-read persisted control/accessibility preferences after a settings change.</summary>
        public void ApplySettings()
        {
            AimSensitivity = GameSettings.AimSensitivity;
            ShowAimGuide = GameSettings.ShowAimGuide;
        }

        void Build()
        {
            _arrowMaterial = Prim.Material(new Color(0.55f, 0.36f, 0.20f));

            _camera = Camera.main;
            var bowParent = _camera != null ? _camera.transform : _root;
            _shoulder = Prim.Empty("Shoulder", bowParent, BowLocalOffset).transform;
            _bowPivot = Prim.Empty("BowPivot", _shoulder, Vector3.zero).transform;
            _bowRoll = Prim.Empty("BowRoll", _bowPivot, Vector3.zero).transform;
            _bowRoll.localRotation = Quaternion.Euler(0f, 0f, BowRoll);

            BuildBow();
            BuildTrajectory();
            ResetPose();
        }

        void BuildBow()
        {
            var bowMat = Prim.Material(new Color(0.43f, 0.25f, 0.10f));
            var wrap = Prim.Material(new Color(0.20f,0.12f,0.075f));
            Prim.Create(PrimitiveType.Cylinder,"Leather grip",_bowRoll,new Vector3(0,0,0.40f),new Vector3(0.09f,0.13f,0.09f),wrap,false);
            for(int side=-1;side<=1;side+=2)
            {
                Vector3[] points = { new Vector3(0,side*0.12f,0.4f),new Vector3(0.06f,side*0.40f,0.36f),new Vector3(0.045f,side*0.62f,0.25f),new Vector3(0,side*0.70f,0.14f) };
                for(int i=0;i<points.Length-1;i++)
                {
                    var direction=points[i+1]-points[i];
                    var limb=Prim.Create(PrimitiveType.Cylinder,"Curved bow limb",_bowRoll,(points[i]+points[i+1])*0.5f,new Vector3(0.055f-i*0.01f,direction.magnitude*0.5f,0.055f-i*0.01f),bowMat,false);
                    limb.transform.localRotation=Quaternion.FromToRotation(Vector3.up,direction);
                    _limbs[(side == -1 ? 0 : 3) + i] = limb.transform;
                }
            }

            Prim.Create(PrimitiveType.Capsule,"Ranger glove",_bowRoll,new Vector3(-0.06f,-0.06f,0.35f),new Vector3(0.16f,0.1f,0.16f),wrap,false);
            for(int i=0;i<3;i++)
                Prim.Create(PrimitiveType.Cube,"Grip finger",_bowRoll,new Vector3(0.01f,-0.08f+i*0.045f,0.44f),new Vector3(0.12f,0.033f,0.055f),wrap,false);
            _stringTop = Prim.Empty("StringTop", _bowRoll, new Vector3(0f, 0.70f, 0.14f)).transform;
            _stringBottom = Prim.Empty("StringBottom", _bowRoll, new Vector3(0f, -0.70f, 0.14f)).transform;

            var stringMat = Prim.Unlit(new Color(0.9f, 0.9f, 0.9f));
            var stringObj = Prim.Empty("String", _bowRoll, Vector3.zero);
            _string = stringObj.AddComponent<LineRenderer>();
            ConfigureLine(_string, stringMat, 0.012f);
            _string.positionCount = 3;

            _nock = Prim.Empty("Nock", _bowRoll, new Vector3(0f, 0f, 0.235f)).transform;
            _nockedArrow = BuildNockedArrow();
        }

        Transform BuildNockedArrow()
        {
            var arrow = Prim.Empty("NockedArrow", _nock, Vector3.zero).transform;
            var shaft = Prim.Create(PrimitiveType.Cylinder, "Shaft", arrow, new Vector3(0f, 0f, 0.1f),
                new Vector3(0.03f, 0.4f, 0.03f), _arrowMaterial, false);
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Prim.Create(PrimitiveType.Cube, "Head", arrow, new Vector3(0f, 0f, 0.52f),
                new Vector3(0.06f, 0.06f, 0.14f), Prim.Material(new Color(0.8f, 0.82f, 0.88f), 0.6f, 0.9f), false)
                .transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            Prim.Create(PrimitiveType.Cube, "Fletching", arrow, new Vector3(0f, 0f, -0.24f),
                new Vector3(0.12f, 0.02f, 0.12f), Prim.Material(new Color(0.85f, 0.3f, 0.25f)), false);
            return arrow;
        }

        void BuildTrajectory()
        {
            var obj = Prim.Empty("Trajectory", _arrowParent, Vector3.zero);
            _trajectory = obj.AddComponent<LineRenderer>();
            ConfigureLine(_trajectory, Prim.Unlit(new Color(1f, 0.95f, 0.6f, 0.6f)), 0.045f);
            _trajectory.positionCount = 0;
        }

        static void ConfigureLine(LineRenderer line, Material mat, float width)
        {
            line.material = mat;
            line.widthMultiplier = width;
            line.useWorldSpace = true;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.View;
        }

        void Update()
        {
            var gm = GameManager.Instance;
            _recoil = Mathf.MoveTowards(_recoil, 0f, Time.deltaTime * 5f);
            if (!_aiming) UpdateVisuals();
            // Practice always shows the trajectory guide: it is the teaching aid
            // for gravity. Players can still turn it off for the raids themselves.
            if (gm != null && gm.State == GameState.Tutorial) ShowAimGuide = true;
            // Aiming requires active play AND ammunition, so we never show a
            // draw/release that cannot launch an arrow.
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (_aiming) CancelAim(); else gm?.TogglePause();
            }
            bool canAim = gm != null && (gm.State == GameState.Playing || gm.State == GameState.Tutorial)
                && (gm.State == GameState.Tutorial || gm.Arrows > 0);

            // A finger already down when play (re)starts is the UI press that
            // started the raid, not a draw; require a fresh press first.
            if (canAim && !_canAimLast && InputService.Held)
                _suppressUntilRelease = true;
            _canAimLast = canAim;
            if (_suppressUntilRelease && !InputService.Held)
                _suppressUntilRelease = false;

            // Single cancellation path: game over, pause, no ammo, restart, etc.
            if (_aiming && !canAim)
            {
                CancelAim();
                return;
            }

            bool allow = canAim && !_suppressUntilRelease && !Hud.BlocksPointer(InputService.Position);

            if (allow && InputService.PressedThisFrame)
                BeginAim();
            if (_aiming && Hud.BlocksPointer(InputService.Position)) { CancelAim(); return; }
            if (_aiming)
            {
                ChargePower();
                UpdateAim(InputService.Position);
                UpdateVisuals();

                if (InputService.ReleasedThisFrame)
                {
                    Fire();
                    CancelAim();
                }
            }

        }

        public void CancelAim()
        {
            _aiming = false;
            _suppressUntilRelease = true;
            HasPrediction = false;
            _power = MinPower;
            if (_nockedArrow != null) _nockedArrow.gameObject.SetActive(false);
            if (_trajectory != null) _trajectory.positionCount = 0;
            UpdateVisuals();
        }

        void OnApplicationFocus(bool focused) { if (!focused && _nock != null) CancelAim(); }
        void OnDisable() { if (_nock != null) CancelAim(); }

        void BeginAim()
        {
            _aiming = true;
            _anchor = InputService.Position;
            _aimScreen = _anchor;
            AimAtScreenPoint(AimPoint(_anchor));
            // A touch drag is relative to where the finger landed. The first
            // tap still places the shot; subsequent movement only nudges the
            // aim, so the finger never has to sit on the reticle.
            _anchorYaw = _yaw;
            _anchorPitch = _pitch;
            _power = MinPower;
            _nockedArrow.gameObject.SetActive(true);
        }

        /// <summary>
        /// Where the shot is actually aimed. On a phone the finger covers the
        /// target, so the aim point is lifted above the touch; with a mouse the
        /// pointer itself is the aim point.
        /// </summary>
        Vector2 AimPoint(Vector2 pointer) =>
            LiftForTouch(pointer, InputService.IsTouch, TouchAimLiftFraction, Screen.height);

        /// <summary>Pure helper so the touch offset can be verified without a device.</summary>
        public static Vector2 LiftForTouch(Vector2 pointer, bool touch, float fraction, float screenHeight)
        {
            if (!touch || fraction <= 0f) return pointer;
            return pointer + Vector2.up * (screenHeight * fraction);
        }

        /// <summary>
        /// Converts a touch drag into yaw/pitch relative to the touch anchor.
        /// Dragging <paramref name="fullDrawFraction"/> of the smaller screen
        /// dimension sweeps the full yaw range; vertical response is scaled
        /// separately. Pure helper so the touch controls can be tested without
        /// a device.
        /// </summary>
        public static Vector2 RelativeAimAngles(Vector2 anchor, Vector2 pointer, Vector2 startAngles,
            float fullDrawFraction, float verticalSensitivity, Vector2 screenSize,
            float maxYaw, float minPitch, float maxPitch)
        {
            float unit = Mathf.Max(1f, Mathf.Min(screenSize.x, screenSize.y) * Mathf.Max(0.01f, fullDrawFraction));
            Vector2 delta = pointer - anchor;
            float yaw = Mathf.Clamp(startAngles.x + delta.x / unit * maxYaw * 2f, -maxYaw, maxYaw);
            float range = Mathf.Max(0.01f, maxPitch - minPitch);
            float pitch = Mathf.Clamp(startAngles.y + delta.y / unit * range * verticalSensitivity, minPitch, maxPitch);
            return new Vector2(yaw, pitch);
        }

        void AimAtScreenPoint(Vector2 screen)
        {
            if (_camera == null)
            {
                _yaw = 0f;
                _pitch = 5f;
                return;
            }

            Ray ray = _camera.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            // Restrict ray distance to the battlefield. Ignore our own projectile colliders.
            Vector3 target = ray.origin + ray.direction * 35f;
            float nearest = 80f;
            int count = Physics.RaycastNonAlloc(ray, _aimHits, 80f);
            for (int i = 0; i < count; i++)
            {
                var hit = _aimHits[i];
                if (hit.collider.GetComponentInParent<Arrow>() != null || hit.distance >= nearest) continue;
                nearest = hit.distance;
                target = hit.point;
            }
            // Aim along the player's chosen direction. Gravity is never auto-corrected:
            // the player learns to aim higher for distance and weaker draws.
            Vector3 origin = _nock != null ? _nock.position : _shoulder.position;
            Vector3 delta = target - origin;
            float distance = new Vector2(delta.x, delta.z).magnitude;
            float pitch = Mathf.Atan2(delta.y, Mathf.Max(distance, 0.01f)) * Mathf.Rad2Deg;
            _yaw = Mathf.Clamp(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, -MaxYaw, MaxYaw);
            _pitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
        }

        void ChargePower()
        {
            // Charge the remaining range (MinPower -> 1) over exactly ChargeTime
            // seconds, so the configured duration matches the tooltip.
            float rate = (1f - MinPower) / Mathf.Max(ChargeTime, 0.01f);
            _power = Mathf.Min(1f, _power + Time.deltaTime * rate);
        }

        void UpdateAim(Vector2 pointer)
        {
            _aimScreen = pointer;
            if (InputService.IsTouch)
            {
                // Touch is relative: the tap places the shot, then dragging
                // adjusts it from the anchor instead of chasing the finger.
                // Sensitivity scales the drag before the normal clamps apply.
                Vector2 drag = (pointer - _anchor) * Mathf.Clamp(AimSensitivity, 0.5f, 1.5f);
                Vector2 angles = RelativeAimAngles(_anchor, _anchor + drag, new Vector2(_anchorYaw, _anchorPitch),
                    FullDrawScreenFraction, VerticalAimSensitivity, new Vector2(Screen.width, Screen.height),
                    MaxYaw, MinPitch, MaxPitch);
                _yaw = angles.x;
                _pitch = angles.y;
            }
            else
            {
                AimAtScreenPoint(AimPoint(pointer));
            }
        }

        void ResetPose()
        {
            _yaw = 0f;
            _pitch = 5f;
            _power = MinPower;
            if (_nockedArrow != null) _nockedArrow.gameObject.SetActive(false);
            if (_trajectory != null) _trajectory.positionCount = 0;
            UpdateVisuals();
        }

        void UpdateVisuals()
        {
            if (_bowPivot != null)
                _bowPivot.rotation = Quaternion.LookRotation(AimDirection, Vector3.up);

            float draw = _aiming ? Mathf.InverseLerp(MinPower, 1f, _power) : 0f;

            // The tips bend toward the drawing hand; release lets the limbs settle.
            float spring = _recoil * Mathf.Sin((1f - _recoil) * 32f) * 0.06f;
            float flex = draw * 0.18f + spring;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 a = new Vector3(0, side * .12f, .4f);
                for (int i = 0; i < 3; i++)
                {
                    float y = i == 0 ? .40f : i == 1 ? .62f : .70f;
                    float z = i == 0 ? .36f - flex * .25f : i == 1 ? .25f - flex * .65f : .14f - flex;
                    float x = i == 0 ? .06f : i == 1 ? .045f : 0;
                    Vector3 b = new Vector3(x, side * (y - flex * (i + 1) * .12f), z);
                    var limb = _limbs[(side == -1 ? 0 : 3) + i];
                    // The bow is torn down with the camera; a late Update
                    // during teardown would otherwise throw every frame.
                    if (limb == null) continue;
                    limb.localPosition = (a + b) * .5f;
                    limb.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
                    limb.localScale = new Vector3(.055f - i * .01f, (b - a).magnitude * .5f, .055f - i * .01f);
                    a = b;
                }
                (side == 1 ? _stringTop : _stringBottom).localPosition = a;
            }
            GameFeedback.Instance?.SetBowTension(_aiming ? draw : -1f);
            float drawBack = draw * 0.55f;
            _nock.localPosition = new Vector3(0f, 0f, 0.235f - drawBack);

            if (_shoulder != null)
                _shoulder.localPosition = BowLocalOffset + Vector3.back * (draw * 0.18f + _recoil*0.12f);

            if (_bowRoll != null)
                _bowRoll.localRotation = Quaternion.Euler(0f, 0f, BowRoll + draw * 8f - _recoil*7f);

            if (_string != null)
            {
                _string.SetPosition(0, _stringTop.position);
                _string.SetPosition(1, _nock.position);
                _string.SetPosition(2, _stringBottom.position);
            }

            if (_aiming) UpdateTrajectory();
        }

        Vector3 AimDirection
        {
            get
            {
                var rot = Quaternion.Euler(0f, _yaw, 0f) * Quaternion.Euler(-_pitch, 0f, 0f);
                return rot * Vector3.forward;
            }
        }

        float CurrentSpeed => Mathf.Lerp(MinLaunchSpeed, MaxLaunchSpeed, _power);

        void UpdateTrajectory()
        {
            Vector3 p0 = LaunchPosition;
            Vector3 v0 = LaunchVelocity;
            const int maxPoints = 80;
            const float dt = 0.04f;
            bool show = ShowAimGuide;
            _trajectory.positionCount = show ? maxPoints : 0;
            Vector3 previous = p0;
            HasPrediction = false;
            for (int i = 0; i < maxPoints; i++)
            {
                float t = i * dt;
                Vector3 p = p0 + v0 * t + 0.5f * Physics.gravity * t * t;
                if (i > 0 && Physics.Linecast(previous, p, out var hit))
                {
                    p = hit.point;
                    PredictedImpact = p;
                    HasPrediction = true;
                    if (show) { _trajectory.SetPosition(i, p); _trajectory.positionCount = i + 1; }
                    break;
                }
                if (show) _trajectory.SetPosition(i, p);
                PredictedImpact = p;
                HasPrediction = true;
                previous = p;
                if (p.y <= 0.02f) { if (show) _trajectory.positionCount = i + 1; break; }
            }
        }

        void Fire()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.TryConsumeArrow()) return;

            gm.RegisterShot();
            Vector3 dir = AimDirection;
            Vector3 spawn = LaunchPosition;
            GameFeedback.Instance?.Play(FeedbackSound.Shot);
            _recoil = 1f;
            Arrow.Create(_arrowParent, spawn, dir * CurrentSpeed, ArrowDamage, _arrowMaterial,
                gm.Piercing, gm.PierceCapacity,
                gm.SlowingShot ? gm.SlowFactor : 1f,
                gm.SlowingShot ? gm.SlowDuration : 0f,
                gm.HeadshotDamageMultiplier);
        }
    }
}
