using System;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace ProjectorDash
{
    /// <summary>
    /// One touch-first remote shared by the normal projector workflow and
    /// tablet-only fullscreen mode, with a native trackpad for a separate
    /// projector. Text entry uses the movable Windows keyboard.
    /// </summary>
    public sealed class RemoteControlWindow : Window
    {
        public RemoteControlWindow(WinForms.Screen tablet, Action<int> sendKey,
            Action<int> adjustVolume, Action toggleMute, Action showKeyboard,
            Func<WinForms.Screen> targetScreen, Func<IntPtr> targetWindow, bool trackpadEnabled,
            Func<bool> showPointerMarker = null)
        {
            Title = "Current app remote";
            Width = 760;
            Height = 520;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Ui.ReturnShade;

            Border card = new Border();
            card.Padding = new Thickness(20);
            card.Background = Ui.ReturnShade;
            card.BorderBrush = Ui.Line;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(14);

            Grid content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel copy = new StackPanel();
            TextBlock title = Ui.Label("Make yourself comfortable", 19, Ui.Text);
            title.FontWeight = FontWeights.SemiBold;
            copy.Children.Add(title);
            TextBlock note = Ui.Label(
                "Control the current app from here.", 12, Ui.TextDim);
            copy.Children.Add(note);
            heading.Children.Add(copy);
            Button close = Ui.Btn("Done", 14, Ui.PanelHi, Ui.Text,
                delegate { Hide(); });
            close.MinWidth = 90;
            Grid.SetColumn(close, 1);
            heading.Children.Add(close);
            content.Children.Add(heading);

            Grid transport = ButtonRow(3);
            AddRemoteButton(transport, 0, "← Rewind", delegate
                { if (sendKey != null) sendKey(0x25); });
            AddRemoteButton(transport, 1, "Play / pause", delegate
                { if (sendKey != null) sendKey(0x20); });
            AddRemoteButton(transport, 2, "Forward →", delegate
                { if (sendKey != null) sendKey(0x27); });
            Grid.SetRow(transport, 1);
            content.Children.Add(transport);

            Grid audio = ButtonRow(3);
            AddRemoteButton(audio, 0, "Volume −", delegate
                { if (adjustVolume != null) adjustVolume(-5); });
            AddRemoteButton(audio, 1, "Mute", delegate
                { if (toggleMute != null) toggleMute(); });
            AddRemoteButton(audio, 2, "Volume +", delegate
                { if (adjustVolume != null) adjustVolume(5); });
            Grid.SetRow(audio, 2);
            content.Children.Add(audio);

            Grid utility = ButtonRow(4);
            AddRemoteButton(utility, 0, "Back / Esc", delegate
                { if (sendKey != null) sendKey(0x1B); });
            AddRemoteButton(utility, 1, "Fullscreen", delegate
                { if (sendKey != null) sendKey(0x46); });
            AddRemoteButton(utility, 2, "Enter", delegate
                { if (sendKey != null) sendKey(0x0D); });
            AddRemoteButton(utility, 3, "Keyboard", delegate
                { if (showKeyboard != null) showKeyboard(); });
            Grid.SetRow(utility, 3);
            content.Children.Add(utility);

            Grid shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid tabs = ButtonRow(2);
            tabs.Margin = new Thickness(0, 0, 0, 14);
            NativeTrackpad trackpad = new NativeTrackpad(targetScreen, targetWindow, showKeyboard, showPointerMarker);
            trackpad.Visibility = Visibility.Collapsed;
            Button playbackTab = null, trackpadTab = null;
            playbackTab = Ui.Btn("Playback", 17, Ui.AccentDim, Ui.Accent, delegate
            {
                trackpad.Cancel(); trackpad.Visibility = Visibility.Collapsed;
                content.Visibility = Visibility.Visible;
                playbackTab.Background = Ui.AccentDim; playbackTab.Foreground = Ui.Accent;
                trackpadTab.Background = Ui.Panel; trackpadTab.Foreground = Ui.Text;
            });
            playbackTab.Margin = new Thickness(0, 0, 8, 0);
            tabs.Children.Add(playbackTab);
            trackpadTab = Ui.Btn("Trackpad", 17, Ui.Panel, Ui.Text, delegate
            {
                content.Visibility = Visibility.Collapsed; trackpad.Visibility = Visibility.Visible;
                trackpadTab.Background = Ui.AccentDim; trackpadTab.Foreground = Ui.Accent;
                playbackTab.Background = Ui.Panel; playbackTab.Foreground = Ui.Text;
            });
            Grid.SetColumn(trackpadTab, 1); tabs.Children.Add(trackpadTab);
            if (!trackpadEnabled)
            {
                // On the same display, injected mouse input would hit this
                // topmost remote. The app already supports direct touch.
                tabs.Visibility = Visibility.Collapsed;
            }
            // Keep Done available in both modes.
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Button dismiss = Ui.Btn("Done", 16, Ui.PanelHi, Ui.Text, delegate { trackpad.Cancel(); Hide(); });
            dismiss.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(dismiss, 2); shell.Children.Add(dismiss);
            close.Visibility = Visibility.Collapsed;
            shell.Children.Add(tabs);
            Grid.SetRow(content, 1); shell.Children.Add(content);
            Grid.SetRow(trackpad, 1); shell.Children.Add(trackpad);
            IsVisibleChanged += delegate { if (!IsVisible) trackpad.Cancel(); };
            Closed += delegate { trackpad.Cancel(); };
            card.Child = shell;
            Content = card;
            SourceInitialized += delegate
            {
                ScreenUtil.PlaceRemoteOverlay(this, tablet,
                    (int)(760 * ScreenUtil.DpiScale(this)), (int)(520 * ScreenUtil.DpiScale(this)));
            };
        }

        private static Grid ButtonRow(int count)
        {
            Grid row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            for (int i = 0; i < count; i++)
                row.ColumnDefinitions.Add(new ColumnDefinition
                    { Width = new GridLength(1, GridUnitType.Star) });
            return row;
        }

        private static void AddRemoteButton(Grid row, int column, string label,
            RoutedEventHandler click)
        {
            Button button = Ui.Btn(label, 16, Ui.PanelHi, Ui.Text, click);
            button.MinHeight = 52;
            button.Padding = new Thickness(8, 8, 8, 8);
            button.Margin = new Thickness(column == 0 ? 0 : 4, 0,
                column == row.ColumnDefinitions.Count - 1 ? 0 : 4, 0);
            Grid.SetColumn(button, column);
            row.Children.Add(button);
        }
    }
}
