using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WinForms = System.Windows.Forms;

namespace ProjectorDash
{
    /// <summary>
    /// Minimal idle surface for the projector. It remains completely static
    /// between updates: the clock changes once a minute visually, weather
    /// roughly every fifteen minutes, and sky data about once a minute.
    /// </summary>
    public class ProjectorWindow : Window
    {
        private readonly Grid _ambient;
        private readonly TextBlock _clock;
        private readonly TextBlock _date;
        private readonly TextBlock _temperature;
        private readonly TextBlock _condition;
        private readonly TextBlock _weatherDetail;
        private readonly TextBlock _sky;

        public ProjectorWindow(WinForms.Screen screen, string mode)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Cursor = System.Windows.Input.Cursors.None;

            RadialGradientBrush bg = new RadialGradientBrush();
            bg.Center = new Point(0.5, 0.48);
            bg.GradientOrigin = new Point(0.5, 0.48);
            bg.RadiusX = 0.92;
            bg.RadiusY = 0.92;
            bg.GradientStops.Add(new GradientStop(
                (Color)ColorConverter.ConvertFromString("#0B2228"), 0.0));
            bg.GradientStops.Add(new GradientStop(
                (Color)ColorConverter.ConvertFromString("#03070A"), 1.0));
            bg.Freeze();
            Background = bg;

            _ambient = new Grid { Margin = new Thickness(60, 40, 60, 42) };
            _ambient.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _ambient.RowDefinitions.Add(new RowDefinition
                { Height = new GridLength(1, GridUnitType.Star) });
            _ambient.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock mark = Ui.Label("PROJECTOR  /  AMBIENT", 15, Ui.Accent);
            mark.FontWeight = FontWeights.SemiBold;
            _ambient.Children.Add(mark);

            Grid center = new Grid();
            center.HorizontalAlignment = HorizontalAlignment.Stretch;
            center.VerticalAlignment = VerticalAlignment.Center;
            center.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });

            StackPanel time = new StackPanel { Margin = new Thickness(0, 0, 46, 0) };
            _clock = new TextBlock();
            _clock.FontFamily = new FontFamily(Ui.FontLight);
            _clock.FontSize = 190;
            _clock.Foreground = Ui.Text;
            _clock.TextAlignment = TextAlignment.Center;
            time.Children.Add(_clock);
            _date = Ui.Label("", 30, Ui.TextDim);
            _date.TextAlignment = TextAlignment.Center;
            _date.Margin = new Thickness(0, -8, 0, 0);
            time.Children.Add(_date);
            center.Children.Add(time);

            Rectangle divider = new Rectangle();
            divider.Width = 1;
            divider.Height = 190;
            divider.Fill = Ui.Line;
            divider.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(divider, 1);
            center.Children.Add(divider);

            StackPanel weather = new StackPanel
            {
                Margin = new Thickness(46, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock weatherMark = Ui.Label("WEATHER NOW", 14, Ui.Accent);
            weatherMark.FontWeight = FontWeights.SemiBold;
            weather.Children.Add(weatherMark);
            _temperature = Ui.Label("--", 76, Ui.Text);
            _temperature.FontFamily = new FontFamily(Ui.FontLight);
            _temperature.Margin = new Thickness(0, 4, 0, 0);
            weather.Children.Add(_temperature);
            _condition = Ui.Label("Set location on the tablet", 25, Ui.Text);
            weather.Children.Add(_condition);
            _weatherDetail = Ui.Label("", 17, Ui.TextDim);
            _weatherDetail.Margin = new Thickness(0, 10, 0, 0);
            _weatherDetail.TextWrapping = TextWrapping.Wrap;
            weather.Children.Add(_weatherDetail);
            Grid.SetColumn(weather, 2);
            center.Children.Add(weather);
            Grid.SetRow(center, 1);
            _ambient.Children.Add(center);

            Border skyBar = new Border();
            skyBar.Background = Ui.Hex("#091318");
            skyBar.BorderBrush = Ui.Line;
            skyBar.BorderThickness = new Thickness(1);
            skyBar.CornerRadius = new CornerRadius(8);
            skyBar.Padding = new Thickness(22, 14, 22, 14);
            Grid skyGrid = new Grid();
            skyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            skyGrid.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock skyMark = Ui.Label("SKY NOW", 13, Ui.Accent);
            skyMark.FontWeight = FontWeights.SemiBold;
            skyMark.VerticalAlignment = VerticalAlignment.Center;
            skyGrid.Children.Add(skyMark);
            _sky = Ui.Label("Set a location to show aircraft, ISS, and planets", 17, Ui.TextDim);
            _sky.Margin = new Thickness(28, 0, 0, 0);
            _sky.TextAlignment = TextAlignment.Right;
            _sky.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(_sky, 1);
            skyGrid.Children.Add(_sky);
            skyBar.Child = skyGrid;
            Grid.SetRow(skyBar, 2);
            _ambient.Children.Add(skyBar);

            Content = _ambient;
            SetMode(mode);
            SourceInitialized += delegate { ScreenUtil.FillScreen(this, screen); };
            UpdateTime(DateTime.Now);
        }

        public void SetMode(string mode)
        {
            _ambient.Visibility = mode == "blank"
                ? Visibility.Collapsed : Visibility.Visible;
        }

        public void UpdateTime(DateTime now)
        {
            if (_ambient.Visibility != Visibility.Visible) return;
            _clock.Text = now.ToString("h:mm");
            _date.Text = now.ToString("dddd, MMMM d");
        }

        public void UpdateWeather(string temperature, string condition, string detail)
        {
            _temperature.Text = temperature;
            _condition.Text = condition;
            _weatherDetail.Text = detail;
        }

        public void UpdateSky(string summary)
        {
            _sky.Text = summary;
        }
    }
}
