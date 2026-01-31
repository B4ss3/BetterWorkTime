# definitions_WORKING.md

## Purpose
Canonical names + meanings for terms/entities used across the project.

**Truth set:** v0.48

---

## Entities
- **Project**: Top-level container for work.
- **Task**: Sub-item under a Project.
- **Tag**: Optional label attached to time entries.
- **Time Entry**: A contiguous tracked time segment.
- **Idle Entry**: A time entry representing inactivity while tracking (`is_idle=true`).
- **Unassigned**: Represents NULL project/task on a time entry (not a stored entity).

## Concepts / policies
- **Tracking**: Running timer creates/updates time entries.
- **Idle prompt**: Prompt shown after idle threshold; choices Keep / Discard / Split.
- **Discard (idle)**: Creates an Idle Entry (`is_idle=true`) and excludes it from default reports.
- **Include idle**: Report toggle that includes idle entries; shows Work Total + Idle Total.
- **Export what you see**: Export output matches active report filters/toggles.
- **Rounding**: None in v1.0 (exact seconds).

## Formats
- **Unix epoch seconds**: Integer seconds since 1970-01-01T00:00:00Z (DB timestamp storage).
- **ISO-8601**: Datetime string format (used for CSV exports).
- **UTF-8 with BOM**: CSV encoding for Excel-friendly opening on Windows.

## UI labels (canonical)
- Tray: “Start”, “Stop”, “Switch Task…”, “Add Note…”, “Open BetterWorkTime”, “Quit”
- Reports toggle: “Include idle”
- Settings section: “Hydration”
