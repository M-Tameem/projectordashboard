using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private Button _updateBtn;
        private Button _autoLockToggleBtn;
        private Popup _autoLockPopup;
        private TextBox _autoLockTime;
        private bool _suppressSliderEvents;
        private bool _updateBusy;

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
            Activate(); // keep touch focus on the controller
        }

        private void TickClock()
        {
            DateTime now = DateTime.Now;
            _clockText.Text = now.ToString("h:mm");
            _dateText.Text = now.ToString("dddd, MMMM d");
            if (_projector != null) _projector.UpdateTime(now);
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

            // --- Header: clock, date, system buttons -----------------------
            Grid header = new Grid();
            header.Margin = new Thickness(28, 18, 28, 6);
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel clockStack = new StackPanel();
            _clockText = new TextBlock();
            _clockText.FontFamily = new FontFamily(Ui.FontLight);
            _clockText.FontSize = 78;
            _clockText.Foreground = Ui.Text;
            _clockText.Margin = new Thickness(0, -10, 0, 0);
            clockStack.Children.Add(_clockText);
            _dateText = Ui.Label("", 20, Ui.TextDim);
            _dateText.Margin = new Thickness(4, -6, 0, 0);
            clockStack.Children.Add(_dateText);
            _alarmStatusText = Ui.Label("", 15, Ui.Accent);
            _alarmStatusText.Margin = new Thickness(4, 4, 0, 0);
            clockStack.Children.Add(_alarmStatusText);
            Grid.SetColumn(clockStack, 0);
            header.Children.Add(clockStack);

            Grid sys = new Grid();
            sys.VerticalAlignment = VerticalAlignment.Top;
            sys.HorizontalAlignment = HorizontalAlignment.Right;
            sys.Width = 870;
            sys.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            sys.ColumnDefinitions.Add(new ColumnDefinition
                { Width = GridLength.Auto });

            WrapPanel tools = new WrapPanel();
            tools.Orientation = Orientation.Horizontal;
            tools.VerticalAlignment = VerticalAlignment.Top;
            tools.HorizontalAlignment = HorizontalAlignment.Right;
            tools.Margin = new Thickness(0, 0, 10, 0);
            sys.Children.Add(tools);

            Button rb = Ui.Btn("Reset Supermium", 16, Ui.Panel, Ui.Text,
                delegate { RestartBrowser(); });
            rb.ToolTip = "Force-close Supermium and reopen it when the browser is stuck.";
            rb.Margin = new Thickness(0, 0, 10, 10);
            tools.Children.Add(rb);

            Button kb = Ui.Btn("Keyboard", 17, Ui.Panel, Ui.Text, delegate { ShowTouchKeyboard(); });
            kb.Margin = new Thickness(0, 0, 10, 10);
            tools.Children.Add(kb);

            _sleepBtn = Ui.Btn(_cfg.TabletOnlyMode ? "Sleep app" : "Sleep projector",
                16, Ui.Panel, Ui.Text,
                delegate { ToggleProjectorSleep(); });
            _sleepBtn.ToolTip = _cfg.TabletOnlyMode
                ? "Hide the tablet app without closing its tabs; tap again to restore it."
                : "Hide projector apps without closing their tabs; tap again to wake.";
            _sleepBtn.Margin = new Thickness(0, 0, 10, 10);
            tools.Children.Add(_sleepBtn);

            _autoLockBtn = Ui.Btn("Auto-lock: Off", 16, Ui.Panel, Ui.Text,
                delegate { OpenAutoLockPopup(); });
            _autoLockBtn.Margin = new Thickness(0, 0, 10, 10);
            _autoLockBtn.ToolTip = "Schedule a daily Windows lock (same as Win+L).";
            tools.Children.Add(_autoLockBtn);
            _autoLockPopup = BuildAutoLockPopup(_autoLockBtn);

            _previewBtn = Ui.Btn("Preview", 17, Ui.Panel, Ui.Text,
                delegate { OpenPreview(); });
            _previewBtn.Margin = new Thickness(0, 0, 10, 10);
            _previewBtn.IsEnabled = !_cfg.TabletOnlyMode;
            _previewBtn.Opacity = _cfg.TabletOnlyMode ? 0.45 : 1.0;
            _previewBtn.ToolTip = _cfg.TabletOnlyMode
                ? "Preview is available when the projector is the active target."
                : "Show a live copy of the current projector window without opening another tab.";
            tools.Children.Add(_previewBtn);

            Button desk = Ui.Btn("Desktop", 17, Ui.Panel, Ui.Text,
                delegate
                {
                    if (_cfg.TabletOnlyMode) EnterTabletAppMode();
                    else WindowState = WindowState.Minimized;
                });
            desk.Margin = new Thickness(0, 0, 10, 10);
            tools.Children.Add(desk);

            Button settings = Ui.Btn("Settings", 17, Ui.Panel, Ui.Text,
                delegate { OpenSettings(); });
            settings.Margin = new Thickness(0, 0, 10, 10);
            tools.Children.Add(settings);

            _updateBtn = Ui.Btn("Update", 16, Ui.Panel, Ui.Text,
                delegate { CheckForUpdates(); });
            _updateBtn.Margin = new Thickness(0, 0, 10, 10);
            _updateBtn.ToolTip = "One tap: install the latest stable GitHub release. Current version: " +
                SelfUpdater.CurrentVersion;
            tools.Children.Add(_updateBtn);

            Button emergency = Ui.Btn("Emergency off", 16,
                Ui.Hex("#761527"), Ui.Hex("#FFF4F5"),
                delegate { EmergencyOff(); });
            emergency.BorderBrush = Ui.Danger;
            emergency.Margin = new Thickness(0, 0, 10, 10);
            emergency.ToolTip = "One tap: close all Supermium tabs, lock Windows, and turn off every display.";
            tools.Children.Add(emergency);

            StackPanel closeTools = new StackPanel();
            closeTools.VerticalAlignment = VerticalAlignment.Top;
            closeTools.HorizontalAlignment = HorizontalAlignment.Right;

            Button closeApp = Ui.Btn("Close app", 16, Ui.Panel, Ui.Danger,
                delegate { CloseProjectedApp(); });
            closeApp.BorderBrush = Ui.Danger;
            closeApp.Margin = new Thickness(0, 0, 0, 10);
            closeApp.MinWidth = 158;
            closeTools.Children.Add(closeApp);

            Button closeDashboard = Ui.Btn("Close dashboard", 16,
                Ui.Hex("#6B1727"), Ui.Hex("#FFF4F5"), delegate { Close(); });
            closeDashboard.BorderBrush = Ui.Hex("#E05269");
            closeDashboard.Margin = new Thickness(0, 0, 0, 10);
            closeDashboard.MinWidth = 158;
            closeTools.Children.Add(closeDashboard);
            Grid.SetColumn(closeTools, 1);
            sys.Children.Add(closeTools);

            Grid.SetColumn(sys, 1);
            header.Children.Add(sys);

            Grid.SetRow(header, 0);
            Grid.SetColumnSpan(header, 2);
            root.Children.Add(header);

            // --- Launcher grid --------------------------------------------
            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroller.PanningMode = PanningMode.VerticalOnly;
            scroller.Margin = new Thickness(21, 4, 21, 4);

            _tilePanel = new WrapPanel();
            _tilePanel.Orientation = Orientation.Horizontal;
            scroller.Content = _tilePanel;

            Grid.SetRow(scroller, 1);
            Grid.SetColumnSpan(scroller, 2);
            root.Children.Add(scroller);

            // Live one-source projector mirror. Like Settings, it leaves the
            // bottom audio/brightness and TouchMousePointer strip available.
            _previewOverlay = BuildPreviewOverlay();
            _previewOverlay.Visibility = Visibility.Collapsed;
            Grid.SetRow(_previewOverlay, 0);
            Grid.SetRowSpan(_previewOverlay, 2);
            Grid.SetColumnSpan(_previewOverlay, 2);
            root.Children.Add(_previewOverlay);

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

            TextBlock reservedLabel = Ui.Label("touch pad", 14, Ui.Hex("#4A443C"));
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
            TextBlock title = SectionTitle("Daily tablet auto-lock");
            panel.Children.Add(title);
            panel.Children.Add(WrappedNote(
                "Locks Windows at this time every day—the same as pressing Win+L."));

            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 12, 0, 0);
            _autoLockTime = Ui.Input("1:00 AM", 17);
            _autoLockTime.Width = 130;
            row.Children.Add(_autoLockTime);
            Button save = Ui.Btn("Save", 16, Ui.Accent, Ui.Hex("#141210"),
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
            Grid overlay = new Grid { Background = Ui.Hex("#0C0A14") };
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
            Button back = Ui.Btn("Back", 17, Ui.Accent, Ui.Hex("#141210"),
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
            add.Background = Ui.Hex("#150F24");
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

        private void OpenPreview()
        {
            if (_cfg.TabletOnlyMode || _previewOverlay == null) return;
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
            if (_projector != null)
            {
                _projector.SetMode(_cfg.ProjectorMode);
                _projector.UpdateTime(DateTime.Now);
            }
            UpdateSleepButton();
            if (restoredTabletApp) EnterTabletAppMode();
            else Activate();
        }

        private void UpdateSleepButton()
        {
            if (_sleepBtn == null) return;
            if (_cfg.TabletOnlyMode)
                _sleepBtn.Content = _projectorSleeping ? "Wake app" : "Sleep app";
            else
                _sleepBtn.Content = _projectorSleeping ? "Wake projector" : "Sleep projector";
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
                _updateBtn.Content = "Update";
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
            RefreshShortcutList(-1);
            _editResW.Text = _cfg.ReservedWidth.ToString();
            _editResH.Text = _cfg.ReservedHeight.ToString();
            UpdateProjectorModeButton();
            UpdateLaunchModeButton();
            UpdateTabletModeButton();
            UpdateAlarmUi();
            _editBrowser.Text = _cfg.BrowserPath ?? "";
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
            overlay.Background = Ui.Hex("#0C0A14");

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
            Button done = Ui.Btn("Done", 17, Ui.Accent, Ui.Hex("#141210"),
                delegate { CloseSettings(); });
            Grid.SetColumn(done, 1);
            titleRow.Children.Add(done);
            inner.Children.Add(titleRow);

            WrapPanel tabs = new WrapPanel();
            tabs.Orientation = Orientation.Horizontal;
            tabs.Margin = new Thickness(0, 6, 0, 4);
            string[] names = new string[] { "Shortcuts", "Projector", "Audio + browser", "Alarm" };
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
                BuildAlarmSettingsPage()
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
            save.Foreground = Ui.Hex("#141210");
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
                : "Sleep projector hides open apps and tabs without closing them, then shows the idle clock until you wake it."));
            Button sleep = SmallBtn("Sleep / wake now", delegate { ToggleProjectorSleep(); });
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
            combo.Foreground = Ui.Hex("#17131F");
            combo.BorderBrush = Ui.Line;
            combo.Padding = new Thickness(10, 6, 10, 6);
            Style items = new Style(typeof(ComboBoxItem));
            items.Setters.Add(new Setter(ComboBoxItem.MinHeightProperty, 46.0));
            items.Setters.Add(new Setter(ComboBoxItem.PaddingProperty, new Thickness(10, 7, 10, 7)));
            items.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, Brushes.White));
            items.Setters.Add(new Setter(ComboBoxItem.ForegroundProperty, Ui.Hex("#17131F")));
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
            overlay.Background = Ui.Hex("#E60B0810");

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
            Button dismiss = Ui.Btn("Dismiss", 21, Ui.Hex("#6B1727"),
                Ui.Hex("#FFF4F5"), delegate { DismissAlarm(); });
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
