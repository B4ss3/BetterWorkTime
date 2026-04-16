using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using BetterWorkTime.Data.Sqlite;
using BetterWorkTime.Platform.Windows;
using Microsoft.Win32;

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
    private bool _idlePromptShowing;
    private long? _idleStartUtc;

    // Hydration
    private long _hydrationAccSec;
    private long _hydrationLastTickUtc;
    private bool _hydrationPromptShowing;

    // Snapshot + sleep/wake
    private readonly DispatcherTimer _snapshotTick = new() { Interval = TimeSpan.FromSeconds(5) };
    private long _lastSnapshotUtc;

    // Global hotkeys
    private GlobalHotkeyManager? _hotkeys;

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

        AppLogger.Initialize(Path.Combine(baseDir, "Logs"));

        var dbPath = Path.Combine(baseDir, "betterworktime.sqlite");
        DbInitializer.EnsureCreated(dbPath);

        _dbPath = dbPath;
        _repo = new TimeEntryRepository(dbPath);
        _runtime = new RuntimeStateRepository(dbPath);
        _tagRepo = new TagRepository(dbPath);

        RestoreRuntimeState();

        if (_isTracking && !string.IsNullOrWhiteSpace(_runningEntryId))
        {
            var lastSeenStr = _runtime?.Get("tracking.last_seen_utc");
            long? lastSeenUtc = long.TryParse(lastSeenStr, out var ls) && ls > 0 ? ls : null;

            var dlg = new RecoveryDialog(lastSeenUtc);
            dlg.ShowDialog();

            switch (dlg.Choice)
            {
                case RecoveryChoice.StopAtLastSeen:
                    AppLogger.Log($"Recovery: stop at last seen ({lastSeenUtc})");
                    StopRunningAt(lastSeenUtc!.Value);
                    break;
                case RecoveryChoice.StopNow:
                    AppLogger.Log("Recovery: stop now");
                    StopRunningAt(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    break;
                default:
                    AppLogger.Log("Recovery: resume");
                    PersistRunningState();
                    break;
            }
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

        _snapshotTick.Tick += OnSnapshotTick;
        _snapshotTick.Start();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        ApplyHotkeySettings();

        var settings = new SettingsRepository(_dbPath!);
        if (!settings.GetBool(SettingsWindow.KeyStartMinimized, true))
            ShowMainWindow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Log("App exiting");
        _idleTick.Stop();
        _snapshotTick.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _hotkeys?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    // ── Idle detection ───────────────────────────────────────────────────

    private int IdleThresholdSeconds =>
        new SettingsRepository(_dbPath!).GetInt(SettingsWindow.KeyIdleThreshold, 5) * 60;

    private void OnIdleTick(object? sender, EventArgs e)
    {
        TickHydration();

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

    // ── Hydration ────────────────────────────────────────────────────────

    private void TickHydration()
    {
        if (_hydrationPromptShowing) return;

        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (_isTracking && _hydrationLastTickUtc > 0)
            _hydrationAccSec += nowUtc - _hydrationLastTickUtc;

        _hydrationLastTickUtc = nowUtc;

        if (!_isTracking) return;

        var settings          = new SettingsRepository(_dbPath!);
        var enabled           = settings.GetBool(SettingsWindow.KeyHydrationEnabled, false);
        if (!enabled) return;

        var intervalSec = settings.GetInt(SettingsWindow.KeyHydrationInterval, 30) * 60;
        if (_hydrationAccSec < intervalSec) return;

        var respectFocus = settings.GetBool(SettingsWindow.KeyRespectFocusAssist, true);
        if (respectFocus && FocusAssistDetector.IsActive()) return;

        ShowHydrationPrompt(settings);
    }

    private void ShowHydrationPrompt(SettingsRepository settings)
    {
        _hydrationPromptShowing = true;
        _hydrationAccSec        = 0;

        // Play sound
        var soundPath = settings.GetString(SettingsWindow.KeyHydrationSound);
        if (!string.IsNullOrWhiteSpace(soundPath) && File.Exists(soundPath))
        {
            try { new SoundPlayer(soundPath).Play(); } catch { /* best effort */ }
        }

        var prompt = new HydrationPromptWindow();
        prompt.Closed += (_, _) => { _hydrationPromptShowing = false; };
        prompt.Show();
    }

    internal void ResetHydrationTimer()
    {
        _hydrationAccSec        = 0;
        _hydrationPromptShowing = false;
    }

    // ── Snapshot + sleep/wake ────────────────────────────────────────────

    private void OnSnapshotTick(object? sender, EventArgs e)
    {
        if (!_isTracking) return;
        _lastSnapshotUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _runtime?.Set("tracking.last_seen_utc", _lastSnapshotUtc.ToString());
        PersistRunningState();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;

        AppLogger.Log("System resumed from sleep");

        if (!_isTracking || _idlePromptShowing) return;

        // Read last snapshot timestamp from DB
        var lastSeenStr = _runtime?.Get("tracking.last_seen_utc");
        if (!long.TryParse(lastSeenStr, out var lastSeen) || lastSeen == 0) return;

        var now     = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var gapSec  = now - lastSeen;
        var threshold = IdleThresholdSeconds;

        if (gapSec < threshold) return;

        AppLogger.Log($"Sleep gap detected: {gapSec}s — showing idle prompt");

        // Clamp idle start to entry start
        var idleStart = Math.Max(lastSeen, _runningStartUtc ?? lastSeen);
        if (idleStart <= (_runningStartUtc ?? 0)) return;

        _idleStartUtc     = idleStart;
        _idlePromptShowing = true;

        Dispatcher.Invoke(() =>
        {
            var prompt = new IdlePromptWindow(TimeSpan.FromSeconds(gapSec));
            prompt.Closed += (s, _) => ApplyIdleDecision(((IdlePromptWindow)s!).Choice);
            prompt.Show();
        });
    }

    // ── Global hotkeys ───────────────────────────────────────────────────

    internal void ApplyHotkeySettings()
    {
        var enabled = new SettingsRepository(_dbPath!).GetBool(SettingsWindow.KeyHotkeysEnabled, false);

        if (enabled && _hotkeys == null)
        {
            _hotkeys = new GlobalHotkeyManager();
            _hotkeys.StartStopPressed  += () => Dispatcher.Invoke(() => ToggleTracking());
            _hotkeys.SwitchTaskPressed += () => Dispatcher.Invoke(SwitchTask);
            _hotkeys.OpenMainPressed   += () => Dispatcher.Invoke(ShowMainWindow);
            _hotkeys.AddNotePressed    += () => Dispatcher.Invoke(OpenAddNoteDialog);
            _hotkeys.Register();
            AppLogger.Log("Global hotkeys registered");
        }
        else if (!enabled && _hotkeys != null)
        {
            _hotkeys.Dispose();
            _hotkeys = null;
            AppLogger.Log("Global hotkeys unregistered");
        }
    }

    // ── Settings ──────────────────────────────────────────────────────────

    internal void OpenSettings()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(OpenSettings); return; }

        var win = new SettingsWindow(_dbPath!) { Owner = MainWindow };
        win.ShowDialog();
        // Settings are read live from DB on next tick — no extra wiring needed
    }

    internal void OpenReports()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(OpenReports); return; }

        var win = new ReportsWindow(_dbPath!);
        win.Owner = MainWindow;
        win.Show();
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
            AppLogger.Log($"Tracking started: project={projectId ?? "none"} task={taskName ?? "none"}");

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

        AppLogger.Log("Tracking stopped");
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

        var reports = new MenuItem { Header = "Reports…" };
        reports.Click += (_, __) => Dispatcher.Invoke(OpenReports);

        var settings = new MenuItem { Header = "Settings…" };
        settings.Click += (_, __) => OpenSettings();

        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, __) => QuitApp();

        menu.Items.Add(_trayStartStopItem);
        menu.Items.Add(_traySwitchTaskItem);
        menu.Items.Add(addNote);
        menu.Items.Add(new Separator());
        menu.Items.Add(open);
        menu.Items.Add(reports);
        menu.Items.Add(settings);
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
