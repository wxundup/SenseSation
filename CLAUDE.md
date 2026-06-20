# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test

```bash
dotnet build SenseSation.sln                       # build everything
dotnet run --project src/SenseSation.Web          # run dashboard (localhost:5080)
dotnet test                                        # run all xUnit tests
powershell -File build-dist.ps1                   # build self-contained win-x64 exe into dist/
dotnet publish src/SenseSation.Web -c Release -r win-x64 -p:PublishSingleFile=true -o dist
```

## Architecture

Five C# projects in a .NET 8 Blazor Server desktop app. Data flows through interfaces defined in Core, with runtime-switchable implementations:

### SenseSation.Core
Domain models, abstractions, coaching logic. No external deps.
- `Models/` — `MatchSummary`, `MatchDetail`, `Scoreline`, `Rank`, `RrSnapshot`, `PlayerSummary`, `RoundEconomy`, `LiveLobby`, `PlayerCareer`, `PreGameLobby`, enums (`MatchResult`, `Team`, `BuyType`, `Severity`)
- `Abstractions/` — `IMatchDataSource`, `ILiveClient`, `IMatchStore` (the three data contracts)
- `Training/` — `RadiantBenchmarks` (8 metrics with Good/Radiant thresholds), `MetricsCalculator` (aggregate perfs from matches), `TrainerEngine` (rule-based coach), `DrillCatalog` (concrete practice items)

### SenseSation.Data
Data source implementations, all behind Core interfaces.
- `HenrikDev/` — `HenrikDevClient` (HTTP via `IHttpClientFactory`, needs API key). Works without game running.
- `Radiant/` — `RadiantConnectClient` (ILiveClient), `RadiantMatchSource` (IMatchDataSource from local Riot), `RiotSession` (lockfile auth + auto-reauth on BAD_CLAIMS). Needs Valorant running.
- `Storage/` — `SqliteMatchStore` (Dapper + SQLite for RR history, cached matches).
- `Json/` — `JsonExt` deserialization helpers.

### SenseSation.Capture
Screen recording + VOD review. Uses ffmpeg and OpenCV.
- `ScreenRecorder` — ffmpeg process wrapper
- `EngagementDetector` — OpenCV-based action detection
- `VodReviewService` — orchestrates detect → clip export
- `RoundReviewService` — per-round clip assembly
- `CaptureOptions` — ffmpeg path, clip pre/post seconds, output dir

### SenseSation.Web
Blazor Server dashboard. InteractiveServer render mode.
- `Program.cs` — DI wiring, standalone port (5080), `/media/` streaming endpoint with range support
- `Services/` — `AppData` (per-circuit cache facade, falls back to SQLite on API failure), `MatchSourceRouter` (routes between HenrikDev/Radiant per-call), `SettingsStore` (persists account + API key to `%LOCALAPPDATA%\SenseSation\settings.json`), `AgentInfo` (agent name → icon mapping)
- `Components/Pages/` — `Home` (dashboard), `Matches`, `MatchView`, `Trainer` (Smart Trainer), `RankPage` (RR chart), `Live` (live lobby with auto-poll + career viewer), `AgentPicker` (pre-game agent select), `Vod`, `Settings`
- `Components/Shared/` — `RankPill`, `StatCard`, `BenchmarkBar`, `RrChart`, `TeamTable`, `StateBanner`, `Ring`, `Tile`, `Icon`, `Ring`
- `wwwroot/app.css` — All styles (no CSS framework)

### SenseSation.Tests
xUnit tests. Only `TrainerEngineTests` and `RankTableTests` currently.

## Key Data Flow

### Match data
```
IMatchDataSource (HttpClientHenrikDev or RadiantMatchSource via Router)
  → AppData (scoped, caches per-circuit)
    → SqliteMatchStore (fallback when API fails)
    → MetricsCalculator + TrainerEngine (on each load)
```

### Live lobby (always local Riot client, independent of data source mode)
```
RiotSession (lockfile auth, auto-reauth)
  → RadiantConnectClient (ILiveClient)
    → Live.razor (polls every 4s, shows both teams' ranks, inline career viewer)
    → AgentPicker.razor (pre-game agent select + lock via ILiveClient)
```

### VOD review
```
ScreenRecorder (ffmpeg) → EngagementDetector (OpenCV) → VodReviewService (clip export)
```

## Two Data-Source Modes

- **Mode A (no key)** — `RadiantMatchSource` reads the signed-in account via local Riot client. Game must be running.
- **Mode B (HenrikDev key)** — `HenrikDevClient` accesses any public account via HTTP. Game not needed.

Switched at runtime by `MatchSourceRouter` which checks `SettingsStore.HasHenrikKey` per-call. No restart needed. Mode B activated by setting the key in Settings UI or via `dotnet user-secrets`.

## Important Patterns & Constraints

- `MatchSourceRouter` implements `IMatchDataSource` — delegates to HenrikDev or RadiantMatchSource per-call based on whether a key is present
- `RiotSession.ExecuteAsync` retries once on auth expiry (BAD_CLAIMS, "validating/decoding RSO Access Token") by invalidating the session and re-authenticating via lockfile
- `AppData.LoadAsync` catches API exceptions and falls back to `SqliteMatchStore` transparently — dashboard stays usable offline
- `TrainerEngine` never reports strengths, only weaknesses. The coach is deliberately critical.
- `PublishSingleFile` must keep `InvariantGlobalization=false` — RadiantConnect creates `CultureInfo("en-us")` in a static initializer
- Appsettings keys: `Henrik:ApiKey`, `Account:Region`, `Account:Name`, `Account:Tag`, `Capture:FfmpegPath`, `Capture:OutputDirectory`

## ToS Boundary

This app only reads account metadata (names, ranks) from the local Riot client API, never game memory or live positional/economy state. No in-match coaching. The VOD Review analyzes recordings after the game ends. Do not add features that cross this line.
