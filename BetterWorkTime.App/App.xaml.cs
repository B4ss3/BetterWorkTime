using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace BetterWorkTime.App;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    internal static bool IsQuitting { get; private set; }

    private bool _isTracking; // placeholder for M0 (we'll replace with real engine later)

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Tray-first: app must stay alive even with no window open
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "BetterWorkTime",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenu = BuildTrayMenu()
        };

        // Left click toggles main window
        _trayIcon.TrayLeftMouseUp += (_, __) =>
            Dispatcher.Invoke(ToggleMainWindow);

    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();

        var startStop = new MenuItem { Header = "Start" };
        startStop.Click += (_, __) =>
        {
            _isTracking = !_isTracking;
            startStop.Header = _isTracking ? "Stop" : "Start";
        };

        var switchTask = new MenuItem { Header = "Switch Task..." };
        switchTask.Click += (_, __) => MessageBox.Show("Switch Task... (placeholder)", "BetterWorkTime");

        var addNote = new MenuItem { Header = "Add Note..." };
        addNote.Click += (_, __) => MessageBox.Show("Add Note... (placeholder)", "BetterWorkTime");

        var open = new MenuItem { Header = "Open BetterWorkTime" };
        open.Click += (_, __) => Dispatcher.Invoke(ShowMainWindow);


        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, __) => QuitApp();

        menu.Items.Add(startStop);
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

        // If it was minimized/hidden/off-screen-ish, normalize it
        MainWindow.ShowInTaskbar = true;
        MainWindow.WindowState = WindowState.Normal;

        MainWindow.Show();
        MainWindow.Activate();

        // Force it to front reliably
        MainWindow.Topmost = true;
        MainWindow.Topmost = false;

        MainWindow.Focus();
    }


    private void QuitApp()
    {
        IsQuitting = true;

        try
        {
            MainWindow?.Close();
        }
        catch { /* ignore */ }

        _trayIcon?.Dispose();
        Shutdown();
    }
}
