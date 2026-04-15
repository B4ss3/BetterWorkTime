using System.Windows;
using System.Windows.Controls;
using BetterWorkTime.Data.Sqlite;

namespace BetterWorkTime.App;

public partial class SwitchTaskDialog : Window
{
    private sealed record ProjectItem(string? Id, string Name);
    private sealed record TaskItem(string? Id, string Name);

    private readonly ProjectRepository _projects;
    private readonly TaskRepository _tasks;

    public string? SelectedProjectId { get; private set; }
    public string? SelectedTaskId { get; private set; }

    public SwitchTaskDialog(ProjectRepository projects, TaskRepository tasks)
    {
        InitializeComponent();
        _projects = projects;
        _tasks = tasks;
        Loaded += (_, _) => LoadProjects();
    }

    private void LoadProjects()
    {
        ProjectCombo.Items.Clear();
        ProjectCombo.Items.Add(new ProjectItem(null, "(Unassigned)"));
        foreach (var p in _projects.GetAllActive())
            ProjectCombo.Items.Add(new ProjectItem(p.Id, p.Name));
        ProjectCombo.SelectedIndex = 0;
    }

    private void LoadTasks(string? projectId)
    {
        TaskCombo.Items.Clear();
        TaskCombo.Items.Add(new TaskItem(null, "(Unassigned)"));
        foreach (var t in _tasks.GetByProject(projectId))
            TaskCombo.Items.Add(new TaskItem(t.Id, t.Name));
        TaskCombo.SelectedIndex = 0;
    }

    private void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = ProjectCombo.SelectedItem as ProjectItem;
        LoadTasks(selected?.Id);
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        SelectedProjectId = (ProjectCombo.SelectedItem as ProjectItem)?.Id;
        SelectedTaskId = (TaskCombo.SelectedItem as TaskItem)?.Id;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
