# planning_WORKING.md

## Project
**Name:** BetterWorkTime  
**Type:** Windows PC application (tray-first desktop app)  
**Intent:** Track time while you work (local-first).  
**Truth set:** v0.48  
**Last updated:** 2026-01-31

---

## 1. Product summary
BetterWorkTime is a tray-first Windows time tracker for logging work time per **Project / Task / Tags** with:
- fast Start/Stop from tray
- idle detection with Keep/Discard/Split (idle stored separately)
- reports with filters + breakdowns
- CSV export (“export what you see”)
- optional hydration reminders while tracking is running

Non-goals for v1.0: cloud sync, automatic activity tracking, invoicing, pomodoro.

---

## 2. Target platform & distribution (LOCKED)
- OS: Windows 10/11
- Arch: x64 (MVP)
- Build: self-contained
- Installer: simple installer; no auto-update in v1.0

---

## 3. Tech stack (LOCKED)
- UI: WPF
- Runtime: .NET LTS (net10.0-windows)
- Storage: SQLite
- Data access: Microsoft.Data.Sqlite (+ optional Dapper)
- Tray: Hardcodet.NotifyIcon.WPF (recommended)
- DI/Host: Microsoft.Extensions.Hosting (Generic Host)
- MVVM: CommunityToolkit.Mvvm
- Tests: xUnit

---

## 4. Solution structure (LOCKED)
Projects:
- BetterWorkTime.App (WPF, net10.0-windows)
- BetterWorkTime.Core (net10.0) — domain + engines + interfaces
- BetterWorkTime.Data (net10.0) — SQLite persistence
- BetterWorkTime.Platform.Windows (net10.0-windows) — Win32 services (idle detection, focus assist checks, helpers)
- Tests: BetterWorkTime.Tests.Core / BetterWorkTime.Tests.Data

Dependency rules:
- App → Core, Data, Platform.Windows
- Data → Core
- Platform.Windows → Core
- Core → none

---

## 5. MVP scope (v1.0) (LOCKED)
### In scope
- Tray-first Start/Stop
- Projects/Tasks/Tags/Notes
- Switch Task
- Idle detection prompt: Keep / Discard / Split
- Hydration reminders (opt-in) while tracking runs
- Today screen (timeline, edit/split/delete entry)
- Reports (filters + breakdown tabs + entries list)
- Export CSV (“export what you see”)
- Settings (General/Tracking/Hydration/Export/About)
- Optional global hotkeys (disabled by default)
- Crash recovery prompt + sleep/wake handling
- Logging + data folder access

### Out of scope
- automatic app/window tracking
- accounts/cloud sync
- start with Windows
- invoicing/billing
- pomodoro/goals
- hydration snooze
- soft-delete for time entries (delete is permanent in v1.0)

---

## 6. Core behaviors (LOCKED)
### 6.1 Tracking entries
- Start creates/continues a running work entry
- Stop finalizes the running entry
- Switch Task while running closes current entry and starts a new one at “now”
- “Unassigned” is allowed (NULL project/task)

### 6.2 Idle handling (LOCKED)
Trigger: tracking running + inactivity ≥ `idle.threshold_minutes`

Prompt actions:
- Keep → idle stays as work time (no idle entry created)
- Discard → create an **Idle Entry** (`is_idle=true`) for the idle segment, exclude from work entry
- Split → create an Idle Entry and preserve work segments before/after

Reporting rule:
- Default reports exclude `is_idle=true`
- Toggle “Include idle” shows Work Total + Idle Total separately

### 6.3 Hydration reminders (LOCKED)
- Optional feature enabled in Settings
- Trigger only when tracking is running and tracked-work progress reaches interval
- Popup is always-on-top, plays sound (optional) and shows water animation
- Button: Close (acknowledge) → hydration progress resets to 0
- No snooze in v1.0

---

## 7. UI spec (MVP) (LOCKED)
### 7.1 Tray menu
- Start / Stop (dynamic)
- Switch Task…
- Add Note…
- Open BetterWorkTime
- Quit

### 7.2 Today screen
- Project dropdown (includes Unassigned)
- Task dropdown (scoped by project; includes Unassigned)
- Tags selector (typeahead)
- Note field
- Big Start/Stop button
- Status: session timer + idle/hydration indicators
- Today timeline list:
  - shows idle entries distinctly (Idle badge)
  - actions: Edit / Split / Delete
- Totals: Work primary; Idle secondary (if any)

### 7.3 Idle prompt popup
- Always-on-top
- Keep / Discard / Split

### 7.4 Hydration popup
- Always-on-top + sound + animation
- Close resets hydration timer

### 7.5 Reports screen
Top bar:
- range selector (Today/Yesterday/This Week/Last 7 Days/This Month/Custom)
- Include idle toggle (default Off)
- Export CSV…

Filters:
- Projects multi-select (+ Unassigned)
- Tasks multi-select (+ Unassigned; scoped)
- Include tags (OR/any)
- Exclude tags (OR/any)
- Search notes (case-insensitive substring)

Tabs:
- By Project (drill-down to tasks)
- By Task
- By Tag
- Entries

### 7.6 Export dialog
- Read-only summary of active filters + Include idle state
- File picker + suggested filename `BetterWorkTime_YYYY-MM-DD_to_YYYY-MM-DD.csv`
- “Open file location after export” (default On)
- Export uses “export what you see”

---

## 8. Editing & splitting rules (LOCKED)
Editing:
- Work entries: start/end, project/task, tags, note
- Idle entries: start/end, project/task/tags/note allowed; `is_idle` not editable
Validation:
- start < end
- no overlaps allowed (MVP: error and manual correction)

Splitting:
- Split at time
- Copies project/task/tags/note to both parts by default
Delete:
- Delete entry exists in v1.0 with confirmation (hard delete)

---

## 9. Projects / Tasks / Tags management (LOCKED)
- No delete in v1.0: Archive/Unarchive only
- Unassigned is not an entity (NULL foreign key)
Uniqueness:
- Projects unique among non-archived
- Tags unique among non-archived
- Tasks unique per project among non-archived
Archived behavior:
- hidden from pickers by default
- shown in history/reports as “(Archived)” in UI

---

## 10. Report filters (LOCKED)
Default: Work only (`is_idle=0`)
Toggle: Include idle (default Off)

Filters:
- date range presets + custom (local day boundary, Monday week start)
- projects/tasks multi-select (+ Unassigned)
- include tags OR/any; exclude tags OR/any
- note search

---

## 11. Export format (LOCKED)
- Rule: export matches current report (“export what you see”)
- CSV: UTF-8 with BOM, comma delimiter, standard quoting
- UTC/local timestamps exported as ISO-8601

Canonical CSV column order:
1 entry_id
2 project_id
3 project_name
4 task_id
5 task_name
6 tags (semicolon-separated)
7 note
8 source
9 is_idle
10 idle_adjusted
11 start_utc
12 end_utc
13 start_local
14 end_local
15 duration_seconds

---

## 12. SQLite schema (LOCKED)
Principles:
- Store timestamps in DB as unix epoch seconds (UTC) INTEGER
- Use archive flags instead of deleting projects/tasks/tags
- `is_idle=true` for idle segments

Tables:
- meta(key,value) for schema version
- projects(id,name,color,archived,created_at_utc)
- tasks(id,project_id,name,archived,created_at_utc)
- tags(id,name,color,archived,created_at_utc)
- time_entries(id,project_id,task_id,start_utc,end_utc,duration_sec,note,source,is_idle,idle_adjusted,created_at_utc)
- time_entry_tags(time_entry_id,tag_id)
- settings(key,value_json,updated_at_utc)
- runtime_state(key,value_json,updated_at_utc)
- hydration_state(id=1,enabled,progress_sec,last_ack_utc,updated_at_utc)

Indexes:
- time_entries(start_utc), project_id, task_id, is_idle
- time_entry_tags(tag_id)
- unique active names for projects/tags/tasks-per-project

---

## 13. App lifecycle & reliability (LOCKED)
Startup:
- default start minimized to tray

Tick loop:
- 1 second tick while running
- persist runtime snapshot at least every 5 seconds while running + on major events

Crash recovery:
- If a timer was running: show dialog Resume vs Stop at last seen (default: Stop at last seen)

Sleep/wake:
- Persist last_seen on suspend
- Treat sleep gap as idle; prompt once after resume

Logging:
- Rotating logs in LocalAppData

---

## 14. Settings UI (LOCKED)
General:
- start minimized to tray (default On)
- enable global hotkeys (default Off)
- open data folder

Tracking:
- idle threshold (default 5 minutes)

Hydration:
- enable hydration (default Off)
- interval presets + custom (default 60 minutes)
- sound toggle (default On when enabled)
- respect Focus Assist (default On)

Export:
- open folder after export (default On)
- remember last export folder

About:
- version + open logs

---

## 15. Hotkeys (LOCKED)
Global hotkeys are optional and disabled by default.
Defaults:
- Start/Stop: Ctrl+Alt+S
- Switch Task: Ctrl+Alt+T
- Open main: Ctrl+Alt+O
- Add Note: Ctrl+Alt+N

If registration fails due to conflict, show message and keep disabled.

---

## 16. Data locations (LOCKED)
Base: `%LOCALAPPDATA%\BetterWorkTime\`
- DB: `%LOCALAPPDATA%\BetterWorkTime\betterworktime.sqlite`
- Logs: `%LOCALAPPDATA%\BetterWorkTime\Logs\`
- Exports: `%LOCALAPPDATA%\BetterWorkTime\Exports\`

Uninstall:
- removes program files, leaves user data by default

---

## 17. Milestones (LOCKED)
- M0 Repo & skeleton (tray app boots)
- M1 Tracking core
- M2 Idle handling
- M3 Hydration
- M4 Reports + Export
- M5 Hardening (recovery, sleep/wake, logs, installer)

Definition of Done (v1.0):
- no overlaps possible via UI flows
- report totals match CSV export totals
- hydration never triggers while stopped
- discard idle always creates `is_idle=true` entries and default reports exclude idle
- stable tray-first behavior all day with low CPU
