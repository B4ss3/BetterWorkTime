using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BetterWorkTime.Data.Sqlite;

namespace BetterWorkTime.App;

public partial class MainWindow : Window
{
    private sealed record ProjectItem(string? Id, string Name);

    private sealed class TimelineEntryVm
    {
        public string  Id                  { get; init; } = "";
        public long    StartUtc            { get; init; }
        public long?   EndUtc              { get; init; }
        public string  TimeRange           { get; init; } = "";
        public string  Duration            { get; init; } = "";
        public string  Label               { get; init; } = "";
        public string? Note                { get; init; }
        public bool    IsIdle              { get; init; }
        public bool    CanSplit            { get; init; }
        public IReadOnlyList<string> Tags  { get; init; } = Array.Empty<string>();
        public Visibility NoteVisibility   => string.IsNullOrWhiteSpace(Note) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility IdleBadgeVisibility => IsIdle ? Visibility.Visible : Visibility.Collapsed;
        public Visibility TagsVisibility   => Tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private const string DefaultTaskText = "Working hard...";

    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private App AppRef => (App)Application.Current;

    private bool _loadingUi;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            AppRef.TrackingStateChanged += OnTrackingStateChanged;

            _uiTimer.Tick += (_, _) => { RefreshElapsed(); RefreshTimeline(); };
            _uiTimer.Start();

            LoadProjectCombo();
            LoadTagsPanel();
            SetDefaultTaskText();
            RefreshUi();
            RefreshTimeline();
        };
    }

    // ── Event handlers ───────────────────────────────────────────────────

    private void OnTrackingStateChanged(object? sender, EventArgs e)
    {
        RefreshUi();
        RefreshTimeline();
    }

    private void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;

        var hasProject = (ProjectCombo.SelectedItem as ProjectItem)?.Id != null;
        TaskNameBox.IsEnabled = hasProject;
        if (!hasProject) SetDefaultTaskText();

        // Only auto-switch if tracking AND the user changed the project themselves (not a UI reload)
        if (AppRef.IsTracking && e.RemovedItems.Count > 0)
        {
            var newId = (ProjectCombo.SelectedItem as ProjectItem)?.Id;
            var oldId = (e.RemovedItems[0] as ProjectItem)?.Id;
            if (newId != oldId)
                ExecuteSwitchWithCurrentContext();
        }
    }

    private void TaskNameBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TaskNameBox.Text == DefaultTaskText)
        {
            TaskNameBox.Text = string.Empty;
            TaskNameBox.Foreground = SystemColors.ControlTextBrush;
        }
    }

    private void TaskNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TaskNameBox.Text))
            SetDefaultTaskText();
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AppRef.IsTracking)
        {
            var projectId = (ProjectCombo.SelectedItem as ProjectItem)?.Id;
            var taskName  = GetTaskName();
            var tagIds    = GetSelectedTagIds();
            var note      = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
            AppRef.ToggleTracking(projectId, taskName, tagIds, note);
        }
        else
        {
            AppRef.ToggleTracking();
        }

        RefreshUi();
        RefreshTimeline();
    }

    private void SwitchTaskButton_Click(object sender, RoutedEventArgs e)
    {
        AppRef.SwitchTask();
        RefreshUi();
        RefreshTimeline();
    }

    private void NoteBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!AppRef.IsTracking) return;
        var note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
        AppRef.UpdateRunningNote(note);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => AppRef.OpenSettings();

    private void ReportsButton_Click(object sender, RoutedEventArgs e)
        => AppRef.OpenReports();

    private void ManageButton_Click(object sender, RoutedEventArgs e)
    {
        var win = new ManageDataWindow(AppRef.DbPath);
        win.Owner = this;
        win.ShowDialog();

        LoadProjectCombo();
        LoadTagsPanel();
    }

    // ── Timeline action handlers ──────────────────────────────────────────

    private void EditEntry_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string id) return;

        var todayEntries = LoadTodayEntries();
        var entry = todayEntries.FirstOrDefault(x => x.Id == id);
        if (entry == null) return;

        var dlg = new EditEntryDialog(AppRef.DbPath, entry, todayEntries) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var taskId = dlg.ResultProjectId != null && dlg.ResultTaskName != null
            ? new TaskRepository(AppRef.DbPath).FindOrCreate(dlg.ResultTaskName, dlg.ResultProjectId)
            : null;

        new TimeEntryRepository(AppRef.DbPath).UpdateEntryFull(
            id, dlg.ResultStartUtc, dlg.ResultEndUtc,
            dlg.ResultProjectId, taskId, dlg.ResultNote);

        new TagRepository(AppRef.DbPath).SetForEntry(id, dlg.ResultTagIds);

        RefreshTimeline();
    }

    private void SplitEntry_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string id) return;

        var todayEntries = LoadTodayEntries();
        var entry = todayEntries.FirstOrDefault(x => x.Id == id);
        if (entry == null || !entry.EndUtc.HasValue) return;

        var dlg = new SplitEntryDialog(entry) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        new TimeEntryRepository(AppRef.DbPath).SplitEntry(id, dlg.ResultSplitUtc, "manual");
        RefreshTimeline();
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string id) return;

        var result = MessageBox.Show(
            "Delete this time entry? This cannot be undone.",
            "Delete Entry", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        new TimeEntryRepository(AppRef.DbPath).DeleteEntry(id);
        RefreshTimeline();
    }

    // ── Timeline refresh ─────────────────────────────────────────────────

    private void RefreshTimeline()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(RefreshTimeline); return; }

        var entries  = LoadTodayEntries();
        var vms      = new List<TimelineEntryVm>(entries.Count);
        var tagRepo  = new TagRepository(AppRef.DbPath);

        long workSec = 0;
        long idleSec = 0;

        foreach (var entry in entries)
        {
            var localStart = DateTimeOffset.FromUnixTimeSeconds(entry.StartUtc).LocalDateTime;
            string endStr;
            long dur;

            if (entry.EndUtc.HasValue)
            {
                var localEnd = DateTimeOffset.FromUnixTimeSeconds(entry.EndUtc.Value).LocalDateTime;
                endStr = localEnd.ToString("HH:mm");
                dur    = entry.DurationSec;
            }
            else
            {
                endStr = "…";
                dur    = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - entry.StartUtc);
            }

            if (entry.IsIdle) idleSec += dur; else workSec += dur;

            var label = entry.ProjectName != null
                ? (entry.TaskName != null ? $"{entry.ProjectName} / {entry.TaskName}" : entry.ProjectName)
                : "(Unassigned)";

            var tagNames = tagRepo.GetForEntry(entry.Id)
                                   .Select(t => t.Name).ToList();

            vms.Add(new TimelineEntryVm
            {
                Id        = entry.Id,
                StartUtc  = entry.StartUtc,
                EndUtc    = entry.EndUtc,
                TimeRange = $"{localStart:HH:mm} – {endStr}",
                Duration  = FormatDuration(TimeSpan.FromSeconds(dur)),
                Label     = label,
                Note      = entry.Note,
                IsIdle    = entry.IsIdle,
                CanSplit  = entry.EndUtc.HasValue,
                Tags      = tagNames,
            });
        }

        TimelineList.ItemsSource = vms;
        NoEntriesText.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var workSpan = TimeSpan.FromSeconds(workSec);
        var total    = $"Work {FormatDuration(workSpan)}";
        if (idleSec > 0)
            total += $"  Idle {FormatDuration(TimeSpan.FromSeconds(idleSec))}";
        TodayTotalText.Text = total;
    }

    private IReadOnlyList<TimeEntryRow> LoadTodayEntries()
    {
        var now       = DateTime.Now;
        var dayStart  = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Local);
        var dayEnd    = dayStart.AddDays(1);
        var startUtc  = new DateTimeOffset(dayStart).ToUnixTimeSeconds();
        var endUtc    = new DateTimeOffset(dayEnd).ToUnixTimeSeconds();
        return new TimeEntryRepository(AppRef.DbPath).GetTodayEntries(startUtc, endUtc);
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }

    // ── Load helpers ─────────────────────────────────────────────────────

    private void LoadProjectCombo()
    {
        _loadingUi = true;

        var prevId = (ProjectCombo.SelectedItem as ProjectItem)?.Id;

        ProjectCombo.Items.Clear();
        ProjectCombo.Items.Add(new ProjectItem(null, "(Unassigned)"));
        foreach (var p in new ProjectRepository(AppRef.DbPath).GetAllActive())
            ProjectCombo.Items.Add(new ProjectItem(p.Id, p.Name));

        var targetId = AppRef.IsTracking ? AppRef.RunningProjectId : prevId;
        SelectProjectById(targetId);

        var hasProject = (ProjectCombo.SelectedItem as ProjectItem)?.Id != null;
        TaskNameBox.IsEnabled = hasProject;

        _loadingUi = false;
    }

    private void LoadTagsPanel()
    {
        TagsPanel.Children.Clear();
        foreach (var tag in new TagRepository(AppRef.DbPath).GetAllActive())
        {
            var cb = new CheckBox
            {
                Content = tag.Name,
                Tag     = tag.Id,
                Margin  = new Thickness(4, 2, 4, 2)
            };
            TagsPanel.Children.Add(cb);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void SelectProjectById(string? id)
    {
        foreach (ProjectItem item in ProjectCombo.Items)
        {
            if (item.Id == id) { ProjectCombo.SelectedItem = item; return; }
        }
        if (ProjectCombo.Items.Count > 0)
            ProjectCombo.SelectedIndex = 0;
    }

    private string? GetTaskName()
    {
        var text = TaskNameBox.Text.Trim();
        return (text == DefaultTaskText || string.IsNullOrEmpty(text)) ? null : text;
    }

    private void SetDefaultTaskText()
    {
        TaskNameBox.Text = DefaultTaskText;
        TaskNameBox.Foreground = SystemColors.GrayTextBrush;
    }

    private void SetTaskName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            SetDefaultTaskText();
        }
        else
        {
            TaskNameBox.Text = name;
            TaskNameBox.Foreground = SystemColors.ControlTextBrush;
        }
    }

    private IReadOnlyList<string> GetSelectedTagIds()
    {
        var ids = new List<string>();
        foreach (CheckBox cb in TagsPanel.Children)
            if (cb.IsChecked == true && cb.Tag is string id)
                ids.Add(id);
        return ids;
    }

    private void SetSelectedTagIds(IEnumerable<string> tagIds)
    {
        var set = new HashSet<string>(tagIds);
        foreach (CheckBox cb in TagsPanel.Children)
            cb.IsChecked = cb.Tag is string id && set.Contains(id);
    }

    private void ExecuteSwitchWithCurrentContext()
    {
        if (!AppRef.IsTracking) return;
        var projectId = (ProjectCombo.SelectedItem as ProjectItem)?.Id;
        var taskName  = GetTaskName();
        var tagIds    = GetSelectedTagIds();
        var note      = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
        AppRef.SwitchTaskWithContext(projectId, taskName, tagIds, note);
    }

    // ── UI refresh ───────────────────────────────────────────────────────

    private void RefreshUi()
    {
        var running = AppRef.IsTracking;

        StatusText.Text            = running ? "Status: Tracking" : "Status: Stopped";
        StartStopButton.Content    = running ? "Stop" : "Start";
        SwitchTaskButton.IsEnabled = running;

        if (running)
        {
            LoadProjectCombo();
            SetTaskName(AppRef.RunningTaskName);

            if (NoteBox.Text != (AppRef.RunningNote ?? string.Empty))
                NoteBox.Text = AppRef.RunningNote ?? string.Empty;
        }

        RefreshElapsed();
    }

    private void RefreshElapsed()
    {
        var elapsed = AppRef.GetElapsed();
        ElapsedText.Text = $"Elapsed: {elapsed:hh\\:mm\\:ss}";
    }

    // ── Window lifecycle ─────────────────────────────────────────────────

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsQuitting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        AppRef.TrackingStateChanged -= OnTrackingStateChanged;
        _uiTimer.Stop();
        base.OnClosed(e);
    }
}
