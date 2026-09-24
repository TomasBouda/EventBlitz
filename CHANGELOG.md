# Changelog

## 0.1.1 — 2026-09-24

- Changed: the theme switch now has a System option that follows Windows live; earlier Light/Dark choices were reset to System once.
- Added: the header button shows the current mode (half circle = System, sun, moon); the command palette can set each mode directly.

## 0.1.0 — 2026-09-21

- Added: sidebar with every channel of the machine (Windows Logs, Applications and Services Logs, favourites), sizes and record counts, search, empty channels hidden by default.
- Added: merged view — Ctrl+click several channels and see their events in one time-ordered list.
- Added: filter strip with level chips, time range, event ID list (`1000, 1001-1010, !4624`), provider and full-text search that runs through the whole log, streaming matches as they are found.
- Added: live tail (Ctrl+L) that inserts new events at the top as they are written.
- Added: alerts (Ctrl+Shift+A): rules on chosen channels with level, event ID, provider and text conditions run in the background whatever is on screen and fire a sound (a distinct built-in sound per level, or any WAV), a toast in the corner and a taskbar flash; "New from current view" turns the current channels and filter into a rule, hits are listed and open the event on click, mute switch and a per-rule 3 s sound cooldown.
- Added: detail pane with the syntax-highlighted message (labels, numbers, paths, GUIDs, problem words) and event XML, all system properties, plus one-click "only this ID / provider" filters and copy.
- Added: "Restart as administrator" right in the access-denied banner; clear buttons (×) in every search box.
- Added: command palette (Ctrl+K), light/dark theme, restart as administrator for the Security log.
- Added: self-update from GitHub Releases (stable and nightly channels), application log page, `--health` and headless automation channel.
