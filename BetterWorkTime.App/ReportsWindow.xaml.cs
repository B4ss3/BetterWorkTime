using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BetterWorkTime.Data;
using BetterWorkTime.Data.Sqlite;
using Microsoft.Win32;

namespace BetterWorkTime.App;

public partial class ReportsWindow : Window
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
        public string  LiveStr     { get; init; } = "";
        public ReportEntryRow Source { get; init; } = null!;
    }

    private sealed class BreakdownVm
    {
        public string ProjectName  { get; init; } = "";
        public string DurationStr  { get; init; } = "";
        public string ShareStr     { get; init; } = "";
    }

    private sealed record PeriodItem(string Label, Func<(long Start, long End)> GetRange);

    private readonly string _dbPath;
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private List<ReportEntryRow> _currentRows = new();

    public ReportsWindow(string dbPath)
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
            // Set default custom range to this week
            var (s, end) = ThisWeek();
            FromPicker.SelectedDate = DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime.Date;
            ToPicker.SelectedDate   = DateTimeOffset.FromUnixTimeSeconds(end).LocalDateTime.Date.AddDays(-1);
        }
    }

    private void DateFilter_Changed(object? sender, SelectionChangedEventArgs e) => RunQuery();
    private void Filter_Changed(object sender, RoutedEventArgs e)                 => RunQuery();

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
        // null means "all", empty string means "unassigned"
        // The combo uses null for "all" and "" for unassigned — map correctly:
        string? projectId = projectFilter == null ? null : projectFilter; // pass through

        // If the selected item is "All projects" (Id = null sentinel), pass null to mean no filter
        // If it's "(Unassigned)" (Id = ""), pass empty string
        // Otherwise pass the actual project id
        if (ProjectFilterCombo.SelectedIndex == 0) projectId = null; // All

        var query = new ReportQuery
        {
            StartUtc    = startUtc,
            EndUtc      = endUtc,
            ProjectId   = projectId,
            NoteSearch  = string.IsNullOrWhiteSpace(NoteSearchBox.Text) ? null : NoteSearchBox.Text.Trim(),
            IncludeIdle = IncludeIdleBox.IsChecked == true,
        };

        var repo = new ReportRepository(_dbPath);
        _currentRows = new List<ReportEntryRow>(repo.GetEntries(query));

        // Entries tab
        EntriesList.ItemsSource = _currentRows.Select(row =>
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
                IdleStr     = row.IsIdle ? "✓" : "",
                Source      = row,
            };
        }).ToList();

        // Breakdown tab
        var breakdown = repo.GetProjectBreakdown(query);
        var totalSec  = breakdown.Sum(x => x.DurationSec);
        BreakdownList.ItemsSource = breakdown.Select(b => new BreakdownVm
        {
            ProjectName = b.ProjectName,
            DurationStr = FormatDuration(TimeSpan.FromSeconds(b.DurationSec)),
            ShareStr    = totalSec > 0
                ? $"{b.DurationSec * 100.0 / totalSec:F1}%"
                : "—",
        }).ToList();

        // Summary bar
        var workSec = _currentRows.Where(r => !r.IsIdle).Sum(r => r.DurationSec);
        var idleSec = _currentRows.Where(r => r.IsIdle).Sum(r => r.DurationSec);
        var summary = $"{_currentRows.Count} entries  ·  Work {FormatDuration(TimeSpan.FromSeconds(workSec))}";
        if (idleSec > 0) summary += $"  ·  Idle {FormatDuration(TimeSpan.FromSeconds(idleSec))}";
        SummaryText.Text = summary;
    }

    private (long Start, long End)? GetSelectedRange()
    {
        if (PeriodCombo.SelectedItem is not PeriodItem item) return null;

        if (item.Label == "Custom range…")
        {
            if (FromPicker.SelectedDate == null || ToPicker.SelectedDate == null) return null;
            var from = FromPicker.SelectedDate.Value.Date;
            var to   = ToPicker.SelectedDate.Value.Date.AddDays(1); // inclusive end day
            if (to < from) return null;
            return (new DateTimeOffset(from, TimeZoneInfo.Local.GetUtcOffset(from)).ToUnixTimeSeconds(),
                    new DateTimeOffset(to,   TimeZoneInfo.Local.GetUtcOffset(to)).ToUnixTimeSeconds());
        }

        var range = item.GetRange();
        return range.Start == 0 ? null : range;
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

        if (dlg.ShowDialog(this) != true) return;

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
}
