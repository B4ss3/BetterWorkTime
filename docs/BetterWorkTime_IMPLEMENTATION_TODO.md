# BetterWorkTime — Implementation TODO (status)

## Done

### Repo / process
- [x] Specs committed under `/docs` + README links to spec
- [x] GitHub Issues created for M0–M5, labels applied, project board created
- [x] Repo housekeeping: `.gitignore`, `.editorconfig`, `global.json`, `Directory.Build.props`
- [x] GitHub Actions CI set up and green (supports `.slnx`)

### M0 — Solution skeleton + tray boot
- [x] Projects created:
  - [x] BetterWorkTime.App (WPF)
  - [x] BetterWorkTime.Core
  - [x] BetterWorkTime.Data
  - [x] BetterWorkTime.Platform.Windows
  - [x] BetterWorkTime.Tests.Core
  - [x] BetterWorkTime.Tests.Data
- [x] Project references correct (App → Core/Data/Platform; Data → Core; Platform → Core; tests → target)
- [x] Tray-first app works:
  - [x] Tray icon visible
  - [x] Left-click toggles main window
  - [x] Right-click menu shows Start/Stop, Switch Task…, Add Note…, Open, Quit
  - [x] Quit exits cleanly
  - [x] Window close hides to tray

### M1 — Tracking + SQLite (major parts done)
- [x] SQLite package added (`Microsoft.Data.Sqlite`)
- [x] DB created on startup at `%LOCALAPPDATA%\BetterWorkTime\betterworktime.sqlite`
- [x] Schema initialized (tables created)
- [x] Start/Stop writes `time_entries` (creates running entry, finalizes on stop)
- [x] `runtime_state` persistence:
  - [x] `tracking.is_running`
  - [x] `tracking.running_entry_id`
- [x] Recovery prompt on startup:
  - [x] Yes = Resume
  - [x] No = Stop now (finalize entry + clear runtime_state)
- [x] Today window upgraded:
  - [x] Status (Tracking/Stopped)
  - [x] Elapsed timer ticking
  - [x] Start/Stop button synced with tray Start/Stop

### M1.8 — Switch Task (real behavior)
- [x] Make “Switch Task…” while tracking:
  - [x] Stop current entry at now
  - [x] Start a new entry immediately at now
  - [x] Update runtime_state with new running entry id
  - [x] Ensure Today elapsed resets to new entry start
- [x] Minimal UI for switching context:
  - [x] Choose Project/Task (allow Unassigned)
  - [ ] (Later) searchable picker

## In progress (next)

## Not started

### M2 — Idle handling
- [ ] Implement Windows idle detection (last input)
- [ ] Idle prompt UI: Keep / Discard / Split
- [ ] Discard/Split create Idle Entry (`is_idle=true`)
- [ ] Prevent overlaps after applying idle decision
- [ ] Default reports exclude idle; toggle “Include idle”

### M3 — Hydration reminders
- [ ] Settings wiring: enable, interval, sound, respect Focus Assist
- [ ] Accumulate hydration only while tracking is running
- [ ] Topmost popup + sound + animation; Close resets timer (no snooze)
- [ ] Focus Assist respect: delay popup if enabled and active

### M4 — Reports + Export
- [ ] Reports UI (filters + breakdown tabs + entries list)
- [ ] Implement report queries:
  - [ ] Date presets + custom, local day boundary, Monday week start
  - [ ] Project/task filters (+ Unassigned)
  - [ ] Include-tags OR, Exclude-tags OR
  - [ ] Note search
  - [ ] Include idle toggle
- [ ] Export CSV dialog (“export what you see”)
- [ ] CSV writer (LOCKED format):
  - [ ] UTF-8 with BOM, comma delimiter
  - [ ] ISO-8601 timestamps (UTC Z + local offset)
  - [ ] Canonical column order

### M5 — Hardening + Packaging
- [ ] “Stop at last seen” recovery (beyond Stop now)
- [ ] Persist runtime snapshot every 5s while running + major events
- [ ] Sleep/wake: treat gap as idle and prompt once after resume
- [ ] Rotating logs to `%LOCALAPPDATA%\BetterWorkTime\Logs\` + “Open logs”
- [ ] Self-contained installer; uninstall leaves user data by default
