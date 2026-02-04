using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using BetterWorkTime.Data.Sqlite;

namespace BetterWorkTime.App;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private MenuItem? _trayStartStopItem;

    private string? _dbPath;
    private TimeEntryRepository? _repo;
    private RuntimeStateRepository? _runtime;

    private bool _isTracking;
    private string? _runningEntryId;
    private long? _runningStartUtc;

    internal static bool IsQuitting { get; private set; }

    public event EventHandler? TrackingStateChanged;
    public bool IsTracking => _isTracking;

    public TimeSpan GetElapsed()
    {
        if (!_isTracking || _runningStartUtc is null) return TimeSpan.Zero;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sec = Math.Max(0, now - _runningStartUtc.Value);
        return TimeSpan.FromSeconds(sec);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Tray-first: app must stay alive even with no window open
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // --- DB init ---
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BetterWorkTime");

        var dbPath = Path.Combine(baseDir, "betterworktime.sqlite");
        DbInitializer.EnsureCreated(dbPath);

        _dbPath = dbPath;
        _repo = new TimeEntryRepository(dbPath);
        _runtime = new RuntimeStateRepository(dbPath);

        // --- Restore runtime state BEFORE creating tray menu ---
        RestoreRuntimeState();

        // --- Recovery prompt (M1.5) ---
        if (_isTracking && !string.IsNullOrWhiteSpace(_runningEntryId))
        {
            var result = MessageBox.Show(
                "BetterWorkTime detected tracking was running when the app last closed.\n\n" +
                "Yes = Resume\nNo = Stop now (finalize the entry).",
                "Recover tracking session",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
            {
                StopRunningAt(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }
            else
            {
                PersistRunningState();
            }
        }

        // --- Tray icon/menu AFTER restore + recovery ---
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "BetterWorkTime",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenu = BuildTrayMenu()
        };

        _trayIcon.TrayLeftMouseUp += (_, __) => Dispatcher.Invoke(ToggleMainWindow);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private void RestoreRuntimeState()
    {
        if (_runtime == null || _repo == null)
        {
            _isTracking = false;
            _runningEntryId = null;
            _runningStartUtc = null;
            return;
        }

        var isRunningJson = _runtime.Get("tracking.is_running");
        var runningIdJson = _runtime.Get("tracking.running_entry_id");

        var wasRunning =
            bool.TryParse(isRunningJson?.Trim().Trim('"'), out var b) && b;

        string? restoredId = null;
        if (!string.IsNullOrWhiteSpace(runningIdJson) && runningIdJson != "null")
            restoredId = JsonSerializer.Deserialize<string>(runningIdJson);

        _isTracking = wasRunning && !string.IsNullOrWhiteSpace(restoredId);
        _runningEntryId = _isTracking ? restoredId : null;

        _runningStartUtc = (_isTracking && _runningEntryId != null)
            ? _repo.GetStartUtc(_runningEntryId)
            : null;
    }

    internal void ToggleTracking()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ToggleTracking);
            return;
        }

        if (_repo == null) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (!_isTracking)
        {
            _runningEntryId = _repo.StartEntry(now, "manual");
            _runningStartUtc = now;
            _isTracking = true;

            PersistRunningState();
        }
        else
        {
            StopRunningAt(now);
        }

        UpdateTrayStartStopHeader();
        TrackingStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StopRunningAt(long nowUtc)
    {
        if (_repo != null && _runningEntryId != null)
            _repo.StopEntry(_runningEntryId, nowUtc);

        _runningEntryId = null;
        _runningStartUtc = null;
        _isTracking = false;

        PersistStoppedState();
    }

    private void PersistRunningState()
    {
        _runtime?.Set("tracking.is_running", "true");
        _runtime?.Set("tracking.running_entry_id", JsonSerializer.Serialize(_runningEntryId));
    }

    private void PersistStoppedState()
    {
        _runtime?.Set("tracking.is_running", "false");
        _runtime?.Set("tracking.running_entry_id", "null");
    }

    private void UpdateTrayStartStopHeader()
    {
        if (_trayStartStopItem != null)
            _trayStartStopItem.Header = _isTracking ? "Stop" : "Start";
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();

        _trayStartStopItem = new MenuItem { Header = _isTracking ? "Stop" : "Start" };
        _trayStartStopItem.Click += (_, __) => ToggleTracking();

        var switchTask = new MenuItem { Header = "Switch Task..." };
        switchTask.Click += (_, __) => MessageBox.Show("Switch Task... (placeholder)", "BetterWorkTime");

        var addNote = new MenuItem { Header = "Add Note..." };
        addNote.Click += (_, __) => MessageBox.Show("Add Note... (placeholder)", "BetterWorkTime");

        var open = new MenuItem { Header = "Open BetterWorkTime" };
        open.Click += (_, __) => Dispatcher.Invoke(ShowMainWindow);

        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, __) => QuitApp();

        menu.Items.Add(_trayStartStopItem);
        menu.Items.Add(switchTask);
        menu.Items.Add(addNote);
        menu.Items.Add(new Separator());
        menu.Items.Add(open);
        menu.Items.Add(quit);

        return menu;
    }

    private void ToggleMainWindow()
    {
        if (MainWindow == null)
        {
            ShowMainWindow();
            return;
        }

        if (MainWindow.IsVisible)
            MainWindow.Hide();
        else
            ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (MainWindow == null)
            MainWindow = new MainWindow();

        MainWindow.ShowInTaskbar = true;
        MainWindow.WindowState = WindowState.Normal;

        MainWindow.Show();
        MainWindow.Activate();

        // bring-to-front trick
        MainWindow.Topmost = true;
        MainWindow.Topmost = false;

        MainWindow.Focus();
    }

    private void QuitApp()
    {
        IsQuitting = true;

        try { MainWindow?.Close(); } catch { /* ignore */ }

        _trayIcon?.Dispose();
        Shutdown();
    }
}
