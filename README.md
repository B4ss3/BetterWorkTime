# Tuntio

A personal Windows time tracker I built to learn C# and WPF, and to actually use every day.

Tuntio (Finnish for *hour*) lives in your system tray and gets out of your way. Start a timer, pick a project, optionally add a task and tags, and stop when you're done. That's the core loop. The rest (reports, charts, idle detection, hydration reminders) is there when you need it.

I use it for tracking study sessions, side project work, and anything else I want to know where my time goes. If you find it useful too, great.

## Features

- **Tray-first** - runs quietly in the background, one click to open
- **Projects, Tasks & Tags** - organise your time however makes sense to you
- **Idle detection** - notices when you walk away and asks what to do with the gap
- **Pause tracking** - log breaks without stopping your session
- **Reports & CSV export** - see where your time went, filter by date or project, export to spreadsheet
- **Dashboard** - daily bar chart and project breakdown with drill-down into tasks
- **Hydration reminders** - optional water break nudges, only fires while you're actively tracking
- **Global hotkeys** - start/stop without opening the app
- **Auto-updates** - silently downloads updates in the background

## Installation

Download `TuntioSetup.exe` from the [Releases](https://github.com/B4ss3/Tuntio/releases) page and run it. That's it.

Updates are delivered automatically, when a new version is ready you'll get a prompt to restart.

## Tech stack

- C# / WPF / .NET 10
- SQLite (all data stays local on your machine, nothing goes to the cloud)
- Velopack for auto-updates
- GitHub Actions for CI and releases

## Project status

This is a personal learning project. It works and I use it daily, but it's not a polished commercial product. Expect rough edges. Issues and feedback welcome.
