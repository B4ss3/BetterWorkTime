using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace BetterWorkTime.Data.Sqlite;

public sealed class SettingsRepository
{
    private readonly string _connectionString;

    public SettingsRepository(string dbPath)
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
INSERT INTO settings(key, value_json, updated_at_utc)
VALUES ($key, $val, $now)
ON CONFLICT(key) DO UPDATE SET
  value_json     = excluded.value_json,
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
        cmd.CommandText = "SELECT value_json FROM settings WHERE key = $key LIMIT 1;";
        cmd.Parameters.AddWithValue("$key", key);

        var result = cmd.ExecuteScalar();
        return result?.ToString();
    }

    // Typed helpers

    public int GetInt(string key, int defaultValue)
    {
        var raw = Get(key);
        return raw != null && int.TryParse(raw, out var v) ? v : defaultValue;
    }

    public bool GetBool(string key, bool defaultValue)
    {
        var raw = Get(key);
        if (raw == null) return defaultValue;
        if (bool.TryParse(raw.Trim().Trim('"'), out var b)) return b;
        return defaultValue;
    }

    public string? GetString(string key, string? defaultValue = null)
    {
        var raw = Get(key);
        if (raw == null) return defaultValue;
        try { return JsonSerializer.Deserialize<string>(raw); } catch { return defaultValue; }
    }

    public void SetInt(string key, int value)    => Set(key, value.ToString());
    public void SetBool(string key, bool value)  => Set(key, value ? "true" : "false");
    public void SetString(string key, string? value) =>
        Set(key, JsonSerializer.Serialize(value));
}
