using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BetterWorkTime.Data.Sqlite;

namespace BetterWorkTime.App;

public partial class MainWindow : Window
{
    private sealed record ProjectItem(string? Id, string Name);

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

            _uiTimer.Tick += (_, _) => RefreshElapsed();
            _uiTimer.Start();

            LoadProjectCombo();
            LoadTagsPanel();
            SetDefaultTaskText();
            RefreshUi();
        };
    }

    // ── Event handlers ───────────────────────────────────────────────────

    private void OnTrackingStateChanged(object? sender, EventArgs e) => RefreshUi();

    private void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;

        var hasProject = (ProjectCombo.SelectedItem as ProjectItem)?.Id != null;
        TaskNameBox.IsEnabled = hasProject;
        if (!hasProject) SetDefaultTaskText();

        if (AppRef.IsTracking)
            ExecuteSwitchWithCurrentContext();
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
    }

    private void SwitchTaskButton_Click(object sender, RoutedEventArgs e)
    {
        AppRef.SwitchTask();
        RefreshUi();
    }

    private void NoteBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!AppRef.IsTracking) return;
        var note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
        AppRef.UpdateRunningNote(note);
    }

    private void ManageButton_Click(object sender, RoutedEventArgs e)
    {
        var win = new ManageDataWindow(AppRef.DbPath);
        win.Owner = this;
        win.ShowDialog();

        LoadProjectCombo();
        LoadTagsPanel();
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
            // Sync fields to the running entry
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
