namespace BetterWorkTime.Data.Sqlite;

public sealed record TimeEntryRow(
    string  Id,
    long    StartUtc,
    long?   EndUtc,
    long    DurationSec,
    string? ProjectId,
    string? ProjectName,
    string? TaskId,
    string? TaskName,
    string? Note,
    bool    IsIdle);
