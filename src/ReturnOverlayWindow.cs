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
        public ReturnOverlayWindow(WinForms.Screen tablet, Action showDashboard,
            Action emergencyOff)
        {
            Title = "Dashboard return control";
            Width = 300;
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
                { Width = new GridLength(84) });

            Button dashboard = Ui.Btn("← Dashboard", 17, Ui.PanelHi, Ui.Text,
                delegate { if (showDashboard != null) showDashboard(); });
            dashboard.MinHeight = 56;
            dashboard.Margin = new Thickness(0, 0, 7, 0);
            buttons.Children.Add(dashboard);

            Button off = Ui.Btn("OFF", 16, Ui.DangerFill, Ui.DangerText,
                delegate { if (emergencyOff != null) emergencyOff(); });
            off.MinHeight = 56;
            off.BorderBrush = Ui.Danger;
            off.ToolTip = "Close all Supermium tabs, lock Windows, and turn off every display.";
            Grid.SetColumn(off, 1);
            buttons.Children.Add(off);

            card.Child = buttons;
            Content = card;
            SourceInitialized += delegate
            {
                ScreenUtil.PlaceTopRightOverlay(this, tablet, 300, 78);
            };
        }
    }
}
