using Microsoft.Data.Sqlite;
using System.Collections.Generic;

namespace BetterWorkTime.Data.Sqlite;

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
    /// Returns active tasks scoped to a project. Pass null to get tasks with no project assigned.
    /// </summary>
    public IReadOnlyList<(string Id, string Name)> GetByProject(string? projectId)
    {
        var result = new List<(string, string)>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        if (projectId == null)
        {
            cmd.CommandText =
                "SELECT id, name FROM tasks WHERE project_id IS NULL AND archived = 0 ORDER BY name COLLATE NOCASE;";
        }
        else
        {
            cmd.CommandText =
                "SELECT id, name FROM tasks WHERE project_id = $pid AND archived = 0 ORDER BY name COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("$pid", projectId);
        }

        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add((r.GetString(0), r.GetString(1)));

        return result;
    }
}
