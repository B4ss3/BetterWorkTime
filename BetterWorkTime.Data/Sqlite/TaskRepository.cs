using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace BetterWorkTime.Data.Sqlite;

public sealed record TaskRow(string Id, string Name, string ProjectId, string ProjectName, bool Archived);

public sealed class TaskRepository
{
    private readonly string _connectionString;

    public TaskRepository(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    /// <summary>
    /// Returns active tasks scoped to a project. Returns empty list when projectId is null
    /// (tasks always belong to a project per schema).
    /// </summary>
    public IReadOnlyList<(string Id, string Name)> GetByProject(string? projectId)
    {
        if (projectId == null) return [];

        var result = new List<(string, string)>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, name FROM tasks WHERE project_id = $pid AND archived = 0 ORDER BY name COLLATE NOCASE;";
        cmd.Parameters.AddWithValue("$pid", projectId);

        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add((r.GetString(0), r.GetString(1)));

        return result;
    }

    public IReadOnlyList<TaskRow> GetAll()
    {
        var result = new List<TaskRow>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
SELECT t.id, t.name, t.project_id, p.name, t.archived
FROM tasks t
JOIN projects p ON p.id = t.project_id
ORDER BY t.archived, p.name COLLATE NOCASE, t.name COLLATE NOCASE;
""";

        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add(new TaskRow(
                r.GetString(0),
                r.GetString(1),
                r.GetString(2),
                r.GetString(3),
                r.GetInt32(4) == 1));

        return result;
    }

    public string Create(string name, string projectId)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
INSERT INTO tasks(id, project_id, name, archived, created_at_utc)
VALUES ($id, $pid, $name, 0, $now);
""";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$pid", projectId);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();

        return id;
    }

    public void SetArchived(string id, bool archived)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE tasks SET archived = $v WHERE id = $id;";
        cmd.Parameters.AddWithValue("$v", archived ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Returns the ID of an active task with the given name under the given project,
    /// creating it if it doesn't exist.
    /// </summary>
    public string FindOrCreate(string name, string projectId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var find = conn.CreateCommand();
        find.CommandText =
            "SELECT id FROM tasks WHERE name = $name AND project_id = $pid AND archived = 0 LIMIT 1;";
        find.Parameters.AddWithValue("$name", name);
        find.Parameters.AddWithValue("$pid", projectId);

        var existing = find.ExecuteScalar() as string;
        if (existing != null) return existing;

        var id  = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var ins = conn.CreateCommand();
        ins.CommandText = """
INSERT INTO tasks(id, project_id, name, archived, created_at_utc)
VALUES ($id, $pid, $name, 0, $now);
""";
        ins.Parameters.AddWithValue("$id",   id);
        ins.Parameters.AddWithValue("$pid",  projectId);
        ins.Parameters.AddWithValue("$name", name);
        ins.Parameters.AddWithValue("$now",  now);
        ins.ExecuteNonQuery();

        return id;
    }

    public string? GetName(string taskId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM tasks WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", taskId);

        return cmd.ExecuteScalar() as string;
    }
}
