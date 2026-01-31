# reference_WORKING.md

## Purpose
Canonical numbers/tables/constants (no prose).

**Truth set:** v0.48

---

## Build targets
- Framework: net10.0-windows
- Arch (MVP): x64
- Build: self-contained

## Runtime timing
- Tick interval: 1s
- Persist runtime snapshot: every 5s while running + on major events
- Crash recovery default: Stop at last seen

## Defaults
- ui.start_minimized_to_tray: true
- idle.threshold_minutes: 5
- hydration.enabled: false
- hydration.interval_minutes: 60
- hydration.sound_enabled: true
- hydration.respect_focus_assist: true
- export.open_folder_after: true
- hotkeys.enabled: false
- hotkeys.start_stop: Ctrl+Alt+S
- hotkeys.switch_task: Ctrl+Alt+T
- hotkeys.open_main: Ctrl+Alt+O
- hotkeys.add_note: Ctrl+Alt+N

## Paths (LOCKED)
- data.base: %LOCALAPPDATA%\BetterWorkTime\
- data.db: %LOCALAPPDATA%\BetterWorkTime\betterworktime.sqlite
- data.logs: %LOCALAPPDATA%\BetterWorkTime\Logs\
- data.exports: %LOCALAPPDATA%\BetterWorkTime\Exports\

## Settings keys
- ui.start_minimized_to_tray
- idle.threshold_minutes
- hydration.enabled
- hydration.interval_minutes
- hydration.sound_enabled
- hydration.respect_focus_assist
- export.open_folder_after
- export.last_folder
- hotkeys.enabled
- hotkeys.start_stop
- hotkeys.switch_task
- hotkeys.open_main
- hotkeys.add_note

## CSV export format (LOCKED)
Encoding: UTF-8 with BOM  
Delimiter: comma  
Quote: double quote  
Newlines: CRLF

Timestamps:
- start_utc/end_utc: ISO-8601 UTC with Z
- start_local/end_local: ISO-8601 local with offset

Canonical columns (order):
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
