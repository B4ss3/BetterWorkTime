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
    Mode       = SqliteOpenMode.ReadWriteCreate,
    Cache      = SqliteCacheMode.Shared
}.ToString();

// ── Helpers ───────────────────────────────────────────────────────────────────

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

// ── Projects ──────────────────────────────────────────────────────────────────

string EnsureProject(string name, string color)
{
    var existing = Scalar<string>(
        "SELECT id FROM projects WHERE name = $n AND archived = 0;",
        c => c.Parameters.AddWithValue("$n", name));
    if (existing != null) return existing;

    var id  = NewId();
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    Exec("INSERT INTO projects(id, name, color, archived, created_at_utc) VALUES ($id,$n,$c,0,$t);", c =>
    {
        c.Parameters.AddWithValue("$id", id);
        c.Parameters.AddWithValue("$n",  name);
        c.Parameters.AddWithValue("$c",  color);
        c.Parameters.AddWithValue("$t",  now);
    });
    Console.WriteLine($"  + Project: {name}");
    return id;
}

var pFrontend = EnsureProject("Frontend",      "#4A7FA5");
var pBackend  = EnsureProject("Backend",       "#3D8B5E");
var pDevOps   = EnsureProject("DevOps",        "#A84040");
var pCodeRev  = EnsureProject("Code Review",   "#6B5B9E");
var pPlanning = EnsureProject("Planning",      "#D4882A");
var pDocs     = EnsureProject("Documentation", "#2A8FAA");
var pQA       = EnsureProject("QA / Testing",  "#7A6E3E");

// ── Tasks ─────────────────────────────────────────────────────────────────────

string EnsureTask(string name, string projectId)
{
    var existing = Scalar<string>(
        "SELECT id FROM tasks WHERE name = $n AND project_id = $p AND archived = 0;",
        c => { c.Parameters.AddWithValue("$n", name); c.Parameters.AddWithValue("$p", projectId); });
    if (existing != null) return existing;

    var id  = NewId();
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    Exec("INSERT INTO tasks(id, name, project_id, archived, created_at_utc) VALUES ($id,$n,$p,0,$t);", c =>
    {
        c.Parameters.AddWithValue("$id", id);
        c.Parameters.AddWithValue("$n",  name);
        c.Parameters.AddWithValue("$p",  projectId);
        c.Parameters.AddWithValue("$t",  now);
    });
    return id;
}

// Frontend
var tUI        = EnsureTask("UI component library",        pFrontend);
var tDashboard = EnsureTask("Dashboard redesign",          pFrontend);
var tBugfix    = EnsureTask("Bug fixes",                   pFrontend);
var tA11y      = EnsureTask("Accessibility improvements",  pFrontend);
var tPerf      = EnsureTask("Performance optimisation",    pFrontend);

// Backend
var tApiV2     = EnsureTask("API v2 endpoints",            pBackend);
var tAuth      = EnsureTask("Auth service",                pBackend);
var tDbMig     = EnsureTask("DB migrations",               pBackend);
var tCaching   = EnsureTask("Redis caching layer",         pBackend);
var tWebhooks  = EnsureTask("Webhook system",              pBackend);

// DevOps
var tCI        = EnsureTask("CI pipeline",                 pDevOps);
var tDocker    = EnsureTask("Dockerize services",          pDevOps);
var tK8s       = EnsureTask("Kubernetes manifests",        pDevOps);
var tMonitor   = EnsureTask("Monitoring & alerts",         pDevOps);

// Code Review
var tPRs       = EnsureTask("PR reviews",                  pCodeRev);
var tDesign    = EnsureTask("Design review",               pCodeRev);

// Planning
var tSprint    = EnsureTask("Sprint planning",             pPlanning);
var tRetro     = EnsureTask("Retrospective",               pPlanning);
var tRoadmap   = EnsureTask("Roadmap grooming",            pPlanning);

// Documentation
var tApiDocs   = EnsureTask("API reference docs",          pDocs);
var tRunbooks  = EnsureTask("Runbooks",                    pDocs);
var tChangelog = EnsureTask("Changelog & release notes",   pDocs);

// QA / Testing
var tE2E       = EnsureTask("End-to-end tests",            pQA);
var tLoadTest  = EnsureTask("Load testing",                pQA);
var tBugTriage = EnsureTask("Bug triage",                  pQA);

// ── Entry helpers ─────────────────────────────────────────────────────────────

void AddEntry(DateTime start, DateTime end, string projectId, string taskId, string note)
{
    var id  = NewId();
    var s   = ToUtc(start);
    var e   = ToUtc(end);
    var dur = e - s;
    Exec("""
INSERT INTO time_entries(id, start_utc, end_utc, duration_sec, project_id, task_id,
                          note, is_idle, source, created_at_utc)
VALUES ($id,$s,$e,$dur,$p,$t,$n,0,'manual',$s);
""", c =>
    {
        c.Parameters.AddWithValue("$id",  id);
        c.Parameters.AddWithValue("$s",   s);
        c.Parameters.AddWithValue("$e",   e);
        c.Parameters.AddWithValue("$dur", dur);
        c.Parameters.AddWithValue("$p",   (object?)projectId ?? DBNull.Value);
        c.Parameters.AddWithValue("$t",   (object?)taskId    ?? DBNull.Value);
        c.Parameters.AddWithValue("$n",   (object?)note      ?? DBNull.Value);
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

    Exec("INSERT OR IGNORE INTO time_entry_tags(time_entry_id, tag_id) VALUES ($eid,$tid);", c =>
    {
        c.Parameters.AddWithValue("$eid", id);
        c.Parameters.AddWithValue("$tid", SystemTags.PauseId);
    });
}

// ── Schedule templates ────────────────────────────────────────────────────────
// Each slot: (startOffsetMinutes, durationMinutes, proj?, task?, note)
// proj==null → pause

(int S, int D, string? P, string? T, string N) Work(int s, int d, string p, string t, string n)
    => (s, d, p, t, n);

(int S, int D, string? P, string? T, string N) Pause(int s, int d)
    => (s, d, null, null, "Pause");

var templates = new List<List<(int S, int D, string? P, string? T, string N)>>
{
    // 0 — Backend heavy
    new List<(int,int,string?,string?,string)>
    {
        Work(  0,  90, pBackend,  tApiV2,    "Design REST resource hierarchy"),
        Pause( 90,  15),
        Work(105,  75, pBackend,  tApiV2,    "Implement GET /users and /projects"),
        Pause(180,  45),
        Work(225,  60, pBackend,  tDbMig,    "Add indexes for query performance"),
        Work(285,  60, pCodeRev,  tPRs,      "Review backend API PRs"),
        Pause(345,  15),
        Work(360,  60, pDocs,     tApiDocs,  "Document new v2 endpoints"),
    },
    // 1 — Frontend heavy
    new List<(int,int,string?,string?,string)>
    {
        Work(  0,  60, pFrontend, tUI,       "Build reusable Button component"),
        Work( 60,  90, pFrontend, tUI,       "Build Modal and Toast components"),
        Pause(150,  15),
        Work(165,  45, pFrontend, tBugfix,   "Fix z-index stacking context bug"),
        Pause(210,  45),
        Work(255,  90, pFrontend, tDashboard,"Wire up project breakdown chart"),
        Pause(345,  15),
        Work(360,  60, pCodeRev,  tDesign,   "Design review: new settings layout"),
    },
    // 2 — DevOps + Planning
    new List<(int,int,string?,string?,string)>
    {
        Work(  0,  60, pPlanning, tSprint,   "Sprint planning — estimate tickets"),
        Work( 60,  30, pPlanning, tRoadmap,  "Update Q2 roadmap milestones"),
        Pause( 90,  15),
        Work(105,  75, pDevOps,   tCI,       "Add parallel test jobs to pipeline"),
        Pause(180,  45),
        Work(225,  90, pDevOps,   tDocker,   "Multi-stage Dockerfile for API"),
        Pause(315,  15),
        Work(330,  90, pDevOps,   tK8s,      "Write Helm chart for staging deploy"),
    },
    // 3 — QA + Backend
    new List<(int,int,string?,string?,string)>
    {
        Work(  0,  90, pQA,       tE2E,      "Write Playwright tests for auth flow"),
        Pause( 90,  15),
        Work(105,  75, pQA,       tE2E,      "Write tests for dashboard filters"),
        Pause(180,  45),
        Work(225,  60, pBackend,  tCaching,  "Add Redis cache for session tokens"),
        Work(285,  60, pBackend,  tCaching,  "Cache invalidation on user update"),
        Pause(345,  15),
        Work(360,  60, pQA,       tBugTriage,"Triage 8 open bug reports"),
    },
    // 4 — Docs + Code Review
    new List<(int,int,string?,string?,string)>
    {
        Work(  0,  60, pDocs,     tApiDocs,  "Write pagination API reference"),
        Work( 60,  60, pDocs,     tRunbooks, "Document deployment runbook"),
        Pause(120,  15),
        Work(135,  45, pCodeRev,  tPRs,      "Review 4 small cleanup PRs"),
        Pause(180,  45),
        Work(225,  90, pFrontend, tA11y,     "Add ARIA labels to form controls"),
        Pause(315,  15),
        Work(330,  60, pFrontend, tA11y,     "Keyboard navigation for dropdowns"),
        Work(390,  30, pDocs,     tChangelog,"Write release notes"),
    },
    // 5 — Auth + Performance
    new List<(int,int,string?,string?,string)>
    {
        Work(  0,  90, pBackend,  tAuth,     "OAuth2 Google provider integration"),
        Pause( 90,  15),
        Work(105,  75, pBackend,  tAuth,     "Write integration tests for OAuth"),
        Pause(180,  45),
        Work(225,  60, pFrontend, tPerf,     "Lazy-load route bundles"),
        Work(285,  60, pFrontend, tPerf,     "Reduce re-renders with useMemo"),
        Pause(345,  15),
        Work(360,  60, pDevOps,   tMonitor,  "Set up Grafana dashboard for API p95"),
    },
    // 6 — Sprint events + Webhooks
    new List<(int,int,string?,string?,string)>
    {
        Work(  0,  30, pPlanning, tRetro,    "Sprint retrospective"),
        Work( 30,  30, pPlanning, tSprint,   "Next sprint backlog refinement"),
        Pause( 60,  15),
        Work( 75,  75, pBackend,  tWebhooks, "Design webhook event schema"),
        Pause(150,  45),
        Work(195,  90, pBackend,  tWebhooks, "Implement webhook delivery with retry"),
        Pause(285,  15),
        Work(300,  60, pQA,       tLoadTest, "k6 load test on /events endpoint"),
        Work(360,  60, pCodeRev,  tPRs,      "Review webhook implementation PR"),
    },
    // 7 — Infra + Docs light day
    new List<(int,int,string?,string?,string)>
    {
        Work(  0,  90, pDevOps,   tMonitor,  "Alert rules for error rate spikes"),
        Pause( 90,  15),
        Work(105,  45, pDevOps,   tMonitor,  "PagerDuty integration & on-call rota"),
        Pause(150,  45),
        Work(195,  60, pDocs,     tRunbooks, "Incident response runbook"),
        Work(255,  60, pBackend,  tApiV2,    "Rate limiting middleware"),
        Pause(315,  15),
        Work(330,  60, pFrontend, tDashboard,"Add date range picker to dashboard"),
        Work(390,  30, pCodeRev,  tDesign,   "Quick design review: mobile nav"),
    },
};

// ── Seed last 30 calendar days (weekdays only) ────────────────────────────────

var today = DateTime.Today;
var seed  = 0;

Console.WriteLine($"\nSeeding last 30 days up to {today:yyyy-MM-dd}");

for (int daysBack = 29; daysBack >= 1; daysBack--)
{
    var day = today.AddDays(-daysBack);

    if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        continue;

    var dayStart = ToUtc(day.Date);
    var dayEnd   = ToUtc(day.Date.AddDays(1));
    var existing = Scalar<long>(
        "SELECT COUNT(*) FROM time_entries WHERE start_utc >= $s AND start_utc < $e;",
        c => { c.Parameters.AddWithValue("$s", dayStart); c.Parameters.AddWithValue("$e", dayEnd); });

    if (existing > 0)
    {
        Console.WriteLine($"  Skipping {day:ddd yyyy-MM-dd} (already has {existing} entries)");
        seed++;
        continue;
    }

    var template = templates[seed % templates.Count];
    seed++;

    Console.WriteLine($"  Seeding  {day:ddd yyyy-MM-dd}  (template {(seed - 1) % templates.Count})");

    var anchor = day.Date.AddHours(9); // workday starts at 09:00

    foreach (var slot in template)
    {
        var s = anchor.AddMinutes(slot.S);
        var e = s.AddMinutes(slot.D);

        if (slot.P == null)
            AddPause(s, e);
        else
            AddEntry(s, e, slot.P, slot.T!, slot.N);
    }
}

Console.WriteLine("\nDone! Seed data inserted successfully.");
