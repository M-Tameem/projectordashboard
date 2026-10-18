using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ProjectorDash
{
    // A small software marker survives Windows touch cursor suppression. It
    // updates only on trackpad input; no polling, animation or screen capture.
    public sealed class PointerMarkerWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseEvent
        {
            public int X, Y;
            public uint Data, Flags, Time;
            public UIntPtr ExtraInfo;
        }
        private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, MouseHook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string module);
        private readonly MouseHook _callback;
        private IntPtr _hook, _hwnd;

        public PointerMarkerWindow()
        {
            Width = Height = 34;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowActivated = false; ShowInTaskbar = false; Topmost = true;
            Focusable = false; IsHitTestVisible = false;
            Grid marker = new Grid { IsHitTestVisible = false };
            marker.Children.Add(new Ellipse { Width = 26, Height = 26, Stroke = Brushes.Black, StrokeThickness = 5 });
            marker.Children.Add(new Ellipse { Width = 26, Height = 26, Stroke = Brushes.White, StrokeThickness = 2 });
            Content = marker;
            _callback = OnMouse;
            SourceInitialized += delegate
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                SetWindowLong(_hwnd, -20, GetWindowLong(_hwnd, -20) | 0x20 | 0x80 | 0x08000000);
                HwndSource.FromHwnd(_hwnd).AddHook(delegate(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
                {
                    if (msg == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
                    if (msg == 0x84) { handled = true; return new IntPtr(-1); } // HTTRANSPARENT
                    return IntPtr.Zero;
                });
            };
            Closed += delegate { RemoveHook(); };
        }

        public void ShowAt(double x, double y)
        {
            // Install before showing. If hooks are unavailable, do not leave a
            // stale marker behind when a physical mouse takes over.
            if (_hook == IntPtr.Zero) _hook = SetWindowsHookEx(14, _callback, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) return;
            if (_hwnd == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
            int size = Math.Max(24, (int)Math.Round(34 * ScreenUtil.DpiScale(this)));
            SetWindowPos(_hwnd, new IntPtr(-1), (int)Math.Round(x - size / 2.0),
                (int)Math.Round(y - size / 2.0), size, size, 0x0010); // NOACTIVATE
            if (!IsVisible) Show();
        }
        public void Dismiss()
        {
            if (IsVisible) Hide();
            RemoveHook();
        }
        private void RemoveHook()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero;
        }
        private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && message.ToInt32() == 0x200)
            {
                MouseEvent input = (MouseEvent)Marshal.PtrToStructure(data, typeof(MouseEvent));
                ulong extra = input.ExtraInfo.ToUInt64();
                // Ignore our injected movement and Windows' touch/pen-promoted
                // mouse movement. A real mouse/another controller takes over.
                if (extra != ScreenUtil.PointerInputTag && (extra & 0xFFFFFF00UL) != 0xFF515700UL)
                    Dismiss();
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }
    }
}
