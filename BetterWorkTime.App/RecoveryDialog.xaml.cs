using System;
using System.Windows;

namespace BetterWorkTime.App;

public enum RecoveryChoice { Resume, StopAtLastSeen, StopNow }

public partial class RecoveryDialog : Window
{
    public RecoveryChoice Choice { get; private set; } = RecoveryChoice.Resume;

    public RecoveryDialog(long? lastSeenUtc)
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (lastSeenUtc.HasValue)
            {
                var local = DateTimeOffset.FromUnixTimeSeconds(lastSeenUtc.Value).LocalDateTime;
                LastSeenText.Text = $"Last seen active: {local:yyyy-MM-dd HH:mm:ss}";
                StopAtLastSeenButton.Content =
                    $"Stop at last seen — finalize entry at {local:HH:mm:ss}";
            }
            else
            {
                LastSeenText.Text = "Last seen time is unknown.";
                StopAtLastSeenButton.IsEnabled = false;
                StopAtLastSeenButton.Content   = "Stop at last seen (unavailable)";
            }
        };
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        Choice = RecoveryChoice.Resume;
        Close();
    }

    private void StopAtLastSeen_Click(object sender, RoutedEventArgs e)
    {
        Choice = RecoveryChoice.StopAtLastSeen;
        Close();
    }

    private void StopNow_Click(object sender, RoutedEventArgs e)
    {
        Choice = RecoveryChoice.StopNow;
        Close();
    }
}
