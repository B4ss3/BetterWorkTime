using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BetterWorkTime.Data.Sqlite;

namespace BetterWorkTime.Data;

public static class CsvExporter
{
    // Canonical column order — never change without a version bump
    private static readonly string[] Headers =
    [
        "id", "start_utc", "start_local", "end_utc", "end_local",
        "duration_sec", "project", "task", "tags", "note", "is_idle"
    ];

    /// <summary>
    /// Writes rows as UTF-8 with BOM, comma-delimited CSV to <paramref name="path"/>.
    /// </summary>
    public static void Write(string path, IReadOnlyList<ReportEntryRow> rows)
    {
        using var fs  = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var sw  = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        sw.WriteLine(string.Join(",", Headers));

        foreach (var row in rows)
        {
            var startOffset = DateTimeOffset.FromUnixTimeSeconds(row.StartUtc);
            var endOffset   = DateTimeOffset.FromUnixTimeSeconds(row.EndUtc);

            sw.WriteLine(string.Join(",",
                Escape(row.Id),
                Escape(startOffset.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz")
                    .Replace("+00:00", "Z")),
                Escape(startOffset.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz")),
                Escape(endOffset.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz")
                    .Replace("+00:00", "Z")),
                Escape(endOffset.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz")),
                row.DurationSec.ToString(),
                Escape(row.ProjectName ?? ""),
                Escape(row.TaskName ?? ""),
                Escape(string.Join("; ", row.TagNames)),
                Escape(row.Note ?? ""),
                row.IsIdle ? "1" : "0"
            ));
        }
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
