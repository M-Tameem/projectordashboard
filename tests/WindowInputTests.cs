using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ProjectorDash;
using Forms = System.Windows.Forms;

// Windows integration: real external HWNDs, WPF routed touch, and SendInput.
// The synthetic contact exercises WPF routing, not the hardware touch driver.
internal static class WindowInputTests
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    private static int passed;
    private static void Check(bool ok, string message)
    {
        if (!ok)
        {
            System.Drawing.Rectangle bounds = Forms.SystemInformation.VirtualScreen;
            using (System.Drawing.Bitmap image = new System.Drawing.Bitmap(bounds.Width, bounds.Height))
            using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(image))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, image.Size);
                Directory.CreateDirectory(".visual-check");
                image.Save(".visual-check/input-failure.png");
            }
            throw new Exception(message);
        }
        passed++; Console.WriteLine("PASS: " + message);
    }
    private static void Pump()
    {
        for (int i = 0; i < 10; i++) { Forms.Application.DoEvents(); Thread.Sleep(10); }
    }
    private static Process StartFixture(string path)
    {
        Process p = Process.Start(new ProcessStartInfo(path, "fixture") { UseShellExecute = false });
        for (int i = 0; i < 50 && p.MainWindowHandle == IntPtr.Zero; i++) { Thread.Sleep(100); p.Refresh(); }
        if (p.MainWindowHandle == IntPtr.Zero) { if (!p.HasExited) p.Kill(); p.Dispose(); throw new Exception("Fixture has no HWND"); }
        return p;
    }
    private sealed class Fixture : Forms.Form
    {
        private int clicks, rightClicks, wheels, releases;
        protected override void WndProc(ref Forms.Message m)
        {
            if (m.Msg == 0x0201) clicks++;
            if (m.Msg == 0x0204) rightClicks++;
            if (m.Msg == 0x020A) wheels++;
            if (m.Msg == 0x0202) releases++;
            if (m.Msg >= 0x8001 && m.Msg <= 0x8004)
            {
                m.Result = new IntPtr(m.Msg == 0x8001 ? clicks : m.Msg == 0x8002 ? rightClicks : m.Msg == 0x8003 ? wheels : releases);
                return;
            }
            base.WndProc(ref m);
        }
    }
    private sealed class Contact : TouchDevice
    {
        private readonly FrameworkElement surface;
        private Point point;
        private TouchAction action;
        public Contact(int id, FrameworkElement element) : base(id) { surface = element; }
        public override TouchPoint GetTouchPoint(IInputElement relativeTo)
        {
            UIElement relative = relativeTo as UIElement ?? (UIElement)ActiveSource.RootVisual;
            Point p = surface.TranslatePoint(point, relative);
            return new TouchPoint(this, p, new Rect(p, new Size(1, 1)), action);
        }
        public override TouchPointCollection GetIntermediateTouchPoints(IInputElement relativeTo)
        { return new TouchPointCollection { GetTouchPoint(relativeTo) }; }
        public void Down(double x, double y)
        {
            point = new Point(x, y); action = TouchAction.Down;
            SetActiveSource(PresentationSource.FromVisual(surface)); Activate(); ReportDown();
        }
        public void Move(double x, double y) { point = new Point(x, y); action = TouchAction.Move; ReportMove(); }
        public void Up() { action = TouchAction.Up; ReportUp(); Deactivate(); }
    }
    private static int Count(IntPtr hwnd, int which) { return SendMessage(hwnd, 0x8000 + which, IntPtr.Zero, IntPtr.Zero).ToInt32(); }
    private static object Field(object obj, string name) { return obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj); }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            Forms.Application.Run(new Fixture { Text = "External input fixture", Width = 800, Height = 600 });
            return 0;
        }
        new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Process target = null, unrelated = null;
        Window own = null, padWindow = null;
        string copy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UnrelatedWindowFixture.exe");
        try
        {
            HashSet<IntPtr> before = ScreenUtil.SnapshotTopLevelWindows();
            own = new Window { Title = "Dashboard remote", Width = 760, Height = 520, Left = 25, Top = 25 };
            own.Show(); Pump();
            IntPtr ownHwnd = new WindowInteropHelper(own).Handle;
            Check(!ScreenUtil.IsExternalAppWindow(ownHwnd), "Dashboard control is never an external app");
            Check(ScreenUtil.FindLaunchWindow(before, 0, "", true, true) == IntPtr.Zero, "Shell launch cannot capture a newly opened remote");
            Check(ScreenUtil.FindLaunchWindow(before, Process.GetCurrentProcess().Id, "", true, true) == IntPtr.Zero, "Even a matching PID cannot select dashboard controls");
            double width = own.ActualWidth, left = own.Left;
            ScreenUtil.PlaceExternalWindow(ownHwnd, Forms.Screen.PrimaryScreen, true); Pump();
            Check(own.ActualWidth == width && own.Left == left, "External placement cannot resize/move the remote");
            ScreenUtil.TagShortcutWindow(ownHwnd, 920015);
            Check(ScreenUtil.FindTaggedShortcutWindow(920015) == IntPtr.Zero, "Remote cannot acquire a shortcut association");
            Check(!ScreenUtil.RequestCloseWindow(ownHwnd), "Remote cannot be closed through app controls");
            Check(!ScreenUtil.FocusInputWindow(ownHwnd), "Remote cannot become its own input target");
            string exe = Assembly.GetExecutingAssembly().Location;
            target = StartFixture(exe);
            IntPtr hwnd = target.MainWindowHandle;
            Check(ScreenUtil.FindLaunchWindow(before, target.Id, exe, false, true) == hwnd, "A new external app is found by PID");
            before = ScreenUtil.SnapshotTopLevelWindows();
            File.Copy(exe, copy, true); unrelated = StartFixture(copy);
            Check(ScreenUtil.FindLaunchWindow(before, 0, "missing-browser.exe", true, true) == IntPtr.Zero, "Known executable launch cannot steal an unrelated new window");
            Check(ScreenUtil.FindLaunchWindow(before, 0, exe, true, true) == hwnd, "Browser process reuse prefers the existing matching app");
            Check(ScreenUtil.FindLaunchWindow(before, 0, "osk.exe", true, true) == IntPtr.Zero, "Keyboard placement cannot steal an unrelated new window");
            List<ScreenUtil.BrowserWindow> browsers = ScreenUtil.FindBrowserWindows(exe);
            Check(browsers.Count == 1 && browsers[0].Handle == hwnd,
                "Browser discovery finds an existing configured app and excludes own/unrelated windows");
            Check(browsers[0].Title == "External input fixture" && string.Equals(browsers[0].ExecutablePath, exe, StringComparison.OrdinalIgnoreCase),
                "Discovered browser includes its actual title and executable path");
            ShowWindow(hwnd, 6); Pump();
            browsers = ScreenUtil.FindBrowserWindows(exe);
            Check(browsers.Count == 1 && browsers[0].Handle == hwnd, "Minimized browser windows remain discoverable");
            Check(ScreenUtil.FindBrowserWindows(copy).Count == 1, "Browser discovery respects a different configured executable");
            unrelated.Kill(); unrelated.WaitForExit(); unrelated.Dispose(); unrelated = null;
            own.Close(); own = null;
            ScreenUtil.PlaceExternalWindow(hwnd, Forms.Screen.PrimaryScreen, true);
            NativeTrackpad pad = new NativeTrackpad(delegate { return Forms.Screen.PrimaryScreen; }, delegate { return hwnd; }, null);
            padWindow = new Window { Content = pad, Width = 390, Height = 420, Left = 0, Top = 0, Topmost = true };
            padWindow.Show(); padWindow.Activate(); Pump();
            IntPtr foreground = GetForegroundWindow();
            Check((bool)typeof(NativeTrackpad).GetMethod("Prepare", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pad, null), "Trackpad connects to the external app");
            Check(GetForegroundWindow() == foreground, "Connecting does not steal focus from touch input");
            Border surface = (Border)Field(pad, "_surface");
            Contact finger = new Contact(1, surface);
            finger.Down(70, 40);
            Check(finger.Captured == surface, "Routed touch captures the trackpad");
            finger.Move(90, 50); Pump();
            System.Drawing.Rectangle bounds = Forms.Screen.PrimaryScreen.Bounds;
            System.Drawing.Point cursor = Forms.Cursor.Position;
            Check(Math.Abs(cursor.X - (bounds.Left + bounds.Width / 2 + 40)) <= 2 &&
                Math.Abs(cursor.Y - (bounds.Top + bounds.Height / 2 + 20)) <= 2, "Routed touch moves the actual Windows pointer");
            finger.Up(); Pump();
            Check(Count(hwnd, 1) == 0, "Moving a finger does not click");
            finger = new Contact(2, surface); finger.Down(70, 40); finger.Up(); Pump();
            for (int i = 0; i < 10 && Count(hwnd, 4) == 0; i++) Pump();
            Check(Count(hwnd, 1) == 1 && Count(hwnd, 4) == 1,
                "A routed tap reaches the external app as one click and release (down=" + Count(hwnd, 1) +
                ", up=" + Count(hwnd, 4) + ", cursor=" + Forms.Cursor.Position +
                ", hit=" + WindowFromPoint(Forms.Cursor.Position) + ", target=" + hwnd +
                ", foreground=" + GetForegroundWindow() + ", status=" + ((TextBlock)Field(pad, "_status")).Text + ")");
            typeof(NativeTrackpad).GetMethod("Click", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pad, new object[] { true }); Pump();
            Check(Count(hwnd, 2) == 1, "Right click reaches the external app");
            TrackpadGesture gesture = (TrackpadGesture)Field(pad, "_gesture");
            gesture.DragNextTouch = true;
            finger = new Contact(3, surface); finger.Down(70, 40); finger.Move(80, 45); pad.Cancel(); Pump();
            Check(Count(hwnd, 1) == 2 && Count(hwnd, 4) == 2, "Cancelling a drag releases the actual mouse button");
            finger.Up();
            // Start in the dashboard, then scroll through actual routed contacts.
            padWindow.Activate(); Pump();
            finger = new Contact(4, surface);
            Contact second = new Contact(5, surface);
            finger.Down(70, 40); second.Down(90, 40);
            finger.Move(70, 76); Pump();
            Check(Count(hwnd, 3) == 1 && gesture.ContactCount == 2,
                "Two-finger touch delivers a native wheel event without losing contacts");
            second.Move(90, 76); Pump();
            Check(Count(hwnd, 3) == 2, "Scrolling continues after focus moves to the app");
            second.Up(); finger.Up(); Pump();
            Check(Count(hwnd, 1) == 2, "Lifting scroll fingers does not click the app");
            Console.WriteLine("Window/input integration: " + passed + " checks passed");
            return 0;
        }
        finally
        {
            if (own != null) own.Close();
            if (padWindow != null) padWindow.Close();
            foreach (Process p in new Process[] { target, unrelated }) if (p != null) { if (!p.HasExited) p.Kill(); p.Dispose(); }
            if (File.Exists(copy)) File.Delete(copy);
        }
    }
}
