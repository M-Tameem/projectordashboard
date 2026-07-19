using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace ProjectorDash
{
    /// <summary>
    /// What the projector shows while nothing is playing: a very dark ambient
    /// gradient with a large clock, or nothing at all. Deliberately static —
    /// no animation, so the Atom's GPU and CPU stay idle.
    /// </summary>
    public class ProjectorWindow : Window
    {
        private readonly TextBlock _clock;
        private readonly TextBlock _date;
        private readonly StackPanel _stack;

        public ProjectorWindow(WinForms.Screen screen, string mode)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Cursor = System.Windows.Input.Cursors.None;

            // Near-black radial wash with a deep-purple heart; projectors bloom
            // pure black poorly, and the faint tinted center reads as
            // intentional rather than "no signal".
            RadialGradientBrush bg = new RadialGradientBrush();
            bg.Center = new Point(0.5, 0.55);
            bg.GradientOrigin = new Point(0.5, 0.55);
            bg.RadiusX = 0.9;
            bg.RadiusY = 0.9;
            bg.GradientStops.Add(new GradientStop(
                (Color)ColorConverter.ConvertFromString("#1A1233"), 0.0));
            bg.GradientStops.Add(new GradientStop(
                (Color)ColorConverter.ConvertFromString("#06050C"), 1.0));
            bg.Freeze();
            Background = bg;

            _stack = new StackPanel();
            _stack.HorizontalAlignment = HorizontalAlignment.Center;
            _stack.VerticalAlignment = VerticalAlignment.Center;

            _clock = new TextBlock();
            _clock.FontFamily = new FontFamily(Ui.FontLight);
            _clock.FontSize = 220;
            _clock.Foreground = Ui.Hex("#B9AC99");
            _clock.TextAlignment = TextAlignment.Center;
            _stack.Children.Add(_clock);

            _date = new TextBlock();
            _date.FontFamily = new FontFamily(Ui.FontUi);
            _date.FontSize = 34;
            _date.Foreground = Ui.Hex("#5E564C");
            _date.TextAlignment = TextAlignment.Center;
            _date.Margin = new Thickness(0, 6, 0, 0);
            _stack.Children.Add(_date);

            Content = _stack;
            SetMode(mode);

            SourceInitialized += delegate { ScreenUtil.FillScreen(this, screen); };
            UpdateTime(DateTime.Now);
        }

        public void SetMode(string mode)
        {
            _stack.Visibility = (mode == "blank")
                ? Visibility.Collapsed : Visibility.Visible;
        }

        public void UpdateTime(DateTime now)
        {
            if (_stack.Visibility != Visibility.Visible) return;
            _clock.Text = now.ToString("h:mm");
            _date.Text = now.ToString("dddd, MMMM d");
        }
    }
}
