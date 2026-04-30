using System;
using Velopack;

namespace BetterWorkTime.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must be called before anything else — handles update apply on restart
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
