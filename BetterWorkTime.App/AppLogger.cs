using System;
using System.IO;

namespace BetterWorkTime.App;

/// <summary>
/// Simple append-only daily log. Files are kept for 7 days.
/// Thread-safe via lock.
/// </summary>
internal static class AppLogger
{
    private static string _logDir = "";
    private static readonly object _lock = new();

    internal static void Initialize(string logDir)
    {
        _logDir = logDir;
        try { Directory.CreateDirectory(logDir); } catch { /* best effort */ }
        CleanOldLogs();
        Log("App started");
    }

    internal static string LogDir => _logDir;

    internal static void Log(string message)
    {
        if (string.IsNullOrEmpty(_logDir)) return;
        try
        {
            var path = Path.Combine(_logDir, $"bwt-{DateTime.Today:yyyy-MM-dd}.log");
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            lock (_lock)
                File.AppendAllText(path, line + Environment.NewLine);
        }
        catch { /* best effort — never throw from logger */ }
    }

    private static void CleanOldLogs()
    {
        try
        {
            var cutoff = DateTime.Today.AddDays(-7);
            foreach (var file in Directory.GetFiles(_logDir, "bwt-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch { /* best effort */ }
    }
}
