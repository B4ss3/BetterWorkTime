using System;
using System.Collections.Generic;

namespace BetterWorkTime.Data.Sqlite;

/// <summary>Filter parameters for a report query.</summary>
public sealed class ReportQuery
{
    public long   StartUtc      { get; init; }
    public long   EndUtc        { get; init; }
    public string? ProjectId    { get; init; }   // null = all; empty string = unassigned
    public string? NoteSearch   { get; init; }
    public bool   IncludeIdle   { get; init; } = false;
    public IReadOnlyList<string> TagIds { get; init; } = Array.Empty<string>();
}

/// <summary>One row in the flat entries list.</summary>
public sealed record ReportEntryRow(
    string  Id,
    long    StartUtc,
    long    EndUtc,
    long    DurationSec,
    string? ProjectId,
    string? ProjectName,
    string? TaskId,
    string? TaskName,
    string? Note,
    bool    IsIdle,
    IReadOnlyList<string> TagNames);

/// <summary>One row in the project breakdown.</summary>
public sealed record ProjectBreakdownRow(
    string? ProjectId,
    string  ProjectName,
    long    DurationSec);
