using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BetterWorkTime.App;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        RenderChangelog();
    }

    public void ShowWhatsNew() => Tabs.SelectedItem = WhatsNewTab;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ── Changelog rendering ───────────────────────────────────────────────

    private void RenderChangelog()
    {
        ChangelogPanel.Children.Clear();

        var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "changelog.json");
        if (!File.Exists(jsonPath))
        {
            ChangelogPanel.Children.Add(new TextBlock
            {
                Text = "changelog.json not found.",
                Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                FontStyle = FontStyles.Italic
            });
            return;
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllText(jsonPath)); }
        catch
        {
            ChangelogPanel.Children.Add(new TextBlock
            {
                Text = "Could not read changelog.json.",
                Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38))
            });
            return;
        }

        foreach (var versionEntry in doc.RootElement.EnumerateArray())
        {
            var version = versionEntry.GetProperty("version").GetString() ?? "";
            var date    = versionEntry.GetProperty("date").GetString() ?? "";

            // Version header card
            var headerCard = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(29, 78, 216)),
                CornerRadius    = new CornerRadius(6),
                Padding         = new Thickness(14, 10, 14, 10),
                Margin          = new Thickness(0, 0, 0, 6),
            };
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
            headerStack.Children.Add(new TextBlock
            {
                Text       = $"v{version}",
                FontSize   = 14, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new TextBlock
            {
                Text       = $"   {date}",
                FontSize   = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerCard.Child = headerStack;
            ChangelogPanel.Children.Add(headerCard);

            // Change entries
            var entriesCard = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(248, 250, 255)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(219, 234, 254)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(6),
                Padding         = new Thickness(12, 8, 12, 8),
                Margin          = new Thickness(0, 0, 0, 16),
            };
            var entriesStack = new StackPanel();

            foreach (var change in versionEntry.GetProperty("changes").EnumerateArray())
            {
                var type = change.GetProperty("type").GetString() ?? "changed";
                var text = change.GetProperty("text").GetString() ?? "";

                var (badgeText, badgeBg, badgeFg) = type switch
                {
                    "added"   => ("Added",   Color.FromRgb(220, 252, 231), Color.FromRgb(22,  101, 52)),
                    "fixed"   => ("Fixed",   Color.FromRgb(219, 234, 254), Color.FromRgb(29,  78,  216)),
                    "changed" => ("Changed", Color.FromRgb(254, 243, 199), Color.FromRgb(146, 64,  14)),
                    "removed" => ("Removed", Color.FromRgb(254, 226, 226), Color.FromRgb(185, 28,  28)),
                    _         => ("Note",    Color.FromRgb(243, 244, 246), Color.FromRgb(75,  85,  99)),
                };

                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };

                var badge = new Border
                {
                    Background   = new SolidColorBrush(badgeBg),
                    CornerRadius = new CornerRadius(3),
                    Padding      = new Thickness(6, 2, 6, 2),
                    Margin       = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
                DockPanel.SetDock(badge, Dock.Left);
                badge.Child = new TextBlock
                {
                    Text       = badgeText,
                    FontSize   = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(badgeFg)
                };

                var body = new TextBlock
                {
                    Text         = text,
                    FontSize     = 12.5,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground   = new SolidColorBrush(Color.FromRgb(55, 65, 81)),
                    VerticalAlignment = VerticalAlignment.Center
                };

                row.Children.Add(badge);
                row.Children.Add(body);
                entriesStack.Children.Add(row);
            }

            entriesCard.Child = entriesStack;
            ChangelogPanel.Children.Add(entriesCard);
        }
    }
}
