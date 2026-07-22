using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace ProjectorDash
{
    /// <summary>
    /// Shown on first launch (or when a saved display disappears).
    /// Flashes a big number on every attached screen, then lets the user tap
    /// which one is the tablet and which is the projector. Saved permanently.
    /// </summary>
    public class DisplayPickerWindow : Window
    {
        private readonly AppConfig _cfg;
        private readonly List<Row> _rows = new List<Row>();
        private Button _okButton;

        private class Row
        {
            public WinForms.Screen Screen;
            public Button TabletBtn;
            public Button ProjectorBtn;
            public string Role; // "", "tablet", "projector"
        }

        public DisplayPickerWindow(AppConfig cfg)
        {
            _cfg = cfg;

            Title = "Choose displays";
            Background = Ui.Bg;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;

            StackPanel root = new StackPanel();
            root.Margin = new Thickness(36);
            root.MinWidth = 560;

            TextBlock h = Ui.Label("Set up your displays", 30, Ui.Text);
            h.FontWeight = FontWeights.SemiBold;
            root.Children.Add(h);

            TextBlock sub = Ui.Label(
                "Numbers are showing on each screen. Tap the role for each one.",
                16, Ui.TextDim);
            sub.Margin = new Thickness(0, 6, 0, 20);
            root.Children.Add(sub);

            WinForms.Screen[] screens = ScreenUtil.AllScreens();
            for (int i = 0; i < screens.Length; i++)
            {
                root.Children.Add(BuildRow(i + 1, screens[i]));
            }

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Margin = new Thickness(0, 22, 0, 0);

            Button identify = Ui.Btn("Show numbers again", 16, Ui.Panel, Ui.Text,
                delegate { FlashIdentifiers(); });
            identify.Margin = new Thickness(0, 0, 12, 0);
            actions.Children.Add(identify);

            _okButton = Ui.Btn("Save and start", 16, Ui.Accent, Ui.Ink, OnOk);
            _okButton.IsEnabled = false;
            actions.Children.Add(_okButton);

            root.Children.Add(actions);
            Content = root;

            Loaded += delegate { FlashIdentifiers(); };
        }

        private UIElement BuildRow(int number, WinForms.Screen screen)
        {
            Row row = new Row();
            row.Screen = screen;
            row.Role = "";

            Border card = new Border();
            card.Background = Ui.Panel;
            card.BorderBrush = Ui.Line;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(12);
            card.Padding = new Thickness(18);
            card.Margin = new Thickness(0, 0, 0, 12);

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel info = new StackPanel();
            TextBlock name = Ui.Label(
                string.Format("Display {0}{1}", number, screen.Primary ? "  (Windows primary)" : ""),
                20, Ui.Text);
            name.FontWeight = FontWeights.SemiBold;
            info.Children.Add(name);
            TextBlock res = Ui.Label(
                string.Format("{0} × {1}   {2}", screen.Bounds.Width, screen.Bounds.Height, screen.DeviceName),
                14, Ui.TextDim);
            res.Margin = new Thickness(0, 4, 0, 0);
            info.Children.Add(res);
            Grid.SetColumn(info, 0);
            g.Children.Add(info);

            row.TabletBtn = Ui.Btn("Tablet", 16, Ui.PanelHi, Ui.Text,
                delegate { SetRole(row, "tablet"); });
            row.TabletBtn.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(row.TabletBtn, 1);
            g.Children.Add(row.TabletBtn);

            row.ProjectorBtn = Ui.Btn("Projector", 16, Ui.PanelHi, Ui.Text,
                delegate { SetRole(row, "projector"); });
            row.ProjectorBtn.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(row.ProjectorBtn, 2);
            g.Children.Add(row.ProjectorBtn);

            card.Child = g;
            _rows.Add(row);
            return card;
        }

        private void SetRole(Row target, string role)
        {
            // A role can only belong to one screen; taking it clears it elsewhere.
            foreach (Row r in _rows)
            {
                if (r != target && r.Role == role) r.Role = "";
            }
            target.Role = (target.Role == role) ? "" : role;
            foreach (Row r in _rows) Restyle(r);

            bool hasTablet = false;
            foreach (Row r in _rows) if (r.Role == "tablet") hasTablet = true;
            _okButton.IsEnabled = hasTablet;
        }

        private void Restyle(Row r)
        {
            r.TabletBtn.Background = (r.Role == "tablet") ? Ui.Accent : Ui.PanelHi;
                r.TabletBtn.Foreground = (r.Role == "tablet") ? Ui.Ink : Ui.Text;
            r.ProjectorBtn.Background = (r.Role == "projector") ? Ui.Accent : Ui.PanelHi;
                r.ProjectorBtn.Foreground = (r.Role == "projector") ? Ui.Ink : Ui.Text;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            _cfg.TabletDevice = "";
            _cfg.ProjectorDevice = "";
            foreach (Row r in _rows)
            {
                if (r.Role == "tablet") _cfg.TabletDevice = r.Screen.DeviceName;
                if (r.Role == "projector") _cfg.ProjectorDevice = r.Screen.DeviceName;
            }
            DialogResult = true;
            Close();
        }

        /// <summary>Big number in the corner of every screen for three seconds.</summary>
        private void FlashIdentifiers()
        {
            WinForms.Screen[] screens = ScreenUtil.AllScreens();
            for (int i = 0; i < screens.Length; i++)
            {
                Window w = new Window();
                w.WindowStyle = WindowStyle.None;
                w.ResizeMode = ResizeMode.NoResize;
                w.AllowsTransparency = true;
                w.Background = Brushes.Transparent;
                w.Topmost = true;
                w.ShowInTaskbar = false;
                w.ShowActivated = false;

                Border badge = new Border();
            badge.Background = Ui.ReturnShade;
                badge.BorderBrush = Ui.Accent;
                badge.BorderThickness = new Thickness(2);
                badge.CornerRadius = new CornerRadius(20);
                badge.Padding = new Thickness(60, 20, 60, 20);
                badge.HorizontalAlignment = HorizontalAlignment.Center;
                badge.VerticalAlignment = VerticalAlignment.Center;

                TextBlock num = Ui.Label((i + 1).ToString(), 160, Ui.Accent);
                num.FontFamily = new FontFamily(Ui.FontLight);
                badge.Child = num;
                w.Content = badge;

                WinForms.Screen s = screens[i];
                w.SourceInitialized += MakeFillHandler(w, s);
                w.Show();

                DispatcherTimer t = new DispatcherTimer();
                t.Interval = TimeSpan.FromSeconds(3);
                Window closeTarget = w;
                t.Tick += delegate
                {
                    t.Stop();
                    closeTarget.Close();
                };
                t.Start();
            }
        }

        private static EventHandler MakeFillHandler(Window w, WinForms.Screen s)
        {
            return delegate { ScreenUtil.FillScreen(w, s); };
        }
    }
}
