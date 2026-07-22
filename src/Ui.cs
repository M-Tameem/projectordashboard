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
    /// built from the same handful of colors and sizes. Every palette stays
    /// nearly black at night, but the complete app can change hue together.
    /// </summary>
    public static class Ui
    {
        public static readonly string[] ThemeNames =
            new string[] { "Cyan", "Amber", "Violet", "Emerald" };

        public static string ThemeName { get; private set; }
        public static Brush Bg { get; private set; }
        public static Brush Panel { get; private set; }
        public static Brush PanelHi { get; private set; }
        public static Brush Line { get; private set; }
        public static Brush Text { get; private set; }
        public static Brush TextDim { get; private set; }
        public static Brush Accent { get; private set; }
        public static Brush AccentDim { get; private set; }
        public static Brush Danger { get; private set; }

        // Structural and semantic colors used outside the small control
        // factories. Keeping them here prevents one screen from remaining
        // cyan after the user changes the rest of the app.
        public static Brush SurfaceLow { get; private set; }
        public static Brush Overlay { get; private set; }
        public static Brush SkyBg { get; private set; }
        public static Brush SourceDim { get; private set; }
        public static Brush ReservedDim { get; private set; }
        public static Brush Ink { get; private set; }
        public static Brush Sunrise { get; private set; }
        public static Brush Planet { get; private set; }
        public static Brush Star { get; private set; }
        public static Brush IssLine { get; private set; }
        public static Brush MapCorner { get; private set; }
        public static Brush MapGrid { get; private set; }
        public static Brush MapGridText { get; private set; }
        public static Brush MapZenith { get; private set; }
        public static Brush AircraftHistory { get; private set; }
        public static Brush AircraftOutline { get; private set; }
        public static Brush AircraftCourse { get; private set; }
        public static Brush Airport { get; private set; }
        public static Brush Reference { get; private set; }
        public static Brush ReferenceDim { get; private set; }
        public static Brush LabelBg { get; private set; }
        public static Brush LabelLine { get; private set; }
        public static Brush DangerFill { get; private set; }
        public static Brush DangerText { get; private set; }
        public static Brush AlarmShade { get; private set; }
        public static Brush ComboText { get; private set; }
        public static Brush ReturnShade { get; private set; }

        private static string _gradientStart;
        private static string _gradientMid;
        private static string _gradientEnd;
        private static string _projectorCenter;
        private static string _projectorOuter;

        static Ui()
        {
            ApplyTheme("Cyan");
        }

        public static string NormalizeTheme(string name)
        {
            foreach (string candidate in ThemeNames)
            {
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return "Cyan";
        }

        public static string NextTheme(string current)
        {
            string normalized = NormalizeTheme(current);
            for (int i = 0; i < ThemeNames.Length; i++)
                if (ThemeNames[i] == normalized)
                    return ThemeNames[(i + 1) % ThemeNames.Length];
            return ThemeNames[0];
        }

        public static void ApplyTheme(string name)
        {
            ThemeName = NormalizeTheme(name);
            if (ThemeName == "Amber")
            {
                Bg = Hex("#0D0904"); Panel = Hex("#191208"); PanelHi = Hex("#241A0D");
                Line = Hex("#49351C"); Text = Hex("#FFF8E8"); TextDim = Hex("#B39A72");
                Accent = Hex("#FFBE55"); AccentDim = Hex("#4A2D0C");
                SurfaceLow = Hex("#160E05"); Overlay = Hex("#100B05"); SkyBg = Hex("#090603");
                SourceDim = Hex("#8A6A3B"); ReservedDim = Hex("#73562E"); Ink = Hex("#171005");
                MapCorner = Hex("#71532C"); MapGrid = Hex("#35230D"); MapGridText = Hex("#76542B");
                MapZenith = Hex("#9C7640"); AircraftHistory = Hex("#B77924");
                AircraftOutline = Hex("#FFF1CB"); AircraftCourse = Hex("#654115");
                LabelBg = Hex("#130D05"); LabelLine = Hex("#513517");
                _gradientStart = "#090603"; _gradientMid = "#160E05"; _gradientEnd = "#2A1907";
                _projectorCenter = "#2B1B08"; _projectorOuter = "#080503";
            }
            else if (ThemeName == "Violet")
            {
                Bg = Hex("#090710"); Panel = Hex("#15111F"); PanelHi = Hex("#20182E");
                Line = Hex("#3B2A55"); Text = Hex("#F8F1FF"); TextDim = Hex("#9B8EAD");
                Accent = Hex("#B798FF"); AccentDim = Hex("#2C1D50");
                SurfaceLow = Hex("#110C1B"); Overlay = Hex("#0D0915"); SkyBg = Hex("#07050C");
                SourceDim = Hex("#75658B"); ReservedDim = Hex("#635478"); Ink = Hex("#110A1B");
                MapCorner = Hex("#594472"); MapGrid = Hex("#251936"); MapGridText = Hex("#5F4A77");
                MapZenith = Hex("#8774A0"); AircraftHistory = Hex("#7556B7");
                AircraftOutline = Hex("#F2E9FF"); AircraftCourse = Hex("#432B67");
                LabelBg = Hex("#100B18"); LabelLine = Hex("#38254F");
                _gradientStart = "#07050C"; _gradientMid = "#100B1A"; _gradientEnd = "#211238";
                _projectorCenter = "#26183A"; _projectorOuter = "#06040A";
            }
            else if (ThemeName == "Emerald")
            {
                Bg = Hex("#050B09"); Panel = Hex("#0B1713"); PanelHi = Hex("#10231C");
                Line = Hex("#1D4437"); Text = Hex("#EEFDF7"); TextDim = Hex("#7FA092");
                Accent = Hex("#58E6AB"); AccentDim = Hex("#103B2B");
                SurfaceLow = Hex("#08140F"); Overlay = Hex("#07100D"); SkyBg = Hex("#030906");
                SourceDim = Hex("#4E7665"); ReservedDim = Hex("#406653"); Ink = Hex("#04150E");
                MapCorner = Hex("#285A48"); MapGrid = Hex("#0E2B20"); MapGridText = Hex("#376C58");
                MapZenith = Hex("#61917C"); AircraftHistory = Hex("#299E70");
                AircraftOutline = Hex("#DFFFF2"); AircraftCourse = Hex("#15553D");
                LabelBg = Hex("#06120D"); LabelLine = Hex("#174A37");
                _gradientStart = "#030806"; _gradientMid = "#07150F"; _gradientEnd = "#0B2A1E";
                _projectorCenter = "#0B2B20"; _projectorOuter = "#020805";
            }
            else
            {
                Bg = Hex("#060A0E"); Panel = Hex("#0D151C"); PanelHi = Hex("#121F28");
                Line = Hex("#1C3540"); Text = Hex("#EDF7F8"); TextDim = Hex("#79909A");
                Accent = Hex("#59DCE2"); AccentDim = Hex("#10363C");
                SurfaceLow = Hex("#091218"); Overlay = Hex("#071015"); SkyBg = Hex("#04090C");
                SourceDim = Hex("#4D6971"); ReservedDim = Hex("#3E5961"); Ink = Hex("#041014");
                MapCorner = Hex("#25434B"); MapGrid = Hex("#132A31"); MapGridText = Hex("#38525A");
                MapZenith = Hex("#78939A"); AircraftHistory = Hex("#317D86");
                AircraftOutline = Hex("#D5FFFF"); AircraftCourse = Hex("#1B4851");
                LabelBg = Hex("#071116"); LabelLine = Hex("#1C3941");
                _gradientStart = "#05090C"; _gradientMid = "#071218"; _gradientEnd = "#0A2027";
                _projectorCenter = "#0B2228"; _projectorOuter = "#03070A";
            }

            // Object-category and warning colors retain their meaning while
            // every surrounding surface and instrument line follows the theme.
            Danger = Hex("#EE7180");
            Sunrise = Hex("#F4C66A");
            Planet = Hex("#A99BE8");
            Star = Hex("#61737D");
            IssLine = Hex("#806A3D");
            Airport = MapZenith;
            // Fixed neutral instrument colors keep permanent bearings, zenith,
            // and rulers distinct from the user-selected live-traffic accent.
            Reference = Hex("#A7BAC2");
            ReferenceDim = Hex("#536870");
            DangerFill = Hex("#461721");
            DangerText = Hex("#FFF4F5");
            AlarmShade = Hex("#E60B0810");
            ComboText = Hex("#17131F");
            ReturnShade = Hex("#EB0C0A14");
        }

        // Shared black -> deep-blue wash used as the controller backdrop.
        public static Brush BgGradient()
        {
            LinearGradientBrush g = new LinearGradientBrush();
            g.StartPoint = new Point(0.1, 0.0);
            g.EndPoint   = new Point(0.9, 1.0);
            g.GradientStops.Add(new GradientStop(
                    (Color)ColorConverter.ConvertFromString(_gradientStart), 0.0));
            g.GradientStops.Add(new GradientStop(
                    (Color)ColorConverter.ConvertFromString(_gradientMid), 0.58));
            g.GradientStops.Add(new GradientStop(
                    (Color)ColorConverter.ConvertFromString(_gradientEnd), 1.0));
            g.Freeze();
            return g;
        }

        public static Brush ProjectorGradient()
        {
            RadialGradientBrush g = new RadialGradientBrush();
            g.Center = new Point(0.5, 0.48);
            g.GradientOrigin = new Point(0.5, 0.48);
            g.RadiusX = 0.92;
            g.RadiusY = 0.92;
            g.GradientStops.Add(new GradientStop(
                (Color)ColorConverter.ConvertFromString(_projectorCenter), 0.0));
            g.GradientStops.Add(new GradientStop(
                (Color)ColorConverter.ConvertFromString(_projectorOuter), 1.0));
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
                    " CornerRadius=\"9\">" +
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

        /// <summary>Compact, uppercase action used by the controller toolbar.</summary>
        public static Button HeaderBtn(string text, RoutedEventHandler click)
        {
            Button b = Btn(text.ToUpperInvariant(), 13, Panel, Text, click);
            b.FontWeight = FontWeights.SemiBold;
            b.MinWidth = 84;
            b.Height = 48;
            b.MinHeight = 48;
            b.Padding = new Thickness(13, 7, 13, 7);
            b.Margin = new Thickness(0, 0, 8, 0);
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
            title.FontSize = 22;
            title.FontFamily = new FontFamily(FontUi);
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = Text;
            title.TextAlignment = TextAlignment.Center;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(title);

            TextBlock sub = new TextBlock();
            sub.Text = detail;
            sub.FontSize = 12;
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
            badge.Width = 36;
            badge.Height = 36;
            badge.Margin = new Thickness(10, 0, 9, 0);
            badge.Background = AccentDim;
            badge.BorderBrush = Line;
            badge.BorderThickness = new Thickness(1);
            badge.CornerRadius = new CornerRadius(10);
            if (icon != null)
            {
                Image image = new Image();
                image.Source = icon;
                image.Width = 26;
                image.Height = 26;
                image.Stretch = Stretch.Uniform;
                badge.Child = image;
            }
            else
            {
                string initial = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
                TextBlock letter = Label(initial, 19, Accent);
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
            b.Height = 62;
            b.Margin = new Thickness(3);
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
