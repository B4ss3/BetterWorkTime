using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BetterWorkTime.Data;
using BetterWorkTime.Data.Sqlite;
using Microsoft.Win32;

namespace BetterWorkTime.App.Views;

public partial class ReportsView : UserControl
{
    private sealed record ProjectFilterItem(string? Id, string Name);

    private sealed class EntryVm
    {
        public string  DateStr     { get; init; } = "";
        public string  StartStr    { get; init; } = "";
        public string  EndStr      { get; init; } = "";
        public string  DurationStr { get; init; } = "";
        public string  ProjectName { get; init; } = "";
        public string  TaskName    { get; init; } = "";
        public string  Tags        { get; init; } = "";
        public string? Note        { get; init; }
        public string  IdleStr     { get; init; } = "";
        public string  PauseStr    { get; init; } = "";
        public string  LiveStr     { get; init; } = "";
        public ReportEntryRow Source { get; init; } = null!;
    }

    private sealed class BreakdownVm
    {
        public string ProjectName  { get; init; } = "";
        public long   DurationSec  { get; init; }
        public string DurationStr  { get; set; }  = "";
        public string ShareStr     { get; set; }  = "";
    }

    private sealed record PeriodItem(string Label, Func<(long Start, long End)> GetRange);

    private readonly string _dbPath;
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private List<ReportEntryRow> _currentRows = new();

    private string _sortColumn = "Date";
    private bool   _sortAscending = false;
    private GridViewColumnHeader? _lastSortHeader;

    public ReportsView(string dbPath)
    {
        InitializeComponent();
        _dbPath = dbPath;

        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); RunQuery(); };

        Loaded += (_, _) =>
        {
            PopulatePeriodCombo();
            PopulateProjectCombo();
            PeriodCombo.SelectedIndex = 0;
        };
    }

    // ── Combo population ─────────────────────────────────────────────────

    private void PopulatePeriodCombo()
    {
        PeriodCombo.Items.Clear();
        PeriodCombo.Items.Add(new PeriodItem("Today",         () => DayRange(DateTime.Today)));
        PeriodCombo.Items.Add(new PeriodItem("Yesterday",     () => DayRange(DateTime.Today.AddDays(-1))));
        PeriodCombo.Items.Add(new PeriodItem("This week",     ThisWeek));
        PeriodCombo.Items.Add(new PeriodItem("Last week",     LastWeek));
        PeriodCombo.Items.Add(new PeriodItem("This month",    ThisMonth));
        PeriodCombo.Items.Add(new PeriodItem("Last month",    LastMonth));
        PeriodCombo.Items.Add(new PeriodItem("Last 7 days",   () => RelativeDays(-7)));
        PeriodCombo.Items.Add(new PeriodItem("Last 30 days",  () => RelativeDays(-30)));
        PeriodCombo.Items.Add(new PeriodItem("Custom range…", () => (0, 0)));
        PeriodCombo.DisplayMemberPath = "Label";
    }

    private void PopulateProjectCombo()
    {
        ProjectFilterCombo.Items.Clear();
        ProjectFilterCombo.Items.Add(new ProjectFilterItem(null, "(All projects)"));
        ProjectFilterCombo.Items.Add(new ProjectFilterItem(string.Empty, "(Unassigned)"));
        foreach (var p in new ProjectRepository(_dbPath).GetAllActive())
            ProjectFilterCombo.Items.Add(new ProjectFilterItem(p.Id, p.Name));
        ProjectFilterCombo.SelectedIndex = 0;
    }

    // ── Filter event handlers ─────────────────────────────────────────────

    private void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeriodCombo.SelectedItem is not PeriodItem item) return;

        var isCustom = item.Label == "Custom range…";
        CustomRangePanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

        if (!isCustom)
            RunQuery();
        else
        {
            var (s, end) = ThisWeek();
            FromPicker.SelectedDate = DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime.Date;
            ToPicker.SelectedDate   = DateTimeOffset.FromUnixTimeSeconds(end).LocalDateTime.Date.AddDays(-1);
        }
    }

    private void DateFilter_Changed(object? sender, SelectionChangedEventArgs e) => RunQuery();
    private void Filter_Changed(object sender, RoutedEventArgs e)                 => RunQuery();

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header || header.Tag is not string col) return;

        if (_sortColumn == col)
            _sortAscending = !_sortAscending;
        else
        {
            if (_lastSortHeader != null)
                _lastSortHeader.Content = ((string)_lastSortHeader.Content).TrimEnd(' ', '▲', '▼');
            _sortColumn   = col;
            _sortAscending = true;
        }

        header.Content = $"{col} {(_sortAscending ? "▲" : "▼")}";
        _lastSortHeader = header;

        SortAndBindEntries();
    }

    private void NoteSearch_Changed(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    // ── Query ────────────────────────────────────────────────────────────

    private void RunQuery()
    {
        var range = GetSelectedRange();
        if (range == null) return;
        var (startUtc, endUtc) = range.Value;

        var projectFilter = (ProjectFilterCombo.SelectedItem as ProjectFilterItem)?.Id;
        string? projectId = projectFilter == null ? null : projectFilter;
        if (ProjectFilterCombo.SelectedIndex == 0) projectId = null;

        var query = new ReportQuery
        {
            StartUtc      = startUtc,
            EndUtc        = endUtc,
            ProjectId     = projectId,
            NoteSearch    = string.IsNullOrWhiteSpace(NoteSearchBox.Text) ? null : NoteSearchBox.Text.Trim(),
            IncludeIdle   = IncludeIdleBox.IsChecked   == true,
            IncludePauses = IncludePausesBox.IsChecked == true,
        };

        var repo = new ReportRepository(_dbPath);
        _currentRows = new List<ReportEntryRow>(repo.GetEntries(query));
        SortAndBindEntries();

        var breakdown     = repo.GetProjectBreakdown(query);
        var breakdownVms  = breakdown.Select(b => new BreakdownVm
        {
            ProjectName = b.ProjectName,
            DurationSec = b.DurationSec,
        }).ToList();

        if (query.IncludePauses)
        {
            var pauseSecs = _currentRows.Where(r => r.IsPause).Sum(r => r.DurationSec);
            if (pauseSecs > 0)
                breakdownVms.Add(new BreakdownVm { ProjectName = "⏸ Paused", DurationSec = pauseSecs });
        }

        var totalBreakdownSec = breakdownVms.Sum(x => x.DurationSec);
        foreach (var bvm in breakdownVms)
        {
            bvm.DurationStr = FormatDuration(TimeSpan.FromSeconds(bvm.DurationSec));
            bvm.ShareStr    = totalBreakdownSec > 0
                ? $"{bvm.DurationSec * 100.0 / totalBreakdownSec:F1}%"
                : "—";
        }
        BreakdownList.ItemsSource = breakdownVms;

        var workSec  = _currentRows.Where(r => !r.IsIdle).Sum(r => r.DurationSec);
        var idleSec  = _currentRows.Where(r => r.IsIdle && !r.IsPause).Sum(r => r.DurationSec);
        var pauseSec = _currentRows.Where(r => r.IsPause).Sum(r => r.DurationSec);
        var workCount = _currentRows.Count(r => !r.IsIdle);
        var summary = $"{workCount} entries  ·  Work {FormatDuration(TimeSpan.FromSeconds(workSec))}";
        if (pauseSec > 0) summary += $"  ·  Paused {FormatDuration(TimeSpan.FromSeconds(pauseSec))}";
        if (idleSec  > 0) summary += $"  ·  Idle {FormatDuration(TimeSpan.FromSeconds(idleSec))}";
        SummaryText.Text = summary;

        RenderChart();
    }

    private (long Start, long End)? GetSelectedRange()
    {
        if (PeriodCombo.SelectedItem is not PeriodItem item) return null;

        if (item.Label == "Custom range…")
        {
            if (FromPicker.SelectedDate == null || ToPicker.SelectedDate == null) return null;
            var from = FromPicker.SelectedDate.Value.Date;
            var to   = ToPicker.SelectedDate.Value.Date.AddDays(1);
            if (to < from) return null;
            return (new DateTimeOffset(from, TimeZoneInfo.Local.GetUtcOffset(from)).ToUnixTimeSeconds(),
                    new DateTimeOffset(to,   TimeZoneInfo.Local.GetUtcOffset(to)).ToUnixTimeSeconds());
        }

        var range = item.GetRange();
        return range.Start == 0 ? null : range;
    }

    private void SortAndBindEntries()
    {
        IEnumerable<ReportEntryRow> sorted = _sortColumn switch
        {
            "Start"    => _sortAscending ? _currentRows.OrderBy(r => r.StartUtc)    : _currentRows.OrderByDescending(r => r.StartUtc),
            "End"      => _sortAscending ? _currentRows.OrderBy(r => r.EndUtc)      : _currentRows.OrderByDescending(r => r.EndUtc),
            "Duration" => _sortAscending ? _currentRows.OrderBy(r => r.DurationSec) : _currentRows.OrderByDescending(r => r.DurationSec),
            "Project"  => _sortAscending ? _currentRows.OrderBy(r => r.ProjectName) : _currentRows.OrderByDescending(r => r.ProjectName),
            "Task"     => _sortAscending ? _currentRows.OrderBy(r => r.TaskName)    : _currentRows.OrderByDescending(r => r.TaskName),
            _          => _sortAscending ? _currentRows.OrderBy(r => r.StartUtc)    : _currentRows.OrderByDescending(r => r.StartUtc),
        };

        EntriesList.ItemsSource = sorted.Select(row =>
        {
            var start = DateTimeOffset.FromUnixTimeSeconds(row.StartUtc).LocalDateTime;
            var end   = DateTimeOffset.FromUnixTimeSeconds(row.EndUtc).LocalDateTime;
            return new EntryVm
            {
                DateStr     = start.ToString("yyyy-MM-dd"),
                StartStr    = start.ToString("HH:mm"),
                EndStr      = row.IsLive ? "▶ live" : end.ToString("HH:mm"),
                DurationStr = FormatDuration(TimeSpan.FromSeconds(row.DurationSec)),
                ProjectName = row.ProjectName ?? "(Unassigned)",
                TaskName    = row.TaskName ?? "",
                Tags        = string.Join(", ", row.TagNames),
                Note        = row.Note,
                IdleStr     = (row.IsIdle && !row.IsPause) ? "✓" : "",
                PauseStr    = row.IsPause ? "✓" : "",
                Source      = row,
            };
        }).ToList();
    }

    // ── Export ────────────────────────────────────────────────────────────

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRows.Count == 0)
        {
            MessageBox.Show("No entries to export.", "Export CSV");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title      = "Export CSV",
            Filter     = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName   = $"BetterWorkTime_{DateTime.Today:yyyy-MM-dd}.csv",
        };

        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            CsvExporter.Write(dlg.FileName, _currentRows);

            var settings = new SettingsRepository(_dbPath);
            settings.SetString(SettingsWindow.KeyLastExportFolder,
                System.IO.Path.GetDirectoryName(dlg.FileName));

            if (settings.GetBool(SettingsWindow.KeyOpenFolderAfterExport, false))
            {
                var dir = System.IO.Path.GetDirectoryName(dlg.FileName);
                if (dir != null) System.Diagnostics.Process.Start("explorer.exe", dir);
            }
            else
            {
                MessageBox.Show($"Exported {_currentRows.Count} entries to:\n{dlg.FileName}",
                    "Export CSV", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Export CSV",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Date range helpers ───────────────────────────────────────────────

    private static (long Start, long End) DayRange(DateTime date)
    {
        var start = date.Date;
        var end   = start.AddDays(1);
        return (ToUtc(start), ToUtc(end));
    }

    private static (long Start, long End) ThisWeek()
    {
        var today = DateTime.Today;
        int diff  = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
        if (diff < 0) diff += 7;
        var start = today.AddDays(-diff);
        return (ToUtc(start), ToUtc(start.AddDays(7)));
    }

    private static (long Start, long End) LastWeek()
    {
        var (s, _) = ThisWeek();
        var start  = DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime.AddDays(-7);
        return (ToUtc(start), ToUtc(start.AddDays(7)));
    }

    private static (long Start, long End) ThisMonth()
    {
        var today = DateTime.Today;
        var start = new DateTime(today.Year, today.Month, 1);
        return (ToUtc(start), ToUtc(start.AddMonths(1)));
    }

    private static (long Start, long End) LastMonth()
    {
        var (s, _) = ThisMonth();
        var start  = DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime.AddMonths(-1);
        return (ToUtc(start), ToUtc(start.AddMonths(1)));
    }

    private static (long Start, long End) RelativeDays(int days)
    {
        var end   = DateTime.Today.AddDays(1);
        var start = DateTime.Today.AddDays(days);
        return (ToUtc(start), ToUtc(end));
    }

    private static long ToUtc(DateTime local)
        => new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUnixTimeSeconds();

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }

    // ── Chart ─────────────────────────────────────────────────────────────

    private void RenderChart()
    {
        ChartPanel.Children.Clear();

        var range = GetSelectedRange();
        if (range == null) return;
        var (startUtc, endUtc) = range.Value;

        var chartQuery = new ReportQuery
        {
            StartUtc      = startUtc,
            EndUtc        = endUtc,
            ProjectId     = (ProjectFilterCombo.SelectedIndex == 0)
                                ? null
                                : (ProjectFilterCombo.SelectedItem as ProjectFilterItem)?.Id,
            IncludeIdle   = true,
            IncludePauses = true,
        };

        var rows = new ReportRepository(_dbPath).GetEntries(chartQuery);

        if (rows.Count == 0)
        {
            ChartPanel.Children.Add(new TextBlock
            {
                Text       = "No entries for the selected period.",
                FontStyle  = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(155, 161, 176)),
                Margin     = new Thickness(0, 20, 0, 0),
            });
            return;
        }

        var projectColors = new ProjectRepository(_dbPath).GetAll()
            .Where(p => p.Color != null)
            .ToDictionary(p => p.Id, p => ParseHexColor(p.Color!));

        ChartPanel.Children.Add(BuildLegend(projectColors));

        const double BAR_H     = 34;
        const double BAR_W     = 680;
        const double LABEL_W   = 80;
        const double TICK_H    = 18;
        const double ROW_GAP   = 20;

        var byDay = rows
            .GroupBy(r => DateTimeOffset.FromUnixTimeSeconds(r.StartUtc).LocalDateTime.Date)
            .OrderBy(g => g.Key);

        foreach (var dayGroup in byDay)
        {
            var entries   = dayGroup.OrderBy(e => e.StartUtc).ToList();
            var date      = dayGroup.Key;
            var nowUtc    = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var firstStart = entries.Min(e => e.StartUtc);
            var lastEnd    = entries.Max(e => e.IsLive ? nowUtc : e.EndUtc);
            var localFirst = DateTimeOffset.FromUnixTimeSeconds(firstStart).LocalDateTime;
            var localLast  = DateTimeOffset.FromUnixTimeSeconds(lastEnd).LocalDateTime;
            var axisStartL = localFirst.Date.AddHours(localFirst.Hour);
            var axisEndL   = localLast.Date.AddHours(localLast.Hour + 1);
            var axisStart  = ToUtcLocal(axisStartL);
            var axisEnd    = ToUtcLocal(axisEndL);
            var totalSec   = (double)(axisEnd - axisStart);
            if (totalSec <= 0) continue;

            var container = new StackPanel { Margin = new Thickness(0, 0, 0, ROW_GAP) };

            var dayLabel = date == DateTime.Today ? "Today" : date.ToString("ddd, MMM d");
            container.Children.Add(new TextBlock
            {
                Text       = dayLabel,
                FontSize   = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(92, 101, 120)),
                Margin     = new Thickness(LABEL_W, 0, 0, 4),
            });

            var canvas = new System.Windows.Controls.Canvas { Width = LABEL_W + BAR_W, Height = BAR_H };

            var track = new Border
            {
                Width           = BAR_W,
                Height          = BAR_H,
                Background      = new SolidColorBrush(Color.FromRgb(242, 243, 245)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(221, 225, 231)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
            };
            System.Windows.Controls.Canvas.SetLeft(track, LABEL_W);
            System.Windows.Controls.Canvas.SetTop(track, 0);
            canvas.Children.Add(track);

            foreach (var entry in entries)
            {
                var entryEnd = entry.IsLive ? nowUtc : entry.EndUtc;
                var xRatio   = (entry.StartUtc - axisStart) / totalSec;
                var wRatio   = (entryEnd - entry.StartUtc)  / totalSec;
                var x        = LABEL_W + xRatio * BAR_W;
                var w        = Math.Max(2, wRatio * BAR_W - 1);

                Color blockColor;
                if (entry.IsPause)
                    blockColor = Color.FromRgb(107, 91, 158);
                else if (entry.IsIdle)
                    blockColor = Color.FromRgb(161, 130, 30);
                else if (entry.ProjectId != null && projectColors.TryGetValue(entry.ProjectId, out var pc))
                    blockColor = pc;
                else
                    blockColor = Color.FromRgb(74, 127, 165);

                var tipProject = entry.IsPause ? "⏸ Pause"
                               : entry.IsIdle  ? "💤 Idle"
                               : entry.ProjectName ?? "(Unassigned)";
                if (!entry.IsPause && !entry.IsIdle && entry.TaskName != null)
                    tipProject += $"  /  {entry.TaskName}";
                var sLocal = DateTimeOffset.FromUnixTimeSeconds(entry.StartUtc).LocalDateTime;
                var eLocal = DateTimeOffset.FromUnixTimeSeconds(entryEnd).LocalDateTime;
                var tip = $"{tipProject}\n{sLocal:HH:mm} – {eLocal:HH:mm}   {FormatDuration(TimeSpan.FromSeconds(entry.DurationSec))}";

                var block = new Border
                {
                    Width        = w,
                    Height       = BAR_H - 4,
                    Background   = new SolidColorBrush(blockColor),
                    CornerRadius = new CornerRadius(3),
                    ToolTip      = tip,
                };
                System.Windows.Controls.Canvas.SetLeft(block, x);
                System.Windows.Controls.Canvas.SetTop(block, 2);
                canvas.Children.Add(block);
            }

            container.Children.Add(canvas);

            var tickCanvas = new System.Windows.Controls.Canvas { Width = LABEL_W + BAR_W, Height = TICK_H };
            for (var h = axisStartL; h <= axisEndL; h = h.AddHours(1))
            {
                var hUtc = ToUtcLocal(h);
                var x    = LABEL_W + (hUtc - axisStart) / totalSec * BAR_W;
                if (x < LABEL_W - 1 || x > LABEL_W + BAR_W + 1) continue;

                var tick = new TextBlock
                {
                    Text       = h.ToString("HH:mm"),
                    FontSize   = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(155, 161, 176)),
                };
                System.Windows.Controls.Canvas.SetLeft(tick, x - 14);
                System.Windows.Controls.Canvas.SetTop(tick, 2);
                tickCanvas.Children.Add(tick);
            }

            container.Children.Add(tickCanvas);
            ChartPanel.Children.Add(container);
        }
    }

    private UIElement BuildLegend(Dictionary<string, Color> projectColors)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };

        void AddSwatch(Color c, string label)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 16, 4),
            };
            row.Children.Add(new Border
            {
                Width        = 14, Height = 14,
                Background   = new SolidColorBrush(c),
                CornerRadius = new CornerRadius(3),
                Margin       = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text       = label,
                FontSize   = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(92, 101, 120)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            panel.Children.Add(row);
        }

        var projects = new ProjectRepository(_dbPath).GetAll().Where(p => !p.Archived);
        foreach (var p in projects)
        {
            var c = p.Color != null ? ParseHexColor(p.Color) : Color.FromRgb(74, 127, 165);
            AddSwatch(c, p.Name);
        }

        AddSwatch(Color.FromRgb(74, 127, 165), "(Unassigned)");
        AddSwatch(Color.FromRgb(107, 91, 158), "⏸ Pause");
        AddSwatch(Color.FromRgb(161, 130, 30), "💤 Idle");

        return panel;
    }

    private static long ToUtcLocal(DateTime local)
        => new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUnixTimeSeconds();

    private static Color ParseHexColor(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                var r = Convert.ToByte(hex[..2], 16);
                var g = Convert.ToByte(hex[2..4], 16);
                var b = Convert.ToByte(hex[4..6], 16);
                return Color.FromRgb(r, g, b);
            }
        }
        catch { }
        return Color.FromRgb(74, 127, 165);
    }
}
