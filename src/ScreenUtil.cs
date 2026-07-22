using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace ProjectorDash
{
    public static class ScreenUtil
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int index, int newStyle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint message,
            IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint threadId,
            ref GuiThreadInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount,
            NativeInput[] inputs, int inputSize);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetProp(IntPtr hWnd, string name, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetProp(IntPtr hWnd, string name);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr RemoveProp(IntPtr hWnd, string name);

        [DllImport("user32.dll", EntryPoint = "LockWorkStation")]
        private static extern bool LockWorkStationNative();

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            public int cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public NativeRect rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInput
        {
            public uint type;
            public InputUnion data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MouseInput mouse;
            [FieldOffset(0)] public KeyboardInput keyboard;
            [FieldOffset(0)] public HardwareInput hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr extraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort virtualKey;
            public ushort scanCode;
            public uint flags;
            public uint time;
            public IntPtr extraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HardwareInput
        {
            public uint message;
            public ushort lowParam;
            public ushort highParam;
        }

        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int SW_RESTORE = 9;
        private const int SW_MAXIMIZE = 3;
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const uint WM_CLOSE = 0x0010;
        private const uint WM_SYSCOMMAND = 0x0112;
        private const int SC_MONITORPOWER = 0xF170;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private static readonly IntPtr HwndBroadcast = new IntPtr(0xFFFF);
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private const string ShortcutProperty = "ProjectorDash.Shortcut";

        public static int ScreenCount()
        {
            return Screen.AllScreens.Length;
        }

        public static Screen[] AllScreens()
        {
            return Screen.AllScreens;
        }

        public static bool DeviceExists(string deviceName)
        {
            foreach (Screen s in Screen.AllScreens)
            {
                if (string.Equals(s.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static Screen FindByDevice(string deviceName, Screen fallback)
        {
            foreach (Screen s in Screen.AllScreens)
            {
                if (string.Equals(s.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return s;
                }
            }
            return fallback;
        }

        /// <summary>
        /// Makes a borderless WPF window exactly fill the given screen.
        /// Works in raw device pixels via SetWindowPos, so it is correct on
        /// mixed-DPI multi-monitor setups where WPF's DIP coordinates lie.
        /// Call from (or after) the window's SourceInitialized event.
        /// </summary>
        public static void FillScreen(Window window, Screen screen)
        {
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            System.Drawing.Rectangle b = screen.Bounds;
            SetWindowPos(hwnd, IntPtr.Zero, b.X, b.Y, b.Width, b.Height,
                SWP_NOZORDER | SWP_SHOWWINDOW);
        }

        /// <summary>
        /// DPI scale factor (device pixels per DIP) for the window's monitor.
        /// Used to convert the reserved-area pixel size from config into DIPs.
        /// </summary>
        public static double DpiScale(Window window)
        {
            PresentationSource src = PresentationSource.FromVisual(window);
            if (src != null && src.CompositionTarget != null)
            {
                return src.CompositionTarget.TransformToDevice.M11;
            }
            return 1.0;
        }

        /// <summary>Places the tablet-mode return control above kiosk windows.</summary>
        public static void PlaceTopRightOverlay(Window window, Screen screen,
            int width, int height)
        {
            if (window == null || screen == null) return;
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            System.Drawing.Rectangle work = screen.WorkingArea;
            int x = work.Right - width - 12;
            int y = work.Top + 12;
            SetWindowPos(hwnd, HwndTopmost, x, y, width, height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        /// <summary>Centers the shared touch remote below the return strip.</summary>
        public static void PlaceRemoteOverlay(Window window, Screen screen,
            int requestedWidth, int requestedHeight)
        {
            if (window == null || screen == null) return;
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            System.Drawing.Rectangle work = screen.WorkingArea;
            int width = Math.Min(requestedWidth, Math.Max(320, work.Width - 24));
            int height = Math.Min(requestedHeight, Math.Max(240, work.Height - 24));
            int x = work.Left + Math.Max(12, (work.Width - width) / 2);
            int y = work.Top + Math.Min(104, Math.Max(12, work.Height - height - 12));
            SetWindowPos(hwnd, HwndTopmost, x, y, width, height,
                SWP_SHOWWINDOW);
        }

        /// <summary>
        /// Takes a snapshot before Process.Start so the newly-created window
        /// can be distinguished even when Chromium hands it to an existing
        /// browser process.
        /// </summary>
        public static HashSet<IntPtr> SnapshotTopLevelWindows()
        {
            HashSet<IntPtr> handles = new HashSet<IntPtr>();
            EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
            {
                handles.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return handles;
        }

        /// <summary>
        /// Finds the best top-level window belonging to a launch. New windows
        /// are preferred; process id/name matching handles normal executables,
        /// while allowAnyNew handles ShellExecute and Chromium process reuse.
        /// </summary>
        public static IntPtr FindLaunchWindow(HashSet<IntPtr> before,
            int launchedProcessId, string expectedExePath,
            bool allowExistingMatch, bool allowAnyNew)
        {
            string expectedName = "";
            try
            {
                expectedName = System.IO.Path.GetFileNameWithoutExtension(expectedExePath ?? "");
            }
            catch { }

            List<IntPtr> suitable = EnumerateSuitableWindows();
            IntPtr firstNew = IntPtr.Zero;
            IntPtr firstMatch = IntPtr.Zero;

            foreach (IntPtr hwnd in suitable)
            {
                bool isNew = before == null || !before.Contains(hwnd);
                bool matches = WindowMatchesProcess(hwnd, launchedProcessId, expectedName);
                if (isNew && matches) return hwnd;
                if (isNew && firstNew == IntPtr.Zero) firstNew = hwnd;
                if (matches && firstMatch == IntPtr.Zero) firstMatch = hwnd;
            }

            if (allowAnyNew && firstNew != IntPtr.Zero) return firstNew;
            if (allowExistingMatch) return firstMatch;
            return IntPtr.Zero;
        }

        /// <summary>
        /// Moves an external application's window to the selected projector.
        /// Fullscreen is true borderless fill; maximized retains the app frame.
        /// </summary>
        public static void PlaceExternalWindow(IntPtr hwnd, Screen screen, bool fullscreen)
        {
            if (hwnd == IntPtr.Zero || screen == null || !IsWindow(hwnd)) return;

            System.Drawing.Rectangle bounds = fullscreen ? screen.Bounds : screen.WorkingArea;

            if (fullscreen)
            {
                int style = GetWindowLong(hwnd, GWL_STYLE);
                if ((style & (WS_CAPTION | WS_THICKFRAME)) != 0 ||
                    (style & WS_POPUP) == 0)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                }
                style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX |
                    WS_MAXIMIZEBOX | WS_SYSMENU);
                style |= WS_POPUP;
                SetWindowLong(hwnd, GWL_STYLE, style);
                SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y,
                    bounds.Width, bounds.Height,
                    SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            }
            else
            {
                if (IsZoomed(hwnd) && WindowIsOnScreen(hwnd, screen)) return;
                ShowWindow(hwnd, SW_RESTORE);
                SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y,
                    Math.Max(320, bounds.Width - 1), Math.Max(240, bounds.Height - 1),
                    SWP_NOACTIVATE | SWP_SHOWWINDOW);
                ShowWindow(hwnd, SW_MAXIMIZE);
            }
        }

        /// <summary>
        /// Restores, places, and activates a tracked shortcut window. This is
        /// the reuse path for an already-open tile, so tapping it never starts
        /// a duplicate browser window or app instance.
        /// </summary>
        public static bool BringAppWindowToFront(IntPtr hwnd, Screen screen,
            bool fullscreen)
        {
            if (hwnd == IntPtr.Zero || screen == null || !IsWindow(hwnd)) return false;
            ShowWindow(hwnd, SW_RESTORE);
            PlaceExternalWindow(hwnd, screen, fullscreen);
            BringWindowToTop(hwnd);
            return SetForegroundWindow(hwnd);
        }

        public static void TagShortcutWindow(IntPtr hwnd, int shortcutToken)
        {
            if (hwnd == IntPtr.Zero || shortcutToken == 0 || !IsWindow(hwnd)) return;
            SetProp(hwnd, ShortcutProperty, new IntPtr(shortcutToken));
        }

        public static IntPtr FindTaggedShortcutWindow(int shortcutToken)
        {
            if (shortcutToken == 0) return IntPtr.Zero;
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
            {
                if (GetProp(hwnd, ShortcutProperty).ToInt64() == shortcutToken)
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static void UntagShortcutWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
            RemoveProp(hwnd, ShortcutProperty);
        }

        /// <summary>Sends one ordinary virtual key to the selected app.</summary>
        public static bool SendVirtualKey(IntPtr hwnd, int virtualKey)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd) ||
                virtualKey < 1 || virtualKey > 255) return false;
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            // The remote click temporarily owns foreground input. A tiny wait
            // gives Windows time to return focus to the selected app before
            // emitting the key pair.
            System.Threading.Thread.Sleep(35);
            NativeInput[] inputs = new NativeInput[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].data.keyboard.virtualKey = (ushort)virtualKey;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].data.keyboard.virtualKey = (ushort)virtualKey;
            inputs[1].data.keyboard.flags = KEYEVENTF_KEYUP;
            return SendInput(2, inputs,
                Marshal.SizeOf(typeof(NativeInput))) == 2;
        }

        /// <summary>
        /// Returns a stable identifier while an ordinary app on the selected
        /// screen owns a native text caret. Chromium exposes its focused edit
        /// control through this Windows thread state, allowing the dashboard
        /// to summon OSK without polling page contents or injecting scripts.
        /// </summary>
        public static string ForegroundTextInputKey(Screen screen,
            int excludedProcessId)
        {
            if (screen == null) return "";
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || !IsWindow(foreground) ||
                !WindowIsOnScreen(foreground, screen)) return "";

            uint processId;
            uint threadId = GetWindowThreadProcessId(foreground, out processId);
            if (threadId == 0 || (int)processId == excludedProcessId) return "";
            GuiThreadInfo info = new GuiThreadInfo();
            info.cbSize = Marshal.SizeOf(typeof(GuiThreadInfo));
            if (!GetGUIThreadInfo(threadId, ref info) ||
                info.hwndCaret == IntPtr.Zero) return "";
            IntPtr focus = info.hwndFocus == IntPtr.Zero
                ? foreground : info.hwndFocus;
            return processId.ToString() + ":" + focus.ToInt64().ToString() +
                ":" + info.hwndCaret.ToInt64().ToString();
        }

        /// <summary>
        /// Positions the classic on-screen keyboard in the lower-left portion
        /// of the tablet, keeping the configured TouchMousePointer rectangle
        /// clear in the lower-right.
        /// </summary>
        public static void PlaceKeyboardWindow(IntPtr hwnd, Screen tablet,
            int reservedWidth, int reservedHeight)
        {
            if (hwnd == IntPtr.Zero || tablet == null || !IsWindow(hwnd)) return;
            System.Drawing.Rectangle work = tablet.WorkingArea;
            int width = Math.Max(640, work.Width - Math.Max(0, reservedWidth));
            if (width > work.Width) width = work.Width;
            int height = Math.Max(220, Math.Min(360, reservedHeight));
            if (height > work.Height) height = work.Height;
            int y = work.Bottom - height;
            ShowWindow(hwnd, SW_RESTORE);
            SetWindowPos(hwnd, IntPtr.Zero, work.Left, y, width, height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public static bool WindowIsOnScreen(IntPtr hwnd, Screen screen)
        {
            if (hwnd == IntPtr.Zero || screen == null || !IsWindow(hwnd)) return false;
            NativeRect rect;
            if (!GetWindowRect(hwnd, out rect)) return false;
            int cx = rect.Left + ((rect.Right - rect.Left) / 2);
            int cy = rect.Top + ((rect.Bottom - rect.Top) / 2);
            return screen.Bounds.Contains(cx, cy);
        }

        /// <summary>Returns the topmost ordinary app window centered on a screen.</summary>
        public static IntPtr FindTopAppWindowOnScreen(Screen screen, int excludedProcessId)
        {
            if (screen == null) return IntPtr.Zero;
            foreach (IntPtr hwnd in EnumerateSuitableWindows())
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if ((int)pid == excludedProcessId) continue;
                if (WindowIsOnScreen(hwnd, screen)) return hwnd;
            }
            return IntPtr.Zero;
        }

        public static bool RequestCloseWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
            return PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        public static bool LockTablet()
        {
            try { return LockWorkStationNative(); }
            catch { return false; }
        }

        /// <summary>
        /// Requests display power-off for every attached monitor. A projector
        /// may remain physically powered but should lose the Windows signal.
        /// Normal mouse, keyboard, or touch input wakes the displays again.
        /// </summary>
        public static bool TurnOffAllDisplays()
        {
            try
            {
                return PostMessage(HwndBroadcast, WM_SYSCOMMAND,
                    new IntPtr(SC_MONITORPOWER), new IntPtr(2));
            }
            catch { return false; }
        }

        /// <summary>
        /// Hides projector apps without minimizing or closing them. Their
        /// handles are returned in top-to-bottom order for a later wake.
        /// </summary>
        public static List<IntPtr> HideAppWindowsOnScreen(Screen screen,
            int excludedProcessId)
        {
            List<IntPtr> hidden = new List<IntPtr>();
            if (screen == null) return hidden;
            foreach (IntPtr hwnd in EnumerateSuitableWindows())
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if ((int)pid == excludedProcessId) continue;
                if (!WindowIsOnScreen(hwnd, screen)) continue;
                if (ShowWindow(hwnd, SW_HIDE)) hidden.Add(hwnd);
            }
            return hidden;
        }

        public static void RestoreAppWindows(List<IntPtr> windows, Screen screen,
            bool fullscreen)
        {
            if (windows == null) return;
            // Restore bottom-first so the window that was originally topmost
            // ends up topmost again.
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                IntPtr hwnd = windows[i];
                if (!IsWindow(hwnd)) continue;
                ShowWindow(hwnd, SW_SHOW);
                if (screen != null) PlaceExternalWindow(hwnd, screen, fullscreen);
            }
        }

        private static List<IntPtr> EnumerateSuitableWindows()
        {
            List<IntPtr> windows = new List<IntPtr>();
            EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
            {
                if (IsSuitableAppWindow(hwnd)) windows.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private static bool IsSuitableAppWindow(IntPtr hwnd)
        {
            if (!IsWindowVisible(hwnd)) return false;
            NativeRect rect;
            if (!GetWindowRect(hwnd, out rect)) return false;
            if (rect.Right - rect.Left < 160 || rect.Bottom - rect.Top < 100) return false;

            StringBuilder cls = new StringBuilder(128);
            GetClassName(hwnd, cls, cls.Capacity);
            string name = cls.ToString();
            if (name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd" ||
                name == "Progman" || name == "WorkerW" || name == "DV2ControlHost")
                return false;
            return true;
        }

        private static bool WindowMatchesProcess(IntPtr hwnd, int processId, string expectedName)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (processId > 0 && (int)pid == processId) return true;
            if (string.IsNullOrEmpty(expectedName) || pid == 0) return false;
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                {
                    return string.Equals(p.ProcessName, expectedName,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }
    }
}
