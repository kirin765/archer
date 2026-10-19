using UnityEngine;
using UnityEngine.InputSystem;

namespace Archer
{
    /// <summary>Track one finger until release; a second finger cannot steal the drawn shot.</summary>
    public static class InputService
    {
        static int _frame = -1, _touchId = -1;
        static bool _pressed, _held, _released, _touch;
        static Vector2 _position;
        public static Vector2 Position { get { Read(); return _position; } }
        public static bool PressedThisFrame { get { Read(); return _pressed; } }
        public static bool Held { get { Read(); return _held; } }
        public static bool ReleasedThisFrame { get { Read(); return _released; } }
        /// <summary>True while the current gesture comes from a touchscreen.</summary>
        public static bool IsTouch { get { Read(); return _touch; } }

        static void Read()
        {
            if(_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _pressed = _released = false;
            _touch = false;
            var screen = Touchscreen.current;
            if(screen != null)
            {
                if(_touchId < 0)
                {
                    foreach(var touch in screen.touches)
                        if(touch.press.wasPressedThisFrame)
                        { _touchId=touch.touchId.ReadValue(); break; }
                }
                if(_touchId >= 0)
                {
                    _touch = true;
                    foreach(var touch in screen.touches)
                    {
                        if(touch.touchId.ReadValue()!=_touchId) continue;
                        _position=touch.position.ReadValue();
                        _pressed=touch.press.wasPressedThisFrame;
                        _held=touch.press.isPressed;
                        _released=touch.press.wasReleasedThisFrame;
                        if(!_held) { _released=true; _touchId=-1; }
                        return;
                    }
                    _held=false; _released=true; _touchId=-1; return;
                }
            }
            var mouse=Mouse.current;
            _position=mouse != null ? mouse.position.ReadValue() : _position;
            _pressed=mouse != null && mouse.leftButton.wasPressedThisFrame;
            _held=mouse != null && mouse.leftButton.isPressed;
            _released=mouse != null && mouse.leftButton.wasReleasedThisFrame;
        }
    }
}
