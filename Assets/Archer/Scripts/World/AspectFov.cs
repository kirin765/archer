using UnityEngine;

namespace Archer
{
    /// <summary>
    /// Keeps a sensible horizontal field of view on tall (9:16) screens.
    /// A vertical FOV that looks fine in landscape becomes a very narrow
    /// letterbox in portrait, so we widen it as the aspect gets narrower,
    /// clamped so the image never gets too distorted.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class AspectFov : MonoBehaviour
    {
        [Tooltip("Minimum visible horizontal field of view, in degrees.")]
        public float MinHorizontalFov = 44f;
        [Tooltip("Vertical FOV used on wide (landscape/square) screens.")]
        public float MinVerticalFov = 50f;
        [Tooltip("Upper clamp for the vertical FOV on narrow screens.")]
        public float MaxVerticalFov = 70f;

        Camera _camera;
        float _lastAspect = -1f;

        void Awake()
        {
            _camera = GetComponent<Camera>();
            Apply(Mathf.Max(_camera.aspect, 0.0001f));
        }

        void Update()
        {
            float aspect = Mathf.Max(_camera.aspect, 0.0001f);
            if (Mathf.Abs(aspect - _lastAspect) > 0.0005f)
                Apply(aspect);
        }

        void Apply(float aspect)
        {
            _lastAspect = aspect;

            float halfH = MinHorizontalFov * 0.5f * Mathf.Deg2Rad;
            float vFromHorizontal = 2f * Mathf.Atan(Mathf.Tan(halfH) / aspect) * Mathf.Rad2Deg;
            float v = Mathf.Clamp(vFromHorizontal, MinVerticalFov, MaxVerticalFov);

            _camera.fieldOfView = v;
        }
    }
}
