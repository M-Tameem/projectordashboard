using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace ProjectorDash
{
    /// <summary>Full-tablet live mirror of one existing projector window.</summary>
    public sealed class MirrorWindow : Window
    {
        private readonly WinForms.Screen _tablet;
        private readonly DwmWindowMirror _mirror = new DwmWindowMirror();
        private readonly TextBlock _status;
        private IntPtr _source;

        public MirrorWindow(WinForms.Screen tablet, IntPtr source)
        {
            _tablet = tablet;
            _source = source;
            Title = "Projector mirror";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Background = Brushes.Black;

            Grid root = new Grid { Background = Brushes.Black };
            _status = Ui.Label("Connecting to projector preview…", 22, Ui.TextDim);
            _status.HorizontalAlignment = HorizontalAlignment.Center;
            _status.VerticalAlignment = VerticalAlignment.Center;
            root.Children.Add(_status);
            Content = root;

            SourceInitialized += delegate
            {
                ScreenUtil.FillScreen(this, _tablet);
                AttachAndLayout();
            };
            SizeChanged += delegate { UpdateLayoutRectangle(); };
            Closed += delegate { _mirror.Dispose(); };
        }

        public void SetSource(IntPtr source)
        {
            if (source == _source && _mirror.Attached) return;
            _source = source;
            AttachAndLayout();
        }

        private void AttachAndLayout()
        {
            IntPtr destination = new WindowInteropHelper(this).Handle;
            bool attached = destination != IntPtr.Zero &&
                _mirror.Attach(destination, _source);
            _status.Visibility = attached ? Visibility.Collapsed : Visibility.Visible;
            _status.Text = _source == IntPtr.Zero
                ? "No projector app is open"
                : "This window cannot be mirrored";
            if (attached)
            {
                Dispatcher.BeginInvoke(new Action(UpdateLayoutRectangle),
                    DispatcherPriority.Loaded);
            }
        }

        private void UpdateLayoutRectangle()
        {
            if (!_mirror.Attached) return;
            PresentationSource presentation = PresentationSource.FromVisual(this);
            double sx = 1.0;
            double sy = 1.0;
            if (presentation != null && presentation.CompositionTarget != null)
            {
                sx = presentation.CompositionTarget.TransformToDevice.M11;
                sy = presentation.CompositionTarget.TransformToDevice.M22;
            }
            int width = Math.Max(1, (int)Math.Round(ActualWidth * sx));
            int height = Math.Max(1, (int)Math.Round(ActualHeight * sy));
            _mirror.Update(0, 0, width, height);
        }
    }
}
