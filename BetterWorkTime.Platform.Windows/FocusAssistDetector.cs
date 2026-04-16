using System.Runtime.Versioning;
using Microsoft.Win32;

namespace BetterWorkTime.Platform.Windows;

/// <summary>
/// Detects whether Windows Focus Assist (Do Not Disturb) is active.
/// Registry key is undocumented but stable since Windows 10 1903.
/// </summary>
public static class FocusAssistDetector
{
    private const string KeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\" +
        @"default$windows.data.notifications.quiethourssettings\windows.data.notifications.quiethourssettings";

    /// <summary>
    /// Returns true if Focus Assist is on (Priority only or Alarms only).
    /// Returns false if off or if the registry key is unreadable.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool IsActive()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key == null) return false;

            var data = key.GetValue("Data") as byte[];
            if (data == null || data.Length < 2) return false;

            // Byte at offset 1: 0 = off, 1 = priority only, 2 = alarms only
            return data[1] is 1 or 2;
        }
        catch
        {
            return false;
        }
    }
}
