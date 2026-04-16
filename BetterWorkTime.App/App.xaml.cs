using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using BetterWorkTime.Data.Sqlite;
using BetterWorkTime.Platform.Windows;

namespace BetterWorkTime.App;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private MenuItem? _trayStartStopItem;
    private MenuItem? _traySwitchTaskItem;

    private string? _dbPath;
    private TimeEntryRepository? _repo;
    private RuntimeStateRepository? _runtime;
    private TagRepository? _tagRepo;

    private bool _isTracking;
    private string? _runningEntryId;
    private long? _runningStartUtc;
    private string? _runningProjectId;
    private string? _runningTaskName;
    private string? _runningNote;

    // Idle detection
    private readonly DispatcherTimer _idleTick = new() { Interval = TimeSpan.FromSeconds(1) };
    private const int IdleThresholdSeconds = 5 * 60; // 5 min default (settings wiring in M5)
    private bool _idlePromptShowing;
    private long? _idleStartUtc;

    internal static bool IsQuitting { get; private set; }
    internal string DbPath => _dbPath!;

    public event EventHandler? TrackingStateChanged;
    public bool IsTracking => _isTracking;
    public string? RunningProjectId => _runningProjectId;
    public string? RunningTaskName  => _runningTaskName;
    public string? RunningNote      => _runningNote;

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

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BetterWorkTime");

        var dbPath = Path.Combine(baseDir, "betterworktime.sqlite");
        DbInitializer.EnsureCreated(dbPath);

        _dbPath = dbPath;
        _repo = new TimeEntryRepository(dbPath);
        _runtime = new RuntimeStateRepository(dbPath);
        _tagRepo = new TagRepository(dbPath);

        RestoreRuntimeState();

        if (_isTracking && !string.IsNullOrWhiteSpace(_runningEntryId))
        {
            var result = MessageBox.Show(
                "BetterWorkTime detected tracking was running when the app last closed.\n\n" +
                "Yes = Resume\nNo = Stop now (finalize the entry).",
                "Recover tracking session",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
                StopRunningAt(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            else
                PersistRunningState();
        }

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "BetterWorkTime",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenu = BuildTrayMenu()
        };

        _trayIcon.TrayLeftMouseUp += (_, __) => Dispatcher.Invoke(ToggleMainWindow);

        _idleTick.Tick += OnIdleTick;
        _idleTick.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _idleTick.Stop();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    // ── Idle detection ───────────────────────────────────────────────────

    private void OnIdleTick(object? sender, EventArgs e)
    {
        if (!_isTracking || _idlePromptShowing) return;

        var idleSec = IdleDetector.GetIdleSeconds();
        if (idleSec < IdleThresholdSeconds) return;

        // Capture idle start = now - how long they've been idle
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var computedIdleStart = now - idleSec;

        // Clamp to running entry start — can't be idle before we started tracking
        var idleStart = Math.Max(computedIdleStart, _runningStartUtc ?? computedIdleStart);

        // Need at least 1 second of work before the idle
        if (idleStart <= (_runningStartUtc ?? 0)) return;

        _idleStartUtc = idleStart;
        _idlePromptShowing = true;

        var prompt = new IdlePromptWindow(TimeSpan.FromSeconds(idleSec));
        prompt.Closed += (s, _) => ApplyIdleDecision(((IdlePromptWindow)s!).Choice);
        prompt.Show();
    }

    private void ApplyIdleDecision(IdleChoice choice)
    {
        _idlePromptShowing = false;

        if (choice == IdleChoice.Keep || !_isTracking || _idleStartUtc == null || _repo == null)
        {
            _idleStartUtc = null;
            return;
        }

        var now       = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var idleStart = _idleStartUtc.Value;
        var taskId    = ResolveTaskId(_runningProjectId, _runningTaskName);

        // Trim current running entry to idle start
        _repo.StopEntry(_runningEntryId!, idleStart);

        // Create idle entry for the idle segment
        _repo.CreateIdleEntry(idleStart, now, _runningProjectId, taskId);

        _idleStartUtc = null;

        if (choice == IdleChoice.Split)
        {
            // Resume tracking from now
            _runningEntryId  = _repo.StartEntry(now, "manual", _runningProjectId, taskId);
            _runningStartUtc = now;
            _isTracking      = true;
            PersistRunningState();
        }
        else // Discard — stop tracking
        {
            _runningEntryId  = null;
            _runningStartUtc = null;
            _isTracking      = false;
            PersistStoppedState();
        }

        UpdateTrayStartStopHeader();
        UpdateTraySwitchTaskEnabled();
        TrackingStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Tracking ─────────────────────────────────────────────────────────

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

        if (_isTracking && _runningEntryId != null)
        {
            var meta = _repo.GetEntryMeta(_runningEntryId);
            _runningProjectId = meta.ProjectId;
            _runningTaskName  = meta.TaskId != null
                ? new TaskRepository(_dbPath!).GetName(meta.TaskId)
                : null;
            _runningNote = meta.Note;
        }
    }

    internal void ToggleTracking(string? projectId = null, string? taskName = null,
        IReadOnlyList<string>? tagIds = null, string? note = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ToggleTracking(projectId, taskName, tagIds, note));
            return;
        }

        if (_repo == null) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (!_isTracking)
        {
            var taskId = ResolveTaskId(projectId, taskName);

            _runningEntryId   = _repo.StartEntry(now, "manual", projectId, taskId);
            _runningStartUtc  = now;
            _runningProjectId = projectId;
            _runningTaskName  = taskName;
            _runningNote      = note;
            _isTracking       = true;

            if (!string.IsNullOrWhiteSpace(note))
                _repo.UpdateNote(_runningEntryId, note);

            if (tagIds != null && tagIds.Count > 0)
                _tagRepo?.SetForEntry(_runningEntryId, tagIds);

            PersistRunningState();
        }
        else
        {
            StopRunningAt(now);
        }

        UpdateTrayStartStopHeader();
        UpdateTraySwitchTaskEnabled();
        TrackingStateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SwitchTask()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(SwitchTask); return; }
        if (_repo == null || !_isTracking) return;

        var dlg = new SwitchTaskDialog(new ProjectRepository(_dbPath!), _runningProjectId, _runningTaskName);
        if (dlg.ShowDialog() != true) return;

        ApplySwitch(dlg.SelectedProjectId, dlg.SelectedTaskName, null, null);
    }

    internal void SwitchTaskWithContext(string? projectId, string? taskName,
        IReadOnlyList<string>? tagIds, string? note)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SwitchTaskWithContext(projectId, taskName, tagIds, note));
            return;
        }

        if (_repo == null || !_isTracking) return;

        ApplySwitch(projectId, taskName, tagIds, note);
    }

    private void ApplySwitch(string? projectId, string? taskName,
        IReadOnlyList<string>? tagIds, string? note)
    {
        var now    = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var taskId = ResolveTaskId(projectId, taskName);

        StopRunningAt(now);

        _runningEntryId   = _repo!.StartEntry(now, "manual", projectId, taskId);
        _runningStartUtc  = now;
        _runningProjectId = projectId;
        _runningTaskName  = taskName;
        _runningNote      = note;
        _isTracking       = true;

        if (!string.IsNullOrWhiteSpace(note))
            _repo.UpdateNote(_runningEntryId, note);

        if (tagIds != null && tagIds.Count > 0)
            _tagRepo?.SetForEntry(_runningEntryId, tagIds);

        PersistRunningState();
        UpdateTrayStartStopHeader();
        UpdateTraySwitchTaskEnabled();
        TrackingStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private string? ResolveTaskId(string? projectId, string? taskName)
    {
        if (projectId == null || string.IsNullOrWhiteSpace(taskName)) return null;
        return new TaskRepository(_dbPath!).FindOrCreate(taskName.Trim(), projectId);
    }

    internal void UpdateRunningNote(string? note)
    {
        if (_repo == null || _runningEntryId == null) return;
        _runningNote = note;
        _repo.UpdateNote(_runningEntryId, note);
    }

    private void StopRunningAt(long nowUtc)
    {
        if (_repo != null && _runningEntryId != null)
            _repo.StopEntry(_runningEntryId, nowUtc);

        _runningEntryId   = null;
        _runningStartUtc  = null;
        _runningProjectId = null;
        _runningTaskName  = null;
        _runningNote      = null;
        _isTracking       = false;

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

    private void UpdateTraySwitchTaskEnabled()
    {
        if (_traySwitchTaskItem != null)
            _traySwitchTaskItem.IsEnabled = _isTracking;
    }

    // ── Tray / windows ───────────────────────────────────────────────────

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();

        _trayStartStopItem = new MenuItem { Header = _isTracking ? "Stop" : "Start" };
        _trayStartStopItem.Click += (_, __) => ToggleTracking();

        _traySwitchTaskItem = new MenuItem { Header = "Switch Task...", IsEnabled = _isTracking };
        _traySwitchTaskItem.Click += (_, __) => SwitchTask();

        var addNote = new MenuItem { Header = "Add Note..." };
        addNote.Click += (_, __) => OpenAddNoteDialog();

        var open = new MenuItem { Header = "Open BetterWorkTime" };
        open.Click += (_, __) => Dispatcher.Invoke(ShowMainWindow);

        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, __) => QuitApp();

        menu.Items.Add(_trayStartStopItem);
        menu.Items.Add(_traySwitchTaskItem);
        menu.Items.Add(addNote);
        menu.Items.Add(new Separator());
        menu.Items.Add(open);
        menu.Items.Add(quit);

        return menu;
    }

    private void OpenAddNoteDialog()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(OpenAddNoteDialog); return; }
        if (!_isTracking)
        {
            MessageBox.Show("Start tracking first to add a note.", "BetterWorkTime");
            return;
        }

        var dlg = new AddNoteDialog(_runningNote);
        if (dlg.ShowDialog() == true)
            UpdateRunningNote(dlg.Note);
    }

    private void ToggleMainWindow()
    {
        if (MainWindow == null) { ShowMainWindow(); return; }
        if (MainWindow.IsVisible) MainWindow.Hide();
        else ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (MainWindow == null)
            MainWindow = new MainWindow();

        MainWindow.ShowInTaskbar = true;
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Show();
        MainWindow.Activate();
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
