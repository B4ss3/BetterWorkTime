using System;
using System.Threading;
using Velopack;

namespace BetterWorkTime.App;

public static class Program
{
    private const string MutexName  = "BetterWorkTime_SingleInstance";
    private const string EventName  = "BetterWorkTime_ShowWindow";

    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        // If another instance is already running, signal it to show itself and exit
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);

        var mutex = new Mutex(true, MutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            showEvent.Set();
            return;
        }

        // Listen for "bring to front" signals from future launches
        var listener = new Thread(() =>
        {
            while (true)
            {
                showEvent.WaitOne();
                App.Current?.Dispatcher.Invoke(() =>
                    (App.Current as App)?.BringToFront());
            }
        })
        { IsBackground = true };
        listener.Start();

        var app = new App();
        app.InitializeComponent();
        app.Run();

        mutex.ReleaseMutex();
    }
}
