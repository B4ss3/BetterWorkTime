using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using BetterWorkTime.Data.Sqlite;
using Microsoft.Win32;

namespace BetterWorkTime.App;

public partial class SettingsWindow : Window
{
    private sealed record SoundItem(string Label, string? Path);

    private readonly SettingsRepository _settings;
    private readonly string _dbPath;
    private string? _customSoundPath;

    // Setting keys (shared with App)
    internal const string KeyIdleThreshold        = "tracking.idle_threshold_min";
    internal const string KeyHydrationEnabled     = "hydration.enabled";
    internal const string KeyHydrationInterval    = "hydration.interval_min";
    internal const string KeyHydrationSound       = "hydration.sound_path";
    internal const string KeyRespectFocusAssist   = "hydration.respect_focus_assist";
    internal const string KeyStartMinimized       = "general.start_minimized";
    internal const string KeyHotkeysEnabled       = "general.hotkeys_enabled";
    internal const string KeyOpenFolderAfterExport = "export.open_folder_after";
    internal const string KeyLastExportFolder     = "export.last_folder";

    public SettingsWindow(string dbPath)
    {
        InitializeComponent();
        _dbPath   = dbPath;
        _settings = new SettingsRepository(dbPath);

        Loaded += (_, _) =>
        {
            PopulateSoundCombo();
            LoadValues();
        };
    }

    private void PopulateSoundCombo()
    {
        SoundCombo.Items.Clear();
        SoundCombo.Items.Add(new SoundItem("Chimes",       @"C:\Windows\Media\chimes.wav"));
        SoundCombo.Items.Add(new SoundItem("Chord",        @"C:\Windows\Media\chord.wav"));
        SoundCombo.Items.Add(new SoundItem("Notification", @"C:\Windows\Media\notify.wav"));
        SoundCombo.Items.Add(new SoundItem("(Custom)",     null));
        SoundCombo.SelectedIndex = 0;
    }

    private void LoadValues()
    {
        // General
        StartMinimizedBox.IsChecked    = _settings.GetBool(KeyStartMinimized, true);
        HotkeysEnabledBox.IsChecked    = _settings.GetBool(KeyHotkeysEnabled, false);

        // Tracking
        IdleThresholdBox.Text = _settings.GetInt(KeyIdleThreshold, 5).ToString();

        // Hydration
        HydrationEnabledBox.IsChecked     = _settings.GetBool(KeyHydrationEnabled, false);
        HydrationIntervalBox.Text         = _settings.GetInt(KeyHydrationInterval, 30).ToString();
        RespectFocusAssistBox.IsChecked   = _settings.GetBool(KeyRespectFocusAssist, true);

        var savedSound = _settings.GetString(KeyHydrationSound);
        if (!string.IsNullOrWhiteSpace(savedSound))
        {
            bool found = false;
            foreach (SoundItem item in SoundCombo.Items)
            {
                if (item.Path != null &&
                    string.Equals(item.Path, savedSound, StringComparison.OrdinalIgnoreCase))
                {
                    SoundCombo.SelectedItem = item;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                _customSoundPath = savedSound;
                SelectCustomItem();
            }
        }

        // Export
        OpenFolderAfterExportBox.IsChecked = _settings.GetBool(KeyOpenFolderAfterExport, false);
        var lastFolder = _settings.GetString(KeyLastExportFolder);
        LastExportFolderText.Text = string.IsNullOrWhiteSpace(lastFolder) ? "(none)" : lastFolder;

        // About
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = ver != null ? $"Version {ver.Major}.{ver.Minor}.{ver.Build}" : "Version —";

        UpdateControlStates();
    }

    private void UpdateControlStates()
    {
        var enabled = HydrationEnabledBox.IsChecked == true;
        HydrationIntervalBox.IsEnabled  = enabled;
        SoundCombo.IsEnabled            = enabled;
        RespectFocusAssistBox.IsEnabled = enabled;
    }

    private void HydrationEnabled_Changed(object sender, RoutedEventArgs e)
        => UpdateControlStates();

    private void SoundCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SoundCombo.SelectedItem is SoundItem { Path: null })
            CustomSoundLabel.Visibility = _customSoundPath != null ? Visibility.Visible : Visibility.Collapsed;
        else
            CustomSoundLabel.Visibility = Visibility.Collapsed;
    }

    private void BrowseSound_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select sound file",
            Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        _customSoundPath            = dlg.FileName;
        SelectCustomItem();
        CustomSoundLabel.Text       = Path.GetFileName(_customSoundPath);
        CustomSoundLabel.Visibility = Visibility.Visible;
    }

    private void SelectCustomItem()
    {
        foreach (SoundItem item in SoundCombo.Items)
            if (item.Path == null) { SoundCombo.SelectedItem = item; return; }
    }

    private void PreviewSound_Click(object sender, RoutedEventArgs e)
    {
        var path = GetSelectedSoundPath();
        if (path == null || !File.Exists(path))
        {
            MessageBox.Show("Sound file not found.", "BetterWorkTime"); return;
        }
        try { using var player = new SoundPlayer(path); player.Play(); }
        catch (Exception ex) { MessageBox.Show($"Could not play sound: {ex.Message}", "BetterWorkTime"); }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(_dbPath);
        if (dir != null && Directory.Exists(dir))
            Process.Start("explorer.exe", dir);
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = AppLogger.LogDir;
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            Process.Start("explorer.exe", dir);
        else
            MessageBox.Show("Logs folder not found.", "BetterWorkTime");
    }

    private void ClearExportFolder_Click(object sender, RoutedEventArgs e)
    {
        _settings.SetString(KeyLastExportFolder, null);
        LastExportFolderText.Text = "(none)";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IdleThresholdBox.Text.Trim(), out var idleMin) || idleMin < 1)
        {
            MessageBox.Show("Idle threshold must be a whole number of minutes (minimum 1).",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(HydrationIntervalBox.Text.Trim(), out var hydrMin) || hydrMin < 1)
        {
            MessageBox.Show("Hydration interval must be a whole number of minutes (minimum 1).",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.SetBool(KeyStartMinimized,       StartMinimizedBox.IsChecked == true);
        _settings.SetBool(KeyHotkeysEnabled,        HotkeysEnabledBox.IsChecked == true);
        _settings.SetInt(KeyIdleThreshold,          idleMin);
        _settings.SetBool(KeyHydrationEnabled,      HydrationEnabledBox.IsChecked == true);
        _settings.SetInt(KeyHydrationInterval,      hydrMin);
        _settings.SetString(KeyHydrationSound,      GetSelectedSoundPath());
        _settings.SetBool(KeyRespectFocusAssist,    RespectFocusAssistBox.IsChecked == true);
        _settings.SetBool(KeyOpenFolderAfterExport, OpenFolderAfterExportBox.IsChecked == true);

        // Apply hotkey changes immediately
        ((App)Application.Current).ApplyHotkeySettings();

        DialogResult = true;
    }

    private string? GetSelectedSoundPath()
    {
        if (SoundCombo.SelectedItem is SoundItem item)
            return item.Path ?? _customSoundPath;
        return null;
    }
}
