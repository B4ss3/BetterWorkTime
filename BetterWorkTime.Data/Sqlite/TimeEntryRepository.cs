using Microsoft.Data.Sqlite;
using System;

namespace BetterWorkTime.Data.Sqlite;

public sealed class TimeEntryRepository
{
    private readonly string _connectionString;

    public TimeEntryRepository(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public string StartEntry(long startUtc, string source, string? projectId = null, string? taskId = null)
    {
        var id = Guid.NewGuid().ToString("N");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
INSERT INTO time_entries(
    id, project_id, task_id, start_utc, end_utc, duration_sec, note, source,
    is_idle, idle_adjusted, created_at_utc
) VALUES (
    $id, $projectId, $taskId, $start, NULL, 0, NULL, $source,
    0, 0, $created
);
""";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$projectId", (object?)projectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$taskId", (object?)taskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$start", startUtc);
        cmd.Parameters.AddWithValue("$source", source);
        cmd.Parameters.AddWithValue("$created", startUtc);
        cmd.ExecuteNonQuery();

        return id;
    }

    public void StopEntry(string entryId, long endUtc)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
UPDATE time_entries
SET end_utc = $end,
    duration_sec = CASE
        WHEN $end > start_utc THEN ($end - start_utc)
        ELSE 0
    END
WHERE id = $id;
""";
        cmd.Parameters.AddWithValue("$id", entryId);
        cmd.Parameters.AddWithValue("$end", endUtc);
        cmd.ExecuteNonQuery();
    }

    public (int count, long? lastStart, long? lastEnd, int? lastDur) GetLatestEntryInfo()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
SELECT
  (SELECT COUNT(*) FROM time_entries) AS cnt,
  (SELECT start_utc FROM time_entries ORDER BY start_utc DESC LIMIT 1) AS last_start,
  (SELECT end_utc   FROM time_entries ORDER BY start_utc DESC LIMIT 1) AS last_end,
  (SELECT duration_sec FROM time_entries ORDER BY start_utc DESC LIMIT 1) AS last_dur;
""";

        using var r = cmd.ExecuteReader();
        r.Read();

        var cnt = r.GetInt32(0);
        long? ls = r.IsDBNull(1) ? null : r.GetInt64(1);
        long? le = r.IsDBNull(2) ? null : r.GetInt64(2);
        int? ld = r.IsDBNull(3) ? null : r.GetInt32(3);

        return (cnt, ls, le, ld);
    }

    public long? GetStartUtc(string entryId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT start_utc FROM time_entries WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", entryId);

        var result = cmd.ExecuteScalar();
        return result == null ? null : Convert.ToInt64(result);
    }

    public (string? ProjectId, string? TaskId, string? Note) GetEntryMeta(string entryId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT project_id, task_id, note FROM time_entries WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", entryId);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (null, null, null);

        return (
            r.IsDBNull(0) ? null : r.GetString(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2));
    }

    public void UpdateNote(string entryId, string? note)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE time_entries SET note = $note WHERE id = $id;";
        cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", entryId);
        cmd.ExecuteNonQuery();
    }

    public void UpdateProjectTask(string entryId, string? projectId, string? taskId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE time_entries SET project_id = $pid, task_id = $tid WHERE id = $id;";
        cmd.Parameters.AddWithValue("$pid", (object?)projectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tid", (object?)taskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", entryId);
        cmd.ExecuteNonQuery();
    }

}
