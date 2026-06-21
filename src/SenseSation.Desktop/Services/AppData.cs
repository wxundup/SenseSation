using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SenseSation.Core.Abstractions;
using SenseSation.Core.Models;
using SenseSation.Core.Training;
using SenseSation.Data.HenrikDev;

namespace SenseSation.Desktop.Services;

/// <summary>
/// Loads stats once, caches in memory, falls back to the local store when the API fails.
/// UI view-models read these properties and call <see cref="LoadAsync"/>.
/// </summary>
public sealed class AppData(IMatchDataSource source, IMatchStore store, SettingsStore settings, HenrikOptions henrikOptions)
{
    private readonly IMatchDataSource _source = source;
    private readonly IMatchStore _store = store;
    private readonly SettingsStore _settings = settings;
    private readonly HenrikOptions _henrikOptions = henrikOptions;

    public RiotId Account => _settings.Current;
    public string SourceName => _source.SourceName;
    public bool SourceNeedsRiotId => _source.NeedsRiotId;
    public bool IsConfigured => !_source.NeedsRiotId || _settings.IsConfigured;

    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }
    public bool ServedFromCache { get; private set; }

    public IReadOnlyList<MatchSummary> Matches { get; private set; } = [];
    public Rank? Rank { get; private set; }
    public PlayerMetrics Metrics { get; private set; } = new();
    public TrainerReport? Report { get; private set; }
    public IReadOnlyList<RrSnapshot> RrHistory { get; private set; } = [];
    public bool HasData => Matches.Count > 0;

    public event Action? Changed;

    public async Task LoadAsync(int count = 20, CancellationToken ct = default)
    {
        if (!IsConfigured) { Error = "Set your Riot ID (region, name, tag) to load stats."; Changed?.Invoke(); return; }

        IsLoading = true; Error = null; ServedFromCache = false; Changed?.Invoke();
        var id = _settings.Current;
        try
        {
            var matches = await _source.GetRecentMatchesAsync(id, count, ct);
            if (matches.Count > 0) { await _store.UpsertMatchesAsync(id, matches, ct); Matches = matches; }
            else { Matches = await _store.GetMatchesAsync(id, count, ct); ServedFromCache = Matches.Count > 0; }

            Rank = await _source.GetRankAsync(id, ct);

            var rrApi = await _source.GetMmrHistoryAsync(id, ct);
            if (rrApi.Count > 0) await _store.AppendRrAsync(id, rrApi, ct);
            RrHistory = await _store.GetRrHistoryAsync(id, ct);
            if (RrHistory.Count == 0) RrHistory = rrApi;
        }
        catch (Exception ex)
        {
            Matches = await Safe(() => _store.GetMatchesAsync(id, count, ct), Matches);
            RrHistory = await Safe(() => _store.GetRrHistoryAsync(id, ct), RrHistory);
            ServedFromCache = Matches.Count > 0;
            Error = ServedFromCache ? $"Showing cached data — live fetch failed: {ex.Message}" : ex.Message;
        }
        finally
        {
            Metrics = MetricsCalculator.Compute(Matches);
            Report = TrainerEngine.Analyze(Metrics);
            IsLoading = false;
            Changed?.Invoke();
        }
    }

    public Task<MatchDetail?> GetMatchAsync(string matchId, CancellationToken ct = default)
        => _source.GetMatchAsync(_settings.Current.Region, matchId, _settings.Current, ct);

    public void UpdateAccount(RiotId id) { _settings.SaveAccount(id); Reset(); }

    public void UpdateHenrikKey(string key)
    {
        _settings.SaveHenrikKey(key);
        _henrikOptions.ApiKey = _settings.HenrikApiKey;
        Reset();
    }

    private void Reset()
    {
        Matches = []; Rank = null; Report = null; RrHistory = []; Metrics = new();
        Changed?.Invoke();
    }

    private static async Task<T> Safe<T>(Func<Task<T>> fn, T fallback)
    {
        try { return await fn(); } catch { return fallback; }
    }
}
