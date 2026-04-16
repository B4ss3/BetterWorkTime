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

### M1.9 — Projects / Tasks / Tags management
- [x] Projects CRUD:
  - [x] Create project (name, color)
  - [x] Archive / Unarchive project
  - [x] Simple management UI (list + add form)
- [x] Tasks CRUD:
  - [x] Tasks are free-text (typed inline, auto-created via FindOrCreate)
  - [x] No separate task management UI — tasks are created on the fly per project
- [x] Tags CRUD:
  - [x] Create tag (name, color)
  - [x] Archive / Unarchive tag
- [x] Today window wiring:
  - [x] Project dropdown populated from DB (includes Unassigned)
  - [x] Task as free-text field (defaults to “Working hard...” when empty)
  - [x] Tags selector (checkbox list)
  - [x] Note field (saved to running entry on focus loss)
  - [x] Switch Task dialog pre-populated with current project + task name
- [x] “Add Note…” tray item: update note on running entry (opens small input popup)

### M2 — Idle handling
- [x] Implement Windows idle detection via `GetLastInputInfo` in `BetterWorkTime.Platform.Windows`
- [x] 1s tick checks idle threshold (default 5 min, from settings)
- [x] Idle prompt UI: Keep / Discard / Split (always-on-top)
- [x] Discard/Split create Idle Entry (`is_idle=true`)
- [x] Prevent overlaps after applying idle decision
- [ ] Default reports exclude idle; toggle “Include idle” (deferred to M4)

### M2.5 — Today screen timeline
- [x] Query all time_entries for today (local day boundary)
- [x] Timeline list in main window: start–end, duration, project/task, note
- [x] Idle entries shown with distinct “Idle” badge
- [x] Totals row: Work total (+ Idle total if any idle entries exist)
- [x] Entry actions:
  - [x] Edit (start/end, project/task, tags, note)
  - [x] Split at time (copies project/task to both parts)
  - [x] Delete with confirmation (hard delete)
- [x] Overlap validation: reject edits that would cause overlaps

### M3 — Hydration reminders + Settings window

#### M3a — Settings window
- [x] Settings window (tabbed): opened from tray menu + main window “Settings…” button
- [x] **Tracking tab**: idle threshold (minutes, default 5)
- [x] **Hydration tab**:
  - [x] Enable/disable toggle
  - [x] Interval (minutes, default 30)
  - [x] Sound picker: Windows system sounds (Chimes/Chord/Notify) + “Browse…” for custom file
  - [x] Preview button to play selected sound
- [x] Settings persisted to `settings` table
- [x] `SettingsRepository` to read/write settings

#### M3b — Hydration logic
- [x] Accumulate progress only while tracking is running (pause when stopped)
- [x] Topmost popup when interval hit; Close resets timer (no snooze)
- [x] Sound plays on popup show
- [x] Focus Assist respect: delay popup if Focus Assist is active

### M4 — Reports + Export
- [x] Reports screen (separate window)
- [x] Reports UI (filters + breakdown tabs + entries list)
- [x] Implement report queries:
  - [x] Date presets + custom, local day boundary, Monday week start
  - [x] Project/task filters (+ Unassigned)
  - [x] Note search
  - [x] Include idle toggle
- [x] Export CSV dialog (“export what you see”)
- [x] CSV writer (LOCKED format):
  - [x] UTF-8 with BOM, comma delimiter
  - [x] ISO-8601 timestamps (UTC Z + local offset)
  - [x] Canonical column order

### M5 — Hardening + Packaging
- [x] “Stop at last seen” recovery (beyond Stop now)
- [x] Persist runtime snapshot every 5s while running + major events
- [x] Sleep/wake: treat gap as idle and prompt once after resume
- [x] Rotating logs to `%LOCALAPPDATA%\BetterWorkTime\Logs\` + “Open logs”
- [x] Settings UI remaining tabs:
  - [x] General: start minimized, open data folder, hotkeys toggle
  - [x] Export: open folder after export, remember last folder
  - [x] About: version + open logs
- [x] Global hotkeys (disabled by default):
  - [x] Start/Stop: Ctrl+Alt+S
  - [x] Switch Task: Ctrl+Alt+T
  - [x] Open main: Ctrl+Alt+O
  - [x] Add Note: Ctrl+Alt+N
- [ ] Self-contained installer; uninstall leaves user data by default

### M6 — Polish + Bug fixes

#### Bugs
- [ ] **Tags lost on split** — `SplitEntry` duplicates project/task/note but not `time_entry_tags` rows; second half has no tags
- [ ] **Tags not editable in Edit Entry dialog** — dialog shows project/task/note but tags are not displayed or preserved
- [ ] **Project dropdown switches task while tracking** — `ProjectCombo_SelectionChanged` calls `ExecuteSwitchWithCurrentContext()` on every selection change, including accidental ones; should require explicit confirmation or only trigger on Start
- [ ] **`IdleThresholdSeconds` reads DB on every 1s tick** — creates a new `SettingsRepository` + SQLite connection 60× per minute; cache the value and re-read only when settings are saved

#### UX improvements
- [ ] **Tray icon reflects tracking state** — green icon when tracking, gray when stopped (requires a custom .ico with two states)
- [ ] **Tray tooltip shows running entry** — e.g. `”MyProject / Task — 1h 23m”` instead of the static `”BetterWorkTime”`
- [ ] **Quick-start from tray** — “Recent entries” submenu (last 5 distinct project/task combos) for one-click resume without opening the main window
- [ ] **Tags shown in today's timeline** — timeline entry cards currently omit tags; show them as small chips below the time range row
- [ ] **Reports window singleton** — opening Reports… multiple times creates duplicate windows; bring existing window to focus if already open
- [ ] **Running entry visible in reports** — `GetEntries` filters `end_utc IS NOT NULL` so the live session never appears; show it as a “live” row with current elapsed
