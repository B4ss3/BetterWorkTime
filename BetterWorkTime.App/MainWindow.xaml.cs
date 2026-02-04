using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace BetterWorkTime.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private App AppRef => (App)Application.Current;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, __) =>
        {
            AppRef.TrackingStateChanged += OnTrackingStateChanged;

            _uiTimer.Tick += (_, __) => RefreshUi();
            _uiTimer.Start();

            RefreshUi();
        };
    }

    private void OnTrackingStateChanged(object? sender, EventArgs e) => RefreshUi();

    private void RefreshUi()
    {
        var running = AppRef.IsTracking;

        StatusText.Text = running ? "Status: Tracking" : "Status: Stopped";
        StartStopButton.Content = running ? "Stop" : "Start";

        var elapsed = AppRef.GetElapsed();
        ElapsedText.Text = $"Elapsed: {elapsed:hh\\:mm\\:ss}";
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        AppRef.ToggleTracking();
        RefreshUi();
    }

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
        _uiTimer.Stop();
        base.OnClosed(e);
    }
}
