using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace ProjectorDash
{
    /// <summary>Native touch input to the existing app through Windows SendInput.</summary>
    public sealed class NativeTrackpad : Border
    {
        private readonly Func<WinForms.Screen> _screen;
        private readonly Func<IntPtr> _target;
        private readonly TrackpadGesture _gesture = new TrackpadGesture();
        private readonly Stopwatch _time = Stopwatch.StartNew();
        private readonly TextBlock _status;
        private readonly Button _drag;
        private readonly Border _surface;
        private IntPtr _window;
        private System.Drawing.Rectangle _bounds;
        private string _device;
        private double _x, _y;
        private bool _buttonDown;
        private bool _releasingCapture;

        public NativeTrackpad(Func<WinForms.Screen> screen, Func<IntPtr> target, Action keyboard)
        {
            _screen = screen; _target = target;
            Background = Ui.Panel;
            BorderBrush = Ui.Line;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(18);
            Padding = new Thickness(16);
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            DockPanel heading = new DockPanel();
            Button keys = Ui.Btn("Keyboard", 14, Ui.PanelHi, Ui.Text,
                delegate { Cancel(); if (keyboard != null) keyboard(); });
            keys.Padding = new Thickness(12, 6, 12, 6);
            keys.MinHeight = 48;
            DockPanel.SetDock(keys, Dock.Right);
            heading.Children.Add(keys);
            StackPanel copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(Ui.Label("Trackpad", 22, Ui.Text));
            copy.Children.Add(Ui.Label("Control your screen", 12, Ui.TextDim));
            heading.Children.Add(copy);
            grid.Children.Add(heading);

            _surface = new Border { Background = Ui.SurfaceLow, BorderBrush = Ui.Line,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 14, 0, 12), MinHeight = 90, ClipToBounds = true };
            StackPanel hint = new StackPanel { VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
            TextBlock symbol = Ui.Label("↖", 40, Ui.Accent);
            symbol.HorizontalAlignment = HorizontalAlignment.Center;
            hint.Children.Add(symbol);
            TextBlock instructions = Ui.Label("Slide to move · tap to click\nTwo fingers to scroll", 14, Ui.TextDim);
            instructions.TextAlignment = TextAlignment.Center;
            instructions.Margin = new Thickness(0, 6, 0, 0);
            hint.Children.Add(instructions);
            _surface.Child = hint;
            Stylus.SetIsPressAndHoldEnabled(_surface, false);
            Stylus.SetIsFlicksEnabled(_surface, false);
            Stylus.SetIsTapFeedbackEnabled(_surface, false);
            Stylus.SetIsTouchFeedbackEnabled(_surface, false);
            _surface.TouchDown += OnDown;
            _surface.TouchMove += OnMove;
            _surface.TouchUp += OnUp;
            _surface.LostTouchCapture += OnUnexpectedCaptureLoss;
            Grid.SetRow(_surface, 1); grid.Children.Add(_surface);

            Grid clicks = new Grid();
            for (int i = 0; i < 3; i++) clicks.ColumnDefinitions.Add(new ColumnDefinition());
            AddButton(clicks, 0, "Click", delegate { Click(false); });
            AddButton(clicks, 1, "Right click", delegate { Click(true); });
            _drag = AddButton(clicks, 2, "Drag", delegate
            {
                bool armed = !_gesture.DragNextTouch;
                Cancel();
                _gesture.DragNextTouch = armed;
                _drag.Background = armed ? Ui.AccentDim : Ui.PanelHi;
                _status.Text = armed ? "Slide with one finger; lift to release" : "Ready";
            });
            Grid.SetRow(clicks, 2); grid.Children.Add(clicks);
            _status = Ui.Label("No extra mouse software needed", 12, Ui.TextDim);
            _status.TextTrimming = TextTrimming.CharacterEllipsis;
            _status.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(_status, 3); grid.Children.Add(_status);
            Child = grid;

            _gesture.MovePointer = delegate(double dx, double dy)
            {
                _x = PointerMath.Clamp(_x + dx * 2.0, _bounds.Left, _bounds.Width);
                _y = PointerMath.Clamp(_y + dy * 2.0, _bounds.Top, _bounds.Height);
                Send(0, 0);
            };
            _gesture.Scroll = delegate(int amount) { Send(0x0800, amount); };
            _gesture.Tap = delegate { Click(false); };
            _gesture.Drag = delegate(bool down)
            {
                if (down) _buttonDown = Send(0x0002, 0);
                else ReleaseButton();
            };
            IsVisibleChanged += delegate { if (!IsVisible) Cancel(); };
            Loaded += delegate
            {
                Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChange;
            };
            Unloaded += delegate
            {
                Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChange;
                Cancel();
            };
        }

        private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(Cancel));
        }
        private void OnDisplayChange(object sender, EventArgs e)
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(delegate
            {
                Cancel(); _device = null; _window = IntPtr.Zero;
                _status.Text = "Displays changed · touch to reconnect";
            }));
        }

        private Button AddButton(Grid grid, int column, string text, RoutedEventHandler action)
        {
            Button button = Ui.Btn(text, 14, Ui.PanelHi, Ui.Text, action);
            button.Padding = new Thickness(6, 8, 6, 8);
            button.Margin = new Thickness(column == 0 ? 0 : 6, 0, 0, 0);
            Grid.SetColumn(button, column); grid.Children.Add(button);
            return button;
        }

        private bool Prepare()
        {
            WinForms.Screen screen = _screen();
            _window = _target();
            if (screen == null || _window == IntPtr.Zero || !ScreenUtil.IsWindow(_window))
            {
                Cancel(); _status.Text = "Open an app to use the trackpad"; return false;
            }
            if (_device != screen.DeviceName || _bounds != screen.Bounds)
            {
                ReleaseButton();
                _device = screen.DeviceName; _bounds = screen.Bounds;
                _x = _bounds.Left + _bounds.Width / 2.0;
                _y = _bounds.Top + _bounds.Height / 2.0;
            }
            if (!ScreenUtil.FocusInputWindow(_window))
            {
                Cancel(); _status.Text = "Tap the app tile to reconnect"; return false;
            }
            _status.Text = "Connected · two fingers to scroll";
            return true;
        }

        private void OnDown(object sender, TouchEventArgs e)
        {
            e.Handled = true;
            if (_gesture.ContactCount == 0 && !Prepare()) return;
            if (!_surface.CaptureTouch(e.TouchDevice)) return;
            Point p = e.GetTouchPoint(_surface).Position;
            _gesture.Down(e.TouchDevice.Id, p.X, p.Y, _time.ElapsedMilliseconds);
        }
        private void OnMove(object sender, TouchEventArgs e)
        {
            e.Handled = true;
            Point p = e.GetTouchPoint(_surface).Position;
            _gesture.Move(e.TouchDevice.Id, p.X, p.Y);
        }
        private void OnUp(object sender, TouchEventArgs e)
        {
            e.Handled = true;
            _gesture.Up(e.TouchDevice.Id, _time.ElapsedMilliseconds);
            // Normal release must not cancel a second finger's scroll gesture.
            _releasingCapture = true;
            e.TouchDevice.Capture(null);
            _releasingCapture = false;
            _drag.Background = Ui.PanelHi;
        }
        private void OnUnexpectedCaptureLoss(object sender, TouchEventArgs e)
        { if (!_releasingCapture) Cancel(); }

        private bool Send(uint flags, int data)
        {
            if (_window == IntPtr.Zero || !ScreenUtil.IsWindow(_window) ||
                !ScreenUtil.InjectPointer(_x, _y, flags, data))
            {
                ReleaseButton();
                _status.Text = "Input unavailable · reopen the app";
                return false;
            }
            return true;
        }
        private void Click(bool right)
        {
            if (!Prepare()) return;
            if (!Send(right ? 0x0008u : 0x0002u, 0)) return;
            // Always release, even if the target closes between the two events.
            ScreenUtil.ReleasePointerButton(right);
        }
        private void ReleaseButton()
        {
            if (!_buttonDown) return;
            ScreenUtil.ReleasePointerButton(false);
            _buttonDown = false;
        }
        public void Cancel()
        {
            _gesture.Cancel(); ReleaseButton();
            _releasingCapture = true;
            if (_surface != null) _surface.ReleaseAllTouchCaptures();
            _releasingCapture = false;
            if (_drag != null) _drag.Background = Ui.PanelHi;
        }
    }
}
