using System;
using System.Net.Http;
using System.Threading.Tasks;
using SenseSation.Core.Abstractions;
using SenseSation.Data.HenrikDev;
using SenseSation.Data.Radiant;
using SenseSation.Data.Storage;

namespace SenseSation.Desktop.Services;

/// <summary>Manual composition root — wires the existing .NET backend for the desktop app.</summary>
public static class Bootstrap
{
    public static SettingsStore Settings { get; private set; } = null!;
    public static AppData Data { get; private set; } = null!;
    public static ILiveClient Live { get; private set; } = null!;

    private static HttpClient? _http;

    public static async Task InitAsync()
    {
        Settings = new SettingsStore();
        var henrikOpts = new HenrikOptions { ApiKey = Settings.HenrikApiKey };
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        var henrik = new HenrikDevClient(_http, henrikOpts);

        var session = new RiotSession();
        Live = new RadiantConnectClient(session);
        var radiant = new RadiantMatchSource(session);
        var router = new MatchSourceRouter(Settings, henrik, radiant);

        var store = new SqliteMatchStore(new SqliteStoreOptions());
        await store.InitializeAsync();

        Data = new AppData(router, store, Settings, henrikOpts);
    }
}
