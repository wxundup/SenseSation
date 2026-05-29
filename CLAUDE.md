# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

SenseSation is a VALORANT **personal analytics + self-coaching** dashboard (Blazor Server, .NET 8).
It analyzes the user's *own* matches, tracks rank, coaches against benchmarks, shows live lobby
ranks, and turns the user's own screen recordings into fight-review clips.

## Hard boundary — do not cross (this overrides feature requests)

This is an informer for the player's own improvement, **not a cheat**. Only ever surface:
- The user's **own** match/stat data (HenrikDev API).
- **Account metadata** (names, ranks) for the current lobby, read from the **local Riot client
  API** — the same data Blitz/Tracker.gg show. Never game memory, never process injection.
- The user's **own screen recordings**, analyzed **after** the game.

Never add: live enemy positions/economy/movement, buy/position prediction, live in-match
coaching, crosshair/aim assistance, or anything that reads the game process. That line is what
keeps the app ToS-safe and Vanguard-unbannable. If a request asks to cross it, refuse and offer
the safe equivalent (post-match analysis on own data).

> The original project brief in git history described a live-data "gamesense" tool. That design
> was a bannable cheat and was deliberately **not** built. Don't resurrect it.

## Commands

```bash
dotnet build SenseSation.sln                 # build all
dotnet run --project src/SenseSation.Web     # run dashboard (listens on the launchSettings port)
dotnet test                                  # all tests
dotnet test --filter "FullyQualifiedName~TrainerEngine"   # single test class
```

`global.json` pins the SDK to 8.0.x so templates/builds target `net8.0`. RadiantConnect ships
net8/9/10 TFMs; keep projects on net8.0.

## Architecture (the parts that span files)

- **Dependency direction**: everything points at `SenseSation.Core`. `Core` defines the domain
  records (`MatchSummary`, `MatchDetail`, `Rank`, `PlayerSummary`, `LiveLobby`, `RrSnapshot`) and
  the three abstractions that decouple the rest: `IMatchDataSource` (stats),
  `ILiveClient` (local live ranks), `IMatchStore` (persistence). Implementations live in `Data`.
- **Two interchangeable `IMatchDataSource` impls, chosen in `Program.cs` by whether `Henrik:ApiKey`
  is set:** `HenrikDevClient` (HTTP, any account, no game needed) or `RadiantMatchSource` (no key —
  reads the signed-in player's own stats from the local Riot client). `IMatchDataSource.NeedsRiotId`
  tells the UI which mode is active; `AppData.IsConfigured` is always true in local-client mode.
- **`RiotSession` (singleton) owns the one lockfile auth + `Initiator`.** Both `RadiantConnectClient`
  (live lobby) and `RadiantMatchSource` (stats) depend on it, so the user authenticates once via
  their running client. `RadiantMatchSource` binds RadiantConnect's typed records directly (aliased
  `RC`) — they're stable, unlike the live `CurrentGame` shapes which still need `ReflectionHelpers`.
- **The coaching engine is pure + deterministic**: `MetricsCalculator.Compute(matches)` →
  `PlayerMetrics`; `TrainerEngine.Analyze(metrics)` scores each metric against
  `RadiantBenchmarks.All`, ranks the gaps, and maps the top gaps to `DrillCatalog` entries. No
  ML, no I/O — easy to unit test. Add a new coached metric by adding a `Benchmark`, a
  `PlayerMetrics.ValueFor` case, and a `DrillCatalog.For` case.
- **`AppData` (scoped, in `Web/Services`) is the UI's single entry point.** It loads once per
  Blazor circuit, caches in memory, and on API failure falls back to the SQLite store so the
  dashboard works offline. Pages call `App.LoadAsync()` in `OnInitializedAsync`; they don't talk
  to clients directly.
- **HenrikDev parsing is intentionally defensive.** `HenrikDevClient` reads `JsonElement` via the
  `JsonExt` probing helpers (not rigid DTOs) because HenrikDev's schema differs across API
  versions. First-blood/multikill/econ-discipline are derived from the `rounds[]` array; if a
  field is absent the value degrades to 0/neutral rather than throwing.
- **RadiantConnect is accessed reflectively on purpose.** Its DTO shapes are version-volatile and
  the adapter only runs on a live machine (can't be tested here), so `RadiantConnectClient` uses
  `ReflectionHelpers` to pull tier/name out of whatever object the SDK returns, wrapped in
  try/catch that fails soft (returns null/false when no client is running). Real API entry points:
  `Authentication.AuthenticateWithLockFile()` → `new Initiator(rso)` →
  `Endpoints.PvpEndpoints.FetchPlayerMMRAsync` / `Endpoints.CurrentGameEndpoints.GetCurrentGameMatchAsync`;
  self puuid at `Initiator.Client.UserId`.
- **VOD review is record-then-analyze.** `ScreenRecorder` shells out to ffmpeg (gdigrab) and must
  be stopped via `StopAsync` (sends `q` to stdin) so the mp4 finalizes. `EngagementDetector`
  (OpenCvSharp) samples frames and flags motion spikes; `VodReviewService` cuts a clip around each
  via ffmpeg. Recordings/clips are served to the browser through the `/media/{**path}` endpoint in
  `Program.cs` (path-traversal guarded).

## Gotchas

- **Razor page class names collide with `Core` model names.** A page file `Foo.razor` compiles to
  class `Foo`; if `Core.Models` also has a `Foo` (e.g. `Rank`, `MatchDetail`), the page shadows the
  model across the whole `Pages` namespace and breaks unrelated files. That's why the rank/match
  pages are `RankPage.razor` / `MatchView.razor`. Don't name a page after a model type.
- **Secrets**: `Henrik:ApiKey` goes in user-secrets or `appsettings.Development.json` (gitignored).
  `appsettings.json` ships an empty key on purpose. `HenrikDevClient` throws a clear
  `HenrikApiException` if the key is missing.
- Config sections bound in `Program.cs`: `Henrik` (`HenrikOptions`), `Capture` (`CaptureOptions`),
  `Storage` (`SqliteStoreOptions`), plus `Account` seeding `SettingsStore` on first run.
