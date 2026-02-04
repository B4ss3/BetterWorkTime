namespace BetterWorkTime.Data.Sqlite;

internal static class DbSchema
{
    // Schema version for future migrations
    public const int SchemaVersion = 1;

    // Keep this as one multi-statement script (SQLite accepts this).
    public const string InitSql = """
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;

CREATE TABLE IF NOT EXISTS meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS projects (
    id             TEXT PRIMARY KEY,
    name           TEXT NOT NULL,
    color          TEXT NULL,
    archived       INTEGER NOT NULL DEFAULT 0,
    created_at_utc INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS tasks (
    id             TEXT PRIMARY KEY,
    project_id     TEXT NOT NULL,
    name           TEXT NOT NULL,
    archived       INTEGER NOT NULL DEFAULT 0,
    created_at_utc INTEGER NOT NULL,
    FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS tags (
    id             TEXT PRIMARY KEY,
    name           TEXT NOT NULL,
    color          TEXT NULL,
    archived       INTEGER NOT NULL DEFAULT 0,
    created_at_utc INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS time_entries (
    id             TEXT PRIMARY KEY,
    project_id     TEXT NULL,
    task_id        TEXT NULL,
    start_utc      INTEGER NOT NULL,
    end_utc        INTEGER NULL,
    duration_sec   INTEGER NOT NULL DEFAULT 0,
    note           TEXT NULL,
    source         TEXT NOT NULL,
    is_idle        INTEGER NOT NULL DEFAULT 0,
    idle_adjusted  INTEGER NOT NULL DEFAULT 0,
    created_at_utc INTEGER NOT NULL,
    FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE SET NULL,
    FOREIGN KEY(task_id)    REFERENCES tasks(id)    ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS time_entry_tags (
    time_entry_id TEXT NOT NULL,
    tag_id        TEXT NOT NULL,
    PRIMARY KEY(time_entry_id, tag_id),
    FOREIGN KEY(time_entry_id) REFERENCES time_entries(id) ON DELETE CASCADE,
    FOREIGN KEY(tag_id)        REFERENCES tags(id)        ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS settings (
    key            TEXT PRIMARY KEY,
    value_json     TEXT NOT NULL,
    updated_at_utc INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS runtime_state (
    key            TEXT PRIMARY KEY,
    value_json     TEXT NOT NULL,
    updated_at_utc INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS hydration_state (
    id            INTEGER PRIMARY KEY CHECK (id = 1),
    enabled       INTEGER NOT NULL DEFAULT 0,
    progress_sec  INTEGER NOT NULL DEFAULT 0,
    last_ack_utc  INTEGER NULL,
    updated_at_utc INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_time_entries_start_utc ON time_entries(start_utc);
CREATE INDEX IF NOT EXISTS ix_time_entries_project   ON time_entries(project_id);
CREATE INDEX IF NOT EXISTS ix_time_entries_task      ON time_entries(task_id);
CREATE INDEX IF NOT EXISTS ix_time_entries_is_idle   ON time_entries(is_idle);

-- Ensure single hydration row exists
INSERT OR IGNORE INTO hydration_state(id, enabled, progress_sec, last_ack_utc, updated_at_utc)
VALUES (1, 0, 0, NULL, strftime('%s','now'));

-- Store schema version
INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', '1');
""";
}
