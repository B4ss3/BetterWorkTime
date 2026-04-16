using System;
using System.Windows;

namespace BetterWorkTime.App;

public enum IdleChoice { Keep, Discard, Split }

public partial class IdlePromptWindow : Window
{
    public IdleChoice Choice { get; private set; } = IdleChoice.Keep;

    public IdlePromptWindow(TimeSpan idleDuration)
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            IdleTimeText.Text = $"Idle for {FormatDuration(idleDuration)}";
            Activate();
        };
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1)
            return $"{(int)t.TotalMinutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }

    private void Keep_Click(object sender, RoutedEventArgs e)
    {
        Choice = IdleChoice.Keep;
        Close();
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        Choice = IdleChoice.Discard;
        Close();
    }

    private void Split_Click(object sender, RoutedEventArgs e)
    {
        Choice = IdleChoice.Split;
        Close();
    }
}
