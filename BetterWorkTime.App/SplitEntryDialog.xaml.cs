using System;
using System.Windows;
using BetterWorkTime.Data.Sqlite;

namespace BetterWorkTime.App;

public partial class SplitEntryDialog : Window
{
    private readonly long _startUtc;
    private readonly long _endUtc;
    private readonly DateOnly _entryDate;

    public long ResultSplitUtc { get; private set; }

    public SplitEntryDialog(TimeEntryRow entry)
    {
        InitializeComponent();

        _startUtc  = entry.StartUtc;
        _endUtc    = entry.EndUtc!.Value; // only called for completed entries
        _entryDate = DateOnly.FromDateTime(
            DateTimeOffset.FromUnixTimeSeconds(_startUtc).LocalDateTime);

        Loaded += (_, _) =>
        {
            var localStart = DateTimeOffset.FromUnixTimeSeconds(_startUtc).LocalDateTime;
            var localEnd   = DateTimeOffset.FromUnixTimeSeconds(_endUtc).LocalDateTime;
            RangeLabel.Text = $"{localStart:HH:mm} – {localEnd:HH:mm}";

            // Default split point: midpoint
            var midSec = (_startUtc + _endUtc) / 2;
            var mid = DateTimeOffset.FromUnixTimeSeconds(midSec).LocalDateTime;
            SplitTimeBox.Text = mid.ToString("HH:mm");
            SplitTimeBox.Focus();
            SplitTimeBox.SelectAll();
        };
    }

    private void Split_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var parts = (SplitTimeBox.Text ?? "").Trim().Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) ||
            h < 0 || h > 23 || m < 0 || m > 59)
        {
            ShowError("Time must be HH:mm (e.g. 10:30).");
            return;
        }

        var splitDt  = _entryDate.ToDateTime(new TimeOnly(h, m), DateTimeKind.Local);
        var splitUtc = new DateTimeOffset(splitDt).ToUnixTimeSeconds();

        if (splitUtc <= _startUtc || splitUtc >= _endUtc)
        {
            var localStart = DateTimeOffset.FromUnixTimeSeconds(_startUtc).LocalDateTime;
            var localEnd   = DateTimeOffset.FromUnixTimeSeconds(_endUtc).LocalDateTime;
            ShowError($"Split time must be between {localStart:HH:mm} and {localEnd:HH:mm}.");
            return;
        }

        ResultSplitUtc = splitUtc;
        DialogResult   = true;
    }

    private void ShowError(string msg)
    {
        ErrorText.Text       = msg;
        ErrorText.Visibility = Visibility.Visible;
    }
}
