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

    /// <summary>
    /// Creates a completed idle entry for the given time range.
    /// </summary>
    public string CreateIdleEntry(long startUtc, long endUtc, string? projectId = null, string? taskId = null)
    {
        var id       = Guid.NewGuid().ToString("N");
        var duration = endUtc > startUtc ? endUtc - startUtc : 0L;

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
INSERT INTO time_entries(
    id, project_id, task_id, start_utc, end_utc, duration_sec, note, source,
    is_idle, idle_adjusted, created_at_utc
) VALUES (
    $id, $projectId, $taskId, $start, $end, $duration, NULL, 'idle',
    1, 0, $created
);
""";
        cmd.Parameters.AddWithValue("$id",        id);
        cmd.Parameters.AddWithValue("$projectId", (object?)projectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$taskId",    (object?)taskId    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$start",     startUtc);
        cmd.Parameters.AddWithValue("$end",       endUtc);
        cmd.Parameters.AddWithValue("$duration",  duration);
        cmd.Parameters.AddWithValue("$created",   startUtc);
        cmd.ExecuteNonQuery();

        return id;
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

    public IReadOnlyList<TimeEntryRow> GetTodayEntries(long dayStartUtc, long dayEndUtc)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
SELECT te.id, te.start_utc, te.end_utc, te.duration_sec,
       te.project_id, p.name,
       te.task_id,    t.name,
       te.note,       te.is_idle
FROM time_entries te
LEFT JOIN projects p ON te.project_id = p.id
LEFT JOIN tasks    t ON te.task_id    = t.id
WHERE te.start_utc < $dayEnd
  AND (te.end_utc IS NULL OR te.end_utc > $dayStart)
ORDER BY te.start_utc ASC;
""";
        cmd.Parameters.AddWithValue("$dayStart", dayStartUtc);
        cmd.Parameters.AddWithValue("$dayEnd",   dayEndUtc);

        var rows = new List<TimeEntryRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new TimeEntryRow(
                Id:          r.GetString(0),
                StartUtc:    r.GetInt64(1),
                EndUtc:      r.IsDBNull(2) ? null : r.GetInt64(2),
                DurationSec: r.IsDBNull(3) ? 0L   : r.GetInt64(3),
                ProjectId:   r.IsDBNull(4) ? null : r.GetString(4),
                ProjectName: r.IsDBNull(5) ? null : r.GetString(5),
                TaskId:      r.IsDBNull(6) ? null : r.GetString(6),
                TaskName:    r.IsDBNull(7) ? null : r.GetString(7),
                Note:        r.IsDBNull(8) ? null : r.GetString(8),
                IsIdle:      r.GetInt32(9) == 1));
        }
        return rows;
    }

    public void DeleteEntry(string entryId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM time_entries WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", entryId);
        cmd.ExecuteNonQuery();
    }

    public void UpdateEntryFull(string entryId, long startUtc, long endUtc,
        string? projectId, string? taskId, string? note)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
UPDATE time_entries
SET start_utc    = $start,
    end_utc      = $end,
    duration_sec = CASE WHEN $end > $start THEN ($end - $start) ELSE 0 END,
    project_id   = $pid,
    task_id      = $tid,
    note         = $note
WHERE id = $id;
""";
        cmd.Parameters.AddWithValue("$start", startUtc);
        cmd.Parameters.AddWithValue("$end",   endUtc);
        cmd.Parameters.AddWithValue("$pid",   (object?)projectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tid",   (object?)taskId    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$note",  (object?)note      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id",    entryId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Splits a completed entry at splitUtc, returning the id of the second (new) entry.</summary>
    public string SplitEntry(string entryId, long splitUtc, string source)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        // Read original entry
        string? projectId = null, taskId = null, note = null;
        long originalEnd = 0;

        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT project_id, task_id, note, end_utc FROM time_entries WHERE id = $id;";
            read.Parameters.AddWithValue("$id", entryId);
            using var r = read.ExecuteReader();
            if (r.Read())
            {
                projectId   = r.IsDBNull(0) ? null : r.GetString(0);
                taskId      = r.IsDBNull(1) ? null : r.GetString(1);
                note        = r.IsDBNull(2) ? null : r.GetString(2);
                originalEnd = r.IsDBNull(3) ? splitUtc : r.GetInt64(3);
            }
        }

        // Trim original to split point
        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = """
UPDATE time_entries
SET end_utc = $split,
    duration_sec = CASE WHEN $split > start_utc THEN ($split - start_utc) ELSE 0 END
WHERE id = $id;
""";
            upd.Parameters.AddWithValue("$split", splitUtc);
            upd.Parameters.AddWithValue("$id",    entryId);
            upd.ExecuteNonQuery();
        }

        // Insert second entry
        var newId = Guid.NewGuid().ToString("N");
        var dur   = originalEnd > splitUtc ? originalEnd - splitUtc : 0L;

        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
INSERT INTO time_entries(id, project_id, task_id, start_utc, end_utc, duration_sec,
                         note, source, is_idle, idle_adjusted, created_at_utc)
VALUES ($id, $pid, $tid, $start, $end, $dur, $note, $source, 0, 0, $created);
""";
            ins.Parameters.AddWithValue("$id",      newId);
            ins.Parameters.AddWithValue("$pid",     (object?)projectId ?? DBNull.Value);
            ins.Parameters.AddWithValue("$tid",     (object?)taskId    ?? DBNull.Value);
            ins.Parameters.AddWithValue("$start",   splitUtc);
            ins.Parameters.AddWithValue("$end",     originalEnd);
            ins.Parameters.AddWithValue("$dur",     dur);
            ins.Parameters.AddWithValue("$note",    (object?)note      ?? DBNull.Value);
            ins.Parameters.AddWithValue("$source",  source);
            ins.Parameters.AddWithValue("$created", splitUtc);
            ins.ExecuteNonQuery();
        }

        // Copy tags to the new entry
        using (var copyTags = conn.CreateCommand())
        {
            copyTags.Transaction = tx;
            copyTags.CommandText = """
INSERT INTO time_entry_tags(time_entry_id, tag_id)
SELECT $newId, tag_id FROM time_entry_tags WHERE time_entry_id = $oldId;
""";
            copyTags.Parameters.AddWithValue("$newId", newId);
            copyTags.Parameters.AddWithValue("$oldId", entryId);
            copyTags.ExecuteNonQuery();
        }

        tx.Commit();
        return newId;
    }

}
