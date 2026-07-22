using System;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace ProjectorDash
{
    /// <summary>
    /// One touch-first remote shared by the normal projector workflow and
    /// tablet-only fullscreen mode. It intentionally sends only common media
    /// keys; text entry is delegated to the movable Windows keyboard.
    /// </summary>
    public sealed class RemoteControlWindow : Window
    {
        public RemoteControlWindow(WinForms.Screen tablet, Action<int> sendKey,
            Action<int> adjustVolume, Action toggleMute, Action showKeyboard)
        {
            Title = "Current app remote";
            Width = 680;
            Height = 320;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Ui.ReturnShade;

            Border card = new Border();
            card.Padding = new Thickness(12);
            card.Background = Ui.ReturnShade;
            card.BorderBrush = Ui.Accent;
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
            TextBlock title = Ui.Label("REMOTE · CURRENT APP", 19, Ui.Text);
            title.FontWeight = FontWeights.SemiBold;
            copy.Children.Add(title);
            TextBlock note = Ui.Label(
                "Large bedside controls · closes without stopping playback", 12, Ui.TextDim);
            copy.Children.Add(note);
            heading.Children.Add(copy);
            Button close = Ui.Btn("CLOSE", 14, Ui.PanelHi, Ui.Text,
                delegate { Hide(); });
            close.MinWidth = 90;
            Grid.SetColumn(close, 1);
            heading.Children.Add(close);
            content.Children.Add(heading);

            Grid transport = ButtonRow(3);
            AddRemoteButton(transport, 0, "LEFT  ←", delegate
                { if (sendKey != null) sendKey(0x25); });
            AddRemoteButton(transport, 1, "PLAY / PAUSE · SPACE", delegate
                { if (sendKey != null) sendKey(0x20); });
            AddRemoteButton(transport, 2, "RIGHT  →", delegate
                { if (sendKey != null) sendKey(0x27); });
            Grid.SetRow(transport, 1);
            content.Children.Add(transport);

            Grid audio = ButtonRow(3);
            AddRemoteButton(audio, 0, "VOLUME  −", delegate
                { if (adjustVolume != null) adjustVolume(-5); });
            AddRemoteButton(audio, 1, "MUTE", delegate
                { if (toggleMute != null) toggleMute(); });
            AddRemoteButton(audio, 2, "VOLUME  +", delegate
                { if (adjustVolume != null) adjustVolume(5); });
            Grid.SetRow(audio, 2);
            content.Children.Add(audio);

            Grid utility = ButtonRow(4);
            AddRemoteButton(utility, 0, "ESC / BACK", delegate
                { if (sendKey != null) sendKey(0x1B); });
            AddRemoteButton(utility, 1, "FULLSCREEN · F", delegate
                { if (sendKey != null) sendKey(0x46); });
            AddRemoteButton(utility, 2, "ENTER", delegate
                { if (sendKey != null) sendKey(0x0D); });
            AddRemoteButton(utility, 3, "KEYBOARD", delegate
                { if (showKeyboard != null) showKeyboard(); });
            Grid.SetRow(utility, 3);
            content.Children.Add(utility);

            card.Child = content;
            Content = card;
            SourceInitialized += delegate
            {
                ScreenUtil.PlaceRemoteOverlay(this, tablet, 680, 320);
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
            button.MinHeight = 58;
            button.Margin = new Thickness(column == 0 ? 0 : 4, 0,
                column == row.ColumnDefinitions.Count - 1 ? 0 : 4, 0);
            Grid.SetColumn(button, column);
            row.Children.Add(button);
        }
    }
}
