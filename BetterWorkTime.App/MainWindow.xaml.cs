using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BetterWorkTime.App.Views;

namespace BetterWorkTime.App;

public partial class MainWindow : Window
{
    private App AppRef => (App)Application.Current;

    // Cached view instances
    private TimerView? _timerView;

    // Currently active nav tag
    private string _currentView = "";

    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            AppRef.TrackingStateChanged += OnTrackingStateChanged;
            ManageView.ProjectsOrTagsChanged += OnProjectsOrTagsChanged;

            Navigate("Timer");
        };
    }

    // ── Navigation ───────────────────────────────────────────────────────

    public void Navigate(string viewName)
    {
        if (_currentView == viewName && ViewHost.Content != null) return;

        _currentView = viewName;

        // Update nav button highlight
        UpdateNavButtons(viewName);

        // Swap content
        ViewHost.Content = viewName switch
        {
            "Timer"     => GetTimerView(),
            "Dashboard" => new DashboardView(AppRef.DbPath),
            "Reports"   => new ReportsView(AppRef.DbPath),
            "Manage"    => new ManageView(AppRef.DbPath),
            "Settings"  => new SettingsView(AppRef.DbPath),
            _           => GetTimerView()
        };

        // If navigating to timer, refresh its data
        if (viewName == "Timer")
            _timerView?.OnNavigatedTo();
    }

    private TimerView GetTimerView()
    {
        _timerView ??= new TimerView();
        return _timerView;
    }

    private void UpdateNavButtons(string activeView)
    {
        var activeColor = Color.FromRgb(55, 65, 80);
        var whiteBrush  = new SolidColorBrush(Colors.White);
        var transparentBrush = new SolidColorBrush(Colors.Transparent);

        foreach (var btn in new[] { NavTimer, NavDashboard, NavReports, NavManage, NavSettings })
        {
            var isActive = btn.Tag?.ToString() == activeView;
            btn.Background      = isActive ? new SolidColorBrush(activeColor) : transparentBrush;
            btn.BorderBrush     = isActive ? whiteBrush : transparentBrush;
            btn.BorderThickness = isActive ? new Thickness(0, 0, 0, 2) : new Thickness(0);
        }
    }

    // ── Tracking status badge ────────────────────────────────────────────

    private void OnTrackingStateChanged(object? sender, EventArgs e)
        => UpdateStatusBadge();

    private void UpdateStatusBadge()
    {
        if (AppRef.IsTracking)
        {
            StatusBadge.Visibility = Visibility.Visible;
            if (AppRef.IsPaused)
            {
                StatusBadgeText.Text = "⏸ Paused";
                StatusBadge.Background = new SolidColorBrush(Color.FromRgb(91, 79, 138));
            }
            else
            {
                StatusBadgeText.Text = "● Tracking";
                StatusBadge.Background = new SolidColorBrush(Color.FromRgb(22, 101, 52));
            }
        }
        else
        {
            StatusBadge.Visibility = Visibility.Collapsed;
        }
    }

    // ── Manage → Timer refresh ───────────────────────────────────────────

    private void OnProjectsOrTagsChanged(object? sender, EventArgs e)
    {
        // If TimerView is active, refresh it immediately; otherwise it refreshes on next nav via OnNavigatedTo
        if (_currentView == "Timer")
            _timerView?.OnNavigatedTo();
    }

    // ── Event handlers ───────────────────────────────────────────────────

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string view) return;
        // Allow re-navigation to same view (recreates non-timer views)
        _currentView = "";
        Navigate(view);
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
        => AppRef.OpenHelp();

    // ── Window lifecycle ─────────────────────────────────────────────────

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsQuitting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        AppRef.TrackingStateChanged -= OnTrackingStateChanged;
        ManageView.ProjectsOrTagsChanged -= OnProjectsOrTagsChanged;
        base.OnClosed(e);
    }
}
