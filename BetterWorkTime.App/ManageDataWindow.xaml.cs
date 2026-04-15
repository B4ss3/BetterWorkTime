using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using BetterWorkTime.Data.Sqlite;

namespace BetterWorkTime.App;

public partial class ManageDataWindow : Window
{
    private sealed record ColorOption(string Label, string? Hex);

    private sealed record ProjectVm(string Id, string Name, string? Color, bool Archived)
    {
        public string ArchivedLabel => Archived ? "Archived" : "Active";
        public string ToggleLabel   => Archived ? "Unarchive" : "Archive";
    }

    private sealed record TagVm(string Id, string Name, string? Color, bool Archived)
    {
        public string ArchivedLabel => Archived ? "Archived" : "Active";
        public string ToggleLabel   => Archived ? "Unarchive" : "Archive";
    }

    private static readonly IReadOnlyList<ColorOption> ColorPresets =
    [
        new("— None —", null),
        new("Blue",     "#3B82F6"),
        new("Green",    "#22C55E"),
        new("Red",      "#EF4444"),
        new("Orange",   "#F97316"),
        new("Purple",   "#A855F7"),
        new("Teal",     "#14B8A6"),
        new("Pink",     "#EC4899"),
        new("Gray",     "#6B7280"),
    ];

    private readonly ProjectRepository _projects;
    private readonly TagRepository     _tags;

    public ManageDataWindow(string dbPath)
    {
        InitializeComponent();
        _projects = new ProjectRepository(dbPath);
        _tags     = new TagRepository(dbPath);

        Loaded += (_, _) =>
        {
            InitColorCombos();
            RefreshProjects();
            RefreshTags();
        };
    }

    // ── Init ────────────────────────────────────────────────────────────

    private void InitColorCombos()
    {
        foreach (var c in ColorPresets)
        {
            NewProjectColor.Items.Add(c);
            NewTagColor.Items.Add(c);
        }
        NewProjectColor.SelectedIndex = 0;
        NewTagColor.SelectedIndex = 0;
    }

    // ── Projects ────────────────────────────────────────────────────────

    private void RefreshProjects()
    {
        var vms = new List<ProjectVm>();
        foreach (var p in _projects.GetAll())
            vms.Add(new ProjectVm(p.Id, p.Name, p.Color, p.Archived));
        ProjectsGrid.ItemsSource = vms;
    }

    private void AddProject_Click(object sender, RoutedEventArgs e)
    {
        var name = NewProjectName.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var color = (NewProjectColor.SelectedItem as ColorOption)?.Hex;
        _projects.Create(name, color);
        NewProjectName.Clear();
        RefreshProjects();
    }

    private void ToggleProjectArchive_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not ProjectVm vm) return;
        _projects.SetArchived(vm.Id, !vm.Archived);
        RefreshProjects();
    }

    // ── Tags ─────────────────────────────────────────────────────────────

    private void RefreshTags()
    {
        var vms = new List<TagVm>();
        foreach (var t in _tags.GetAll())
            vms.Add(new TagVm(t.Id, t.Name, t.Color, t.Archived));
        TagsGrid.ItemsSource = vms;
    }

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        var name = NewTagName.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var color = (NewTagColor.SelectedItem as ColorOption)?.Hex;
        _tags.Create(name, color);
        NewTagName.Clear();
        RefreshTags();
    }

    private void ToggleTagArchive_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not TagVm vm) return;
        _tags.SetArchived(vm.Id, !vm.Archived);
        RefreshTags();
    }
}
