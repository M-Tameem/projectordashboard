using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProjectorDash
{
    public sealed class ResourcePanel : Grid
    {
        private readonly Func<string> _browserPath;
        private readonly DispatcherTimer _timer;
        private ResourceMonitor _monitor = new ResourceMonitor();
        private ResourceSnapshot _snapshot;
        private readonly TextBlock _ram, _commit, _cpu, _browser, _status, _detail, _serviceSummary;
        private readonly ProgressBar _ramBar, _commitBar, _cpuBar;
        private readonly DataGrid _processes;
        private readonly Button _end, _pause, _filter;
        private bool _busy, _paused;
        private int _processFilter;
        private int _generation, _sort;

        public ResourcePanel(Func<string> browserPath)
        {
            _browserPath = browserPath;
            Margin = new Thickness(0, 12, 0, 0);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(320) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid totals = new Grid();
            for (int i = 0; i < 3; i++) totals.ColumnDefinitions.Add(new ColumnDefinition());
            AddMeter(totals, 0, "RAM", out _ram, out _ramBar);
            AddMeter(totals, 1, "Committed memory", out _commit, out _commitBar);
            AddMeter(totals, 2, "CPU", out _cpu, out _cpuBar);
            Children.Add(totals);
            _browser = Note("Supermium: waiting for a sample", 15);
            StackPanel summary = new StackPanel();
            summary.Children.Add(_browser);
            _status = Note("Reading usage… CPU needs two samples.", 13);
            summary.Children.Add(_status);
            _serviceSummary = Note("", 13);
            summary.Children.Add(_serviceSummary);
            Grid.SetRow(summary, 1); Children.Add(summary);
            WrapPanel actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
            actions.Children.Add(ActionButton("Sort: RAM", delegate { _sort = 0; RenderRows(); }));
            actions.Children.Add(ActionButton("Sort: Private", delegate { _sort = 1; RenderRows(); }));
            actions.Children.Add(ActionButton("Sort: CPU", delegate { _sort = 2; RenderRows(); }));
            _filter = ActionButton("All processes", delegate
            {
                _processFilter = (_processFilter + 1) % 3;
                _filter.Content = new string[] { "All processes", "Supermium only", "Service hosts only" }[_processFilter];
                RenderRows();
            });
            actions.Children.Add(_filter);
            _pause = ActionButton("Pause", delegate
            {
                _paused = !_paused; _pause.Content = _paused ? "Resume" : "Pause";
                if (_paused) { _timer.Stop(); _generation++; _status.Text = "Paused · select a process to inspect it"; }
                else { _monitor = new ResourceMonitor(); if (IsVisible) { _timer.Start(); Sample(); } }
            });
            actions.Children.Add(_pause);
            Grid.SetRow(actions, 2); Children.Add(actions);

            _processes = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true,
                CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false,
                SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 46,
                Background = Ui.Panel, Foreground = Ui.Text, BorderBrush = Ui.Line,
                RowBackground = Ui.Panel, AlternatingRowBackground = Ui.SurfaceLow,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                EnableRowVirtualization = true, EnableColumnVirtualization = true, FontSize = 15 };
            Style header = new Style(typeof(DataGridColumnHeader));
            header.Setters.Add(new Setter(Control.BackgroundProperty, Ui.PanelHi));
            header.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Text));
            header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10)));
            _processes.ColumnHeaderStyle = header;
            Style cell = new Style(typeof(DataGridCell));
            cell.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 5, 10, 5)));
            cell.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            Trigger selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, Ui.AccentDim));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Accent));
            cell.Triggers.Add(selected); _processes.CellStyle = cell;
            AddColumn("Process / hosted services", "DisplayName", new DataGridLength(1, DataGridLengthUnitType.Star));
            AddColumn("PID", "Id", new DataGridLength(78));
            AddColumn("RAM", "Ram", new DataGridLength(105));
            AddColumn("Private", "PrivateMemory", new DataGridLength(105));
            AddColumn("CPU", "Cpu", new DataGridLength(85));
            _processes.Columns[0].MinWidth = 180;
            _processes.SizeChanged += delegate
            {
                // The surrounding settings ScrollViewer can measure star
                // columns before a viewport exists. Keep names readable on
                // that first pass, then fill the available viewport exactly.
                _processes.Columns[0].Width = new DataGridLength(Math.Max(180, _processes.ActualWidth - 395));
            };
            _processes.SelectionChanged += delegate { UpdateSelection(); };
            ScrollViewer.SetPanningMode(_processes, PanningMode.VerticalOnly);
            Grid.SetRow(_processes, 3); Children.Add(_processes);
            Grid selection = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            selection.ColumnDefinitions.Add(new ColumnDefinition());
            selection.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _detail = Note("Select a process to inspect it. Windows processes and this dashboard are protected.", 13);
            selection.Children.Add(_detail);
            _end = ActionButton("End task", EndSelected);
            _end.Background = Ui.DangerFill; _end.Foreground = Ui.DangerText; _end.IsEnabled = false;
            Grid.SetColumn(_end, 1); selection.Children.Add(_end);
            Grid.SetRow(selection, 4); Children.Add(selection);
            StackPanel notes = new StackPanel();
            notes.Children.Add(Note("RAM is each process's working set, including shared pages; rows won't add up to total RAM. Private is its committed private memory. CPU is a percentage of all processors. A 32-bit app can exhaust its address space even when RAM is available. These readings show current pressure, not the cause of a past crash.", 12));
            Grid.SetRow(notes, 5); Children.Add(notes);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += delegate { Sample(); };
            IsVisibleChanged += delegate
            {
                _generation++; _timer.Stop();
                if (IsVisible && !_paused) { _monitor = new ResourceMonitor(); _timer.Start(); Sample(); }
            };
            Unloaded += delegate { _generation++; _timer.Stop(); };
        }

        private static TextBlock Note(string text, double size)
        { return new TextBlock { Text = text, FontSize = size, Foreground = Ui.TextDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 8, 0) }; }
        private static Button ActionButton(string text, RoutedEventHandler action)
        {
            Button button = Ui.Btn(text, 14, Ui.PanelHi, Ui.Text, action);
            button.Padding = new Thickness(12, 8, 12, 8); button.Margin = new Thickness(0, 0, 8, 0);
            return button;
        }
        private static void AddMeter(Grid host, int column, string title, out TextBlock value, out ProgressBar bar)
        {
            StackPanel content = new StackPanel();
            content.Children.Add(Note(title, 14)); value = Ui.Label("—", 20, Ui.Text);
            value.TextWrapping = TextWrapping.Wrap; content.Children.Add(value);
            bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6, Foreground = Ui.Accent,
                Background = Ui.Line, BorderThickness = new Thickness(0), Margin = new Thickness(0, 10, 0, 0) };
            content.Children.Add(bar);
            Border card = new Border { Background = Ui.Panel, Padding = new Thickness(14),
                CornerRadius = new CornerRadius(12), Margin = new Thickness(column == 0 ? 0 : 8, 0, 0, 0), Child = content };
            Grid.SetColumn(card, column); host.Children.Add(card);
        }
        private void AddColumn(string title, string property, DataGridLength width)
        {
            Style text = new Style(typeof(TextBlock));
            text.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            text.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(10, 0, 10, 0)));
            text.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            _processes.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width, ElementStyle = text });
        }
        private void Sample()
        {
            if (_busy || _paused || !IsVisible) return;
            _busy = true;
            int generation = _generation;
            ResourceMonitor monitor = _monitor;
            string browser = _browserPath();
            Task.Factory.StartNew(delegate { return monitor.Sample(browser); }).ContinueWith(delegate(Task<ResourceSnapshot> task)
            {
                // Observe failures even when the panel was closed during sampling.
                string error = task.IsFaulted ? task.Exception.GetBaseException().Message : null;
                if (Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _busy = false;
                    if (generation != _generation || !IsVisible) return;
                    if (error != null) { _status.Text = error; return; }
                    ShowSnapshot(task.Result);
                }));
            });
        }
        public void ShowSnapshot(ResourceSnapshot snapshot)
        {
            _snapshot = snapshot;
            _ram.Text = ResourceMonitor.Bytes(snapshot.RamTotal - Math.Min(snapshot.RamTotal, snapshot.RamAvailable)) + " / " + ResourceMonitor.Bytes(snapshot.RamTotal);
            _commit.Text = ResourceMonitor.Bytes(snapshot.CommitUsed) + " / " + ResourceMonitor.Bytes(snapshot.CommitLimit);
            _cpu.Text = snapshot.CpuPercent.HasValue ? snapshot.CpuPercent.Value.ToString("0") + "%" : "Measuring…";
            _ramBar.Value = snapshot.RamTotal == 0 ? 0 : 100.0 * (snapshot.RamTotal - Math.Min(snapshot.RamTotal, snapshot.RamAvailable)) / snapshot.RamTotal;
            _commitBar.Value = snapshot.CommitLimit == 0 ? 0 : 100.0 * snapshot.CommitUsed / snapshot.CommitLimit;
            _cpuBar.Value = snapshot.CpuPercent ?? 0;
            foreach (ProgressBar bar in new ProgressBar[] { _ramBar, _commitBar, _cpuBar }) bar.Foreground = bar.Value >= 90 ? Ui.Danger : Ui.Accent;
            ulong memory = 0; double cpu = 0; int count = 0; bool known = false;
            foreach (ProcessUsage process in snapshot.Processes) if (process.Browser)
            { count++; memory += (ulong)Math.Max(0, process.PrivateBytes); cpu += process.CpuPercent ?? 0; known |= process.CpuPercent.HasValue; }
            _browser.Text = "Supermium · " + count + " processes · " + ResourceMonitor.Bytes(memory) + " private memory · " + (known ? cpu.ToString("0.0") + "% CPU" : "CPU measuring…");
            ProcessUsage largestHost = null;
            foreach (ProcessUsage process in snapshot.Processes)
                if (process.ServiceHost && (largestHost == null || process.PrivateBytes > largestHost.PrivateBytes)) largestHost = process;
            _serviceSummary.Text = snapshot.ServiceError.Length > 0 ? "Windows service names unavailable: " + snapshot.ServiceError
                : largestHost == null ? "No service-host process was readable in this sample."
                : "Largest service host · PID " + largestHost.Id + " · " + largestHost.PrivateMemory + " private · " + largestHost.Ram + " RAM\n" +
                  (largestHost.Services.Count == 0 ? "No running services could be matched to this process."
                    : largestHost.ServiceNames) + "\nMemory belongs to the whole host; this does not identify which shared service allocated it.";
            string pressure = _commitBar.Value >= 90 ? "Commit nearly full · allocations may fail. " : _ramBar.Value >= 90 ? "RAM pressure · paging may slow playback. " : "";
            _status.Text = pressure + ResourceMonitor.Bytes(snapshot.RamAvailable) + " RAM available · peak commit " + ResourceMonitor.Bytes(snapshot.CommitPeak) +
                " · updated " + snapshot.Captured.ToString("h:mm:ss") + (snapshot.Unavailable == 0 ? "" : " · " + snapshot.Unavailable + " processes unavailable");
            RenderRows();
        }
        private void RenderRows()
        {
            if (_snapshot == null) return;
            ProcessUsage selected = _processes.SelectedItem as ProcessUsage;
            List<ProcessUsage> rows = _snapshot.Processes.FindAll(delegate(ProcessUsage p)
                { return _processFilter == 0 || (_processFilter == 1 ? p.Browser : p.ServiceHost); });
            rows.Sort(delegate(ProcessUsage a, ProcessUsage b)
            {
                int order = _sort == 2 ? (b.CpuPercent ?? -1).CompareTo(a.CpuPercent ?? -1)
                    : _sort == 1 ? b.PrivateBytes.CompareTo(a.PrivateBytes) : b.RamBytes.CompareTo(a.RamBytes);
                return order == 0 ? a.Id.CompareTo(b.Id) : order;
            });
            _processes.ItemsSource = rows;
            if (selected != null) _processes.SelectedItem = rows.Find(delegate(ProcessUsage p) { return p.Id == selected.Id && p.Started == selected.Started; });
        }
        private void UpdateSelection()
        {
            ProcessUsage selected = _processes.SelectedItem as ProcessUsage;
            _end.IsEnabled = ResourceMonitor.CanEndTask(selected);
            _detail.Text = selected == null ? "Select a process. Windows processes and this dashboard are protected."
                : selected.Name + " · PID " + selected.Id + " · virtual address space " + ResourceMonitor.Bytes((ulong)Math.Max(0, selected.VirtualBytes)) +
                    (_end.IsEnabled ? "" : " · cannot end here") +
                    (selected.Services.Count == 0 ? "" : "\nHosted services: " + selected.ServiceNames);
        }
        private void EndSelected(object sender, RoutedEventArgs e)
        {
            ProcessUsage selected = _processes.SelectedItem as ProcessUsage;
            if (!ResourceMonitor.CanEndTask(selected)) { UpdateSelection(); return; }
            Window owner = Window.GetWindow(this);
            if (MessageBox.Show(owner, "End " + selected.Name + " (PID " + selected.Id + ")?\n\nUnsaved work in this process will be lost. Ending a browser process may interrupt its tabs or playback.",
                "End task", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            try { ResourceMonitor.EndTask(selected); _status.Text = "Task ended. Usage will update on the next sample."; }
            catch (Exception error) { _status.Text = error.Message; }
            UpdateSelection();
        }
    }
}
