using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BetterWorkTime.Data.Sqlite;

namespace BetterWorkTime.App;

public partial class SwitchTaskDialog : Window
{
    private sealed record ProjectItem(string? Id, string Name);

    private const string DefaultTaskText = "Working hard...";

    private readonly ProjectRepository _projects;

    public string? SelectedProjectId { get; private set; }
    public string? SelectedTaskName  { get; private set; }

    private readonly string? _initialProjectId;
    private readonly string? _initialTaskName;

    public SwitchTaskDialog(ProjectRepository projects,
        string? initialProjectId = null, string? initialTaskName = null)
    {
        InitializeComponent();
        _projects = projects;
        _initialProjectId = initialProjectId;
        _initialTaskName  = initialTaskName;
        Loaded += (_, _) => LoadProjects();
    }

    private void LoadProjects()
    {
        ProjectCombo.Items.Clear();
        ProjectCombo.Items.Add(new ProjectItem(null, "(Unassigned)"));
        foreach (var p in _projects.GetAllActive())
            ProjectCombo.Items.Add(new ProjectItem(p.Id, p.Name));

        // Pre-select initial project if provided, otherwise default to first item
        if (_initialProjectId != null)
        {
            foreach (ProjectItem item in ProjectCombo.Items)
            {
                if (item.Id == _initialProjectId) { ProjectCombo.SelectedItem = item; break; }
            }
        }
        else
        {
            ProjectCombo.SelectedIndex = 0;
        }

        // Pre-fill task name
        if (!string.IsNullOrWhiteSpace(_initialTaskName))
        {
            TaskNameBox.Text = _initialTaskName;
            TaskNameBox.Foreground = SystemColors.ControlTextBrush;
            TaskNameBox.SelectAll();
        }
        else
        {
            SetDefaultTaskText();
        }
    }

    private void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasProject = (ProjectCombo.SelectedItem as ProjectItem)?.Id != null;
        TaskNameBox.IsEnabled = hasProject;
        if (!hasProject)
            SetDefaultTaskText();
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

    private void SetDefaultTaskText()
    {
        TaskNameBox.Text = DefaultTaskText;
        TaskNameBox.Foreground = SystemColors.GrayTextBrush;
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        SelectedProjectId = (ProjectCombo.SelectedItem as ProjectItem)?.Id;
        var raw = TaskNameBox.Text.Trim();
        SelectedTaskName = (raw == DefaultTaskText || string.IsNullOrEmpty(raw)) ? null : raw;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
