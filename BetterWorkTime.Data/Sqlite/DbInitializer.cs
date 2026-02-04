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
    }
}
