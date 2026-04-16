using System.Runtime.InteropServices;

namespace BetterWorkTime.Platform.Windows;

public static class IdleDetector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime; // milliseconds since system start (from GetTickCount)
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    /// <summary>
    /// Returns how many seconds the user has been idle (no keyboard/mouse input).
    /// Returns 0 if the call fails.
    /// </summary>
    public static int GetIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;

        var idleMs = GetTickCount64() - info.dwTime;
        return (int)(idleMs / 1000);
    }
}
