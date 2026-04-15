using Microsoft.Data.Sqlite;
using System.Collections.Generic;

namespace BetterWorkTime.Data.Sqlite;

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
}
