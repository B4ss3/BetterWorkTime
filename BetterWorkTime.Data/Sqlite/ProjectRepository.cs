using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace BetterWorkTime.Data.Sqlite;

public sealed record ProjectRow(string Id, string Name, string? Color, bool Archived);

public sealed class ProjectRepository
{
    private readonly string _connectionString;

    public ProjectRepository(string dbPath)
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
        cmd.CommandText = "SELECT id, name FROM projects WHERE archived = 0 ORDER BY name COLLATE NOCASE;";

        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add((r.GetString(0), r.GetString(1)));

        return result;
    }

    public IReadOnlyList<ProjectRow> GetAll()
    {
        var result = new List<ProjectRow>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, color, archived FROM projects ORDER BY archived, name COLLATE NOCASE;";

        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add(new ProjectRow(
                r.GetString(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetInt32(3) == 1));

        return result;
    }

    public string Create(string name, string? color)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
INSERT INTO projects(id, name, color, archived, created_at_utc)
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
        cmd.CommandText = "UPDATE projects SET archived = $v WHERE id = $id;";
        cmd.Parameters.AddWithValue("$v", archived ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
