# BetterWorkTime — v1.0 Spec Snapshot

**Truth set:** v0.48  
**Product:** BetterWorkTime (Windows 10/11, tray-first PC app)

## Scope
Tray-first time tracking with Projects/Tasks/Tags, idle handling, reports, and CSV export.
Optional hydration reminders while tracking runs.

## Key rules
- Idle Discard/Split create Idle Entries (`is_idle=true`)
- Default reports exclude idle; toggle can include idle
- Export matches report (“export what you see”)
- Hydration triggers only while tracking runs; Close resets; no snooze
- No rounding in v1.0 (exact seconds)
