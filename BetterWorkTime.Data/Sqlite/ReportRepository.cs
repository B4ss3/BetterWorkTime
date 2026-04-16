using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Text;

namespace BetterWorkTime.Data.Sqlite;

public sealed class ReportRepository
{
    private readonly string _connectionString;

    public ReportRepository(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public IReadOnlyList<ReportEntryRow> GetEntries(ReportQuery q)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var sql = new StringBuilder("""
SELECT te.id, te.start_utc,
       COALESCE(te.end_utc, strftime('%s','now')) AS end_utc,
       CASE
           WHEN te.end_utc IS NOT NULL THEN te.duration_sec
           ELSE COALESCE(strftime('%s','now') - te.start_utc, 0)
       END AS duration_sec,
       te.project_id, p.name,
       te.task_id,    t.name,
       te.note,       te.is_idle,
       CASE WHEN te.end_utc IS NULL THEN 1 ELSE 0 END AS is_live
FROM time_entries te
LEFT JOIN projects p ON te.project_id = p.id
LEFT JOIN tasks    t ON te.task_id    = t.id
WHERE te.start_utc >= $start
  AND (te.end_utc IS NULL OR te.end_utc <= $end)
""");

        if (!q.IncludeIdle)
            sql.Append("  AND te.is_idle = 0\n");

        if (q.ProjectId != null)
        {
            if (q.ProjectId == string.Empty)
                sql.Append("  AND te.project_id IS NULL\n");
            else
                sql.Append("  AND te.project_id = $pid\n");
        }

        if (!string.IsNullOrWhiteSpace(q.NoteSearch))
            sql.Append("  AND te.note LIKE $note\n");

        if (q.TagIds.Count > 0)
        {
            sql.Append("""
  AND EXISTS (
      SELECT 1 FROM time_entry_tags tet
      WHERE tet.time_entry_id = te.id
        AND tet.tag_id IN (
""");
            for (int i = 0; i < q.TagIds.Count; i++)
            {
                if (i > 0) sql.Append(", ");
                sql.Append($"$tid{i}");
            }
            sql.Append("))\n");
        }

        sql.Append("ORDER BY te.start_utc ASC;");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$start", q.StartUtc);
        cmd.Parameters.AddWithValue("$end",   q.EndUtc);

        if (q.ProjectId != null && q.ProjectId != string.Empty)
            cmd.Parameters.AddWithValue("$pid", q.ProjectId);

        if (!string.IsNullOrWhiteSpace(q.NoteSearch))
            cmd.Parameters.AddWithValue("$note", $"%{q.NoteSearch}%");

        for (int i = 0; i < q.TagIds.Count; i++)
            cmd.Parameters.AddWithValue($"$tid{i}", q.TagIds[i]);

        var rows = new List<ReportEntryRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            rows.Add(new ReportEntryRow(
                Id:          id,
                StartUtc:    r.GetInt64(1),
                EndUtc:      r.IsDBNull(2) ? 0L : r.GetInt64(2),
                DurationSec: r.IsDBNull(3) ? 0L : r.GetInt64(3),
                ProjectId:   r.IsDBNull(4) ? null : r.GetString(4),
                ProjectName: r.IsDBNull(5) ? null : r.GetString(5),
                TaskId:      r.IsDBNull(6) ? null : r.GetString(6),
                TaskName:    r.IsDBNull(7) ? null : r.GetString(7),
                Note:        r.IsDBNull(8) ? null : r.GetString(8),
                IsIdle:      r.GetInt32(9) == 1,
                IsLive:      r.GetInt32(10) == 1,
                TagNames:    Array.Empty<string>()));
        }

        // Attach tag names
        if (rows.Count > 0)
            AttachTags(conn, rows);

        return rows;
    }

    private static void AttachTags(SqliteConnection conn, List<ReportEntryRow> rows)
    {
        // Build a map entryId -> list of tag names
        var map = new Dictionary<string, List<string>>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
SELECT tet.time_entry_id, tg.name
FROM time_entry_tags tet
JOIN tags tg ON tet.tag_id = tg.id
WHERE tet.time_entry_id IN (
""";
        var sb = new StringBuilder(cmd.CommandText);
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"$r{i}");
            cmd.Parameters.AddWithValue($"$r{i}", rows[i].Id);
        }
        sb.Append(");");
        cmd.CommandText = sb.ToString();

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var eid  = r.GetString(0);
            var name = r.GetString(1);
            if (!map.TryGetValue(eid, out var list))
                map[eid] = list = new List<string>();
            list.Add(name);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            if (map.TryGetValue(rows[i].Id, out var tags))
                rows[i] = rows[i] with { TagNames = tags };
        }
    }

    public IReadOnlyList<ProjectBreakdownRow> GetProjectBreakdown(ReportQuery q)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var sql = new StringBuilder("""
SELECT te.project_id, COALESCE(p.name, '(Unassigned)') AS pname,
       SUM(te.duration_sec) AS total
FROM time_entries te
LEFT JOIN projects p ON te.project_id = p.id
WHERE te.end_utc IS NOT NULL
  AND te.start_utc >= $start
  AND te.end_utc   <= $end
  AND te.is_idle   = 0
""");

        if (q.ProjectId != null)
        {
            if (q.ProjectId == string.Empty)
                sql.Append("  AND te.project_id IS NULL\n");
            else
                sql.Append("  AND te.project_id = $pid\n");
        }

        if (!string.IsNullOrWhiteSpace(q.NoteSearch))
            sql.Append("  AND te.note LIKE $note\n");

        sql.Append("GROUP BY te.project_id, pname\nORDER BY total DESC;");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$start", q.StartUtc);
        cmd.Parameters.AddWithValue("$end",   q.EndUtc);

        if (q.ProjectId != null && q.ProjectId != string.Empty)
            cmd.Parameters.AddWithValue("$pid", q.ProjectId);
        if (!string.IsNullOrWhiteSpace(q.NoteSearch))
            cmd.Parameters.AddWithValue("$note", $"%{q.NoteSearch}%");

        var rows = new List<ProjectBreakdownRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new ProjectBreakdownRow(
                ProjectId:   r.IsDBNull(0) ? null : r.GetString(0),
                ProjectName: r.GetString(1),
                DurationSec: r.IsDBNull(2) ? 0L  : r.GetInt64(2)));
        }
        return rows;
    }
}
