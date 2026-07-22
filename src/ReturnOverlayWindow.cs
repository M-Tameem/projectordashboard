using System;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace ProjectorDash
{
    /// <summary>
    /// Small, always-on-top touch control used while a fullscreen app owns the
    /// tablet display. It is intentionally shorter than the launch-window
    /// detector's minimum app height, so it can never be mistaken for the app
    /// that the dashboard is trying to place.
    /// </summary>
    public sealed class ReturnOverlayWindow : Window
    {
        public ReturnOverlayWindow(WinForms.Screen tablet, bool screenOffEnabled,
            Action showDashboard, Action showRemote, Action sleepDisplay,
            Action emergencyOff)
        {
            Title = "Dashboard return control";
            Width = screenOffEnabled ? 510 : 399;
            Height = 78;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Background = Ui.ReturnShade;

            Border card = new Border();
            card.Padding = new Thickness(7);
            card.Background = Ui.ReturnShade;
            card.BorderBrush = Ui.Accent;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(12);

            Grid buttons = new Grid();
            buttons.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(96) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(screenOffEnabled ? 104 : 0) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(76) });

            Button dashboard = Ui.Btn("← Dashboard", 17, Ui.PanelHi, Ui.Text,
                delegate { if (showDashboard != null) showDashboard(); });
            dashboard.MinHeight = 56;
            dashboard.Margin = new Thickness(0, 0, 7, 0);
            buttons.Children.Add(dashboard);

            Button remote = Ui.Btn("REMOTE", 13, Ui.PanelHi, Ui.Text,
                delegate { if (showRemote != null) showRemote(); });
            remote.MinHeight = 56;
            remote.Margin = new Thickness(0, 0, 7, 0);
            remote.ToolTip = "Open playback, volume, fullscreen, escape, and keyboard controls.";
            Grid.SetColumn(remote, 1);
            buttons.Children.Add(remote);

            if (screenOffEnabled)
            {
                Button sleep = Ui.Btn("SCREEN OFF", 13, Ui.PanelHi, Ui.Accent,
                    delegate { if (sleepDisplay != null) sleepDisplay(); });
                sleep.MinHeight = 56;
                sleep.Margin = new Thickness(0, 0, 7, 0);
                sleep.BorderBrush = Ui.Accent;
                sleep.ToolTip = "Turn off the tablet display while audio keeps playing. Tap the screen to wake it.";
                Grid.SetColumn(sleep, 2);
                buttons.Children.Add(sleep);
            }

            Button off = Ui.Btn("OFF", 16, Ui.DangerFill, Ui.DangerText,
                delegate { if (emergencyOff != null) emergencyOff(); });
            off.MinHeight = 56;
            off.BorderBrush = Ui.Danger;
            off.ToolTip = "Close all Supermium tabs, lock Windows, and turn off every display.";
            Grid.SetColumn(off, 3);
            buttons.Children.Add(off);

            card.Child = buttons;
            Content = card;
            SourceInitialized += delegate
            {
                ScreenUtil.PlaceTopRightOverlay(this, tablet,
                    screenOffEnabled ? 510 : 399, 78);
            };
        }
    }
}
