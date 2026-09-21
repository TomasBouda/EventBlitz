# Changelog

## 0.1.0 — 2026-09-21

- Added: sidebar with every channel of the machine (Windows Logs, Applications and Services Logs, favourites), sizes and record counts, search, empty channels hidden by default.
- Added: merged view — Ctrl+click several channels and see their events in one time-ordered list.
- Added: filter strip with level chips, time range, event ID list (`1000, 1001-1010, !4624`), provider and full-text search that runs through the whole log, streaming matches as they are found.
- Added: live tail (Ctrl+L) that inserts new events at the top as they are written.
- Added: detail pane with the message, all system properties and the event XML, plus one-click "only this ID / provider" filters and copy.
- Added: command palette (Ctrl+K), light/dark theme, restart as administrator for the Security log.
- Added: self-update from GitHub Releases (stable and nightly channels), application log page, `--health` and headless automation channel.
