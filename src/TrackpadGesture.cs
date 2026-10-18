using System;
using System.Collections.Generic;

namespace ProjectorDash
{
    // Input-only state machine. No timers, rendering loop, or Windows dependency.
    public sealed class TrackpadGesture
    {
        private sealed class Contact
        {
            public double X, Y, StartX, StartY;
        }
        private readonly Dictionary<int, Contact> _contacts = new Dictionary<int, Contact>();
        private long _started;
        private bool _moved, _multiTouch, _dragging;
        private double _scrollRemainder;
        public Action<double, double> MovePointer;
        public Action<int> Scroll;
        public Action Tap;
        public Action<bool> Drag;
        public bool DragNextTouch { get; set; }
        public int ContactCount { get { return _contacts.Count; } }

        public void Down(int id, double x, double y, long milliseconds)
        {
            if (_contacts.ContainsKey(id)) return;
            if (_contacts.Count == 0)
            {
                _started = milliseconds;
                _moved = _multiTouch = false;
                _scrollRemainder = 0;
                _dragging = DragNextTouch;
                DragNextTouch = false;
                if (_dragging && Drag != null) Drag(true);
            }
            _contacts.Add(id, new Contact { X = x, Y = y, StartX = x, StartY = y });
            if (_contacts.Count > 1)
            {
                _multiTouch = true;
                EndDrag();
            }
        }

        public void Move(int id, double x, double y)
        {
            Contact contact;
            if (!_contacts.TryGetValue(id, out contact)) return;
            double dx = x - contact.X, dy = y - contact.Y;
            contact.X = x; contact.Y = y;
            double travelX = x - contact.StartX, travelY = y - contact.StartY;
            if (travelX * travelX + travelY * travelY > 64) _moved = true;
            if (_contacts.Count == 2)
            {
                // Each finger contributes half the centroid's movement.
                _scrollRemainder += dy / 2;
                int steps = (int)(_scrollRemainder / 18);
                if (steps != 0)
                {
                    _scrollRemainder -= steps * 18;
                    if (Scroll != null) Scroll(steps * 120);
                }
            }
            else if (_contacts.Count == 1 && !_multiTouch && MovePointer != null)
                MovePointer(dx, dy);
        }

        public void Up(int id, long milliseconds)
        {
            if (!_contacts.Remove(id)) return;
            if (_contacts.Count != 0) return;
            bool tap = !_moved && !_multiTouch && !_dragging && milliseconds - _started <= 350;
            EndDrag();
            if (tap && Tap != null) Tap();
        }

        public void Cancel()
        {
            _contacts.Clear();
            DragNextTouch = false;
            EndDrag();
        }

        private void EndDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            _moved = true;
            if (Drag != null) Drag(false);
        }
    }

    public static class PointerMath
    {
        public static double Clamp(double value, int origin, int length)
        {
            return Math.Max(origin, Math.Min(origin + Math.Max(1, length) - 1, value));
        }
        public static int Normalize(double value, int origin, int length)
        {
            if (length <= 1) return 0;
            return (int)Math.Round((Clamp(value, origin, length) - origin) * 65535.0 / (length - 1));
        }
    }
}
