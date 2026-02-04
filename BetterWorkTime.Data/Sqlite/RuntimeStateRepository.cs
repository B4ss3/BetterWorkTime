using Microsoft.Data.Sqlite;

namespace BetterWorkTime.Data.Sqlite;

public sealed class RuntimeStateRepository
{
    private readonly string _connectionString;

    public RuntimeStateRepository(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public void Set(string key, string valueJson)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
INSERT INTO runtime_state(key, value_json, updated_at_utc)
VALUES ($key, $val, $now)
ON CONFLICT(key) DO UPDATE SET
  value_json = excluded.value_json,
  updated_at_utc = excluded.updated_at_utc;
""";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$val", valueJson);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    public string? Get(string key)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value_json FROM runtime_state WHERE key = $key LIMIT 1;";
        cmd.Parameters.AddWithValue("$key", key);

        var result = cmd.ExecuteScalar();
        return result?.ToString();
    }
}
