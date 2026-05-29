using SenseSation.Core.Abstractions;
using SenseSation.Core.Models;
using SenseSation.Core.Training;
using SenseSation.Data.HenrikDev;

namespace SenseSation.Web.Services;

/// <summary>
/// Per-circuit facade over the remote data source, local store, and trainer.
/// Loads once, caches in memory for snappy navigation, and falls back to the
/// local store when the API is unavailable so the dashboard still works offline.
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

    /// <summary>Ready to load: either the source reads the local client, or a Riot ID is set.</summary>
    public bool IsConfigured => !_source.NeedsRiotId || _settings.IsConfigured;

    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }
    public bool ServedFromCache { get; private set; }
    public DateTimeOffset? LoadedAt { get; private set; }

    public IReadOnlyList<MatchSummary> Matches { get; private set; } = [];
    public Rank? Rank { get; private set; }
    public PlayerMetrics Metrics { get; private set; } = new();
    public TrainerReport? Report { get; private set; }
    public IReadOnlyList<RrSnapshot> RrHistory { get; private set; } = [];

    public bool HasData => Matches.Count > 0;

    /// <summary>Raised after data loads or resets, so shared UI (e.g. the topbar) can refresh.</summary>
    public event Action? Changed;

    public async Task LoadAsync(int count = 20, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            Error = "Set your Riot ID (region, name, tag) to load stats.";
            return;
        }

        IsLoading = true;
        Error = null;
        ServedFromCache = false;
        var id = _settings.Current;

        try
        {
            var matches = await _source.GetRecentMatchesAsync(id, count, ct);
            if (matches.Count > 0)
            {
                await _store.UpsertMatchesAsync(id, matches, ct);
                Matches = matches;
            }
            else
            {
                Matches = await _store.GetMatchesAsync(id, count, ct);
                ServedFromCache = Matches.Count > 0;
            }

            Rank = await _source.GetRankAsync(id, ct);

            var rrApi = await _source.GetMmrHistoryAsync(id, ct);
            if (rrApi.Count > 0) await _store.AppendRrAsync(id, rrApi, ct);
            RrHistory = await _store.GetRrHistoryAsync(id, ct);
            if (RrHistory.Count == 0) RrHistory = rrApi;

            LoadedAt = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            // API failed — serve whatever the local store has so the app stays usable.
            Matches = await SafeAsync(() => _store.GetMatchesAsync(id, count, ct), Matches);
            RrHistory = await SafeAsync(() => _store.GetRrHistoryAsync(id, ct), RrHistory);
            ServedFromCache = Matches.Count > 0;
            Error = ServedFromCache
                ? $"Showing cached data — live fetch failed: {ex.Message}"
                : ex.Message;
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

    public void UpdateAccount(RiotId id)
    {
        _settings.SaveAccount(id);
        ResetCache();
    }

    /// <summary>Sets (or clears) the HenrikDev key, persists it, and applies it live to the running client.</summary>
    public void UpdateHenrikKey(string key)
    {
        _settings.SaveHenrikKey(key);
        _henrikOptions.ApiKey = _settings.HenrikApiKey; // takes effect immediately, no restart
        ResetCache();
    }

    private void ResetCache()
    {
        Matches = [];
        Rank = null;
        Report = null;
        RrHistory = [];
        Metrics = new();
        LoadedAt = null;
        Changed?.Invoke();
    }

    private static async Task<T> SafeAsync<T>(Func<Task<T>> fn, T fallback)
    {
        try { return await fn(); } catch { return fallback; }
    }
}
