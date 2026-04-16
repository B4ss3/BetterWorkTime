using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using BetterWorkTime.Data.Sqlite;

namespace BetterWorkTime.App;

public partial class EditEntryDialog : Window
{
    private sealed record ProjectItem(string? Id, string Name);
    private const string DefaultTaskText = "Working hard...";

    private readonly string _dbPath;
    private readonly string _entryId;
    private readonly DateOnly _entryDate;
    private readonly IReadOnlyList<TimeEntryRow> _todayEntries;

    public long ResultStartUtc  { get; private set; }
    public long ResultEndUtc    { get; private set; }
    public string? ResultProjectId { get; private set; }
    public string? ResultTaskName  { get; private set; }
    public string? ResultNote      { get; private set; }

    public EditEntryDialog(string dbPath, TimeEntryRow entry,
        IReadOnlyList<TimeEntryRow> todayEntries)
    {
        InitializeComponent();
        _dbPath       = dbPath;
        _entryId      = entry.Id;
        _entryDate    = DateOnly.FromDateTime(
            DateTimeOffset.FromUnixTimeSeconds(entry.StartUtc).LocalDateTime);
        _todayEntries = todayEntries;

        Loaded += (_, _) =>
        {
            LoadProjectCombo(entry.ProjectId);
            SetTaskName(entry.TaskName);
            NoteBox.Text = entry.Note ?? string.Empty;

            var localStart = DateTimeOffset.FromUnixTimeSeconds(entry.StartUtc).LocalDateTime;
            StartBox.Text = localStart.ToString("HH:mm");

            if (entry.EndUtc.HasValue)
            {
                var localEnd = DateTimeOffset.FromUnixTimeSeconds(entry.EndUtc.Value).LocalDateTime;
                EndBox.Text = localEnd.ToString("HH:mm");
            }
            else
            {
                EndBox.Text = DateTime.Now.ToString("HH:mm");
            }

            StartBox.Focus();
            StartBox.SelectAll();
        };
    }

    private void LoadProjectCombo(string? selectId)
    {
        ProjectCombo.Items.Clear();
        ProjectCombo.Items.Add(new ProjectItem(null, "(Unassigned)"));
        foreach (var p in new ProjectRepository(_dbPath).GetAllActive())
            ProjectCombo.Items.Add(new ProjectItem(p.Id, p.Name));

        foreach (ProjectItem item in ProjectCombo.Items)
        {
            if (item.Id == selectId) { ProjectCombo.SelectedItem = item; return; }
        }
        ProjectCombo.SelectedIndex = 0;
    }

    private void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasProject = (ProjectCombo.SelectedItem as ProjectItem)?.Id != null;
        TaskBox.IsEnabled = hasProject;
        if (!hasProject) SetDefaultTaskText();
    }

    private void TaskBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TaskBox.Text == DefaultTaskText)
        {
            TaskBox.Text = string.Empty;
            TaskBox.Foreground = SystemColors.ControlTextBrush;
        }
    }

    private void TaskBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TaskBox.Text))
            SetDefaultTaskText();
    }

    private void SetDefaultTaskText()
    {
        TaskBox.Text = DefaultTaskText;
        TaskBox.Foreground = SystemColors.GrayTextBrush;
    }

    private void SetTaskName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) { SetDefaultTaskText(); return; }
        TaskBox.Text = name;
        TaskBox.Foreground = SystemColors.ControlTextBrush;
    }

    private string? GetTaskName()
    {
        var t = TaskBox.Text.Trim();
        return (t == DefaultTaskText || string.IsNullOrEmpty(t)) ? null : t;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (!TryParseTime(StartBox.Text, out var startTime))
        {
            ShowError("Start time must be HH:mm (e.g. 09:15).");
            return;
        }
        if (!TryParseTime(EndBox.Text, out var endTime))
        {
            ShowError("End time must be HH:mm (e.g. 10:30).");
            return;
        }

        var startDt = _entryDate.ToDateTime(startTime, DateTimeKind.Local);
        var endDt   = _entryDate.ToDateTime(endTime,   DateTimeKind.Local);

        // If end is earlier than start, assume it crosses midnight → add a day
        if (endDt <= startDt)
            endDt = endDt.AddDays(1);

        var newStart = new DateTimeOffset(startDt).ToUnixTimeSeconds();
        var newEnd   = new DateTimeOffset(endDt).ToUnixTimeSeconds();

        // Overlap check — exclude the entry being edited
        foreach (var other in _todayEntries)
        {
            if (other.Id == _entryId) continue;
            var oEnd = other.EndUtc ?? long.MaxValue;
            if (newStart < oEnd && newEnd > other.StartUtc)
            {
                var os = DateTimeOffset.FromUnixTimeSeconds(other.StartUtc).LocalDateTime;
                var oe = other.EndUtc.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(other.EndUtc.Value).LocalDateTime.ToString("HH:mm")
                    : "now";
                ShowError($"Overlaps with another entry ({os:HH:mm}–{oe}).");
                return;
            }
        }

        ResultStartUtc  = newStart;
        ResultEndUtc    = newEnd;
        ResultProjectId = (ProjectCombo.SelectedItem as ProjectItem)?.Id;
        ResultTaskName  = GetTaskName();
        ResultNote      = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

        DialogResult = true;
    }

    private void ShowError(string msg)
    {
        ErrorText.Text       = msg;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static bool TryParseTime(string text, out TimeOnly result)
    {
        result = default;
        var parts = (text ?? "").Trim().Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        if (h < 0 || h > 23 || m < 0 || m > 59) return false;
        result = new TimeOnly(h, m);
        return true;
    }
}
