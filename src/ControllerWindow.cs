using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace ProjectorDash
{
    /// <summary>
    /// The tablet-facing control surface. Everything interactive lives outside
    /// the bottom-right reserved rectangle, which belongs to TouchMousePointer.
    /// </summary>
    public class ControllerWindow : Window
    {
        private readonly AppConfig _cfg;
        private readonly AudioEndpoint _audio;
        private readonly BrightnessControl _brightness;
        private ProjectorWindow _projector;
        private ReturnOverlayWindow _returnOverlay;
        private MirrorWindow _fullscreenMirror;
        private readonly DwmWindowMirror _previewMirror = new DwmWindowMirror();
        private DispatcherTimer _clockTimer;
        private DispatcherTimer _alarmSoundTimer;
        private AudioEndpoint.AlarmAudioState _alarmAudioState;
        private IntPtr _lastLaunchedWindow = IntPtr.Zero;
        private int _launchGeneration;
        private int _audioRefreshCounter;
        private DateTime _lastAlarmDate = DateTime.MinValue;
        private DateTime _lastAutoLockDate = DateTime.MinValue;
        private DateTime _snoozeUntil = DateTime.MinValue;
        private bool _projectorSleeping;
        private bool _tabletAppMode;
        private bool _mirrorFullscreen;
        private List<IntPtr> _sleepingWindows = new List<IntPtr>();
        private WeatherReading _weather;
        private SkyReading _sky;
        private DateTime _nextWeatherRefreshUtc = DateTime.MinValue;
        private DateTime _nextSkyRefreshUtc = DateTime.MinValue;
        private bool _weatherRefreshBusy;
        private bool _skyRefreshBusy;

        // Layout pieces that depend on config / DPI
        private RowDefinition _bottomRow;
        private ColumnDefinition _reservedCol;

        // Live-updating controls
        private TextBlock _clockText;
        private TextBlock _dateText;
        private TextBlock _alarmStatusText;
        private WrapPanel _tilePanel;
        private TouchSlider _volSlider;
        private Button _muteBtn;
        private Button _audioDeviceBtn;
        private TouchSlider _briSlider;
        private TextBlock _briValueText;
        private Button _sleepBtn;
        private Button _autoLockBtn;
        private Button _previewBtn;
        private Button _colorThemeBtn;
        private Button _mainOverheadBtn;
        private Button _aircraftRangeBtn;
        private Button _overheadRangeBtn;
        private Button _updateBtn;
        private Button _autoLockToggleBtn;
        private Popup _autoLockPopup;
        private Popup _powerPopup;
        private Popup _aircraftRangePopup;
        private TextBlock _aircraftRangePendingText;
        private int _pendingAircraftRadiusKm;
        private TextBox _autoLockTime;
        private bool _suppressSliderEvents;
        private bool _updateBusy;
        private TextBlock _weatherTempText;
        private TextBlock _weatherConditionText;
        private TextBlock _weatherMetaText;
        private TextBlock _sunriseText;
        private TextBlock _skyCountText;
        private TextBlock _aircraftSourceText;
        private TextBlock _aircraftText;
        private TextBlock _orbitText;
        private Grid _overheadOverlay;
        private OverheadMap _overheadMap;
        private TextBlock _overheadFeedText;
        private TextBlock _overheadObjectsText;
        private TextBlock _overheadUpdatedText;
        private Button _ceilingMapBtn;
        private bool _projectorOverhead;
        private bool _overheadRestoresApp;

        // Live projector preview overlay
        private Grid _previewOverlay;
        private FrameworkElement _previewViewport;
        private TextBlock _previewStatus;

        // Settings overlay
        private Grid _settingsOverlay;
        private ListBox _shortcutList;
        private TextBox _editName;
        private TextBox _editTarget;
        private TextBox _editArgs;
        private TextBox _editResW;
        private TextBox _editResH;
        private TextBox _editBrowser;
        private Button _editDirectCompositionBtn;
        private Button _editD3d9Btn;
        private Button _editVsyncBtn;
        private Button _editIncogBtn;
        private Button _editHideTargetBtn;
        private Button _projectorModeBtn;
        private Button _launchModeBtn;
        private Button _tabletModeBtn;
        private Button _alarmToggleBtn;
        private TextBox _editAlarmTime;
        private ComboBox _audioDeviceCombo;
        private ComboBox _alarmDeviceCombo;
        private TextBlock _audioDeviceStatus;
        private TextBlock _alarmDeviceStatus;
        private TextBox _editLocationSearch;
        private TextBox _editLatitude;
        private TextBox _editLongitude;
        private TextBox _editFacingDegrees;
        private Button _facingBtn;
        private Button _temperatureUnitBtn;
        private TextBlock _locationStatus;
        private Grid[] _settingsPages;
        private Button[] _settingsTabButtons;
        private int _settingsPageIndex;
        private Grid _alarmOverlay;
        private TextBlock _alarmRingTime;
        private TextBlock _alarmRingStatus;

        public ControllerWindow(AppConfig cfg)
        {
            _cfg = cfg;
            _audio = new AudioEndpoint(cfg.AudioDeviceId);
            _brightness = new BrightnessControl();
            bool audioConfigChanged = false;
            if (string.IsNullOrEmpty(_cfg.AudioDeviceId) &&
                !string.IsNullOrEmpty(_audio.CurrentDeviceId))
            {
                _cfg.AudioDeviceId = _audio.CurrentDeviceId;
                audioConfigChanged = true;
            }
            if (string.IsNullOrEmpty(_cfg.AlarmAudioDeviceId))
            {
                _cfg.AlarmAudioDeviceId = _audio.FindLikelyInternalSpeakers();
                audioConfigChanged |= !string.IsNullOrEmpty(_cfg.AlarmAudioDeviceId);
            }
            if (audioConfigChanged) _cfg.Save();

            Title = "Projector Dashboard";
            Background = Ui.BgGradient();
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;

            Content = BuildLayout();

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoadedOnce;
            Closed += OnClosedExit;
        }

        // ------------------------------------------------------------------
        // Startup / shutdown
        // ------------------------------------------------------------------

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            WinForms.Screen tablet = ScreenUtil.FindByDevice(
                _cfg.TabletDevice, WinForms.Screen.PrimaryScreen);
            ScreenUtil.FillScreen(this, tablet);
        }

        private void OnLoadedOnce(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoadedOnce;

            ApplyReservedSize();
            RebuildTiles();
            SyncAudioUi();
            SyncBrightnessUi();
            OpenProjectorWindow();
            UpdateAlarmUi();
            UpdateAutoLockUi();
            ApplyAmbientUi();
            StartAmbientRefresh(true);

            _clockTimer = new DispatcherTimer();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += delegate { TickClock(); };
            _clockTimer.Start();
            TickClock();
        }

        private void OnClosedExit(object sender, EventArgs e)
        {
            // Requirement: closing the controller exits the whole app.
            if (_clockTimer != null) _clockTimer.Stop();
            if (_alarmSoundTimer != null) _alarmSoundTimer.Stop();
            if (_alarmAudioState != null)
            {
                _audio.EndAlarm(_alarmAudioState, _cfg.AudioDeviceId);
                _alarmAudioState = null;
            }
            if (_projectorSleeping) WakeProjector();
            if (_projector != null)
            {
                try { _projector.Close(); }
                catch { }
            }
            if (_returnOverlay != null)
            {
                try { _returnOverlay.Close(); }
                catch { }
            }
            if (_fullscreenMirror != null)
            {
                try { _fullscreenMirror.Close(); }
                catch { }
            }
            _previewMirror.Dispose();
            Application.Current.Shutdown();
        }

        private void OpenProjectorWindow()
        {
            if (_cfg.TabletOnlyMode) return;
            if (string.IsNullOrEmpty(_cfg.ProjectorDevice)) return;
            if (!ScreenUtil.DeviceExists(_cfg.ProjectorDevice)) return;
            if (string.Equals(_cfg.ProjectorDevice, _cfg.TabletDevice,
                StringComparison.OrdinalIgnoreCase)) return;

            WinForms.Screen proj = ScreenUtil.FindByDevice(_cfg.ProjectorDevice, null);
            if (proj == null) return;
            _projector = new ProjectorWindow(proj, _cfg.ProjectorMode);
            _projector.Show();
            ApplyAmbientUi();
            Activate(); // keep touch focus on the controller
        }

        private void TickClock()
        {
            DateTime now = DateTime.Now;
            _clockText.Text = now.ToString("h:mm");
            _dateText.Text = now.ToString("dddd, MMMM d");
            UpdateSunriseText(now);
            if (_projector != null) _projector.UpdateTime(now);
            DateTime utc = DateTime.UtcNow;
            if (_cfg.LocationConfigured && utc >= _nextSkyRefreshUtc)
                RefreshSky();
            if (_cfg.LocationConfigured && utc >= _nextWeatherRefreshUtc)
                RefreshWeather();
            CheckAlarm(now);
            CheckAutoLock(now);
            _audioRefreshCounter++;
            if (_audioRefreshCounter >= 5 &&
                (_alarmOverlay == null || _alarmOverlay.Visibility != Visibility.Visible))
            {
                _audioRefreshCounter = 0;
                if (_audio.RefreshForPreferredDevice(_cfg.AudioDeviceId))
                {
                    SyncAudioUi();
                    UpdateAudioDeviceButton();
                }
            }
            if (_previewOverlay != null &&
                _previewOverlay.Visibility == Visibility.Visible)
                RefreshPreview();
            if (_mirrorFullscreen && _fullscreenMirror != null)
                _fullscreenMirror.SetSource(FindPreviewSource());
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------

        private void CycleColorTheme()
        {
            _cfg.ColorTheme = Ui.NextTheme(_cfg.ColorTheme);
            _cfg.Save();
            Ui.ApplyTheme(_cfg.ColorTheme);

            if (_autoLockPopup != null) _autoLockPopup.IsOpen = false;
            if (_powerPopup != null) _powerPopup.IsOpen = false;
            if (_aircraftRangePopup != null) _aircraftRangePopup.IsOpen = false;
            _previewMirror.Detach();
            if (_projector != null)
            {
                try { _projector.Close(); }
                catch { }
                _projector = null;
            }

            Background = Ui.BgGradient();
            Content = BuildLayout();
            ApplyReservedSize();
            RebuildTiles();
            SyncAudioUi();
            SyncBrightnessUi();
            UpdateAlarmUi();
            UpdateAutoLockUi();
            PopulateAmbientEditor();
            OpenProjectorWindow();
            if (_projector != null && _projectorOverhead)
            {
                _projector.ShowOverhead(true);
                _projector.UpdateOverhead(_sky, _cfg.FacingDegrees);
            }
            ApplyAmbientUi();
            TickClock();
        }

        private UIElement BuildLayout()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _bottomRow = new RowDefinition { Height = new GridLength(340) };
            root.RowDefinitions.Add(_bottomRow);

            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _reservedCol = new ColumnDefinition { Width = new GridLength(420) };
            root.ColumnDefinitions.Add(_reservedCol);

            // --- Compact instrument header --------------------------------
            Border headerCard = new Border();
            headerCard.Margin = new Thickness(18, 10, 18, 4);
            headerCard.Padding = new Thickness(16, 8, 10, 8);
            headerCard.Background = Ui.Panel;
            headerCard.BorderBrush = Ui.Line;
            headerCard.BorderThickness = new Thickness(1);
            headerCard.CornerRadius = new CornerRadius(11);
            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel clockStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _clockText = new TextBlock();
            _clockText.FontFamily = new FontFamily(Ui.FontLight);
            _clockText.FontSize = 54;
            _clockText.Foreground = Ui.Text;
            _clockText.Margin = new Thickness(0, -8, 0, 0);
            clockStack.Children.Add(_clockText);
            _dateText = Ui.Label("", 15, Ui.TextDim);
            _dateText.Margin = new Thickness(2, -5, 0, 0);
            clockStack.Children.Add(_dateText);
            _alarmStatusText = Ui.Label("", 12, Ui.Accent);
            _alarmStatusText.Margin = new Thickness(2, 3, 0, 0);
            clockStack.Children.Add(_alarmStatusText);
            header.Children.Add(clockStack);

            Grid weather = new Grid { Margin = new Thickness(4, 0, 18, 0) };
            weather.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            weather.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _weatherTempText = Ui.Label("--", 38, Ui.Text);
            _weatherTempText.FontFamily = new FontFamily(Ui.FontLight);
            _weatherTempText.Width = 105;
            _weatherTempText.VerticalAlignment = VerticalAlignment.Center;
            weather.Children.Add(_weatherTempText);
            StackPanel weatherCopy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _weatherConditionText = Ui.Label("SET LOCATION", 13, Ui.Accent);
            _weatherConditionText.FontWeight = FontWeights.SemiBold;
            weatherCopy.Children.Add(_weatherConditionText);
            _weatherMetaText = Ui.Label("Settings › Ambient", 13, Ui.TextDim);
            _weatherMetaText.TextTrimming = TextTrimming.CharacterEllipsis;
            weatherCopy.Children.Add(_weatherMetaText);
            _sunriseText = Ui.Label("NEXT SUNRISE  --", 11, Ui.Sunrise);
            _sunriseText.Margin = new Thickness(0, 2, 0, 0);
            weatherCopy.Children.Add(_sunriseText);
            TextBlock source = Ui.Label("OPEN-METEO", 10, Ui.SourceDim);
            source.Margin = new Thickness(0, 3, 0, 0);
            weatherCopy.Children.Add(source);
            Grid.SetColumn(weatherCopy, 1);
            weather.Children.Add(weatherCopy);
            Grid.SetColumn(weather, 1);
            header.Children.Add(weather);

            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            _sleepBtn = Ui.HeaderBtn(_cfg.TabletOnlyMode ? "Hide" : "Idle",
                delegate { ToggleProjectorSleep(); });
            _sleepBtn.ToolTip = _cfg.TabletOnlyMode
                ? "Hide the tablet app without closing its tabs; tap again to restore it."
                : "Hide projector apps without closing their tabs; tap again to wake.";
            actions.Children.Add(_sleepBtn);

            _previewBtn = Ui.HeaderBtn("View",
                delegate { OpenPreview(); });
            _previewBtn.IsEnabled = !_cfg.TabletOnlyMode;
            _previewBtn.Opacity = _cfg.TabletOnlyMode ? 0.45 : 1.0;
            _previewBtn.ToolTip = _cfg.TabletOnlyMode
                ? "Preview is available when the projector is the active target."
                : "Show a live copy of the current projector window without opening another tab.";
            actions.Children.Add(_previewBtn);

            _colorThemeBtn = Ui.HeaderBtn("Color " + Ui.ThemeName,
                delegate { CycleColorTheme(); });
            _colorThemeBtn.MinWidth = 100;
            _colorThemeBtn.ToolTip = "Change the palette across the tablet, projector idle screen, and sky map.";
            actions.Children.Add(_colorThemeBtn);

            Button tools = Ui.HeaderBtn("Tools", delegate { OpenAutoLockPopup(); });
            tools.ToolTip = "Keyboard, Windows desktop, and daily auto-lock.";
            actions.Children.Add(tools);
            _autoLockPopup = BuildAutoLockPopup(tools);

            Button settings = Ui.HeaderBtn("Settings",
                delegate { OpenSettings(); });
            actions.Children.Add(settings);

            Button power = Ui.HeaderBtn("Power", delegate
            {
                if (_powerPopup != null) _powerPopup.IsOpen = true;
            });
            power.Foreground = Ui.Danger;
            power.BorderBrush = Ui.Danger;
            power.Margin = new Thickness(0);
            power.ToolTip = "Projector, display, lock, and dashboard power actions.";
            actions.Children.Add(power);
            _powerPopup = BuildPowerPopup(power);
            Grid.SetColumn(actions, 2);
            header.Children.Add(actions);

            headerCard.Child = header;
            Grid.SetRow(headerCard, 0);
            Grid.SetColumnSpan(headerCard, 2);
            root.Children.Add(headerCard);

            // --- Reactive sky strip + launcher grid -----------------------
            Grid launchArea = new Grid();
            launchArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            launchArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            launchArea.Children.Add(BuildSkyStrip());
            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroller.PanningMode = PanningMode.VerticalOnly;
            scroller.Margin = new Thickness(17, 1, 17, 2);

            _tilePanel = new WrapPanel();
            _tilePanel.Orientation = Orientation.Horizontal;
            scroller.Content = _tilePanel;

            Grid.SetRow(scroller, 1);
            launchArea.Children.Add(scroller);
            Grid.SetRow(launchArea, 1);
            Grid.SetColumnSpan(launchArea, 2);
            root.Children.Add(launchArea);

            // Live one-source projector mirror. Like Settings, it leaves the
            // bottom audio/brightness and TouchMousePointer strip available.
            _previewOverlay = BuildPreviewOverlay();
            _previewOverlay.Visibility = Visibility.Collapsed;
            Grid.SetRow(_previewOverlay, 0);
            Grid.SetRowSpan(_previewOverlay, 2);
            Grid.SetColumnSpan(_previewOverlay, 2);
            root.Children.Add(_previewOverlay);

            _overheadOverlay = BuildOverheadOverlay();
            _overheadOverlay.Visibility = Visibility.Collapsed;
            Grid.SetRow(_overheadOverlay, 0);
            Grid.SetRowSpan(_overheadOverlay, 2);
            Grid.SetColumnSpan(_overheadOverlay, 2);
            root.Children.Add(_overheadOverlay);

            // --- Bottom-left: volume + brightness --------------------------
            Grid controls = new Grid();
            controls.Margin = new Thickness(28, 10, 20, 24);
            controls.VerticalAlignment = VerticalAlignment.Bottom;
            controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            controls.Children.Add(BuildVolumeRow());
            UIElement bri = BuildBrightnessRow();
            Grid.SetRow(bri, 1);
            controls.Children.Add(bri);

            Grid.SetRow(controls, 2);
            Grid.SetColumn(controls, 0);
            root.Children.Add(controls);

            // --- Bottom-right: reserved for TouchMousePointer --------------
            Grid reserved = new Grid();
            reserved.IsHitTestVisible = false;
            reserved.Margin = new Thickness(6, 6, 10, 10);

            Rectangle outline = new Rectangle();
            outline.Stroke = Ui.Line;
            outline.StrokeThickness = 1;
            outline.StrokeDashArray = new DoubleCollection(new double[] { 5, 5 });
            outline.RadiusX = 14;
            outline.RadiusY = 14;
            reserved.Children.Add(outline);

            TextBlock reservedLabel = Ui.Label("touch pad", 14, Ui.ReservedDim);
            reservedLabel.HorizontalAlignment = HorizontalAlignment.Center;
            reservedLabel.VerticalAlignment = VerticalAlignment.Center;
            reserved.Children.Add(reservedLabel);

            Grid.SetRow(reserved, 2);
            Grid.SetColumn(reserved, 1);
            root.Children.Add(reserved);

            // --- Settings overlay (hidden until opened) --------------------
            _settingsOverlay = BuildSettingsOverlay();
            _settingsOverlay.Visibility = Visibility.Collapsed;
            Grid.SetRow(_settingsOverlay, 0);
            Grid.SetRowSpan(_settingsOverlay, 2); // never covers the reserved bottom strip
            Grid.SetColumnSpan(_settingsOverlay, 2);
            root.Children.Add(_settingsOverlay);

            // Alarm covers the interactive area but deliberately leaves the
            // entire bottom strip (including TouchMousePointer) untouched.
            _alarmOverlay = BuildAlarmOverlay();
            _alarmOverlay.Visibility = Visibility.Collapsed;
            Grid.SetRow(_alarmOverlay, 0);
            Grid.SetRowSpan(_alarmOverlay, 2);
            Grid.SetColumnSpan(_alarmOverlay, 2);
            root.Children.Add(_alarmOverlay);

            return root;
        }

        private Border BuildSkyStrip()
        {
            Border card = new Border();
            card.Margin = new Thickness(18, 1, 18, 3);
            card.Padding = new Thickness(14, 7, 14, 7);
            card.Background = Ui.SurfaceLow;
            card.BorderBrush = Ui.Line;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(9);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(138) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel status = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock mark = Ui.Label("OVERHEAD  /  LIVE", 11, Ui.Accent);
            mark.FontWeight = FontWeights.SemiBold;
            status.Children.Add(mark);
            _skyCountText = Ui.Label("SET LOCATION", 15, Ui.Text);
            _skyCountText.FontWeight = FontWeights.SemiBold;
            status.Children.Add(_skyCountText);
            grid.Children.Add(status);

            StackPanel traffic = new StackPanel
            {
                Margin = new Thickness(14, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _aircraftSourceText = Ui.Label("AIR TRAFFIC  /  CONNECTING", 10, Ui.SourceDim);
            traffic.Children.Add(_aircraftSourceText);
            _aircraftText = Ui.Label("Location is not configured", 14, Ui.TextDim);
            _aircraftText.TextTrimming = TextTrimming.CharacterEllipsis;
            traffic.Children.Add(_aircraftText);
            Grid.SetColumn(traffic, 1);
            grid.Children.Add(traffic);

            StackPanel orbit = new StackPanel
            {
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            orbit.Children.Add(Ui.Label("ORBIT + NIGHT SKY  /  LOCAL CALC", 10, Ui.SourceDim));
            _orbitText = Ui.Label("ISS, planets, and major stars appear here", 14, Ui.TextDim);
            _orbitText.TextTrimming = TextTrimming.CharacterEllipsis;
            orbit.Children.Add(_orbitText);
            Grid.SetColumn(orbit, 2);
            grid.Children.Add(orbit);

            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            _aircraftRangeBtn = Ui.Btn("RANGE " + _cfg.AircraftRadiusKm.ToString() + " KM",
                12, Ui.PanelHi, Ui.Text,
                delegate { OpenAircraftRangePopup(_aircraftRangeBtn); });
            _aircraftRangeBtn.Height = 46;
            _aircraftRangeBtn.MinHeight = 46;
            _aircraftRangeBtn.Padding = new Thickness(12, 6, 12, 6);
            _aircraftRangeBtn.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(_aircraftRangeBtn);
            _mainOverheadBtn = Ui.Btn("PROJECT SKY", 13, Ui.Accent, Ui.Ink,
                delegate { ToggleOverheadFromMain(); });
            _mainOverheadBtn.Height = 46;
            _mainOverheadBtn.MinHeight = 46;
            _mainOverheadBtn.Padding = new Thickness(14, 6, 14, 6);
            _mainOverheadBtn.ToolTip = "Send the ceiling sky directly to the projector; use the tablet map when no projector is available.";
            actions.Children.Add(_mainOverheadBtn);
            Grid.SetColumn(actions, 3);
            grid.Children.Add(actions);

            _aircraftRangePopup = BuildAircraftRangePopup(_aircraftRangeBtn);
            card.Child = grid;
            return card;
        }

        private Grid BuildOverheadOverlay()
        {
            Grid overlay = new Grid { Background = Ui.SkyBg };
            Grid inner = new Grid { Margin = new Thickness(22, 12, 22, 8) };
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inner.RowDefinitions.Add(new RowDefinition
                { Height = new GridLength(1, GridUnitType.Star) });

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel heading = new StackPanel();
            TextBlock title = Ui.Label("OVERHEAD  /  LIVE CEILING VIEW", 24, Ui.Text);
            title.FontWeight = FontWeights.SemiBold;
            heading.Children.Add(title);
            TextBlock explanation = Ui.Label(
                "The live ceiling view: celestial objects use true elevation; aircraft use the selected distance range so wider searches stay readable. Bearings follow the saved heading.",
                13, Ui.TextDim);
            heading.Children.Add(explanation);
            header.Children.Add(heading);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(SmallBtn("Refresh", delegate
            {
                _nextSkyRefreshUtc = DateTime.MinValue;
                RefreshSky();
            }));
            _overheadRangeBtn = SmallBtn("Range: " + _cfg.AircraftRadiusKm.ToString() + " km",
                delegate { OpenAircraftRangePopup(_overheadRangeBtn); });
            actions.Children.Add(_overheadRangeBtn);
            _ceilingMapBtn = SmallBtn("Show on projector", delegate { ToggleProjectorOverhead(); });
            _ceilingMapBtn.Background = Ui.AccentDim;
            _ceilingMapBtn.BorderBrush = Ui.Accent;
            _ceilingMapBtn.IsEnabled = !_cfg.TabletOnlyMode;
            actions.Children.Add(_ceilingMapBtn);
            Button back = Ui.Btn("Back", 16, Ui.Accent, Ui.Ink,
                delegate { CloseOverheadView(); });
            actions.Children.Add(back);
            Grid.SetColumn(actions, 1);
            header.Children.Add(actions);
            inner.Children.Add(header);

            Grid body = new Grid { Margin = new Thickness(0, 9, 0, 0) };
            body.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            Border mapFrame = new Border();
            mapFrame.Background = Ui.SkyBg;
            mapFrame.BorderBrush = Ui.Line;
            mapFrame.BorderThickness = new Thickness(1);
            mapFrame.CornerRadius = new CornerRadius(10);
            mapFrame.ClipToBounds = true;
            _overheadMap = new OverheadMap();
            mapFrame.Child = _overheadMap;
            body.Children.Add(mapFrame);

            Border info = new Border();
            info.Margin = new Thickness(10, 0, 0, 0);
            info.Padding = new Thickness(16);
            info.Background = Ui.Panel;
            info.BorderBrush = Ui.Line;
            info.BorderThickness = new Thickness(1);
            info.CornerRadius = new CornerRadius(10);
            StackPanel infoStack = new StackPanel();
            TextBlock live = Ui.Label("LIVE STATUS", 11, Ui.Accent);
            live.FontWeight = FontWeights.SemiBold;
            infoStack.Children.Add(live);
            _overheadFeedText = Ui.Label("Waiting for saved location", 15, Ui.Text);
            _overheadFeedText.Margin = new Thickness(0, 5, 0, 0);
            _overheadFeedText.TextWrapping = TextWrapping.Wrap;
            infoStack.Children.Add(_overheadFeedText);
            _overheadUpdatedText = Ui.Label("", 12, Ui.TextDim);
            _overheadUpdatedText.Margin = new Thickness(0, 4, 0, 0);
            infoStack.Children.Add(_overheadUpdatedText);
            TextBlock objects = Ui.Label("OBJECTS ABOVE", 11, Ui.Accent);
            objects.FontWeight = FontWeights.SemiBold;
            objects.Margin = new Thickness(0, 18, 0, 0);
            infoStack.Children.Add(objects);
            _overheadObjectsText = Ui.Label("Scanning…", 14, Ui.TextDim);
            _overheadObjectsText.Margin = new Thickness(0, 6, 0, 0);
            _overheadObjectsText.TextWrapping = TextWrapping.Wrap;
            infoStack.Children.Add(_overheadObjectsText);
            TextBlock key = Ui.Label(
                "ACCENT  live aircraft + observed path\nAMBER  ISS + observed pass\nVIOLET  planets\nGREY  major stars\n\nAIRCRAFT  center is 0 km and the distance ruler ends at the selected search limit, so 60 km in a 120 km view appears halfway to that limit.\n\nSKY  center is zenith (90°); all edges are horizon (0°). EL 25° / 50° / 75° contours apply to the ISS, planets, and stars. Bearings wrap 360° around center. Dashed lines are reported aircraft courses; solid segments are observed movement.",
                12, Ui.TextDim);
            key.Margin = new Thickness(0, 18, 0, 0);
            key.TextWrapping = TextWrapping.Wrap;
            infoStack.Children.Add(key);
            ScrollViewer infoScroll = new ScrollViewer();
            infoScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            infoScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            infoScroll.PanningMode = PanningMode.VerticalOnly;
            infoScroll.Content = infoStack;
            info.Child = infoScroll;
            Grid.SetColumn(info, 1);
            body.Children.Add(info);
            Grid.SetRow(body, 1);
            inner.Children.Add(body);
            overlay.Children.Add(inner);
            return overlay;
        }

        private Popup BuildAircraftRangePopup(Button target)
        {
            Popup popup = new Popup();
            popup.PlacementTarget = target;
            popup.Placement = PlacementMode.Bottom;
            popup.HorizontalOffset = -120;
            popup.StaysOpen = false;
            popup.AllowsTransparency = true;

            Border card = new Border();
            card.Width = 330;
            card.Padding = new Thickness(16);
            card.Background = Ui.PanelHi;
            card.BorderBrush = Ui.Accent;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(11);
            StackPanel panel = new StackPanel();
            TextBlock title = Ui.Label("AIRCRAFT SEARCH RANGE", 13, Ui.Accent);
            title.FontWeight = FontWeights.SemiBold;
            panel.Children.Add(title);
            TextBlock note = Ui.Label(
                "Choose in 20 km steps. No feed request is made until Done.",
                13, Ui.TextDim);
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(0, 4, 0, 12);
            panel.Children.Add(note);

            Grid selector = new Grid();
            selector.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selector.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            selector.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Button minus = Ui.Btn("−", 24, Ui.Panel, Ui.Text,
                delegate { ChangePendingAircraftRange(-20); });
            minus.Width = 62;
            selector.Children.Add(minus);
            _aircraftRangePendingText = Ui.Label("40 km", 24, Ui.Text);
            _aircraftRangePendingText.FontFamily = new FontFamily(Ui.FontLight);
            _aircraftRangePendingText.TextAlignment = TextAlignment.Center;
            _aircraftRangePendingText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_aircraftRangePendingText, 1);
            selector.Children.Add(_aircraftRangePendingText);
            Button plus = Ui.Btn("+", 24, Ui.Panel, Ui.Text,
                delegate { ChangePendingAircraftRange(20); });
            plus.Width = 62;
            Grid.SetColumn(plus, 2);
            selector.Children.Add(plus);
            panel.Children.Add(selector);

            Button done = Ui.Btn("Done · apply + refresh once", 15,
                Ui.Accent, Ui.Ink, delegate { ApplyPendingAircraftRange(); });
            done.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(done);
            card.Child = panel;
            popup.Child = card;
            return popup;
        }

        private void OpenAircraftRangePopup(Button target)
        {
            if (_aircraftRangePopup == null || target == null) return;
            _pendingAircraftRadiusKm = _cfg.AircraftRadiusKm;
            _aircraftRangePopup.PlacementTarget = target;
            UpdatePendingAircraftRange();
            _aircraftRangePopup.IsOpen = true;
        }

        private void ChangePendingAircraftRange(int deltaKm)
        {
            _pendingAircraftRadiusKm = AppConfig.NormalizeAircraftRadius(
                _pendingAircraftRadiusKm + deltaKm);
            UpdatePendingAircraftRange();
        }

        private void UpdatePendingAircraftRange()
        {
            if (_aircraftRangePendingText != null)
                _aircraftRangePendingText.Text = _pendingAircraftRadiusKm.ToString() + " km" +
                    (_pendingAircraftRadiusKm >= 460 ? " · MAX" : "");
        }

        private void ApplyPendingAircraftRange()
        {
            int selected = AppConfig.NormalizeAircraftRadius(_pendingAircraftRadiusKm);
            if (_aircraftRangePopup != null) _aircraftRangePopup.IsOpen = false;
            if (selected == _cfg.AircraftRadiusKm) return;
            _cfg.AircraftRadiusKm = selected;
            _cfg.Save();
            UpdateAircraftRangeUi();
            _sky = null;
            ApplyAmbientUi();
            _nextSkyRefreshUtc = DateTime.MinValue;
            RefreshSky();
        }

        private void UpdateAircraftRangeUi()
        {
            if (_aircraftRangeBtn != null)
                _aircraftRangeBtn.Content = "RANGE " +
                    _cfg.AircraftRadiusKm.ToString() + " KM";
            if (_overheadRangeBtn != null)
                _overheadRangeBtn.Content = "Range: " +
                    _cfg.AircraftRadiusKm.ToString() + " km";
        }

        private Popup BuildPowerPopup(Button target)
        {
            Popup popup = new Popup();
            popup.PlacementTarget = target;
            popup.Placement = PlacementMode.Bottom;
            popup.HorizontalOffset = -280;
            popup.StaysOpen = false;
            popup.AllowsTransparency = true;

            Border card = new Border();
            card.Width = 390;
            card.Padding = new Thickness(18);
            card.Background = Ui.PanelHi;
            card.BorderBrush = Ui.Danger;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(10);
            StackPanel panel = new StackPanel();
            panel.Children.Add(SectionTitle("Power"));
            panel.Children.Add(WrappedNote(
                "Signal off blanks every display. HDMI-CEC standby physically powers down a compatible projector when a CEC adapter is present."));

            Button closeApp = Ui.Btn("Close current projector app", 15, Ui.Panel, Ui.Text,
                delegate { popup.IsOpen = false; CloseProjectedApp(); });
            closeApp.Margin = new Thickness(0, 12, 0, 0);
            panel.Children.Add(closeApp);
            Button signal = Ui.Btn("Turn display signals off", 15, Ui.Panel, Ui.Text,
                delegate { popup.IsOpen = false; ScreenUtil.TurnOffAllDisplays(); });
            signal.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(signal);
            Button cec = Ui.Btn("Projector standby · HDMI-CEC", 15, Ui.AccentDim, Ui.Accent,
                delegate
                {
                    popup.IsOpen = false;
                    string message;
                    bool ok = CecControl.SendProjectorStandby(out message);
                    if (!ok) MessageBox.Show(this, message, "HDMI-CEC");
                });
            cec.Margin = new Thickness(0, 8, 0, 0);
            cec.ToolTip = CecControl.FindClient().Length > 0
                ? "Send physical standby through the detected free libCEC client."
                : "Requires a CEC-capable HDMI output or USB-CEC adapter and free libCEC client.";
            panel.Children.Add(cec);
            Button secure = Ui.Btn("Lock tablet + emergency off", 15,
                Ui.DangerFill, Ui.DangerText,
                delegate { popup.IsOpen = false; EmergencyOff(); });
            secure.BorderBrush = Ui.Danger;
            secure.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(secure);
            Button exit = Ui.Btn("Exit dashboard", 15, Ui.Panel, Ui.Danger,
                delegate { popup.IsOpen = false; Close(); });
            exit.BorderBrush = Ui.Danger;
            exit.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(exit);
            card.Child = panel;
            popup.Child = card;
            return popup;
        }

        private Popup BuildAutoLockPopup(Button target)
        {
            Popup popup = new Popup();
            popup.PlacementTarget = target;
            popup.Placement = PlacementMode.Bottom;
            popup.HorizontalOffset = -190;
            popup.StaysOpen = false;
            popup.AllowsTransparency = true;

            Border card = new Border();
            card.Width = 380;
            card.Padding = new Thickness(18);
            card.Background = Ui.PanelHi;
            card.BorderBrush = Ui.Accent;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(12);

            StackPanel panel = new StackPanel();
            TextBlock title = SectionTitle("Tools");
            panel.Children.Add(title);
            StackPanel quick = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 12)
            };
            quick.Children.Add(SmallBtn("Keyboard", delegate
            {
                popup.IsOpen = false;
                ShowTouchKeyboard();
            }));
            quick.Children.Add(SmallBtn("Windows desktop", delegate
            {
                popup.IsOpen = false;
                if (_cfg.TabletOnlyMode) EnterTabletAppMode();
                else WindowState = WindowState.Minimized;
            }));
            panel.Children.Add(quick);
            panel.Children.Add(SectionTitle("Daily tablet auto-lock"));
            panel.Children.Add(WrappedNote(
                "Locks Windows at this time every day—the same as pressing Win+L."));

            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 12, 0, 0);
            _autoLockTime = Ui.Input("1:00 AM", 17);
            _autoLockTime.Width = 130;
            row.Children.Add(_autoLockTime);
            Button save = Ui.Btn("Save", 16, Ui.Accent, Ui.Ink,
                delegate { SaveAutoLockTime(); });
            save.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(save);
            panel.Children.Add(row);

            _autoLockToggleBtn = Ui.Btn("Auto-lock: Off", 16, Ui.Panel, Ui.Text,
                delegate { ToggleAutoLock(); });
            _autoLockToggleBtn.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(_autoLockToggleBtn);
            card.Child = panel;
            popup.Child = card;
            return popup;
        }

        private Grid BuildPreviewOverlay()
        {
            Grid overlay = new Grid { Background = Ui.Overlay };
            Grid inner = new Grid { Margin = new Thickness(24, 12, 24, 10) };
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inner.RowDefinitions.Add(new RowDefinition
                { Height = new GridLength(1, GridUnitType.Star) });

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel heading = new StackPanel();
            TextBlock title = Ui.Label("Live projector preview", 27, Ui.Text);
            title.FontWeight = FontWeights.SemiBold;
            heading.Children.Add(title);
            TextBlock explanation = Ui.Label(
                "One compositor mirror of the existing app—no second tab or audio stream.",
                15, Ui.TextDim);
            heading.Children.Add(explanation);
            header.Children.Add(heading);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(SmallBtn("Refresh", delegate { RefreshPreview(); }));
            Button fullscreen = SmallBtn("Fullscreen on tablet",
                delegate { OpenFullscreenPreview(); });
            fullscreen.Background = Ui.AccentDim;
            fullscreen.BorderBrush = Ui.Accent;
            actions.Children.Add(fullscreen);
            Button back = Ui.Btn("Back", 17, Ui.Accent, Ui.Ink,
                delegate { ClosePreview(); });
            actions.Children.Add(back);
            Grid.SetColumn(actions, 1);
            header.Children.Add(actions);
            inner.Children.Add(header);

            Border viewport = new Border();
            viewport.Margin = new Thickness(0, 10, 0, 0);
            viewport.Background = Brushes.Black;
            viewport.BorderBrush = Ui.Line;
            viewport.BorderThickness = new Thickness(1);
            viewport.CornerRadius = new CornerRadius(10);
            viewport.ClipToBounds = true;
            viewport.Cursor = System.Windows.Input.Cursors.Hand;
            viewport.ToolTip = "Tap for a full-tablet mirror.";
            _previewViewport = viewport;

            _previewStatus = Ui.Label("No projector app is open", 19, Ui.TextDim);
            _previewStatus.HorizontalAlignment = HorizontalAlignment.Center;
            _previewStatus.VerticalAlignment = VerticalAlignment.Center;
            viewport.Child = _previewStatus;
            viewport.MouseLeftButtonUp += delegate { OpenFullscreenPreview(); };
            Grid.SetRow(viewport, 1);
            inner.Children.Add(viewport);
            overlay.Children.Add(inner);
            return overlay;
        }

        private UIElement BuildVolumeRow()
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 0, 0, 14);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock label = Ui.Label("Output volume", 18, Ui.TextDim);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);

            _volSlider = new TouchSlider();
            _volSlider.ValueChanged += delegate(int v)
            {
                if (_suppressSliderEvents) return;
                _audio.SetVolume(v);
                if (_audio.GetMute() && v > 0)
                {
                    _audio.SetMute(false);
                    UpdateMuteButton();
                }
            };
            Grid.SetColumn(_volSlider, 1);
            row.Children.Add(_volSlider);

            _audioDeviceBtn = Ui.Btn("Output: " + ShortDeviceName(
                _audio.CurrentDeviceName), 15, Ui.Panel, Ui.Text,
                delegate { OpenSettings(2); });
            _audioDeviceBtn.Width = 190;
            _audioDeviceBtn.Margin = new Thickness(12, 0, 0, 0);
            _audioDeviceBtn.ToolTip = "Choose the Windows audio output device.";
            Grid.SetColumn(_audioDeviceBtn, 2);
            row.Children.Add(_audioDeviceBtn);

            _muteBtn = Ui.Btn("Mute", 17, Ui.Panel, Ui.Text, delegate
            {
                _audio.SetMute(!_audio.GetMute());
                UpdateMuteButton();
            });
            _muteBtn.Margin = new Thickness(12, 0, 0, 0);
            _muteBtn.MinWidth = 110;
            Grid.SetColumn(_muteBtn, 3);
            row.Children.Add(_muteBtn);

            if (!_audio.Available) row.Visibility = Visibility.Collapsed;
            return row;
        }

        private UIElement BuildBrightnessRow()
        {
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock label = Ui.Label("Tablet brightness", 17, Ui.TextDim);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);

            _briSlider = new TouchSlider();
            _briSlider.ValueChanged += delegate(int v)
            {
                if (_suppressSliderEvents) return;
                bool ok = _brightness.SetBrightness(v);
                if (_briValueText != null)
                    _briValueText.Text = ok ? v.ToString() + "%" : "Unavailable";
            };
            Grid.SetColumn(_briSlider, 1);
            row.Children.Add(_briSlider);

            _briValueText = Ui.Label("", 16, Ui.TextDim);
            _briValueText.Width = 214;
            _briValueText.Margin = new Thickness(14, 0, 0, 0);
            _briValueText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_briValueText, 2);
            row.Children.Add(_briValueText);

            if (!_brightness.Available) row.Visibility = Visibility.Collapsed;
            return row;
        }

        private void ApplyReservedSize()
        {
            // Config stores device pixels; the grid wants DIPs.
            double scale = ScreenUtil.DpiScale(this);
            if (scale <= 0) scale = 1.0;
            _reservedCol.Width = new GridLength(_cfg.ReservedWidth / scale);
            _bottomRow.Height = new GridLength(_cfg.ReservedHeight / scale);
        }

        private void SyncAudioUi()
        {
            if (!_audio.Available) return;
            _suppressSliderEvents = true;
            _volSlider.Value = _audio.GetVolume();
            _suppressSliderEvents = false;
            UpdateMuteButton();
            UpdateAudioDeviceButton();
        }

        private void UpdateMuteButton()
        {
            bool muted = _audio.GetMute();
            _muteBtn.Content = muted ? "Muted" : "Mute";
            _muteBtn.Background = muted ? Ui.AccentDim : Ui.Panel;
            _muteBtn.Foreground = muted ? Ui.Accent : Ui.Text;
        }

        private void SyncBrightnessUi()
        {
            if (!_brightness.Available) return;
            int b = _brightness.GetBrightness();
            if (b < 0) return;
            _suppressSliderEvents = true;
            _briSlider.Value = b;
            _suppressSliderEvents = false;
            if (_briValueText != null) _briValueText.Text = b.ToString() + "% · tablet only";
        }

        // ------------------------------------------------------------------
        // Launcher
        // ------------------------------------------------------------------

        private void RebuildTiles()
        {
            _tilePanel.Children.Clear();
            foreach (ShortcutItem s in _cfg.Shortcuts)
            {
                ShortcutItem item = s; // capture per iteration
                string detail = item.HideTarget ? "private shortcut" :
                    (item.IsWeb() ? ShortHost(item.Target) : "app");
                ImageSource icon = item.IsWeb() && !item.HideTarget
                    ? SiteIconCache.TryLoad(item.Target) : null;
                _tilePanel.Children.Add(Ui.Tile(item.Name, detail, icon,
                    delegate { Launch(item); }));
                if (item.IsWeb() && !item.HideTarget && icon == null)
                {
                    SiteIconCache.EnsureCached(item.Target, delegate
                    {
                        Dispatcher.BeginInvoke(new Action(RebuildTiles));
                    });
                }
            }

            Button add = Ui.Tile("+", "add shortcut", delegate { OpenSettings(); });
            add.Background = Ui.SurfaceLow;
            add.BorderBrush = Ui.Line;
            _tilePanel.Children.Add(add);
        }

        private static string ShortHost(string url)
        {
            try
            {
                Uri u = new Uri(url);
                string h = u.Host;
                if (h.StartsWith("www.")) h = h.Substring(4);
                return h;
            }
            catch { return "web"; }
        }

        private void Launch(ShortcutItem item)
        {
            try
            {
                if (_projectorSleeping) WakeProjector();
                WinForms.Screen projector = GetProjectorScreen(true);
                if (projector == null) return;

                ProcessStartInfo psi = new ProcessStartInfo();
                string expectedExe = item.Target;

                if (item.IsWeb())
                {
                    // Website tiles are intentionally Supermium-only. Falling
                    // back to an arbitrary registered browser makes monitor
                    // targeting and fullscreen behaviour unpredictable.
                    if (string.IsNullOrEmpty(_cfg.BrowserPath) ||
                        !File.Exists(_cfg.BrowserPath))
                    {
                        MessageBox.Show(this,
                            "Supermium wasn't found. Choose its chrome.exe or supermium.exe in Settings first.",
                            "Projector Dashboard");
                        OpenSettings();
                        return;
                    }
                    psi.FileName = _cfg.BrowserPath;
                    psi.Arguments = BuildBrowserArguments(item, projector);
                    expectedExe = _cfg.BrowserPath;
                }
                else
                {
                    psi.FileName = item.Target;
                    if (!string.IsNullOrEmpty(item.Args)) psi.Arguments = item.Args;
                }

                psi.UseShellExecute = true;
                HashSet<IntPtr> before = ScreenUtil.SnapshotTopLevelWindows();
                Process started = Process.Start(psi);
                int processId = 0;
                if (started != null)
                {
                    try { processId = started.Id; }
                    catch { }
                    started.Dispose();
                }
                BeginProjectorPlacement(before, processId, expectedExe, projector);
                if (_cfg.TabletOnlyMode) EnterTabletAppMode();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    string.Format("Couldn't open \"{0}\".\n\n{1}\n\nEdit the shortcut in Settings.",
                        item.Name, ex.Message),
                    "Projector Dashboard");
            }
        }

        // Kills every running instance of the configured browser exe, then
        // starts a fresh one. Handy when Supermium hangs or a page wedges the
        // GPU on this old Atom.
        private void RestartBrowser()
        {
            string path = _cfg.BrowserPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MessageBox.Show(this,
                    "No browser is configured. Set the Supermium path in Settings first.",
                    "Projector Dashboard");
                return;
            }
            try
            {
                if (_projectorSleeping) WakeProjector();
                WinForms.Screen projector = GetProjectorScreen(true);
                if (projector == null) return;

                StopBrowserProcesses(path);

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = path;
                System.Drawing.Rectangle b = projector.Bounds;
                psi.Arguments = string.Format(
                    "--new-window --window-position={0},{1} --window-size={2},{3} {4}",
                    b.X, b.Y, b.Width, b.Height,
                    _cfg.LaunchFullscreen ? "--kiosk about:blank" : "--start-maximized about:blank");
                psi.UseShellExecute = true;
                HashSet<IntPtr> before = ScreenUtil.SnapshotTopLevelWindows();
                Process started = Process.Start(psi);
                int processId = 0;
                if (started != null)
                {
                    try { processId = started.Id; } catch { }
                    started.Dispose();
                }
                BeginProjectorPlacement(before, processId, path, projector);
                if (_cfg.TabletOnlyMode) EnterTabletAppMode();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Couldn't restart the browser.\n\n" + ex.Message,
                    "Projector Dashboard");
            }
        }

        private static int StopBrowserProcesses(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            int stopped = 0;
            string procName;
            try { procName = System.IO.Path.GetFileNameWithoutExtension(path); }
            catch { return 0; }
            Process[] procs = Process.GetProcessesByName(procName);
            foreach (Process pr in procs)
            {
                try
                {
                    // Only stop processes from the configured Supermium exe.
                    // A null path generally means Windows denied MainModule
                    // access to a child process of that same browser family.
                    string exe = null;
                    try { exe = pr.MainModule.FileName; } catch { }
                    if (exe == null || string.Equals(exe, path,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        pr.Kill();
                        pr.WaitForExit(2000);
                        stopped++;
                    }
                }
                catch { }
                finally { pr.Dispose(); }
            }
            return stopped;
        }

        private void OpenOverheadView()
        {
            if (!_cfg.LocationConfigured)
            {
                OpenSettings(4);
                if (_locationStatus != null)
                    _locationStatus.Text = "Save a location before opening the live sky map.";
                return;
            }
            ClosePreview();
            if (_settingsOverlay != null &&
                _settingsOverlay.Visibility == Visibility.Visible) CloseSettings();
            if (_overheadOverlay == null) return;
            _overheadOverlay.Visibility = Visibility.Visible;
            UpdateOverheadUi();
            _nextSkyRefreshUtc = DateTime.MinValue;
            RefreshSky();
        }

        private void ToggleOverheadFromMain()
        {
            if (!_cfg.LocationConfigured)
            {
                OpenOverheadView();
                return;
            }
            if (_cfg.TabletOnlyMode || _projector == null)
            {
                OpenOverheadView();
                return;
            }
            ToggleProjectorOverhead();
        }

        private void CloseOverheadView()
        {
            if (_overheadOverlay != null)
                _overheadOverlay.Visibility = Visibility.Collapsed;
            _nextSkyRefreshUtc = DateTime.MinValue;
        }

        private void ToggleProjectorOverhead()
        {
            if (_cfg.TabletOnlyMode || _projector == null) return;
            if (!_projectorOverhead)
            {
                _overheadRestoresApp = !_projectorSleeping;
                if (_overheadRestoresApp) SleepProjector();
                _projectorOverhead = true;
                _projector.ShowOverhead(true);
                _projector.UpdateOverhead(_sky, _cfg.FacingDegrees);
            }
            else
            {
                if (_overheadRestoresApp)
                {
                    _overheadRestoresApp = false;
                    WakeProjector();
                }
                else
                {
                    _projectorOverhead = false;
                    _projector.ShowOverhead(false);
                    _projector.SetMode(_projectorSleeping ? "clock" : _cfg.ProjectorMode);
                }
            }
            UpdateOverheadUi();
            _nextSkyRefreshUtc = DateTime.MinValue;
        }

        private void UpdateOverheadUi()
        {
            if (_overheadMap == null) return;
            UpdateAircraftRangeUi();
            _overheadMap.SetData(_sky, _cfg.FacingDegrees);
            if (_projector != null && _projectorOverhead)
                _projector.UpdateOverhead(_sky, _cfg.FacingDegrees);
            if (_ceilingMapBtn != null)
            {
                _ceilingMapBtn.Content = _projectorOverhead
                    ? "Ceiling map: On" : "Show on projector";
                _ceilingMapBtn.Background = _projectorOverhead ? Ui.Accent : Ui.AccentDim;
                _ceilingMapBtn.Foreground = _projectorOverhead ? Ui.Ink : Ui.Accent;
            }
            if (_mainOverheadBtn != null)
            {
                bool canProject = !_cfg.TabletOnlyMode && _projector != null;
                _mainOverheadBtn.Content = canProject
                    ? (_projectorOverhead ? "SKY ON · STOP" : "PROJECT SKY")
                    : "VIEW SKY";
                _mainOverheadBtn.Background = _projectorOverhead
                    ? Ui.AccentDim : Ui.Accent;
                _mainOverheadBtn.Foreground = _projectorOverhead
                    ? Ui.Accent : Ui.Ink;
            }
            if (!_cfg.LocationConfigured)
            {
                _overheadFeedText.Text = "Location is not configured";
                _overheadObjectsText.Text = "Open Settings › Ambient first.";
                _overheadUpdatedText.Text = "";
                return;
            }
            if (_sky == null)
            {
                _overheadFeedText.Text = "Connecting to live aircraft and ISS feeds…";
                _overheadObjectsText.Text = "Calculating planet and star positions locally…";
                _overheadUpdatedText.Text = "";
                return;
            }

            _overheadFeedText.Text = _sky.AircraftFeedAvailable
                ? (OverheadAircraftCount().ToString() + " overhead · " +
                    _sky.Planes.Count.ToString() + " within " +
                    _cfg.AircraftRadiusKm.ToString() + " km · " +
                    (_sky.AircraftFeedName.Length == 0 ? "live ADS-B" : _sky.AircraftFeedName))
                : ("Aircraft feeds offline\n" + _sky.AircraftError);
            _overheadFeedText.Foreground = _sky.AircraftFeedAvailable ? Ui.Text : Ui.Danger;
            _overheadUpdatedText.Text = "Updated " + _sky.UpdatedUtc.ToLocalTime().ToString("h:mm:ss tt") +
                " · markers move continuously · data sync " + SkyRefreshSeconds().ToString() + " sec";

            List<string> objects = new List<string>();
            int planeDetails = 0;
            for (int planeIndex = 0; planeIndex < _sky.Planes.Count &&
                planeDetails < 6; planeIndex++)
            {
                PlaneReading plane = _sky.Planes[planeIndex];
                double elevation = AmbientService.AircraftElevationDegrees(
                    plane.DistanceKm, plane.AltitudeFeet);
                if (elevation < AircraftMinimumElevation()) continue;
                planeDetails++;
                string identity = plane.Label;
                if (!string.IsNullOrWhiteSpace(plane.AircraftType))
                    identity += " / " + plane.AircraftType;
                if (!string.IsNullOrWhiteSpace(plane.Registration))
                    identity += " / " + plane.Registration;
                string course = plane.HasTrack
                    ? " · course " + AmbientService.Cardinal(plane.TrackDegrees) + " " +
                        Math.Round(plane.TrackDegrees).ToString("0") + "°"
                    : "";
                objects.Add(identity + " · " + Math.Round(plane.DistanceKm).ToString("0") +
                    " km · " + Math.Round(elevation).ToString("0") +
                    "° above horizon · " + AmbientService.Direction(plane.BearingDegrees,
                    _cfg.FacingDegrees) + course);
            }
            int overheadCount = OverheadAircraftCount();
            if (overheadCount > planeDetails)
                objects.Add("+ " + (overheadCount - planeDetails).ToString() +
                    " more aircraft plotted on the live view");
            if (_sky.Iss.Available)
                objects.Add(_sky.Iss.AboveHorizon
                    ? "ISS / NORAD 25544 · " + Math.Round(_sky.Iss.ElevationDegrees).ToString("0") +
                        "° above horizon · " + AmbientService.Direction(_sky.Iss.BearingDegrees,
                        _cfg.FacingDegrees)
                    : "ISS · below horizon");
            foreach (PlanetReading planet in _sky.Planets)
                objects.Add(planet.Name + " · " +
                    Math.Round(planet.AltitudeDegrees).ToString("0") + "° above horizon · " +
                    AmbientService.Direction(planet.BearingDegrees, _cfg.FacingDegrees));
            int starLimit = Math.Min(4, _sky.Stars.Count);
            for (int starIndex = 0; starIndex < starLimit; starIndex++)
            {
                StarReading star = _sky.Stars[starIndex];
                objects.Add(star.Name + " · major star · " +
                    Math.Round(star.AltitudeDegrees).ToString("0") + "° above horizon · " +
                    AmbientService.Direction(star.BearingDegrees, _cfg.FacingDegrees));
            }
            _overheadObjectsText.Text = objects.Count == 0
                ? "No tracked objects are above the horizon right now."
                : string.Join("\n\n", objects.ToArray());
        }

        private bool OverheadIsActive()
        {
            return _projectorOverhead || (_overheadOverlay != null &&
                _overheadOverlay.Visibility == Visibility.Visible);
        }

        private int SkyRefreshSeconds()
        {
            if (!OverheadIsActive()) return 45;
            if (_sky != null && (!_sky.AircraftFeedAvailable ||
                _sky.AircraftFeedName.StartsWith("airplanes.live",
                    StringComparison.OrdinalIgnoreCase))) return 45;
            return 15;
        }

        private int OverheadAircraftCount()
        {
            if (_sky == null) return 0;
            int count = 0;
            foreach (PlaneReading plane in _sky.Planes)
            {
                double elevation = AmbientService.AircraftElevationDegrees(
                    plane.DistanceKm, plane.AltitudeFeet);
                if (elevation >= AircraftMinimumElevation()) count++;
            }
            return count;
        }

        private double AircraftMinimumElevation()
        {
            return _cfg.AircraftRadiusKm > 40 ? 0.5 : 5.0;
        }

        private void OpenPreview()
        {
            if (_cfg.TabletOnlyMode || _previewOverlay == null) return;
            CloseOverheadView();
            _previewOverlay.Visibility = Visibility.Visible;
            RefreshPreview();
            Dispatcher.BeginInvoke(new Action(RefreshPreview),
                DispatcherPriority.Loaded);
        }

        private void ClosePreview()
        {
            _previewMirror.Detach();
            if (_previewOverlay != null)
                _previewOverlay.Visibility = Visibility.Collapsed;
            if (_previewStatus != null)
                _previewStatus.Visibility = Visibility.Visible;
        }

        private IntPtr FindPreviewSource()
        {
            if (_cfg.TabletOnlyMode) return IntPtr.Zero;
            WinForms.Screen projector = GetProjectorScreen(false);
            if (projector == null) return IntPtr.Zero;
            IntPtr top = ScreenUtil.FindTopAppWindowOnScreen(projector,
                GetCurrentProcessId());
            if (top != IntPtr.Zero) return top;
            return ScreenUtil.WindowIsOnScreen(_lastLaunchedWindow, projector)
                ? _lastLaunchedWindow : IntPtr.Zero;
        }

        private void RefreshPreview()
        {
            if (_previewOverlay == null ||
                _previewOverlay.Visibility != Visibility.Visible) return;
            IntPtr source = FindPreviewSource();
            if (source == IntPtr.Zero)
            {
                _previewMirror.Detach();
                _previewStatus.Text = "No projector app is open";
                _previewStatus.Visibility = Visibility.Visible;
                return;
            }

            if (_previewMirror.SourceWindow != source || !_previewMirror.Attached)
            {
                IntPtr destination = new WindowInteropHelper(this).Handle;
                if (!_previewMirror.Attach(destination, source))
                {
                    _previewStatus.Text = "Windows could not mirror this app";
                    _previewStatus.Visibility = Visibility.Visible;
                    return;
                }
            }
            _previewStatus.Visibility = Visibility.Collapsed;
            UpdatePreviewRectangle();
        }

        private void UpdatePreviewRectangle()
        {
            if (!_previewMirror.Attached || _previewViewport == null ||
                !_previewViewport.IsVisible || _previewViewport.ActualWidth < 2 ||
                _previewViewport.ActualHeight < 2) return;
            try
            {
                Point client = PointToScreen(new Point(0, 0));
                Point topLeft = _previewViewport.PointToScreen(new Point(0, 0));
                Point bottomRight = _previewViewport.PointToScreen(new Point(
                    _previewViewport.ActualWidth, _previewViewport.ActualHeight));
                int left = (int)Math.Round(topLeft.X - client.X) + 1;
                int top = (int)Math.Round(topLeft.Y - client.Y) + 1;
                int width = Math.Max(1,
                    (int)Math.Round(bottomRight.X - topLeft.X) - 2);
                int height = Math.Max(1,
                    (int)Math.Round(bottomRight.Y - topLeft.Y) - 2);
                _previewMirror.Update(left, top, width, height);
            }
            catch { }
        }

        private void OpenFullscreenPreview()
        {
            if (_cfg.TabletOnlyMode) return;
            IntPtr source = FindPreviewSource();
            if (source == IntPtr.Zero)
            {
                if (_previewStatus != null)
                {
                    _previewStatus.Text = "Open an app on the projector first";
                    _previewStatus.Visibility = Visibility.Visible;
                }
                return;
            }

            ClosePreview();
            if (_fullscreenMirror != null)
            {
                try { _fullscreenMirror.Close(); }
                catch { }
            }
            WinForms.Screen tablet = ScreenUtil.FindByDevice(
                _cfg.TabletDevice, WinForms.Screen.PrimaryScreen);
            _fullscreenMirror = new MirrorWindow(tablet, source);
            _mirrorFullscreen = true;
            _fullscreenMirror.Show();
            EnsureReturnOverlay(tablet);
            if (!_returnOverlay.IsVisible) _returnOverlay.Show();
            _returnOverlay.Topmost = true;
            WindowState = WindowState.Minimized;
        }

        private void EnsureReturnOverlay(WinForms.Screen tablet)
        {
            if (_returnOverlay != null) return;
            _returnOverlay = new ReturnOverlayWindow(tablet,
                delegate { ReturnToDashboard(); },
                delegate { EmergencyOff(); });
        }

        private void EnterTabletAppMode()
        {
            if (!_cfg.TabletOnlyMode) return;
            WinForms.Screen tablet = ScreenUtil.FindByDevice(
                _cfg.TabletDevice, WinForms.Screen.PrimaryScreen);
            EnsureReturnOverlay(tablet);
            _tabletAppMode = true;
            if (!_returnOverlay.IsVisible) _returnOverlay.Show();
            _returnOverlay.Topmost = true;
            WindowState = WindowState.Minimized;
        }

        private void ReturnToDashboard()
        {
            bool reopenPreview = _mirrorFullscreen;
            _mirrorFullscreen = false;
            if (_fullscreenMirror != null)
            {
                try { _fullscreenMirror.Close(); }
                catch { }
                _fullscreenMirror = null;
            }
            _tabletAppMode = false;
            if (_returnOverlay != null) _returnOverlay.Hide();
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            Activate();
            if (reopenPreview) OpenPreview();
        }

        private void EmergencyOff()
        {
            _launchGeneration++;
            if (_alarmSoundTimer != null) _alarmSoundTimer.Stop();
            if (_alarmAudioState != null)
            {
                _audio.EndAlarm(_alarmAudioState, _cfg.AudioDeviceId);
                _alarmAudioState = null;
            }
            if (_alarmOverlay != null)
                _alarmOverlay.Visibility = Visibility.Collapsed;

            string browser = _cfg.BrowserPath;
            if (string.IsNullOrEmpty(browser)) browser = AppConfig.DetectSupermium();
            StopBrowserProcesses(browser);
            ClosePreview();
            _mirrorFullscreen = false;
            if (_fullscreenMirror != null)
            {
                try { _fullscreenMirror.Close(); }
                catch { }
                _fullscreenMirror = null;
            }
            if (_returnOverlay != null) _returnOverlay.Hide();
            _tabletAppMode = false;

            // Lock before powering down so waking a screen never exposes the
            // user's tabs or unlocked desktop. The short delay lets Windows
            // finish switching to its secure lock screen first.
            ScreenUtil.LockTablet();
            DispatcherTimer powerOff = new DispatcherTimer();
            powerOff.Interval = TimeSpan.FromMilliseconds(450);
            powerOff.Tick += delegate
            {
                powerOff.Stop();
                ScreenUtil.TurnOffAllDisplays();
            };
            powerOff.Start();
        }

        private void ShowTouchKeyboard()
        {
            try
            {
                // The docked TabTip spans the bottom of the tablet and fights
                // TouchMousePointer. The classic OSK is movable, so keep it in
                // the lower-left beside the reserved touch-pad rectangle.
                HashSet<IntPtr> before = ScreenUtil.SnapshotTopLevelWindows();
                Process keyboard = Process.Start("osk.exe");
                if (keyboard != null)
                {
                    int pid = 0;
                    try { pid = keyboard.Id; } catch { }
                    keyboard.Dispose();
                    BeginKeyboardPlacement(before, pid);
                }
            }
            catch
            {
                try
                {
                    string tabTip = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                        "microsoft shared", "ink", "TabTip.exe");
                    if (File.Exists(tabTip)) Process.Start(tabTip);
                }
                catch { }
            }
        }

        private string BuildBrowserArguments(ShortcutItem item, WinForms.Screen projector)
        {
            System.Drawing.Rectangle b = projector.Bounds;
            string extra = string.Format(
                "--new-window --window-position={0},{1} --window-size={2},{3} ",
                b.X, b.Y, b.Width, b.Height);
            if (item.GpuDisableDirectComposition)
                extra += "--disable-direct-composition ";
            if (item.GpuUseD3d9)
                extra += "--use-angle=d3d9 ";
            if (item.GpuDisableVsync)
                extra += "--disable-gpu-vsync ";
            if (item.Incognito) extra += "--incognito ";

            string args = item.Args ?? "";
            if (string.IsNullOrEmpty(args.Trim()))
            {
                args = _cfg.LaunchFullscreen ? "--kiosk {url}" : "--start-maximized {url}";
            }
            else if (_cfg.LaunchFullscreen &&
                args.IndexOf("--kiosk", StringComparison.OrdinalIgnoreCase) < 0 &&
                args.IndexOf("--app", StringComparison.OrdinalIgnoreCase) < 0)
            {
                args = "--kiosk " + args;
            }

            if (args.Contains("{url}"))
                return extra + args.Replace("{url}", "\"" + item.Target + "\"");
            return extra + args + " \"" + item.Target + "\"";
        }

        private WinForms.Screen GetProjectorScreen(bool showError)
        {
            if (_cfg.TabletOnlyMode)
            {
                return ScreenUtil.FindByDevice(_cfg.TabletDevice,
                    WinForms.Screen.PrimaryScreen);
            }
            WinForms.Screen projector = null;
            if (!string.IsNullOrEmpty(_cfg.ProjectorDevice))
                projector = ScreenUtil.FindByDevice(_cfg.ProjectorDevice, null);
            if (projector == null && showError)
            {
                MessageBox.Show(this,
                    "The projector isn't connected or assigned. Open Settings and use Reassign displays.",
                    "Projector Dashboard");
            }
            return projector;
        }

        private void BeginProjectorPlacement(HashSet<IntPtr> before, int processId,
            string expectedExe, WinForms.Screen projector)
        {
            int generation = ++_launchGeneration;
            int ticks = 0;
            int stableTicks = 0;
            IntPtr tracked = IntPtr.Zero;
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(200);
            timer.Tick += delegate
            {
                ticks++;
                if (generation != _launchGeneration || ticks > 150)
                {
                    timer.Stop();
                    return;
                }

                IntPtr candidate = ScreenUtil.FindLaunchWindow(before, processId,
                    expectedExe, ticks > 15, ticks > 3);
                if (candidate != IntPtr.Zero)
                {
                    if (candidate != tracked)
                    {
                        tracked = candidate;
                        stableTicks = 0;
                    }
                    else
                    {
                        stableTicks++;
                    }
                    _lastLaunchedWindow = tracked;
                    ScreenUtil.PlaceExternalWindow(tracked, projector, _cfg.LaunchFullscreen);
                    if (!_cfg.TabletOnlyMode) Activate();

                    // Keep watching long enough to step past splash screens
                    // and Chromium's initial hand-off to its real window.
                    if (stableTicks >= 15 && ticks > 50) timer.Stop();
                }
            };
            timer.Start();
        }

        private void BeginKeyboardPlacement(HashSet<IntPtr> before, int processId)
        {
            WinForms.Screen tablet = ScreenUtil.FindByDevice(
                _cfg.TabletDevice, WinForms.Screen.PrimaryScreen);
            int ticks = 0;
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(120);
            timer.Tick += delegate
            {
                ticks++;
                IntPtr hwnd = ScreenUtil.FindLaunchWindow(before, processId,
                    "osk.exe", ticks > 3, true);
                if (hwnd != IntPtr.Zero)
                {
                    ScreenUtil.PlaceKeyboardWindow(hwnd, tablet,
                        _cfg.ReservedWidth, _cfg.ReservedHeight);
                    if (ticks > 12) timer.Stop();
                }
                if (ticks > 50) timer.Stop();
            };
            timer.Start();
        }

        private void ToggleProjectorSleep()
        {
            if (_projectorSleeping) WakeProjector();
            else SleepProjector();
        }

        private void SleepProjector()
        {
            WinForms.Screen projector = GetProjectorScreen(true);
            if (projector == null) return;
            _launchGeneration++;
            _sleepingWindows = ScreenUtil.HideAppWindowsOnScreen(projector,
                GetCurrentProcessId());
            _projectorSleeping = true;
            if (_projector != null)
            {
                _projector.SetMode("clock");
                _projector.ShowOverhead(_projectorOverhead);
                if (_projectorOverhead)
                    _projector.UpdateOverhead(_sky, _cfg.FacingDegrees);
                _projector.UpdateTime(DateTime.Now);
            }
            UpdateSleepButton();
            if (_cfg.TabletOnlyMode) ReturnToDashboard();
            else Activate();
        }

        private void WakeProjector()
        {
            WinForms.Screen projector = GetProjectorScreen(false);
            bool restoredTabletApp = _cfg.TabletOnlyMode &&
                _sleepingWindows != null && _sleepingWindows.Count > 0;
            ScreenUtil.RestoreAppWindows(_sleepingWindows, projector,
                _cfg.LaunchFullscreen);
            _sleepingWindows.Clear();
            _projectorSleeping = false;
            _projectorOverhead = false;
            _overheadRestoresApp = false;
            if (_projector != null)
            {
                _projector.ShowOverhead(false);
                _projector.SetMode(_cfg.ProjectorMode);
                _projector.UpdateTime(DateTime.Now);
            }
            UpdateSleepButton();
            UpdateOverheadUi();
            if (restoredTabletApp) EnterTabletAppMode();
            else Activate();
        }

        private void UpdateSleepButton()
        {
            if (_sleepBtn == null) return;
            if (_cfg.TabletOnlyMode)
                _sleepBtn.Content = _projectorSleeping ? "WAKE" : "HIDE";
            else
                _sleepBtn.Content = _projectorSleeping ? "WAKE" : "IDLE";
            _sleepBtn.Background = _projectorSleeping ? Ui.AccentDim : Ui.Panel;
            _sleepBtn.BorderBrush = _projectorSleeping ? Ui.Accent : Ui.Line;
        }

        private void CloseProjectedApp()
        {
            if (_projectorSleeping) WakeProjector();
            WinForms.Screen projector = GetProjectorScreen(true);
            if (projector == null) return;
            _launchGeneration++;

            IntPtr hwnd = ScreenUtil.FindTopAppWindowOnScreen(projector,
                GetCurrentProcessId());
            if (hwnd == IntPtr.Zero && ScreenUtil.WindowIsOnScreen(
                _lastLaunchedWindow, projector))
            {
                hwnd = _lastLaunchedWindow;
            }
            if (!ScreenUtil.RequestCloseWindow(hwnd))
            {
                MessageBox.Show(this, _cfg.TabletOnlyMode
                    ? "There isn't another app open on the tablet."
                    : "There isn't an app open on the projector.",
                    "Projector Dashboard");
            }
            if (_cfg.TabletOnlyMode) ReturnToDashboard();
            else Activate();
        }

        private static int GetCurrentProcessId()
        {
            using (Process current = Process.GetCurrentProcess())
            {
                return current.Id;
            }
        }

        private void CheckForUpdates()
        {
            if (_updateBusy) return;
            _updateBusy = true;
            _updateBtn.IsEnabled = false;
            _updateBtn.Content = "Checking…";

            Task<UpdateResult> task = Task.Factory.StartNew(
                delegate
                {
                    return SelfUpdater.CheckAndDownload(
                        delegate(string status)
                        {
                            Dispatcher.BeginInvoke(new Action(
                                delegate { if (_updateBtn != null) _updateBtn.Content = status; }));
                        });
                });
            task.ContinueWith(delegate(Task<UpdateResult> completed)
            {
                UpdateResult result;
                if (completed.IsFaulted)
                {
                    string detail = completed.Exception == null
                        ? "Unknown update error."
                        : completed.Exception.GetBaseException().Message;
                    result = new UpdateResult
                    {
                        Kind = UpdateResultKind.Error,
                        Message = "Update failed.\n\n" + detail
                    };
                }
                else
                {
                    result = completed.Result;
                }

                if (result.Kind == UpdateResultKind.Ready)
                {
                    _updateBtn.Content = "Installing " + result.ReleaseVersion + "…";
                    string error;
                    if (SelfUpdater.BeginInstall(result, out error))
                    {
                        _cfg.Save();
                        Close();
                        return;
                    }
                    result.Kind = UpdateResultKind.Error;
                    result.Message = "Windows could not start the installer.\n\n" + error;
                }

                _updateBusy = false;
                _updateBtn.IsEnabled = true;
                _updateBtn.Content = "Check for update";
                MessageBox.Show(this, result.Message, "Projector Dashboard Update");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ------------------------------------------------------------------
        // Settings
        // ------------------------------------------------------------------

        private void OpenSettings()
        {
            OpenSettings(0);
        }

        private void OpenSettings(int pageIndex)
        {
            ClosePreview();
            CloseOverheadView();
            RefreshShortcutList(-1);
            _editResW.Text = _cfg.ReservedWidth.ToString();
            _editResH.Text = _cfg.ReservedHeight.ToString();
            UpdateProjectorModeButton();
            UpdateLaunchModeButton();
            UpdateTabletModeButton();
            UpdateAlarmUi();
            _editBrowser.Text = _cfg.BrowserPath ?? "";
            PopulateAmbientEditor();
            RefreshAudioDeviceLists();
            SelectSettingsPage(pageIndex);
            _settingsOverlay.Visibility = Visibility.Visible;
        }

        private void CloseSettings()
        {
            CleanupDraftShortcuts();
            _settingsOverlay.Visibility = Visibility.Collapsed;
        }

        private Grid BuildSettingsOverlay()
        {
            Grid overlay = new Grid();
            overlay.Background = Ui.Overlay;

            Grid inner = new Grid();
            inner.Margin = new Thickness(24, 12, 24, 8);
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock title = Ui.Label("Settings", 29, Ui.Text);
            title.FontWeight = FontWeights.SemiBold;
            titleRow.Children.Add(title);
            Button done = Ui.Btn("Done", 17, Ui.Accent, Ui.Ink,
                delegate { CloseSettings(); });
            Grid.SetColumn(done, 1);
            titleRow.Children.Add(done);
            inner.Children.Add(titleRow);

            WrapPanel tabs = new WrapPanel();
            tabs.Orientation = Orientation.Horizontal;
            tabs.Margin = new Thickness(0, 6, 0, 4);
            string[] names = new string[] { "Shortcuts", "Projector", "Audio + browser", "Alarm", "Ambient" };
            _settingsTabButtons = new Button[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                Button tab = Ui.Btn(names[i], 16, Ui.Panel, Ui.Text,
                    delegate { SelectSettingsPage(index); });
                tab.MinWidth = 165;
                tab.Margin = new Thickness(0, 0, 8, 0);
                _settingsTabButtons[i] = tab;
                tabs.Children.Add(tab);
            }
            Grid.SetRow(tabs, 1);
            inner.Children.Add(tabs);

            Grid pageHost = new Grid();
            _settingsPages = new Grid[]
            {
                BuildShortcutSettingsPage(),
                BuildProjectorSettingsPage(),
                BuildAudioSettingsPage(),
                BuildAlarmSettingsPage(),
                BuildAmbientSettingsPage()
            };
            foreach (Grid page in _settingsPages) pageHost.Children.Add(page);
            Grid.SetRow(pageHost, 2);
            inner.Children.Add(pageHost);

            overlay.Children.Add(inner);
            return overlay;
        }

        private Grid BuildShortcutSettingsPage()
        {
            Grid page = new Grid();
            page.Margin = new Thickness(0, 4, 0, 0);
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42, GridUnitType.Star) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58, GridUnitType.Star) });

            Grid left = new Grid();
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _shortcutList = new ListBox();
            _shortcutList.Background = Ui.Panel;
            _shortcutList.Foreground = Ui.Text;
            _shortcutList.BorderBrush = Ui.Line;
            _shortcutList.FontSize = 16;
            Style itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(ListBoxItem.MinHeightProperty, 44.0));
            itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(10, 5, 10, 5)));
            _shortcutList.ItemContainerStyle = itemStyle;
            _shortcutList.SelectionChanged += delegate { LoadSelectedIntoEditor(); };
            left.Children.Add(_shortcutList);
            StackPanel listButtons = new StackPanel();
            listButtons.Orientation = Orientation.Horizontal;
            listButtons.Margin = new Thickness(0, 6, 0, 0);
            listButtons.Children.Add(SmallBtn("Add", delegate { AddShortcut(); }));
            listButtons.Children.Add(SmallBtn("Delete", delegate { RemoveShortcut(); }));
            listButtons.Children.Add(SmallBtn("Up", delegate { MoveShortcut(-1); }));
            listButtons.Children.Add(SmallBtn("Down", delegate { MoveShortcut(1); }));
            Grid.SetRow(listButtons, 1);
            left.Children.Add(listButtons);
            page.Children.Add(left);

            StackPanel editor = new StackPanel();
            _editName = Ui.Input("", 16);
            editor.Children.Add(CompactField("Name", _editName));
            _editTarget = Ui.Input("", 16);
            editor.Children.Add(CompactField("Target", _editTarget));
            _editArgs = Ui.Input("", 16);
            editor.Children.Add(CompactField("Arguments", _editArgs));

            WrapPanel options = new WrapPanel();
            options.Orientation = Orientation.Horizontal;
            options.Margin = new Thickness(108, 3, 0, 3);
            _editDirectCompositionBtn = SmallBtn("Direct comp: Off",
                delegate { ToggleEditorFlag(0); });
            _editDirectCompositionBtn.ToolTip = "Supermium flag: --disable-direct-composition";
            options.Children.Add(_editDirectCompositionBtn);
            _editD3d9Btn = SmallBtn("D3D9: Off", delegate { ToggleEditorFlag(1); });
            _editD3d9Btn.ToolTip = "Supermium flag: --use-angle=d3d9";
            options.Children.Add(_editD3d9Btn);
            _editVsyncBtn = SmallBtn("VSync: Off", delegate { ToggleEditorFlag(2); });
            _editVsyncBtn.ToolTip = "Supermium flag: --disable-gpu-vsync";
            options.Children.Add(_editVsyncBtn);
            _editIncogBtn = SmallBtn("Incognito: Off", delegate { ToggleEditorFlag(3); });
            options.Children.Add(_editIncogBtn);
            editor.Children.Add(options);

            StackPanel editButtons = new StackPanel();
            editButtons.Orientation = Orientation.Horizontal;
            editButtons.Margin = new Thickness(108, 3, 0, 0);
            _editHideTargetBtn = SmallBtn("Hide address: Off",
                delegate { ToggleEditorFlag(4); });
            _editHideTargetBtn.ToolTip = "Hide the real site/address and icon everywhere except this editor.";
            editButtons.Children.Add(_editHideTargetBtn);
            editButtons.Children.Add(SmallBtn("Browse app", delegate { BrowseForProgram(); }));
            Button save = SmallBtn("Save shortcut", delegate { SaveEditedShortcut(); });
            save.Background = Ui.Accent;
            save.Foreground = Ui.Ink;
            editButtons.Children.Add(save);
            editor.Children.Add(editButtons);
            Grid.SetColumn(editor, 2);
            page.Children.Add(editor);
            return page;
        }

        private Grid BuildProjectorSettingsPage()
        {
            Grid page = TwoColumnSettingsPage();
            StackPanel left = new StackPanel();
            left.Children.Add(SectionTitle("Projector behaviour"));
            StackPanel modeRow = new StackPanel { Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 8) };
            _projectorModeBtn = SmallBtn("Idle: Clock", delegate { ToggleProjectorMode(); });
            modeRow.Children.Add(_projectorModeBtn);
            _launchModeBtn = SmallBtn("Launch: Fullscreen", delegate { ToggleLaunchMode(); });
            modeRow.Children.Add(_launchModeBtn);
            _tabletModeBtn = SmallBtn("Mode: Two screens",
                delegate { ToggleTabletOnlyMode(); });
            modeRow.Children.Add(_tabletModeBtn);
            left.Children.Add(modeRow);
            left.Children.Add(WrappedNote(
                _cfg.TabletOnlyMode
                ? "Tablet-only launches fullscreen here and keeps a small Dashboard / emergency-off control above the app. Sleep hides the app without closing its tabs."
                : "Idle hides open apps and tabs without closing them, then shows the ambient screen until you wake it."));
            Button sleep = SmallBtn("Idle / wake now", delegate { ToggleProjectorSleep(); });
            sleep.Margin = new Thickness(0, 10, 0, 0);
            left.Children.Add(sleep);
            page.Children.Add(left);

            StackPanel right = new StackPanel();
            right.Children.Add(SectionTitle("Tablet and displays"));
            right.Children.Add(WrappedNote(
                "TouchMousePointer reserve: narrower and taller by default. Values are physical pixels."));
            StackPanel sizeRow = new StackPanel { Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 8) };
            _editResW = Ui.Input("", 17); _editResW.Width = 100;
            sizeRow.Children.Add(_editResW);
            TextBlock by = Ui.Label("×", 20, Ui.TextDim);
            by.Margin = new Thickness(8, 0, 8, 0);
            by.VerticalAlignment = VerticalAlignment.Center;
            sizeRow.Children.Add(by);
            _editResH = Ui.Input("", 17); _editResH.Width = 100;
            sizeRow.Children.Add(_editResH);
            Button apply = SmallBtn("Apply size", delegate { ApplyReservedFromEditor(); });
            apply.Margin = new Thickness(10, 0, 0, 0);
            sizeRow.Children.Add(apply);
            right.Children.Add(sizeRow);
            right.Children.Add(WrappedNote(_brightness.Available
                ? "Tablet brightness controls only the built-in tablet backlight; projector brightness must be changed on the projector."
                : "Windows does not expose tablet backlight control on this device, so the dashboard hides that slider."));
            Button displays = SmallBtn("Reassign displays", delegate { ReassignDisplays(); });
            displays.Margin = new Thickness(0, 10, 0, 0);
            right.Children.Add(displays);
            right.Children.Add(WrappedNote(CecControl.FindClient().Length > 0
                ? "HDMI-CEC: free libCEC client detected. The Power menu can send physical projector standby."
                : "HDMI-CEC: this PC can blank its video signal. Physical projector standby additionally needs CEC-capable HDMI hardware (commonly a USB-CEC adapter) and the free libCEC client."));
            Grid.SetColumn(right, 2);
            page.Children.Add(right);
            return page;
        }

        private Grid BuildAudioSettingsPage()
        {
            Grid page = TwoColumnSettingsPage();
            StackPanel left = new StackPanel();
            left.Children.Add(SectionTitle("Audio output"));
            _audioDeviceCombo = AudioComboBox();
            left.Children.Add(_audioDeviceCombo);
            Button apply = SmallBtn("Use selected output", delegate { SelectAudioDevice(); });
            apply.Margin = new Thickness(0, 8, 0, 0);
            left.Children.Add(apply);
            _audioDeviceStatus = WrappedNote("");
            _audioDeviceStatus.Margin = new Thickness(0, 8, 0, 0);
            left.Children.Add(_audioDeviceStatus);
            left.Children.Add(WrappedNote(
                "Windows keeps a separate volume and mute value for every endpoint. Supermium follows this system default output."));
            page.Children.Add(left);

            StackPanel right = new StackPanel();
            right.Children.Add(SectionTitle("Supermium"));
            _editBrowser = Ui.Input("", 15);
            right.Children.Add(_editBrowser);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(SmallBtn("Browse", delegate { BrowseBrowser(); }));
            buttons.Children.Add(SmallBtn("Save path", delegate { SaveBrowserPath(); }));
            buttons.Children.Add(SmallBtn("Reset now", delegate { RestartBrowser(); }));
            _updateBtn = SmallBtn("Check for update", delegate { CheckForUpdates(); });
            _updateBtn.ToolTip = "Install the latest stable dashboard release. Current: " +
                SelfUpdater.CurrentVersion;
            buttons.Children.Add(_updateBtn);
            right.Children.Add(buttons);
            right.Children.Add(WrappedNote(
                "Reset now force-closes and reopens Supermium. Use it only when the browser or video playback is stuck."));
            Grid.SetColumn(right, 2);
            page.Children.Add(right);
            return page;
        }

        private Grid BuildAlarmSettingsPage()
        {
            Grid page = TwoColumnSettingsPage();
            StackPanel left = new StackPanel();
            left.Children.Add(SectionTitle("Daily alarm"));
            StackPanel timeRow = new StackPanel { Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 8) };
            _editAlarmTime = Ui.Input("8:00 AM", 17);
            _editAlarmTime.Width = 135;
            timeRow.Children.Add(_editAlarmTime);
            Button save = SmallBtn("Save time", delegate { SaveAlarmTime(); });
            save.Margin = new Thickness(10, 0, 0, 0);
            timeRow.Children.Add(save);
            _alarmToggleBtn = SmallBtn("Alarm: Off", delegate { ToggleAlarm(); });
            timeRow.Children.Add(_alarmToggleBtn);
            left.Children.Add(timeRow);
            left.Children.Add(WrappedNote(
                "The alarm repeats daily while the dashboard is awake and includes Dismiss and 10-minute Snooze."));
            page.Children.Add(left);

            StackPanel right = new StackPanel();
            right.Children.Add(SectionTitle("Tablet alarm speaker"));
            _alarmDeviceCombo = AudioComboBox();
            right.Children.Add(_alarmDeviceCombo);
            Button speaker = SmallBtn("Save tablet speaker", delegate { SaveAlarmDevice(); });
            speaker.Margin = new Thickness(0, 8, 0, 0);
            right.Children.Add(speaker);
            _alarmDeviceStatus = WrappedNote("");
            _alarmDeviceStatus.Margin = new Thickness(0, 8, 0, 0);
            right.Children.Add(_alarmDeviceStatus);
            right.Children.Add(WrappedNote(
                "When ringing, this endpoint is forced to 100% and unmuted. Other app/browser audio sessions are muted, then every previous output, volume, and mute state is restored."));
            Grid.SetColumn(right, 2);
            page.Children.Add(right);
            return page;
        }

        private Grid BuildAmbientSettingsPage()
        {
            Grid page = TwoColumnSettingsPage();
            StackPanel left = new StackPanel();
            left.Children.Add(SectionTitle("One-time location"));
            left.Children.Add(WrappedNote(
                "Enter a city, town, or postal code. Windows Location Services are never used."));
            _editLocationSearch = Ui.Input("", 17);
            _editLocationSearch.Margin = new Thickness(0, 8, 0, 0);
            left.Children.Add(_editLocationSearch);
            Button find = SmallBtn("Find and save", delegate { FindAmbientLocation(); });
            find.Margin = new Thickness(0, 8, 0, 0);
            left.Children.Add(find);
            _locationStatus = WrappedNote("No location saved.");
            _locationStatus.Foreground = Ui.Accent;
            left.Children.Add(_locationStatus);
            left.Children.Add(WrappedNote(
                "Weather + sunrise: Open-Meteo. Aircraft: adsb.lol with airplanes.live fallback. ISS: Where the ISS at? Stars and planets calculate locally. No API keys, accounts, subscriptions, or paid services."));
            page.Children.Add(left);

            StackPanel right = new StackPanel();
            right.Children.Add(SectionTitle("Accuracy + orientation"));
            right.Children.Add(WrappedNote(
                "You can refine the saved coordinates. Enter the true compass direction toward the physical top edge of the projected image. If that edge is above your head, use the direction your head points; if it is above your feet, use the direction your feet point. The ceiling sky is seen from below, so east/west are mirrored from a ground map."));
            StackPanel coordinates = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _editLatitude = Ui.Input("", 16);
            _editLatitude.Width = 132;
            _editLatitude.ToolTip = "Latitude (-90 to 90)";
            coordinates.Children.Add(_editLatitude);
            _editLongitude = Ui.Input("", 16);
            _editLongitude.Width = 132;
            _editLongitude.Margin = new Thickness(8, 0, 0, 0);
            _editLongitude.ToolTip = "Longitude (-180 to 180)";
            coordinates.Children.Add(_editLongitude);
            Button save = SmallBtn("Save exact", delegate { SaveExactLocation(); });
            save.Margin = new Thickness(8, 0, 0, 0);
            coordinates.Children.Add(save);
            right.Children.Add(coordinates);

            StackPanel orientation = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 0)
            };
            _facingBtn = SmallBtn("Top edge: N", delegate { CycleFacingDirection(); });
            orientation.Children.Add(_facingBtn);
            _editFacingDegrees = Ui.Input("0", 16);
            _editFacingDegrees.Width = 70;
            _editFacingDegrees.ToolTip = "True heading toward the projected image's top edge (0 north, 90 east).";
            orientation.Children.Add(_editFacingDegrees);
            Button saveFacing = SmallBtn("Save heading °", delegate { SaveFacingDirection(); });
            saveFacing.Margin = new Thickness(8, 0, 0, 0);
            orientation.Children.Add(saveFacing);
            right.Children.Add(orientation);
            StackPanel choices = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _temperatureUnitBtn = SmallBtn("Units: °C", delegate { ToggleTemperatureUnit(); });
            choices.Children.Add(_temperatureUnitBtn);
            Button refresh = SmallBtn("Refresh now", delegate { StartAmbientRefresh(true); });
            choices.Children.Add(refresh);
            right.Children.Add(choices);
            right.Children.Add(WrappedNote(
                "Planet positions are calculated locally from JPL orbital elements. Bearings are true-north compass directions; cloud and daylight can still hide an object."));
            Grid.SetColumn(right, 2);
            page.Children.Add(right);
            return page;
        }

        private Grid TwoColumnSettingsPage()
        {
            Grid page = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return page;
        }

        private Grid CompactField(string label, Control control)
        {
            Grid row = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(102) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock text = Ui.Label(label, 15, Ui.TextDim);
            text.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(text);
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            return row;
        }

        private ComboBox AudioComboBox()
        {
            ComboBox combo = new ComboBox();
            combo.MinHeight = 52;
            combo.FontSize = 17;
            combo.FontFamily = new FontFamily(Ui.FontUi);
            // The stock Windows ComboBox chrome paints its closed face white on
            // some tablet themes, so use an explicit dark foreground here.
            combo.Background = Brushes.White;
            combo.Foreground = Ui.ComboText;
            combo.BorderBrush = Ui.Line;
            combo.Padding = new Thickness(10, 6, 10, 6);
            Style items = new Style(typeof(ComboBoxItem));
            items.Setters.Add(new Setter(ComboBoxItem.MinHeightProperty, 46.0));
            items.Setters.Add(new Setter(ComboBoxItem.PaddingProperty, new Thickness(10, 7, 10, 7)));
            items.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, Brushes.White));
            items.Setters.Add(new Setter(ComboBoxItem.ForegroundProperty, Ui.ComboText));
            combo.ItemContainerStyle = items;
            return combo;
        }

        private TextBlock SectionTitle(string text)
        {
            TextBlock title = Ui.Label(text, 21, Ui.Text);
            title.FontWeight = FontWeights.SemiBold;
            return title;
        }

        private TextBlock WrappedNote(string text)
        {
            TextBlock note = Ui.Label(text, 15, Ui.TextDim);
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(0, 7, 0, 0);
            return note;
        }

        private void SelectSettingsPage(int index)
        {
            if (_settingsPages == null || index < 0 || index >= _settingsPages.Length) index = 0;
            _settingsPageIndex = index;
            for (int i = 0; i < _settingsPages.Length; i++)
            {
                bool selected = i == index;
                _settingsPages[i].Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                _settingsTabButtons[i].Background = selected ? Ui.AccentDim : Ui.Panel;
                _settingsTabButtons[i].BorderBrush = selected ? Ui.Accent : Ui.Line;
            }
        }

        private void PopulateAmbientEditor()
        {
            if (_editLocationSearch != null)
                _editLocationSearch.Text = _cfg.LocationName ?? "";
            if (_editLatitude != null)
                _editLatitude.Text = _cfg.LocationConfigured
                    ? _cfg.Latitude.ToString("0.######", CultureInfo.InvariantCulture) : "";
            if (_editLongitude != null)
                _editLongitude.Text = _cfg.LocationConfigured
                    ? _cfg.Longitude.ToString("0.######", CultureInfo.InvariantCulture) : "";
            if (_facingBtn != null)
                _facingBtn.Content = "Top edge: " + AmbientService.Cardinal(_cfg.FacingDegrees);
            if (_editFacingDegrees != null)
                _editFacingDegrees.Text = _cfg.FacingDegrees.ToString(CultureInfo.InvariantCulture);
            if (_temperatureUnitBtn != null)
                _temperatureUnitBtn.Content = _cfg.UseFahrenheit ? "Units: °F" : "Units: °C";
            if (_locationStatus != null)
            {
                _locationStatus.Text = _cfg.LocationConfigured
                    ? (_cfg.LocationName + " · " +
                        _cfg.Latitude.ToString("0.####", CultureInfo.InvariantCulture) + ", " +
                        _cfg.Longitude.ToString("0.####", CultureInfo.InvariantCulture))
                    : "No location saved.";
            }
        }

        private void FindAmbientLocation()
        {
            if (_editLocationSearch == null) return;
            string search = _editLocationSearch.Text.Trim();
            if (search.Length == 0)
            {
                _locationStatus.Text = "Enter a city, town, or postal code.";
                return;
            }
            _locationStatus.Text = "Finding location…";
            AmbientService.FindLocationAsync(search).ContinueWith(delegate(Task<AmbientLocation> task)
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Exception error = task.Exception == null ? null : task.Exception.GetBaseException();
                    _locationStatus.Text = error == null ? "Location lookup failed."
                        : "Location lookup failed: " + error.Message;
                    return;
                }
                AmbientLocation location = task.Result;
                _cfg.LocationConfigured = true;
                _cfg.LocationName = location.DisplayName;
                _cfg.Latitude = location.Latitude;
                _cfg.Longitude = location.Longitude;
                _cfg.Save();
                PopulateAmbientEditor();
                StartAmbientRefresh(true);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void SaveExactLocation()
        {
            double latitude, longitude;
            if (_editLatitude == null || _editLongitude == null ||
                !TryCoordinate(_editLatitude.Text, out latitude) ||
                !TryCoordinate(_editLongitude.Text, out longitude) ||
                latitude < -90.0 || latitude > 90.0 ||
                longitude < -180.0 || longitude > 180.0)
            {
                _locationStatus.Text = "Enter latitude -90…90 and longitude -180…180.";
                return;
            }
            _cfg.Latitude = latitude;
            _cfg.Longitude = longitude;
            _cfg.LocationConfigured = true;
            if (string.IsNullOrWhiteSpace(_cfg.LocationName))
                _cfg.LocationName = "Custom location";
            _cfg.Save();
            PopulateAmbientEditor();
            StartAmbientRefresh(true);
        }

        private static bool TryCoordinate(string text, out double value)
        {
            return double.TryParse((text ?? "").Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) ||
                double.TryParse((text ?? "").Trim(), NumberStyles.Float,
                CultureInfo.CurrentCulture, out value);
        }

        private void CycleFacingDirection()
        {
            _cfg.FacingDegrees = (_cfg.FacingDegrees + 45) % 360;
            _cfg.Save();
            PopulateAmbientEditor();
            ApplyAmbientUi();
        }

        private void SaveFacingDirection()
        {
            double heading;
            if (_editFacingDegrees == null ||
                !TryCoordinate(_editFacingDegrees.Text, out heading) ||
                heading < 0.0 || heading >= 360.0)
            {
                _locationStatus.Text = "Heading must be 0…359° clockwise from true north.";
                return;
            }
            _cfg.FacingDegrees = (int)Math.Round(heading) % 360;
            _cfg.Save();
            PopulateAmbientEditor();
            ApplyAmbientUi();
        }

        private void ToggleTemperatureUnit()
        {
            _cfg.UseFahrenheit = !_cfg.UseFahrenheit;
            _cfg.Save();
            _weather = null;
            PopulateAmbientEditor();
            StartAmbientRefresh(true);
        }

        private void StartAmbientRefresh(bool immediate)
        {
            if (!_cfg.LocationConfigured)
            {
                ApplyAmbientUi();
                return;
            }
            if (immediate)
            {
                _nextWeatherRefreshUtc = DateTime.MinValue;
                _nextSkyRefreshUtc = DateTime.MinValue;
            }
            RefreshWeather();
            RefreshSky();
        }

        private void RefreshWeather()
        {
            if (!_cfg.LocationConfigured || _weatherRefreshBusy) return;
            _weatherRefreshBusy = true;
            _nextWeatherRefreshUtc = DateTime.UtcNow.AddMinutes(15);
            AmbientService.GetWeatherAsync(_cfg.Latitude, _cfg.Longitude,
                _cfg.UseFahrenheit).ContinueWith(delegate(Task<WeatherReading> task)
            {
                _weatherRefreshBusy = false;
                if (!task.IsCanceled && !task.IsFaulted) _weather = task.Result;
                else if (_weather == null && _weatherMetaText != null)
                    _weatherMetaText.Text = "Weather offline · retrying";
                ApplyAmbientUi();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void UpdateSunriseText(DateTime now)
        {
            if (_sunriseText == null) return;
            if (_weather == null || _weather.NextSunrise == DateTime.MinValue)
            {
                _sunriseText.Text = "NEXT SUNRISE  --";
                return;
            }
            TimeSpan until = _weather.NextSunrise - now;
            if (until.TotalSeconds < 0) until = TimeSpan.Zero;
            string countdown = until.TotalHours >= 1.0
                ? ((int)until.TotalHours).ToString() + "h " + until.Minutes.ToString("00") + "m"
                : Math.Max(0, until.Minutes).ToString() + "m";
            _sunriseText.Text = "NEXT SUNRISE  " +
                _weather.NextSunrise.ToString("ddd h:mm tt") + "  /  " + countdown;
        }

        private void RefreshSky()
        {
            if (!_cfg.LocationConfigured || _skyRefreshBusy) return;
            _skyRefreshBusy = true;
            _nextSkyRefreshUtc = DateTime.UtcNow.AddSeconds(SkyRefreshSeconds());
            int requestedRadiusKm = _cfg.AircraftRadiusKm;
            AmbientService.GetSkyAsync(_cfg.Latitude, _cfg.Longitude,
                requestedRadiusKm).ContinueWith(
                delegate(Task<SkyReading> task)
                {
                    _skyRefreshBusy = false;
                    if (!task.IsCanceled && !task.IsFaulted &&
                        task.Result.AircraftRadiusKm == _cfg.AircraftRadiusKm)
                        _sky = task.Result;
                    else if (!task.IsCanceled && !task.IsFaulted)
                        _nextSkyRefreshUtc = DateTime.MinValue;
                    ApplyAmbientUi();
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void ApplyAmbientUi()
        {
            if (_weatherTempText == null || _skyCountText == null) return;
            if (!_cfg.LocationConfigured)
            {
                _weatherTempText.Text = "--";
                _weatherConditionText.Text = "SET LOCATION";
                _weatherMetaText.Text = "Settings › Ambient";
                UpdateSunriseText(DateTime.Now);
                _skyCountText.Text = "SET LOCATION";
                if (_aircraftSourceText != null)
                    _aircraftSourceText.Text = "AIR TRAFFIC  /  LOCATION NEEDED";
                _aircraftText.Text = "Location is not configured";
                _orbitText.Text = "ISS, planets, and major stars appear here";
                if (_projector != null)
                {
                    _projector.UpdateWeather("--", "Set location on the tablet",
                        "Settings › Ambient", DateTime.MinValue);
                    _projector.UpdateSky("Set a location to show aircraft, ISS, planets, and stars");
                }
                UpdateOverheadUi();
                return;
            }

            if (_weather != null)
            {
                string degree = Math.Round(_weather.Temperature).ToString("0") + "°";
                _weatherTempText.Text = degree;
                _weatherConditionText.Text = _weather.Condition.ToUpperInvariant();
                _weatherMetaText.Text = "Feels " + Math.Round(_weather.FeelsLike).ToString("0") + "°  ·  H " +
                    Math.Round(_weather.High).ToString("0") + "° / L " +
                    Math.Round(_weather.Low).ToString("0") + "°  ·  Wind " +
                    Math.Round(_weather.WindSpeed).ToString("0") + " " + _weather.WindUnit;
                UpdateSunriseText(DateTime.Now);
                if (_projector != null)
                {
                    _projector.UpdateWeather(degree + (_weather.Fahrenheit ? "F" : "C"),
                        _weather.Condition, _cfg.LocationName + "  ·  " +
                        _weatherMetaText.Text, _weather.NextSunrise);
                }
            }
            else
            {
                _weatherTempText.Text = "--";
                _weatherConditionText.Text = "LOADING WEATHER";
                _weatherMetaText.Text = _cfg.LocationName;
                UpdateSunriseText(DateTime.Now);
            }

            if (_sky == null)
            {
                _skyCountText.Text = "SCANNING";
                if (_aircraftSourceText != null)
                    _aircraftSourceText.Text = "AIR TRAFFIC  /  CONNECTING";
                _aircraftText.Text = "Checking nearby airspace…";
                _orbitText.Text = "Calculating ISS, planets, and stars…";
                UpdateOverheadUi();
                return;
            }

            if (!_sky.AircraftFeedAvailable)
            {
                _skyCountText.Text = "FEED OFFLINE";
                if (_aircraftSourceText != null)
                    _aircraftSourceText.Text = "AIR TRAFFIC  /  BOTH FREE FEEDS FAILED";
                _aircraftText.Text = (_sky.AircraftError.Length == 0
                    ? "Aircraft feed unavailable" : _sky.AircraftError) +
                    " · retrying automatically";
            }
            else
            {
                if (_aircraftSourceText != null)
                    _aircraftSourceText.Text = "AIR TRAFFIC  /  " +
                        (_sky.AircraftFeedName.Length == 0
                            ? "LIVE ADS-B" : _sky.AircraftFeedName.ToUpperInvariant());
                _skyCountText.Text = _sky.Planes.Count == 0 ? "CLEAR AIRSPACE" :
                    _sky.Planes.Count.ToString() + (_sky.Planes.Count == 1 ? " AIRCRAFT" : " AIRCRAFT");
                List<string> traffic = new List<string>();
                int trafficLimit = Math.Min(4, _sky.Planes.Count);
                for (int trafficIndex = 0; trafficIndex < trafficLimit; trafficIndex++)
                {
                    PlaneReading plane = _sky.Planes[trafficIndex];
                    traffic.Add(plane.Label + " · " + Math.Round(plane.DistanceKm).ToString("0") +
                        " km " + AmbientService.Direction(plane.BearingDegrees, _cfg.FacingDegrees) +
                        " · " + plane.AltitudeFeet.ToString("N0") + " ft");
                }
                _aircraftText.Text = traffic.Count == 0
                    ? "No aircraft within " + _cfg.AircraftRadiusKm.ToString() + " km"
                    :
                    string.Join("   |   ", traffic.ToArray());
            }

            List<string> orbit = new List<string>();
            if (_sky.Iss.Available)
            {
                orbit.Add(_sky.Iss.AboveHorizon
                    ? "ISS " + Math.Round(_sky.Iss.ElevationDegrees).ToString("0") + "° " +
                        AmbientService.Direction(_sky.Iss.BearingDegrees, _cfg.FacingDegrees)
                    : "ISS below horizon");
            }
            else orbit.Add("ISS feed offline");
            int planetLimit = Math.Min(3, _sky.Planets.Count);
            for (int i = 0; i < planetLimit; i++)
            {
                PlanetReading planet = _sky.Planets[i];
                orbit.Add(planet.Name + " " + Math.Round(planet.AltitudeDegrees).ToString("0") + "° " +
                    AmbientService.Direction(planet.BearingDegrees, _cfg.FacingDegrees));
            }
            if (_sky.Planets.Count == 0) orbit.Add("No planets above horizon");
            int brightStarLimit = Math.Min(2, _sky.Stars.Count);
            for (int brightStarIndex = 0; brightStarIndex < brightStarLimit;
                brightStarIndex++)
            {
                StarReading star = _sky.Stars[brightStarIndex];
                orbit.Add(star.Name + " star " +
                    Math.Round(star.AltitudeDegrees).ToString("0") + "° " +
                    AmbientService.Direction(star.BearingDegrees, _cfg.FacingDegrees));
            }
            _orbitText.Text = string.Join("   |   ", orbit.ToArray());

            if (_projector != null)
            {
                string planes = _sky.AircraftFeedAvailable
                    ? (_sky.Planes.Count == 0 ? "No nearby aircraft" :
                        _sky.Planes.Count.ToString() + " aircraft nearby")
                    : "Aircraft feed offline";
                _projector.UpdateSky(planes + "  ·  " + string.Join("  ·  ", orbit.ToArray()));
            }
            UpdateOverheadUi();
        }

        private void RefreshAudioDeviceLists()
        {
            List<AudioDeviceInfo> devices = _audio.GetDevices();
            FillAudioCombo(_audioDeviceCombo, devices, _cfg.AudioDeviceId,
                _audio.CurrentDeviceId);
            FillAudioCombo(_alarmDeviceCombo, devices, _cfg.AlarmAudioDeviceId, "");
            if (_audioDeviceStatus != null)
            {
                _audioDeviceStatus.Text = _audio.Available
                    ? "Controlling: " + _audio.CurrentDeviceName
                    : "No active Windows audio output is available.";
            }
            if (_alarmDeviceStatus != null)
            {
                AudioDeviceInfo alarm = _alarmDeviceCombo == null
                    ? null : _alarmDeviceCombo.SelectedItem as AudioDeviceInfo;
                _alarmDeviceStatus.Text = alarm != null &&
                    _audio.IsSafeTabletSpeaker(alarm.Id)
                    ? "Alarm output: " + alarm.Name
                    : "No safe built-in Speakers endpoint is selected; the alarm cannot be enabled.";
            }
        }

        private static void FillAudioCombo(ComboBox combo,
            List<AudioDeviceInfo> devices, string preferredId, string fallbackId)
        {
            if (combo == null) return;
            combo.Items.Clear();
            int select = -1;
            for (int i = 0; i < devices.Count; i++)
            {
                combo.Items.Add(devices[i]);
                if (string.Equals(devices[i].Id, preferredId,
                    StringComparison.OrdinalIgnoreCase)) select = i;
                else if (select < 0 && string.Equals(devices[i].Id, fallbackId,
                    StringComparison.OrdinalIgnoreCase)) select = i;
            }
            combo.SelectedIndex = select;
        }

        private void SelectAudioDevice()
        {
            AudioDeviceInfo selected = _audioDeviceCombo == null
                ? null : _audioDeviceCombo.SelectedItem as AudioDeviceInfo;
            if (selected == null) return;
            if (!_audio.SelectDevice(selected.Id))
            {
                MessageBox.Show(this,
                    "Windows couldn't switch to that output. It may have disconnected.",
                    "Projector Dashboard");
                RefreshAudioDeviceLists();
                return;
            }
            _cfg.AudioDeviceId = selected.Id;
            _cfg.Save();
            SyncAudioUi();
            UpdateAudioDeviceButton();
            RefreshAudioDeviceLists();
        }

        private void SaveAlarmDevice()
        {
            AudioDeviceInfo selected = _alarmDeviceCombo == null
                ? null : _alarmDeviceCombo.SelectedItem as AudioDeviceInfo;
            if (selected == null) return;
            if (!_audio.IsSafeTabletSpeaker(selected.Id))
            {
                MessageBox.Show(this,
                    "For safety, the alarm only accepts an endpoint identified as built-in Speakers. Bluetooth, headphones, USB, HDMI, and display/projector audio are rejected.",
                    "Projector Dashboard");
                RefreshAudioDeviceLists();
                return;
            }
            _cfg.AlarmAudioDeviceId = selected.Id;
            _cfg.Save();
            RefreshAudioDeviceLists();
        }

        private void UpdateAudioDeviceButton()
        {
            if (_audioDeviceBtn == null) return;
            _audioDeviceBtn.Content = "Output: " + ShortDeviceName(_audio.CurrentDeviceName);
            _audioDeviceBtn.ToolTip = string.IsNullOrEmpty(_audio.CurrentDeviceName)
                ? "Choose the Windows audio output device."
                : _audio.CurrentDeviceName;
        }

        private static string ShortDeviceName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "none";
            string shortName = name;
            int paren = shortName.IndexOf(" (");
            if (paren > 0) shortName = shortName.Substring(0, paren);
            if (shortName.Length > 17) shortName = shortName.Substring(0, 16) + "…";
            return shortName;
        }

        private Grid BuildAlarmOverlay()
        {
            Grid overlay = new Grid();
            overlay.Background = Ui.AlarmShade;

            StackPanel panel = new StackPanel();
            panel.HorizontalAlignment = HorizontalAlignment.Center;
            panel.VerticalAlignment = VerticalAlignment.Center;

            TextBlock label = Ui.Label("ALARM", 28, Ui.Danger);
            label.FontWeight = FontWeights.Bold;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(label);

            _alarmRingTime = Ui.Label("", 92, Ui.Text);
            _alarmRingTime.FontFamily = new FontFamily(Ui.FontLight);
            _alarmRingTime.HorizontalAlignment = HorizontalAlignment.Center;
            _alarmRingTime.Margin = new Thickness(0, 0, 0, 24);
            panel.Children.Add(_alarmRingTime);

            _alarmRingStatus = Ui.Label("", 17, Ui.TextDim);
            _alarmRingStatus.TextAlignment = TextAlignment.Center;
            _alarmRingStatus.TextWrapping = TextWrapping.Wrap;
            _alarmRingStatus.MaxWidth = 700;
            _alarmRingStatus.Margin = new Thickness(0, -14, 0, 16);
            panel.Children.Add(_alarmRingStatus);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Center;
            Button snooze = Ui.Btn("Snooze 10 min", 21, Ui.PanelHi, Ui.Text,
                delegate { SnoozeAlarm(); });
            snooze.Margin = new Thickness(0, 0, 16, 0);
            buttons.Children.Add(snooze);
            Button dismiss = Ui.Btn("Dismiss", 21, Ui.DangerFill,
                Ui.DangerText, delegate { DismissAlarm(); });
            dismiss.BorderBrush = Ui.Danger;
            buttons.Children.Add(dismiss);
            panel.Children.Add(buttons);

            overlay.Children.Add(panel);
            return overlay;
        }

        private Button SmallBtn(string text, RoutedEventHandler click)
        {
            Button b = Ui.Btn(text, 16, Ui.Panel, Ui.Text, click);
            b.Margin = new Thickness(0, 0, 10, 0);
            return b;
        }

        private TextBlock FieldLabel(string text)
        {
            TextBlock t = Ui.Label(text, 15, Ui.TextDim);
            t.Margin = new Thickness(0, 14, 0, 6);
            return t;
        }

        private void RefreshShortcutList(int selectIndex)
        {
            _shortcutList.Items.Clear();
            foreach (ShortcutItem s in _cfg.Shortcuts)
            {
                _shortcutList.Items.Add(string.Format("{0}   —   {1}", s.Name,
                    s.HideTarget ? "(hidden address)" : s.Target));
            }
            if (selectIndex >= 0 && selectIndex < _shortcutList.Items.Count)
            {
                _shortcutList.SelectedIndex = selectIndex;
            }
        }

        private void LoadSelectedIntoEditor()
        {
            int i = _shortcutList.SelectedIndex;
            if (i < 0 || i >= _cfg.Shortcuts.Count)
            {
                _editName.Text = "";
                _editTarget.Text = "";
                _editArgs.Text = "";
                SetToggleBtn(_editDirectCompositionBtn, "Direct comp", false);
                SetToggleBtn(_editD3d9Btn, "D3D9", false);
                SetToggleBtn(_editVsyncBtn, "VSync", false);
                SetToggleBtn(_editIncogBtn, "Incognito", false);
                SetToggleBtn(_editHideTargetBtn, "Hide address", false);
                return;
            }
            ShortcutItem s = _cfg.Shortcuts[i];
            _editName.Text = s.Name;
            _editTarget.Text = s.Target;
            _editArgs.Text = s.Args ?? "";
            SetToggleBtn(_editDirectCompositionBtn, "Direct comp",
                s.GpuDisableDirectComposition);
            SetToggleBtn(_editD3d9Btn, "D3D9", s.GpuUseD3d9);
            SetToggleBtn(_editVsyncBtn, "VSync", s.GpuDisableVsync);
            SetToggleBtn(_editIncogBtn, "Incognito", s.Incognito);
            SetToggleBtn(_editHideTargetBtn, "Hide address", s.HideTarget);
        }

        // 0/1/2 are the independent GPU flags; 3 is Incognito; 4 hides the
        // address and site icon. Every new shortcut starts with all flags off.
        private void ToggleEditorFlag(int which)
        {
            int i = _shortcutList.SelectedIndex;
            if (i < 0 || i >= _cfg.Shortcuts.Count) return;
            ShortcutItem s = _cfg.Shortcuts[i];
            if (which == 0)
            {
                s.GpuDisableDirectComposition = !s.GpuDisableDirectComposition;
                SetToggleBtn(_editDirectCompositionBtn, "Direct comp",
                    s.GpuDisableDirectComposition);
            }
            else if (which == 1)
            {
                s.GpuUseD3d9 = !s.GpuUseD3d9;
                SetToggleBtn(_editD3d9Btn, "D3D9", s.GpuUseD3d9);
            }
            else if (which == 2)
            {
                s.GpuDisableVsync = !s.GpuDisableVsync;
                SetToggleBtn(_editVsyncBtn, "VSync", s.GpuDisableVsync);
            }
            else if (which == 3)
            {
                s.Incognito = !s.Incognito;
                SetToggleBtn(_editIncogBtn, "Incognito", s.Incognito);
            }
            else
            {
                s.HideTarget = !s.HideTarget;
                SetToggleBtn(_editHideTargetBtn, "Hide address", s.HideTarget);
                RefreshShortcutList(i);
                RebuildTiles();
            }
            _cfg.Save();
        }

        private void SetToggleBtn(Button b, string label, bool on)
        {
            b.Content = label + (on ? ": On" : ": Off");
            b.Background = on ? Ui.AccentDim : Ui.Panel;
            b.BorderBrush = on ? Ui.Accent : Ui.Line;
        }

        private void AddShortcut()
        {
            _cfg.Shortcuts.Add(new ShortcutItem("New shortcut", ""));
            RefreshShortcutList(_cfg.Shortcuts.Count - 1);
        }

        private void RemoveShortcut()
        {
            int i = _shortcutList.SelectedIndex;
            if (i < 0 || i >= _cfg.Shortcuts.Count) return;
            _cfg.Shortcuts.RemoveAt(i);
            _cfg.Save();
            RefreshShortcutList(Math.Min(i, _cfg.Shortcuts.Count - 1));
            RebuildTiles();
        }

        private void MoveShortcut(int direction)
        {
            int i = _shortcutList.SelectedIndex;
            int j = i + direction;
            if (i < 0 || i >= _cfg.Shortcuts.Count) return;
            if (j < 0 || j >= _cfg.Shortcuts.Count) return;
            ShortcutItem tmp = _cfg.Shortcuts[i];
            _cfg.Shortcuts[i] = _cfg.Shortcuts[j];
            _cfg.Shortcuts[j] = tmp;
            _cfg.Save();
            RefreshShortcutList(j);
            RebuildTiles();
        }

        private void SaveEditedShortcut()
        {
            int i = _shortcutList.SelectedIndex;
            if (i < 0 || i >= _cfg.Shortcuts.Count) return;
            ShortcutItem s = _cfg.Shortcuts[i];
            if (string.IsNullOrEmpty(_editTarget.Text.Trim()) ||
                _editTarget.Text.Trim() == "https://" || _editTarget.Text.Trim() == "http://")
            {
                MessageBox.Show(this, "Enter a complete website address or program path.",
                    "Projector Dashboard");
                return;
            }
            s.Name = string.IsNullOrEmpty(_editName.Text.Trim())
                ? "Shortcut" : _editName.Text.Trim();
            s.Target = _editTarget.Text.Trim();
            s.Args = _editArgs.Text.Trim();
            _cfg.Save();
            RefreshShortcutList(i);
            RebuildTiles();
        }

        private void CleanupDraftShortcuts()
        {
            bool changed = false;
            for (int i = _cfg.Shortcuts.Count - 1; i >= 0; i--)
            {
                string target = (_cfg.Shortcuts[i].Target ?? "").Trim();
                if (target.Length == 0 || target == "https://" || target == "http://")
                {
                    _cfg.Shortcuts.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed) _cfg.Save();
            RebuildTiles();
        }

        private void BrowseForProgram()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*";
            dlg.Title = "Choose a program";
            bool? ok = dlg.ShowDialog(this);
            if (ok == true)
            {
                _editTarget.Text = dlg.FileName;
                if (_editName.Text.Trim() == "" || _editName.Text == "New shortcut")
                {
                    _editName.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                }
            }
        }

        private void BrowseBrowser()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*";
            dlg.Title = "Choose the browser (Supermium chrome.exe)";
            bool? ok = dlg.ShowDialog(this);
            if (ok == true) _editBrowser.Text = dlg.FileName;
        }

        private void SaveBrowserPath()
        {
            string path = _editBrowser.Text.Trim();
            if (path.Length > 0 && !File.Exists(path))
            {
                MessageBox.Show(this,
                    "That file doesn't exist. Choose Supermium's chrome.exe or supermium.exe.",
                    "Projector Dashboard");
                return;
            }
            _cfg.BrowserPath = path;
            _cfg.Save();
        }

        private void ToggleProjectorMode()
        {
            _cfg.ProjectorMode = (_cfg.ProjectorMode == "blank") ? "clock" : "blank";
            _cfg.Save();
            if (_projector != null) _projector.SetMode(
                _projectorSleeping ? "clock" : _cfg.ProjectorMode);
            if (_projector != null) _projector.UpdateTime(DateTime.Now);
            UpdateProjectorModeButton();
        }

        private void UpdateProjectorModeButton()
        {
            _projectorModeBtn.Content = (_cfg.ProjectorMode == "blank")
                ? "Idle: Blank" : "Idle: Clock";
        }

        private void ToggleLaunchMode()
        {
            _cfg.LaunchFullscreen = !_cfg.LaunchFullscreen;
            _cfg.Save();
            UpdateLaunchModeButton();
        }

        private void UpdateLaunchModeButton()
        {
            if (_launchModeBtn == null) return;
            _launchModeBtn.Content = _cfg.LaunchFullscreen
                ? "Launch: Fullscreen" : "Launch: Maximized";
            _launchModeBtn.Background = _cfg.LaunchFullscreen ? Ui.AccentDim : Ui.Panel;
            _launchModeBtn.BorderBrush = _cfg.LaunchFullscreen ? Ui.Accent : Ui.Line;
        }

        private void ToggleTabletOnlyMode()
        {
            _cfg.TabletOnlyMode = !_cfg.TabletOnlyMode;
            _cfg.Save();
            Program.RestartRequested = true;
            Close();
        }

        private void UpdateTabletModeButton()
        {
            if (_tabletModeBtn == null) return;
            _tabletModeBtn.Content = _cfg.TabletOnlyMode
                ? "Mode: Tablet only" : "Mode: Two screens";
            _tabletModeBtn.Background = _cfg.TabletOnlyMode
                ? Ui.AccentDim : Ui.Panel;
            _tabletModeBtn.BorderBrush = _cfg.TabletOnlyMode
                ? Ui.Accent : Ui.Line;
        }

        private void ToggleAlarm()
        {
            if (!_cfg.AlarmEnabled &&
                !_audio.IsSafeTabletSpeaker(_cfg.AlarmAudioDeviceId))
            {
                MessageBox.Show(this,
                    "Choose and save the built-in tablet Speakers first. The alarm will not fall back to Bluetooth, HDMI, headphones, or projector audio.",
                    "Projector Dashboard");
                SelectSettingsPage(3);
                return;
            }
            _cfg.AlarmEnabled = !_cfg.AlarmEnabled;
            _cfg.Save();
            _snoozeUntil = DateTime.MinValue;
            UpdateAlarmUi();
        }

        private void OpenAutoLockPopup()
        {
            UpdateAutoLockUi();
            if (_autoLockPopup != null) _autoLockPopup.IsOpen = true;
        }

        private void ToggleAutoLock()
        {
            _cfg.AutoLockEnabled = !_cfg.AutoLockEnabled;
            _cfg.Save();
            _lastAutoLockDate = DateTime.MinValue;
            UpdateAutoLockUi();
        }

        private void SaveAutoLockTime()
        {
            DateTime parsed;
            if (_autoLockTime == null ||
                !DateTime.TryParse(_autoLockTime.Text.Trim(), out parsed))
            {
                MessageBox.Show(this, "Enter a time like 1:00 AM or 23:00.",
                    "Projector Dashboard");
                return;
            }
            _cfg.AutoLockHour = parsed.Hour;
            _cfg.AutoLockMinute = parsed.Minute;
            _cfg.Save();
            _lastAutoLockDate = DateTime.MinValue;
            UpdateAutoLockUi();
            if (_autoLockPopup != null) _autoLockPopup.IsOpen = false;
        }

        private void UpdateAutoLockUi()
        {
            DateTime time = DateTime.Today.AddHours(_cfg.AutoLockHour)
                .AddMinutes(_cfg.AutoLockMinute);
            string label = _cfg.AutoLockEnabled
                ? "Auto-lock: " + time.ToString("h:mm tt") : "Auto-lock: Off";
            if (_autoLockTime != null) _autoLockTime.Text = time.ToString("h:mm tt");
            if (_autoLockBtn != null)
            {
                _autoLockBtn.Content = label;
                _autoLockBtn.Background = _cfg.AutoLockEnabled ? Ui.AccentDim : Ui.Panel;
                _autoLockBtn.BorderBrush = _cfg.AutoLockEnabled ? Ui.Accent : Ui.Line;
            }
            if (_autoLockToggleBtn != null)
            {
                _autoLockToggleBtn.Content = _cfg.AutoLockEnabled
                    ? "Auto-lock: On" : "Auto-lock: Off";
                _autoLockToggleBtn.Background = _cfg.AutoLockEnabled
                    ? Ui.AccentDim : Ui.Panel;
                _autoLockToggleBtn.BorderBrush = _cfg.AutoLockEnabled
                    ? Ui.Accent : Ui.Line;
            }
        }

        private void CheckAutoLock(DateTime now)
        {
            if (!_cfg.AutoLockEnabled || _lastAutoLockDate.Date == now.Date) return;
            if (_alarmOverlay != null && _alarmOverlay.Visibility == Visibility.Visible) return;
            if (now.Hour == _cfg.AutoLockHour && now.Minute == _cfg.AutoLockMinute)
            {
                _lastAutoLockDate = now.Date;
                if (_autoLockPopup != null) _autoLockPopup.IsOpen = false;
                ScreenUtil.LockTablet();
            }
        }

        private void SaveAlarmTime()
        {
            DateTime parsed;
            if (_editAlarmTime == null ||
                !DateTime.TryParse(_editAlarmTime.Text.Trim(), out parsed))
            {
                MessageBox.Show(this,
                    "Enter a time like 7:30 AM or 19:30.",
                    "Projector Dashboard");
                return;
            }
            _cfg.AlarmHour = parsed.Hour;
            _cfg.AlarmMinute = parsed.Minute;
            _cfg.Save();
            _lastAlarmDate = DateTime.MinValue;
            UpdateAlarmUi();
        }

        private void UpdateAlarmUi()
        {
            DateTime alarmTime = DateTime.Today.AddHours(_cfg.AlarmHour)
                .AddMinutes(_cfg.AlarmMinute);
            string time = alarmTime.ToString("h:mm tt");
            if (_editAlarmTime != null) _editAlarmTime.Text = time;
            if (_alarmToggleBtn != null)
            {
                _alarmToggleBtn.Content = _cfg.AlarmEnabled ? "Alarm: On" : "Alarm: Off";
                _alarmToggleBtn.Background = _cfg.AlarmEnabled ? Ui.AccentDim : Ui.Panel;
                _alarmToggleBtn.BorderBrush = _cfg.AlarmEnabled ? Ui.Accent : Ui.Line;
            }
            if (_alarmStatusText != null)
            {
                _alarmStatusText.Text = _cfg.AlarmEnabled
                    ? "Alarm " + time : "Alarm off";
                _alarmStatusText.Foreground = _cfg.AlarmEnabled ? Ui.Accent : Ui.TextDim;
            }
        }

        private void CheckAlarm(DateTime now)
        {
            if (_alarmOverlay != null && _alarmOverlay.Visibility == Visibility.Visible)
                return;

            if (_snoozeUntil != DateTime.MinValue && now >= _snoozeUntil)
            {
                _snoozeUntil = DateTime.MinValue;
                StartRinging(now);
                return;
            }

            if (!_cfg.AlarmEnabled || _lastAlarmDate.Date == now.Date) return;
            if (now.Hour == _cfg.AlarmHour && now.Minute == _cfg.AlarmMinute)
            {
                _lastAlarmDate = now.Date;
                StartRinging(now);
            }
        }

        private void StartRinging(DateTime now)
        {
            if (_alarmOverlay == null) return;
            if (_tabletAppMode || _mirrorFullscreen) ReturnToDashboard();
            ClosePreview();
            CloseOverheadView();
            _alarmRingTime.Text = now.ToString("h:mm");
            _settingsOverlay.Visibility = Visibility.Collapsed;
            _alarmOverlay.Visibility = Visibility.Visible;

            if (_alarmAudioState != null)
                _audio.EndAlarm(_alarmAudioState, _cfg.AudioDeviceId);
            _alarmAudioState = null;
            if (_audio.IsSafeTabletSpeaker(_cfg.AlarmAudioDeviceId))
                _alarmAudioState = _audio.BeginAlarm(_cfg.AlarmAudioDeviceId,
                    GetCurrentProcessId());

            if (_alarmAudioState != null)
            {
                _alarmRingStatus.Text =
                    "Tablet speakers · 100% · other app audio temporarily muted";
                System.Media.SystemSounds.Exclamation.Play();
            }
            else
            {
                _alarmRingStatus.Text =
                    "Tablet speaker unavailable — visual alarm only (no audio was sent to another device).";
            }

            if (_alarmSoundTimer != null) _alarmSoundTimer.Stop();
            _alarmSoundTimer = new DispatcherTimer();
            _alarmSoundTimer.Interval = TimeSpan.FromSeconds(2);
            _alarmSoundTimer.Tick += delegate
            {
                if (_alarmAudioState != null)
                {
                    _audio.PrepareAlarmPlayback(_alarmAudioState,
                        GetCurrentProcessId());
                    System.Media.SystemSounds.Exclamation.Play();
                }
            };
            _alarmSoundTimer.Start();
            Activate();
        }

        private void StopRinging()
        {
            if (_alarmSoundTimer != null) _alarmSoundTimer.Stop();
            if (_alarmAudioState != null)
            {
                _audio.EndAlarm(_alarmAudioState, _cfg.AudioDeviceId);
                _alarmAudioState = null;
                SyncAudioUi();
                UpdateAudioDeviceButton();
            }
            if (_alarmOverlay != null) _alarmOverlay.Visibility = Visibility.Collapsed;
        }

        private void DismissAlarm()
        {
            _snoozeUntil = DateTime.MinValue;
            _lastAlarmDate = DateTime.Today;
            StopRinging();
            UpdateAlarmUi();
        }

        private void SnoozeAlarm()
        {
            StopRinging();
            _snoozeUntil = DateTime.Now.AddMinutes(10);
            if (_alarmStatusText != null)
                _alarmStatusText.Text = "Snoozed until " + _snoozeUntil.ToString("h:mm tt");
        }

        private void ApplyReservedFromEditor()
        {
            int w, h;
            if (int.TryParse(_editResW.Text.Trim(), out w)
                && int.TryParse(_editResH.Text.Trim(), out h))
            {
                _cfg.ReservedWidth = w;
                _cfg.ReservedHeight = h;
                _cfg.Save();
                _editResW.Text = _cfg.ReservedWidth.ToString();
                _editResH.Text = _cfg.ReservedHeight.ToString();
                ApplyReservedSize();
            }
        }

        private void ReassignDisplays()
        {
            // Clear the saved displays and relaunch; the picker runs at startup.
            _cfg.TabletDevice = "";
            _cfg.ProjectorDevice = "";
            _cfg.Save();
            Program.RestartRequested = true;
            Application.Current.Shutdown();
        }
    }
}
