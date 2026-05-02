using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using BetterWorkTime.Data.Sqlite;
using Microsoft.Win32;

namespace BetterWorkTime.App.Views;

public partial class SettingsView : UserControl
{
    private sealed record SoundItem(string Label, string? Path);

    private readonly SettingsRepository _settings;
    private readonly string _dbPath;
    private string? _customSoundPath;

    public SettingsView(string dbPath)
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
        StartMinimizedBox.IsChecked    = _settings.GetBool(SettingsWindow.KeyStartMinimized, true);
        HotkeysEnabledBox.IsChecked    = _settings.GetBool(SettingsWindow.KeyHotkeysEnabled, false);

        // Tracking
        IdleThresholdBox.Text = _settings.GetInt(SettingsWindow.KeyIdleThreshold, 5).ToString();

        // Hydration
        HydrationEnabledBox.IsChecked     = _settings.GetBool(SettingsWindow.KeyHydrationEnabled, false);
        HydrationIntervalBox.Text         = _settings.GetInt(SettingsWindow.KeyHydrationInterval, 30).ToString();
        RespectFocusAssistBox.IsChecked   = _settings.GetBool(SettingsWindow.KeyRespectFocusAssist, true);

        var savedSound = _settings.GetString(SettingsWindow.KeyHydrationSound);
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
        OpenFolderAfterExportBox.IsChecked = _settings.GetBool(SettingsWindow.KeyOpenFolderAfterExport, false);
        var lastFolder = _settings.GetString(SettingsWindow.KeyLastExportFolder);
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
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

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
            MessageBox.Show("Sound file not found.", "Tuntio"); return;
        }
        try { using var player = new SoundPlayer(path); player.Play(); }
        catch (Exception ex) { MessageBox.Show($"Could not play sound: {ex.Message}", "Tuntio"); }
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
            MessageBox.Show("Logs folder not found.", "Tuntio");
    }

    private void ClearExportFolder_Click(object sender, RoutedEventArgs e)
    {
        _settings.SetString(SettingsWindow.KeyLastExportFolder, null);
        LastExportFolderText.Text = "(none)";
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        LoadValues();
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

        _settings.SetBool(SettingsWindow.KeyStartMinimized,       StartMinimizedBox.IsChecked == true);
        _settings.SetBool(SettingsWindow.KeyHotkeysEnabled,        HotkeysEnabledBox.IsChecked == true);
        _settings.SetInt(SettingsWindow.KeyIdleThreshold,          idleMin);
        _settings.SetBool(SettingsWindow.KeyHydrationEnabled,      HydrationEnabledBox.IsChecked == true);
        _settings.SetInt(SettingsWindow.KeyHydrationInterval,      hydrMin);
        _settings.SetString(SettingsWindow.KeyHydrationSound,      GetSelectedSoundPath());
        _settings.SetBool(SettingsWindow.KeyRespectFocusAssist,    RespectFocusAssistBox.IsChecked == true);
        _settings.SetBool(SettingsWindow.KeyOpenFolderAfterExport, OpenFolderAfterExportBox.IsChecked == true);

        var app = (App)Application.Current;
        app.RefreshCachedSettings();
        app.ApplyHotkeySettings();

        // Show brief "Saved ✓" feedback
        SavedText.Visibility = Visibility.Visible;
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += (_, _) => { timer.Stop(); SavedText.Visibility = Visibility.Collapsed; };
        timer.Start();
    }

    private string? GetSelectedSoundPath()
    {
        if (SoundCombo.SelectedItem is SoundItem item)
            return item.Path ?? _customSoundPath;
        return null;
    }
}
