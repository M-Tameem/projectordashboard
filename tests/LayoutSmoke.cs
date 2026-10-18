using System;
using System.Reflection;
using System.IO;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProjectorDash;

// Windows-only: measure real WPF templates without launching apps or injecting input.
internal static class LayoutSmoke
{
    [STAThread]
    public static int Main()
    {
        new Application();
        if (Marshal.SizeOf(typeof(ScreenUtil).GetNestedType("NativeInput", BindingFlags.NonPublic)) != 28)
            throw new Exception("Windows x86 INPUT struct must be 28 bytes");
        AppConfig config = new AppConfig();
        for (int i = 0; i < 8; i++) config.Shortcuts.Add(new ShortcutItem("Shortcut " + i, "notepad.exe"));
        ControllerWindow window = new ControllerWindow(config);
        Grid root = (Grid)window.Content;
        root.Background = Ui.BgGradient();
        typeof(ControllerWindow).GetMethod("RebuildTiles", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(window, null);
        typeof(ControllerWindow).GetMethod("TickClock", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, null);
        typeof(ControllerWindow).GetMethod("UpdateAlarmUi", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, null);
        foreach (Size size in new Size[] { new Size(1366, 768), new Size(1024, 768), new Size(1093, 614) })
        {
            root.ColumnDefinitions[1].Width = new GridLength(Math.Min(420, (size.Width - 44) * 0.42));
            root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
            CheckControls(root, root, false);
            SaveImage(root, "dashboard-" + size.Width + "x" + size.Height);
            Console.WriteLine("Dashboard layout: " + size);
        }
        // Force all shared button templates and both remote pages through layout.
        RemoteControlWindow remote = new RemoteControlWindow(System.Windows.Forms.Screen.PrimaryScreen,
            delegate(int key) { }, delegate(int delta) { }, delegate { }, delegate { },
            delegate { return null; }, delegate { return IntPtr.Zero; }, true);
        FrameworkElement remoteRoot = (FrameworkElement)remote.Content;
        remoteRoot.Measure(new Size(760, 520)); remoteRoot.Arrange(new Rect(0, 0, 760, 520));
        remoteRoot.UpdateLayout(); CheckControls(remoteRoot, remoteRoot, false);
        SaveImage(remoteRoot, "remote");
        Console.WriteLine("WPF layout smoke checks passed.");
        return 0;
    }

    private static void SaveImage(FrameworkElement root, string name)
    {
        Directory.CreateDirectory(".visual-check");
        RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),
            (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream file = File.Create(Path.Combine(".visual-check", name + ".png"))) encoder.Save(file);
    }

    private static void CheckControls(DependencyObject node, FrameworkElement root, bool inScroll)
    {
        UIElement element = node as UIElement;
        if (element != null && element.Visibility != Visibility.Visible) return;
        inScroll |= node is ScrollViewer;
        FrameworkElement control = node as Button;
        if (control != null && !inScroll)
        {
            Point top = control.TranslatePoint(new Point(0, 0), root);
            if (control.ActualWidth < 40 || control.ActualHeight < 28 ||
                top.X < -1 || top.Y < -1 ||
                top.X + control.ActualWidth > root.ActualWidth + 1 ||
                top.Y + control.ActualHeight > root.ActualHeight + 1)
                throw new Exception("Control out of bounds: " + ((Button)control).Content);
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            CheckControls(VisualTreeHelper.GetChild(node, i), root, inScroll);
    }
}
