using System.Windows;

namespace BetterWorkTime.App;

public partial class HelpWindow : Window
{
    // Update this with every release — shown in the What's New tab
    public static readonly string Changelog = """
v1.0.0 — Initial release
─────────────────────────────
• Time tracking with projects, tasks and tags
• Idle detection with Keep / Discard / Split options
• Today's timeline with edit, split and delete actions
• Reports with date filters, project breakdown and CSV export
• Hydration reminders with custom sound
• Sleep/wake detection — idle prompt on resume
• Global hotkeys (Ctrl+Alt+S/T/O/N)
• Rotating logs and crash recovery
• Auto-updates via GitHub Releases
""";

    public HelpWindow()
    {
        InitializeComponent();
        ChangelogText.Text = Changelog;
    }

    public void ShowWhatsNew()
    {
        Tabs.SelectedItem = WhatsNewTab;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
