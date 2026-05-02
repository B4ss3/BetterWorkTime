using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BetterWorkTime.Data.Sqlite;

namespace BetterWorkTime.App.Views;

public partial class ManageView : UserControl
{
    public static event EventHandler? ProjectsOrTagsChanged;

    private sealed record ColorOption(string Label, string? Hex);

    private sealed record ProjectVm(string Id, string Name, string? Color, bool Archived)
    {
        public string ArchivedLabel => Archived ? "Archived" : "Active";
        public string ToggleLabel   => Archived ? "Unarchive" : "Archive";
        public Color ColorValue     => ParseColor(Color);
    }

    private sealed record TagVm(string Id, string Name, string? Color, bool Archived)
    {
        public string ArchivedLabel => Archived ? "Archived" : "Active";
        public string ToggleLabel   => Archived ? "Unarchive" : "Archive";
        public Color ColorValue     => ParseColor(Color);
    }

    private static Color ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Colors.Transparent;
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Transparent; }
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

    public ManageView(string dbPath)
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

    private void NewProjectName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddProject_Click(sender, e);
    }

    private void AddProject_Click(object sender, RoutedEventArgs e)
    {
        var name = NewProjectName.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var color = (NewProjectColor.SelectedItem as ColorOption)?.Hex;
        _projects.Create(name, color);
        NewProjectName.Clear();
        RefreshProjects();
        ProjectsOrTagsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RenameProject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not ProjectVm vm) return;

        var owner = Window.GetWindow(this);
        var dlg = new RenameDialog(vm.Name) { Owner = owner };
        if (dlg.ShowDialog() != true) return;

        var newName = dlg.ResultName;
        if (string.IsNullOrWhiteSpace(newName) || newName == vm.Name) return;

        _projects.Rename(vm.Id, newName);
        RefreshProjects();
        ProjectsOrTagsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleProjectArchive_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not ProjectVm vm) return;
        _projects.SetArchived(vm.Id, !vm.Archived);
        RefreshProjects();
        ProjectsOrTagsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Tags ─────────────────────────────────────────────────────────────

    private void RefreshTags()
    {
        var vms = new List<TagVm>();
        foreach (var t in _tags.GetAll())
            vms.Add(new TagVm(t.Id, t.Name, t.Color, t.Archived));
        TagsGrid.ItemsSource = vms;
    }

    private void NewTagName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddTag_Click(sender, e);
    }

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        var name = NewTagName.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var color = (NewTagColor.SelectedItem as ColorOption)?.Hex;
        _tags.Create(name, color);
        NewTagName.Clear();
        RefreshTags();
        ProjectsOrTagsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RenameTag_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not TagVm vm) return;

        var owner = Window.GetWindow(this);
        var dlg = new RenameDialog(vm.Name) { Owner = owner };
        if (dlg.ShowDialog() != true) return;

        var newName = dlg.ResultName;
        if (string.IsNullOrWhiteSpace(newName) || newName == vm.Name) return;

        _tags.Rename(vm.Id, newName);
        RefreshTags();
        ProjectsOrTagsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleTagArchive_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not TagVm vm) return;
        _tags.SetArchived(vm.Id, !vm.Archived);
        RefreshTags();
        ProjectsOrTagsChanged?.Invoke(this, EventArgs.Empty);
    }
}
