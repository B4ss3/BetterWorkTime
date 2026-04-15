using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace BetterWorkTime.Data.Sqlite;

public sealed record TagRow(string Id, string Name, string? Color, bool Archived);

public sealed class TagRepository
{
    private readonly string _connectionString;

    public TagRepository(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public IReadOnlyList<(string Id, string Name)> GetAllActive()
    {
        var result = new List<(string, string)>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM tags WHERE archived = 0 ORDER BY name COLLATE NOCASE;";

        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add((r.GetString(0), r.GetString(1)));

        return result;
    }

    public IReadOnlyList<TagRow> GetAll()
    {
        var result = new List<TagRow>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, color, archived FROM tags ORDER BY archived, name COLLATE NOCASE;";

        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add(new TagRow(
                r.GetString(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetInt32(3) == 1));

        return result;
    }

    public IReadOnlyList<(string Id, string Name)> GetForEntry(string entryId)
    {
        var result = new List<(string, string)>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
SELECT t.id, t.name
FROM tags t
JOIN time_entry_tags et ON et.tag_id = t.id
WHERE et.time_entry_id = $eid
ORDER BY t.name COLLATE NOCASE;
""";
        cmd.Parameters.AddWithValue("$eid", entryId);

        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add((r.GetString(0), r.GetString(1)));

        return result;
    }

    /// <summary>
    /// Replaces all tags on an entry with the given set. Safe to call with empty list to clear.
    /// </summary>
    public void SetForEntry(string entryId, IEnumerable<string> tagIds)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var tx = conn.BeginTransaction();

        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM time_entry_tags WHERE time_entry_id = $eid;";
        del.Parameters.AddWithValue("$eid", entryId);
        del.ExecuteNonQuery();

        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO time_entry_tags(time_entry_id, tag_id) VALUES ($eid, $tid);";
        ins.Parameters.AddWithValue("$eid", entryId);
        var tidParam = ins.Parameters.Add("$tid", SqliteType.Text);

        foreach (var tagId in tagIds)
        {
            tidParam.Value = tagId;
            ins.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public string Create(string name, string? color)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
INSERT INTO tags(id, name, color, archived, created_at_utc)
VALUES ($id, $name, $color, 0, $now);
""";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$color", (object?)color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();

        return id;
    }

    public void SetArchived(string id, bool archived)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE tags SET archived = $v WHERE id = $id;";
        cmd.Parameters.AddWithValue("$v", archived ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
