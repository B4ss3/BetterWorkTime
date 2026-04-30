using System.IO;
using Microsoft.Data.Sqlite;

namespace BetterWorkTime.Data.Sqlite;

public static class DbInitializer
{
    public static void EnsureCreated(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        using var conn = new SqliteConnection(cs);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = DbSchema.InitSql;
        cmd.ExecuteNonQuery();

        RunMigrations(conn);
        EnsureSystemTags(conn);
    }

    private static void RunMigrations(SqliteConnection conn)
    {
        var version = GetSchemaVersion(conn);

        if (version < 2)
        {
            // Add is_system column to tags (safe: SQLite allows ADD COLUMN)
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = DbSchema.MigrationV2;
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
            {
                // Already exists — harmless
            }
            SetSchemaVersion(conn, 2);
        }
    }

    private static void EnsureSystemTags(SqliteConnection conn)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
INSERT OR IGNORE INTO tags(id, name, color, archived, is_system, created_at_utc)
VALUES ($id, $name, NULL, 0, 1, $now);
""";
        cmd.Parameters.AddWithValue("$id",   DbSchema.SystemTagPauseId);
        cmd.Parameters.AddWithValue("$name", DbSchema.SystemTagPauseName);
        cmd.Parameters.AddWithValue("$now",  now);
        cmd.ExecuteNonQuery();
    }

    private static int GetSchemaVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = 'schema_version';";
        var raw = cmd.ExecuteScalar() as string;
        return int.TryParse(raw, out var v) ? v : 1;
    }

    private static void SetSchemaVersion(SqliteConnection conn, int version)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', $v);";
        cmd.Parameters.AddWithValue("$v", version.ToString());
        cmd.ExecuteNonQuery();
    }
}
