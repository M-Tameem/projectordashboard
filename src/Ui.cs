using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace ProjectorDash
{
    /// <summary>
    /// Theme + small factory helpers so every control on the dashboard is
    /// built from the same handful of colors and sizes. Warm, dim palette:
    /// this thing lives next to a bed, so amber accents over cool blue.
    /// </summary>
    public static class Ui
    {
        // Palette
        public static readonly Brush Bg        = Hex("#0B0A12"); // near-black, violet tint
        public static readonly Brush Panel     = Hex("#161320"); // card surface
        public static readonly Brush PanelHi   = Hex("#201B30"); // raised surface
        public static readonly Brush Line      = Hex("#2E2744"); // hairline borders
        public static readonly Brush Text      = Hex("#F2F0F7"); // white
        public static readonly Brush TextDim   = Hex("#8E86A6"); // secondary
        public static readonly Brush Accent    = Hex("#A78BFA"); // soft violet
        public static readonly Brush AccentDim = Hex("#3A2D62"); // deep violet fill
        public static readonly Brush Danger    = Hex("#C96A7A");

        // Shared black -> deep-purple wash used as the controller backdrop.
        public static Brush BgGradient()
        {
            LinearGradientBrush g = new LinearGradientBrush();
            g.StartPoint = new Point(0.1, 0.0);
            g.EndPoint   = new Point(0.9, 1.0);
            g.GradientStops.Add(new GradientStop(
                (Color)ColorConverter.ConvertFromString("#0A0910"), 0.0));
            g.GradientStops.Add(new GradientStop(
                (Color)ColorConverter.ConvertFromString("#120E20"), 0.55));
            g.GradientStops.Add(new GradientStop(
                (Color)ColorConverter.ConvertFromString("#1B1233"), 1.0));
            g.Freeze();
            return g;
        }

        public const string FontUi = "Segoe UI";
        public const string FontLight = "Segoe UI Light";

        public static SolidColorBrush Hex(string hex)
        {
            SolidColorBrush b = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        private static ControlTemplate _flatTemplate;

        /// <summary>
        /// Flat button template: rounded border, no Aero chrome, dims while
        /// pressed. One template shared by every button (cheap on RAM).
        /// </summary>
        public static ControlTemplate FlatButtonTemplate()
        {
            if (_flatTemplate == null)
            {
                string xaml =
                    "<ControlTemplate TargetType=\"Button\"" +
                    " xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"" +
                    " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Border x:Name=\"bd\" Background=\"{TemplateBinding Background}\"" +
                    " BorderBrush=\"{TemplateBinding BorderBrush}\"" +
                    " BorderThickness=\"{TemplateBinding BorderThickness}\"" +
                    " CornerRadius=\"12\">" +
                    "<ContentPresenter HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"" +
                    " Margin=\"{TemplateBinding Padding}\"/>" +
                    "</Border>" +
                    "<ControlTemplate.Triggers>" +
                    "<Trigger Property=\"IsPressed\" Value=\"True\">" +
                    "<Setter TargetName=\"bd\" Property=\"Opacity\" Value=\"0.55\"/>" +
                    "</Trigger>" +
                    "<Trigger Property=\"IsEnabled\" Value=\"False\">" +
                    "<Setter TargetName=\"bd\" Property=\"Opacity\" Value=\"0.35\"/>" +
                    "</Trigger>" +
                    "</ControlTemplate.Triggers>" +
                    "</ControlTemplate>";
                _flatTemplate = (ControlTemplate)XamlReader.Parse(xaml);
            }
            return _flatTemplate;
        }

        public static Button Btn(string text, double fontSize, Brush bg, Brush fg,
            RoutedEventHandler click)
        {
            Button b = new Button();
            b.Content = text;
            b.FontSize = fontSize;
            b.FontFamily = new FontFamily(FontUi);
            b.Background = bg;
            b.Foreground = fg;
            b.BorderBrush = Line;
            b.BorderThickness = new Thickness(1);
            b.Padding = new Thickness(18, 10, 18, 10);
            b.MinHeight = 52;
            b.MinWidth = 52;
            b.Template = FlatButtonTemplate();
            b.Focusable = false;
            if (click != null) b.Click += click;
            return b;
        }

        /// <summary>Launcher tile: big name on top, dim detail line under it.</summary>
        public static Button Tile(string name, string detail, RoutedEventHandler click)
        {
            return Tile(name, detail, null, click);
        }

        public static Button Tile(string name, string detail, ImageSource icon,
            RoutedEventHandler click)
        {
            StackPanel sp = new StackPanel();
            sp.VerticalAlignment = VerticalAlignment.Center;

            TextBlock title = new TextBlock();
            title.Text = name;
            title.FontSize = 26;
            title.FontFamily = new FontFamily(FontUi);
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = Text;
            title.TextAlignment = TextAlignment.Center;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(title);

            TextBlock sub = new TextBlock();
            sub.Text = detail;
            sub.FontSize = 13;
            sub.Foreground = TextDim;
            sub.TextAlignment = TextAlignment.Center;
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            sub.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(sub);

            Grid body = new Grid();
            body.VerticalAlignment = VerticalAlignment.Center;
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border badge = new Border();
            badge.Width = 46;
            badge.Height = 46;
            badge.Margin = new Thickness(14, 0, 12, 0);
            badge.Background = AccentDim;
            badge.BorderBrush = Line;
            badge.BorderThickness = new Thickness(1);
            badge.CornerRadius = new CornerRadius(10);
            if (icon != null)
            {
                Image image = new Image();
                image.Source = icon;
                image.Width = 34;
                image.Height = 34;
                image.Stretch = Stretch.Uniform;
                badge.Child = image;
            }
            else
            {
                string initial = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
                TextBlock letter = Label(initial, 24, Accent);
                letter.FontWeight = FontWeights.Bold;
                letter.HorizontalAlignment = HorizontalAlignment.Center;
                letter.VerticalAlignment = VerticalAlignment.Center;
                badge.Child = letter;
            }
            body.Children.Add(badge);
            Grid.SetColumn(sp, 1);
            body.Children.Add(sp);

            Button b = new Button();
            b.Content = body;
            b.Width = 196;
            b.Height = 110;
            b.Margin = new Thickness(7);
            b.Background = Panel;
            b.BorderBrush = Line;
            b.BorderThickness = new Thickness(1);
            b.Template = FlatButtonTemplate();
            b.Focusable = false;
            if (click != null) b.Click += click;
            return b;
        }

        public static TextBlock Label(string text, double size, Brush fg)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = size;
            t.FontFamily = new FontFamily(FontUi);
            t.Foreground = fg;
            return t;
        }

        public static TextBox Input(string text, double fontSize)
        {
            TextBox t = new TextBox();
            t.Text = text;
            t.FontSize = fontSize;
            t.Background = PanelHi;
            t.Foreground = Text;
            t.CaretBrush = Accent;
            t.BorderBrush = Line;
            t.BorderThickness = new Thickness(1);
            t.Padding = new Thickness(10, 8, 10, 8);
            t.MinHeight = 46;
            t.VerticalContentAlignment = VerticalAlignment.Center;
            return t;
        }
    }

    /// <summary>
    /// A finger-sized slider drawn from two rectangles. The whole bar is the
    /// hit target (tap anywhere to jump, drag to scrub), which beats the tiny
    /// thumb of the stock WPF slider on a touchscreen. No animations.
    /// </summary>
    public class TouchSlider : Grid
    {
        private readonly Border _fill;
        private int _value; // 0..100
        private bool _dragging;

        public event Action<int> ValueChanged;

        public TouchSlider()
        {
            Height = 58;
            Background = Ui.Panel;
            ClipToBounds = true;

            Border track = new Border();
            track.Background = Ui.Panel;
            track.BorderBrush = Ui.Line;
            track.BorderThickness = new Thickness(1);
            track.CornerRadius = new CornerRadius(10);
            Children.Add(track);

            _fill = new Border();
            _fill.Background = Ui.AccentDim;
            _fill.BorderBrush = Ui.Accent;
            _fill.BorderThickness = new Thickness(0, 0, 2, 0);
            _fill.CornerRadius = new CornerRadius(10, 0, 0, 10);
            _fill.HorizontalAlignment = HorizontalAlignment.Left;
            Children.Add(_fill);

            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            SizeChanged += delegate { UpdateFill(); };
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v > 100) v = 100;
                _value = v;
                UpdateFill();
            }
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            CaptureMouse();
            Apply(e.GetPosition(this).X);
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
            {
                Apply(e.GetPosition(this).X);
            }
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            ReleaseMouseCapture();
        }

        private void Apply(double x)
        {
            double w = ActualWidth;
            if (w <= 0) return;
            int v = (int)Math.Round((x / w) * 100.0);
            if (v < 0) v = 0;
            if (v > 100) v = 100;
            if (v != _value)
            {
                _value = v;
                UpdateFill();
                if (ValueChanged != null) ValueChanged(v);
            }
        }

        private void UpdateFill()
        {
            double w = ActualWidth * (_value / 100.0);
            if (w < 0) w = 0;
            _fill.Width = w;
        }
    }
}
