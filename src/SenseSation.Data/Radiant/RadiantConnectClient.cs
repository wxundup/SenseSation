using RadiantConnect;
using SenseSation.Core.Abstractions;
using SenseSation.Core.Models;
using RC = RadiantConnect.Network.PVPEndpoints.DataTypes;

namespace SenseSation.Data.Radiant;

/// <summary>
/// <see cref="ILiveClient"/> backed by RadiantConnect, reading the LOCAL Riot
/// client. Surfaces the current lobby's players, teams and competitive ranks —
/// account metadata only, exactly what in-client trackers already show. Never
/// reads positions, economy, or any live in-round state.
///
/// Only functional on a machine where Valorant/Riot Client is running; every
/// call fails soft (returns null/false) when the client is absent.
/// </summary>
public sealed class RadiantConnectClient(RiotSession session) : ILiveClient
{
    private readonly RiotSession _session = session;

    private static readonly string[] TierNames =
        ["tierafterupdate", "competitivetier", "currenttier", "tier"];

    public bool IsConnected => _session.IsConnected;
    public string? LastError => _session.LastError;
    public string? SelfPuuid => _session.SelfPuuid;

    public LiveEnvironment DetectEnvironment() => _session.DetectEnvironment();

    public Task<bool> ConnectAsync(CancellationToken ct = default) => _session.EnsureConnectedAsync(ct);

    public async Task<LiveLobby?> GetCurrentLobbyAsync(CancellationToken ct = default)
    {
        if (!await _session.EnsureConnectedAsync(ct)) return null;

        try
        {
            // CoreGame (INGAME) exposes both teams; this is where enemy ranks are visible.
            // Routed through ExecuteAsync so an expired access token re-auths and retries.
            var match = await _session.ExecuteAsync(
                init => init.Endpoints.CurrentGameEndpoints.GetCurrentGameMatchAsync(), ct);
            if (match is null)
            {
                _session.SetError("Connected, but no in-game match found yet. Enemy ranks appear once the round starts (not during agent select).");
                return null;
            }

            string self = SelfPuuid ?? "";
            var rawPlayers = ReflectionHelpers.AsEnumerable(
                match.GetType().GetProperty("Players")?.GetValue(match)).ToList();
            if (rawPlayers.Count == 0) return null;

            var puuids = rawPlayers
                .Select(p => ReflectionHelpers.GetString(p, "Subject") ?? "")
                .Where(s => s.Length > 0).ToArray();
            var nameMap = await ResolveNamesAsync(puuids, ct);

            string? selfTeam = rawPlayers
                .FirstOrDefault(p => ReflectionHelpers.GetString(p, "Subject") == self) is { } meRow
                ? ReflectionHelpers.GetString(meRow, "TeamId")
                : null;

            var summaries = new List<PlayerSummary>();
            foreach (var p in rawPlayers)
            {
                string puuid = ReflectionHelpers.GetString(p, "Subject") ?? "";
                string teamId = ReflectionHelpers.GetString(p, "TeamId") ?? "";
                int tier = await FetchTierAsync(puuid, ct);
                nameMap.TryGetValue(puuid, out var nt);

                summaries.Add(new PlayerSummary
                {
                    Puuid = puuid,
                    Name = nt.name ?? "",
                    Tag = nt.tag ?? "",
                    Agent = "",
                    Team = teamId.Equals("Red", StringComparison.OrdinalIgnoreCase) ? Team.Red : Team.Blue,
                    Rank = RankTable.Make(tier, 0),
                    IsSelf = puuid == self,
                });
            }

            Team selfTeamEnum = (selfTeam ?? "").Equals("Red", StringComparison.OrdinalIgnoreCase)
                ? Team.Red : Team.Blue;
            var allies = summaries.Where(s =>
                selfTeam is null ? s.IsSelf : s.Team == selfTeamEnum).ToList();
            var enemies = summaries.Except(allies).ToList();
            if (allies.Count == 0) { allies = summaries; enemies = []; }

            _session.SetError(null);
            return new LiveLobby
            {
                MatchId = ReflectionHelpers.GetString(match, "MatchId") ?? "",
                Map = ReflectionHelpers.GetString(match, "MapId") ?? "",
                Mode = ReflectionHelpers.GetString(match, "ModeId") ?? "",
                Phase = "INGAME",
                Allies = allies,
                Enemies = enemies,
            };
        }
        catch (Exception ex)
        {
            _session.SetError($"Lobby read failed: {ex.Message}");
            return null;
        }
    }

    private async Task<int> FetchTierAsync(string puuid, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(puuid)) return 0;
        try
        {
            RC.PlayerMMR? mmr = await _session.ExecuteAsync(
                init => init.Endpoints.PvpEndpoints.FetchPlayerMMRAsync(puuid), ct);
            int tier = (int)(mmr?.LatestCompetitiveUpdate?.TierAfterUpdate ?? 0);
            // Fall back to the seasonal competitive tier when the latest update is unrated (0).
            if (tier == 0) tier = ReflectionHelpers.FindInt(mmr, TierNames) ?? 0;
            return tier;
        }
        catch { return 0; }
    }

    private async Task<Dictionary<string, (string? name, string? tag)>> ResolveNamesAsync(
        string[] puuids, CancellationToken ct)
    {
        var map = new Dictionary<string, (string?, string?)>();
        if (puuids.Length == 0) return map;
        try
        {
            var names = await _session.ExecuteAsync(
                init => init.Endpoints.PvpEndpoints.FetchNameServiceReturn(puuids), ct);
            foreach (var n in ReflectionHelpers.AsEnumerable(names))
            {
                string? id = ReflectionHelpers.GetString(n, "Subject", "Puuid");
                if (id is null) continue;
                map[id] = (ReflectionHelpers.GetString(n, "GameName", "Name"),
                           ReflectionHelpers.GetString(n, "TagLine", "Tag"));
            }
        }
        catch { /* names are best-effort */ }
        return map;
    }
}
