using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace ProjectorDash
{
    /// <summary>
    /// A black, click-through layer above the assigned projector. Normal HDMI
    /// sources cannot reliably change a projector's lamp/LED brightness, so
    /// this provides a dependable bedside dimmer for every projected window.
    /// It never takes focus or intercepts mouse/touch input.
    /// </summary>
    public sealed class ProjectorDimmerWindow : Window
    {
        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int index, int newStyle);

        private readonly WinForms.Screen _screen;
        private int _brightnessPercent = 100;

        public ProjectorDimmerWindow(WinForms.Screen screen)
        {
            _screen = screen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            AllowsTransparency = true;
            Background = Brushes.Black;
            IsHitTestVisible = false;
            Focusable = false;

            SourceInitialized += delegate
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int style = GetWindowLong(hwnd, GwlExStyle);
                SetWindowLong(hwnd, GwlExStyle, style | WsExTransparent |
                    WsExToolWindow | WsExNoActivate);
                ScreenUtil.FillScreen(this, _screen);
            };
        }

        public int BrightnessPercent
        {
            get { return _brightnessPercent; }
        }

        public void SetBrightness(int percent)
        {
            _brightnessPercent = AppConfig.NormalizePercent(percent);
            Opacity = (100 - _brightnessPercent) / 100.0;

            // At full brightness, remove the layered window from DWM entirely.
            if (_brightnessPercent >= 100)
            {
                if (IsVisible) Hide();
                return;
            }

            if (!IsVisible) Show();
        }
    }
}
