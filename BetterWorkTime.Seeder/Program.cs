using BetterWorkTime.Data.Sqlite;
using Microsoft.Data.Sqlite;

var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "BetterWorkTime", "betterworktime.sqlite");

if (!File.Exists(dbPath))
{
    Console.WriteLine($"DB not found at: {dbPath}");
    Console.WriteLine("Launch the app at least once to create it.");
    return;
}

Console.WriteLine($"Seeding: {dbPath}");

DbInitializer.EnsureCreated(dbPath);

var cs = new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = SqliteOpenMode.ReadWriteCreate,
    Cache = SqliteCacheMode.Shared
}.ToString();

// ── Helpers ──────────────────────────────────────────────────────────────────

string NewId() => Guid.NewGuid().ToString("N");

long ToUtc(DateTime local) =>
    new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUnixTimeSeconds();

using var conn = new SqliteConnection(cs);
conn.Open();

T? Scalar<T>(string sql, Action<SqliteCommand>? bind = null)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    bind?.Invoke(cmd);
    var r = cmd.ExecuteScalar();
    return r == null || r == DBNull.Value ? default : (T)Convert.ChangeType(r, typeof(T));
}

void Exec(string sql, Action<SqliteCommand>? bind = null)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    bind?.Invoke(cmd);
    cmd.ExecuteNonQuery();
}

// ── Projects ─────────────────────────────────────────────────────────────────

string EnsureProject(string name, string color)
{
    var existing = Scalar<string>(
        "SELECT id FROM projects WHERE name = $n AND archived = 0;",
        c => c.Parameters.AddWithValue("$n", name));
    if (existing != null) return existing;

    var id = NewId();
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    Exec("INSERT INTO projects(id, name, color, archived, created_at_utc) VALUES ($id,$n,$c,0,$t);", c =>
    {
        c.Parameters.AddWithValue("$id", id);
        c.Parameters.AddWithValue("$n", name);
        c.Parameters.AddWithValue("$c", color);
        c.Parameters.AddWithValue("$t", now);
    });
    Console.WriteLine($"  + Project: {name}");
    return id;
}

var pFrontend  = EnsureProject("Frontend",    "#4A7FA5");
var pBackend   = EnsureProject("Backend",     "#3D8B5E");
var pDevOps    = EnsureProject("DevOps",      "#A84040");
var pCodeRev   = EnsureProject("Code Review", "#6B5B9E");

// ── Tasks ─────────────────────────────────────────────────────────────────────

string EnsureTask(string name, string projectId)
{
    var existing = Scalar<string>(
        "SELECT id FROM tasks WHERE name = $n AND project_id = $p AND archived = 0;",
        c => { c.Parameters.AddWithValue("$n", name); c.Parameters.AddWithValue("$p", projectId); });
    if (existing != null) return existing;

    var id = NewId();
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    Exec("INSERT INTO tasks(id, name, project_id, archived, created_at_utc) VALUES ($id,$n,$p,0,$t);", c =>
    {
        c.Parameters.AddWithValue("$id", id);
        c.Parameters.AddWithValue("$n", name);
        c.Parameters.AddWithValue("$p", projectId);
        c.Parameters.AddWithValue("$t", now);
    });
    return id;
}

var tDashboard     = EnsureTask("Dashboard redesign",      pFrontend);
var tBugfix        = EnsureTask("Bug fixes",               pFrontend);
var tApiEndpoints  = EnsureTask("REST API endpoints",      pBackend);
var tDbMigration   = EnsureTask("DB migrations",           pBackend);
var tAuthService   = EnsureTask("Auth service",            pBackend);
var tCiPipeline    = EnsureTask("CI pipeline",             pDevOps);
var tDockerize     = EnsureTask("Dockerize services",      pDevOps);
var tPrReviews     = EnsureTask("PR reviews",              pCodeRev);
var tDesignReview  = EnsureTask("Design review",           pCodeRev);

// ── Time entry insertion ──────────────────────────────────────────────────────

void AddEntry(DateTime start, DateTime end, string projectId, string taskId,
              string note, bool isIdle = false, string source = "manual")
{
    var id  = NewId();
    var s   = ToUtc(start);
    var e   = ToUtc(end);
    var dur = e - s;
    Exec("""
INSERT INTO time_entries(id, start_utc, end_utc, duration_sec, project_id, task_id,
                          note, is_idle, source, created_at_utc)
VALUES ($id,$s,$e,$dur,$p,$t,$n,$i,$src,$now);
""", c =>
    {
        c.Parameters.AddWithValue("$id",  id);
        c.Parameters.AddWithValue("$s",   s);
        c.Parameters.AddWithValue("$e",   e);
        c.Parameters.AddWithValue("$dur", dur);
        c.Parameters.AddWithValue("$p",   (object?)projectId ?? DBNull.Value);
        c.Parameters.AddWithValue("$t",   (object?)taskId ?? DBNull.Value);
        c.Parameters.AddWithValue("$n",   (object?)note ?? DBNull.Value);
        c.Parameters.AddWithValue("$i",   isIdle ? 1 : 0);
        c.Parameters.AddWithValue("$src", source);
        c.Parameters.AddWithValue("$now", s);
    });
}

void AddPause(DateTime start, DateTime end)
{
    var id  = NewId();
    var s   = ToUtc(start);
    var e   = ToUtc(end);
    var dur = e - s;
    Exec("""
INSERT INTO time_entries(id, start_utc, end_utc, duration_sec, project_id, task_id,
                          note, is_idle, source, created_at_utc)
VALUES ($id,$s,$e,$dur,NULL,NULL,'Pause',1,'pause',$s);
""", c =>
    {
        c.Parameters.AddWithValue("$id",  id);
        c.Parameters.AddWithValue("$s",   s);
        c.Parameters.AddWithValue("$e",   e);
        c.Parameters.AddWithValue("$dur", dur);
    });

    // Attach system pause tag
    var pauseTagId = SystemTags.PauseId;
    Exec("INSERT OR IGNORE INTO time_entry_tags(time_entry_id, tag_id) VALUES ($eid, $tid);", c =>
    {
        c.Parameters.AddWithValue("$eid", id);
        c.Parameters.AddWithValue("$tid", pauseTagId);
    });
}

// ── Seed 5 days (Mon–Fri this week) ──────────────────────────────────────────

// Find Monday of the current week
var today  = DateTime.Today;
var monday = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
if (today.DayOfWeek == DayOfWeek.Sunday) monday = monday.AddDays(-7);

Console.WriteLine($"\nSeeding week starting {monday:yyyy-MM-dd}");

for (int d = 0; d < 5; d++)
{
    var day = monday.AddDays(d);
    if (day > today) { Console.WriteLine($"  Skipping {day:ddd MM-dd} (future)"); continue; }

    Console.WriteLine($"  Seeding {day:ddd MM-dd}...");

    DateTime T(int h, int m = 0) => day.Date.AddHours(h).AddMinutes(m);

    switch (d)
    {
        case 0: // Monday
            AddEntry(T(9, 0),  T(10,30), pFrontend, tDashboard,    "Implement new header component");
            AddPause( T(10,30), T(10,45));
            AddEntry(T(10,45), T(12,15), pFrontend, tDashboard,    "Wire up chart data binding");
            AddPause( T(12,15), T(13, 0));  // lunch
            AddEntry(T(13, 0), T(14,30), pBackend,  tApiEndpoints, "Add /reports endpoint");
            AddEntry(T(14,30), T(15,45), pBackend,  tApiEndpoints, "Write OpenAPI docs");
            AddPause( T(15,45), T(16, 0));
            AddEntry(T(16, 0), T(17, 0), pCodeRev,  tPrReviews,   "Review auth service PR #42");
            break;

        case 1: // Tuesday
            AddEntry(T(9, 0),  T(10, 0), pBackend,  tAuthService,  "JWT refresh token logic");
            AddEntry(T(10, 0), T(11,30), pBackend,  tAuthService,  "Unit tests for token expiry");
            AddPause( T(11,30), T(11,45));
            AddEntry(T(11,45), T(12,30), pDevOps,   tCiPipeline,  "Fix flaky integration test step");
            AddPause( T(12,30), T(13,15));  // lunch
            AddEntry(T(13,15), T(15, 0), pBackend,  tDbMigration, "Add user_roles table migration");
            AddEntry(T(15, 0), T(16, 0), pFrontend, tBugfix,       "Fix dropdown z-index on Safari");
            AddPause( T(16, 0), T(16,15));
            AddEntry(T(16,15), T(17, 0), pCodeRev,  tPrReviews,   "Review backend migration PR #43");
            break;

        case 2: // Wednesday
            AddEntry(T(9, 0),  T(11, 0), pDevOps,   tDockerize,   "Write Dockerfile for API service");
            AddPause( T(11, 0), T(11,15));
            AddEntry(T(11,15), T(12,30), pDevOps,   tDockerize,   "Docker Compose multi-service setup");
            AddPause( T(12,30), T(13,30));  // lunch
            AddEntry(T(13,30), T(14,30), pFrontend, tDashboard,   "Responsive breakpoints");
            AddEntry(T(14,30), T(15,30), pFrontend, tDashboard,   "Dark mode CSS variables");
            AddPause( T(15,30), T(15,45));
            AddEntry(T(15,45), T(16,30), pCodeRev,  tDesignReview,"Design review: new onboarding flow");
            AddEntry(T(16,30), T(17, 0), pCodeRev,  tPrReviews,   "Review devops PR #44");
            break;

        case 3: // Thursday
            AddEntry(T(9, 0),  T(10,30), pBackend,  tApiEndpoints,"Pagination support on list endpoints");
            AddPause( T(10,30), T(10,45));
            AddEntry(T(10,45), T(12, 0), pBackend,  tApiEndpoints,"Add cursor-based pagination tests");
            AddPause( T(12, 0), T(13, 0));  // lunch
            AddEntry(T(13, 0), T(14,15), pFrontend, tBugfix,      "Fix memory leak in useEffect cleanup");
            AddEntry(T(14,15), T(15,30), pFrontend, tDashboard,   "E2E tests for dashboard filters");
            AddPause( T(15,30), T(15,50));
            AddEntry(T(15,50), T(17, 0), pDevOps,   tCiPipeline,  "Add deploy-to-staging stage");
            break;

        case 4: // Friday
            AddEntry(T(9, 0),  T(10, 0), pCodeRev,  tPrReviews,   "Review 3 small cleanup PRs");
            AddEntry(T(10, 0), T(11,15), pBackend,  tAuthService, "OAuth2 provider integration");
            AddPause( T(11,15), T(11,30));
            AddEntry(T(11,30), T(12,30), pBackend,  tAuthService, "Write integration tests for OAuth");
            AddPause( T(12,30), T(13,15));  // lunch
            AddEntry(T(13,15), T(14,30), pFrontend, tDashboard,   "Polish animations and transitions");
            AddEntry(T(14,30), T(15,30), pDevOps,   tDockerize,   "Kubernetes deployment manifests");
            AddPause( T(15,30), T(15,45));
            AddEntry(T(15,45), T(17, 0), pCodeRev,  tDesignReview,"Final review: weekly sprint wrap-up");
            break;
    }
}

Console.WriteLine("\nDone! Seed data inserted successfully.");
