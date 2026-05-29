# SenseSation

A VALORANT **personal analytics + self-coaching** desktop dashboard. It pulls your own
match history and rank, scores your play against high-elo benchmarks, suggests concrete
drills, shows live lobby ranks while you're in a game, and turns your own screen recordings
into a clip-by-clip fight review.

It is an **informer for your own improvement** — not a cheat. See the boundary below.

---

## What it does

| Feature | What you get | Source |
|---|---|---|
| **Dashboard** | Rank, win rate, K/D, HS%, RR trend, coach's headline | HenrikDev |
| **Matches** | Competitive match history, click through to full scoreboards | HenrikDev |
| **Match detail** | Both teams' scoreboard, your per-round buy pattern + econ discipline | HenrikDev |
| **Smart Trainer** | Your metrics vs *Good*/*Radiant* benchmarks, ranked weaknesses, a focused practice plan | computed |
| **Rank Tracker** | RR-over-time graph, game-by-game RR deltas (persisted locally, so it outlives the API window) | HenrikDev + SQLite |
| **Live Lobby** | Teammate & enemy **ranks** for the current match (metadata only) | RadiantConnect (local client) |
| **VOD Review** | Record your screen, auto-detect the action, get short review clips of each fight | ffmpeg + OpenCV |

## Boundary (read this)

SenseSation only ever surfaces:
- **Your own** match/stat data (public HenrikDev API).
- **Account metadata** for the current lobby — names and ranks — the same thing in-client
  trackers (Blitz, Tracker.gg) already show. Read from the **local Riot client API**, never
  game memory.
- **Your own screen recordings**, analyzed **after** the game.

It deliberately does **not** read live enemy positions, economy, or any in-round state, does
not inject into or read the game process, and does no live in-match coaching. That line is
what keeps it ToS-safe and unbannable. Don't move it.

---

## Setup

Requires the **.NET 8 SDK** (to build) and **ffmpeg** on `PATH` (only the VOD Review feature needs ffmpeg).

There are two data-source modes — **no API key is required**:

### Mode A — No key (default): your own Riot client

Just start Valorant, then run the app. It reads *your* signed-in account's stats straight from
Riot's API through the local client session — zero external API, zero signup. The tracked account
is whoever is logged in. (Requires Valorant/Riot Client running; may require running as
administrator, same as Live Lobby.)

```bash
dotnet run --project src/SenseSation.Web
```

### Mode B — HenrikDev key (optional): any account, game not required

Add a free HenrikDev key and the app analyzes any public account without the game running. Store
it with user-secrets so it never lands in source control:

```bash
cd src/SenseSation.Web
dotnet user-secrets set "Henrik:ApiKey" "YOUR_KEY"
```

Then open **Settings**, enter region / Riot name / tag, and **Save & Load**.

The app picks Mode B automatically when a key is present, otherwise Mode A. The Live Lobby feature
always uses the local client and is independent of which mode is active.

---

## Standalone .exe (no SDK needed to run)

Build a self-contained single-file Windows build:

```powershell
powershell -File build-dist.ps1
# or:  dotnet publish src/SenseSation.Web -c Release -r win-x64 -p:PublishSingleFile=true -o dist
```

Output is `dist\SenseSation.exe` (~84 MB, bundles the .NET runtime + SQLite/OpenCV natives —
the target PC needs nothing installed except **ffmpeg** if you use VOD Review).

Running it binds `http://localhost:5080` and opens your browser automatically. Set the API key
either way:

- edit `Henrik.ApiKey` in `dist\appsettings.json`, **or**
- set an environment variable: `setx HENRIK__APIKEY "YOUR_KEY"`

Then open **Settings** and enter your Riot ID.

## Commands

```bash
dotnet build SenseSation.sln          # build everything
dotnet run --project src/SenseSation.Web   # run the dashboard
dotnet test                           # run unit tests
```

## Architecture

```
SenseSation.Core      domain models, abstractions (IMatchDataSource, ILiveClient,
                      IMatchStore), Radiant benchmarks, MetricsCalculator, TrainerEngine
SenseSation.Data      HenrikDevClient (HTTP)  ·  RadiantConnectClient (local)  ·  SqliteMatchStore
SenseSation.Capture   ScreenRecorder (ffmpeg)  ·  EngagementDetector (OpenCV)  ·  VodReviewService
SenseSation.Web       Blazor Server dashboard (interactive), AppData facade, /media streaming
SenseSation.Tests     xUnit
```

Data flows through interfaces in `Core`, so the remote source (HenrikDev) and the live client
(RadiantConnect) are swappable. `AppData` (scoped) caches a load per circuit and falls back to
the local SQLite store when the API is unavailable, so the dashboard still works offline.
