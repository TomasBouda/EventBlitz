# EventBlitz — notes for Claude Code

Avalonia 11.3 / .NET 10 viewer for the Windows Event Log (CommunityToolkit.Mvvm, ops-console styles copied from
IISBlitz with a cyan accent, custom title bar). Chat in Czech, everything in the repository in English.

- Build: `dotnet build EventBlitz.slnx` · tests: `dotnet test tests/EventBlitz.Core.Tests/EventBlitz.Core.Tests.csproj`
- `src/EventBlitz.Core`: models (`EventItem`, `ChannelInfo`, `EventFilter` → XPath, `EventIdSpec`), `IEventLogSource`
  with the real `WindowsEventLogSource` and the JSON `FixtureEventLogSource`, `MergedEventQuery` (k-way merge of
  per-channel cursors, newest first, lazy priming so a full-log text search is cancellable). Keep logic here — it is
  what the tests cover.
- `src/EventBlitz.App`: `MainWindowViewModel` owns channels, filter, paging (1000 per page, 100 per UI chunk), live
  watcher and the detail pane; `CommandPaletteViewModel`; `LogsViewModel` (the app's own Serilog log page).
- Data folder: `%APPDATA%\EventBlitz` or `EVENTBLITZ_DATA_DIR`. When it contains `events.json` the app shows that
  fixture instead of the live log — that is how UI checks stay deterministic.
- Releases: GitHub Releases through the shared `publish-app.yml` of TomLabs.AutoUpdate (`.github/workflows`);
  `UPDATE_SIGNING_KEY` repository secret signs `update.json`, the matching public key sits in `App.axaml.cs`.
  Never add the app store URL or API here — the store lists this public repo as a pointer only.

## Verifying UI changes — use the automation channel, never the desktop

Do **not** verify the UI with `SendKeys`, `SetCursorPos`, `mouse_event`, `SetForegroundWindow` or screen captures.
The app embeds the **TomLabs.UiAutomation** channel (`EVENTBLITZ_AUTOMATION=<port>`, wired in `App.StartAutomation`
and `Program.BuildAvaloniaApp`), driven by the `tomlabs-ui` dotnet tool. Run from the repository root:

```bash
tomlabs-ui start --project src/EventBlitz.App/EventBlitz.App.csproj   # Release build into .ui-build, headless, fixture data
tomlabs-ui call "do/channel?arg=Application,System"                     # select channels (merged view)
tomlabs-ui call "do/range?arg=all"                                       # time range key: 15m 1h 24h 7d 30d all
tomlabs-ui call "set?path=SearchText&value=service"
tomlabs-ui call "click?text=Database connection could not"               # select an event → detail pane
tomlabs-ui call "do/page?arg=log"                                        # app log page (arg=events to go back)
tomlabs-ui shot C:\...\main.png                                          # then Read the PNG
tomlabs-ui call "get?path=StatusText"
tomlabs-ui stop
```

Add `--data-dir <empty folder>` to run against the real Event Log of this machine (read-only, safe) — useful for
performance checks; the fixture in `tests/ui-fixture` covers the visual states (levels, access denied, disabled
and empty channels, favourites). Extend the fixture rather than screenshotting real events.

Generic endpoints: `/screenshot /tree /find /click /key /type /get /set /invoke /theme /resize /wait /quit`.
Anything generic belongs in the TomLabs.UiAutomation package, app shortcuts go into `App.StartAutomation`.

## Release checklist

Bump `VersionPrefix` in `Directory.Build.props`, add the section to `CHANGELOG.md`, keep `README.md` in step,
push, then tag `v<version>` — the Release workflow publishes the zip and manifest.
