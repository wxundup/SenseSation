using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SenseSation.Core.Abstractions;
using SenseSation.Core.Models;
using SenseSation.Core.Training;
using SenseSation.Data.Radiant;
using SenseSation.Desktop.Services;

namespace SenseSation.Desktop.ViewModels;

public abstract partial class PageVm : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Glyph { get; }
    [ObservableProperty] private bool _isActive;
    public virtual Task OnShownAsync() => Task.CompletedTask;
    protected static AppData Data => Bootstrap.Data;
    protected void OnUi(Action a) => Dispatcher.UIThread.Post(a);
}

public partial class MainWindowViewModel : ObservableObject
{
    public ObservableCollection<PageVm> Pages { get; } = [];
    [ObservableProperty] private PageVm? _current;
    [ObservableProperty] private string _account = "Loading…";
    [ObservableProperty] private string _rankText = "";
    [ObservableProperty] private int _rankTier;
    [ObservableProperty] private bool _ready;

    public string Version { get; } =
        "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.1.0");

    public async Task InitAsync()
    {
        await Bootstrap.InitAsync();
        OnUi(ApplyTheme);
        Bootstrap.Data.Changed += () => OnUi(() => { RefreshTopbar(); if (Bootstrap.Settings.UseRankTheme) ApplyTheme(); });

        Pages.Add(new DashboardVm());
        Pages.Add(new MatchesVm());
        Pages.Add(new TrainerVm());
        Pages.Add(new RankVm());
        Pages.Add(new LiveVm());
        Pages.Add(new AgentsVm());
        Pages.Add(new SettingsVm());

        OnUi(() =>
        {
            Ready = true;
            RefreshTopbar();
            Navigate(Pages[0]);
        });

        await Bootstrap.Data.LoadAsync();
    }

    [RelayCommand]
    private void Navigate(PageVm? page)
    {
        if (page is null) return;
        foreach (var p in Pages) p.IsActive = p == page;
        Current = page;
        _ = page.OnShownAsync();
    }

    private void RefreshTopbar()
    {
        var d = Bootstrap.Data;
        Account = d.SourceNeedsRiotId
            ? (d.IsConfigured ? $"{d.Account.Name}#{d.Account.Tag}" : "No account set")
            : "Local Client";
        var r = d.Rank ?? Rank.Unranked;
        RankText = r.Tier > 0 ? $"{r.Name} · {r.Rr} RR" : "Unranked";
        RankTier = r.Tier;
    }

    private void ApplyTheme()
    {
        if (Bootstrap.Settings.UseRankTheme)
            ThemeManager.ApplyRank((Bootstrap.Data.Rank ?? Rank.Unranked).Division);
        else
            ThemeManager.ApplyPreset(Bootstrap.Settings.Theme);
    }

    private static void OnUi(Action a) => Dispatcher.UIThread.Post(a);
}

// ---- Dashboard --------------------------------------------------------------
public partial class DashboardVm : PageVm
{
    public override string Title => "Dashboard";
    public override string Glyph => "▤";

    [ObservableProperty] private bool _loading;
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _rank = "Unranked";
    [ObservableProperty] private string _rr = "0 RR";
    [ObservableProperty] private int _tier;
    [ObservableProperty] private double _winRate;
    [ObservableProperty] private string _winLoss = "";
    [ObservableProperty] private string _kd = "0.00";
    [ObservableProperty] private string _adr = "0";
    [ObservableProperty] private string _hs = "0%";
    [ObservableProperty] private string _entries = "0";
    [ObservableProperty] private string _kast = "0%";
    [ObservableProperty] private string _topAgent = "—";
    public ObservableCollection<MatchSummary> Recent { get; } = [];
    public ObservableCollection<Weakness> Weaknesses { get; } = [];
    public ObservableCollection<Drill> Plan { get; } = [];

    public DashboardVm() => Data.Changed += () => Dispatcher.UIThread.Post(Sync);
    public override Task OnShownAsync() { Sync(); return Task.CompletedTask; }

    private void Sync()
    {
        var d = Data; var m = d.Metrics; var r = d.Rank ?? SenseSation.Core.Models.Rank.Unranked;
        Loading = d.IsLoading;
        Headline = d.Report?.Headline ?? (d.IsConfigured ? "Loading…" : "Set your Riot ID in Settings.");
        Rank = r.Name;
        Rr = r.Tier > 0 ? $"{r.Rr} RR" : "—";
        Tier = r.Tier;
        WinRate = m.WinRate;
        WinLoss = $"{m.Wins}W · {m.Losses}L";
        Kd = m.Kd.ToString("0.00");
        Adr = m.Adr.ToString("0");
        Hs = $"{m.HeadshotPct:0.#}%";
        Entries = m.FirstKillsPerMatch.ToString("0.#");
        Kast = $"{m.Kast:0}%";
        TopAgent = string.IsNullOrEmpty(m.TopAgent) ? "—" : m.TopAgent;
        Recent.Clear();
        foreach (var x in d.Matches.Take(6)) Recent.Add(x);
        Weaknesses.Clear();
        foreach (var w in d.Report?.Weaknesses ?? []) Weaknesses.Add(w);
        Plan.Clear();
        foreach (var p in d.Report?.Plan ?? []) Plan.Add(p);
    }

    [RelayCommand] private async Task Refresh() => await Data.LoadAsync();
}

// ---- Matches ----------------------------------------------------------------
public partial class MatchesVm : PageVm
{
    public override string Title => "Matches";
    public override string Glyph => "⚔";
    public ObservableCollection<MatchSummary> Items { get; } = [];

    public MatchesVm() => Data.Changed += () => Dispatcher.UIThread.Post(Sync);
    public override Task OnShownAsync() { Sync(); return Task.CompletedTask; }
    private void Sync() { Items.Clear(); foreach (var m in Data.Matches) Items.Add(m); }
    [RelayCommand] private async Task Refresh() => await Data.LoadAsync();
}

// ---- Trainer ----------------------------------------------------------------
public sealed class BenchRow
{
    public required string Label { get; init; }
    public required string Vals { get; init; }
    public double FillPct { get; init; }
    public Avalonia.Media.IBrush Brush { get; init; } = Avalonia.Media.Brushes.MediumPurple;
}

/// <summary>Tiny value converters for the views.</summary>
public static class Conv
{
    private static readonly Avalonia.Media.IBrush Sel = Avalonia.Media.Brush.Parse("#FF4655");
    private static readonly Avalonia.Media.IBrush Unsel = Avalonia.Media.Brush.Parse("#26333E");

    public static readonly Avalonia.Data.Converters.IValueConverter SelBorder =
        new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(b => b ? Sel : Unsel);

    // Win=green, Loss=red, Draw=gold
    public static readonly Avalonia.Data.Converters.IValueConverter ResultBrush =
        new Avalonia.Data.Converters.FuncValueConverter<MatchResult, Avalonia.Media.IBrush>(r =>
            r switch
            {
                MatchResult.Win => Avalonia.Media.Brush.Parse("#1FD18E"),
                MatchResult.Loss => Avalonia.Media.Brush.Parse("#FF4655"),
                _ => Avalonia.Media.Brush.Parse("#F5C451"),
            });

    private static readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap?> _iconCache = new();
    public static readonly Avalonia.Data.Converters.IValueConverter AgentIcon =
        new Avalonia.Data.Converters.FuncValueConverter<string?, Avalonia.Media.Imaging.Bitmap?>(LoadAgent);

    private static Avalonia.Media.Imaging.Bitmap? LoadAgent(string? name) => Load($"agents/{name}");

    public static readonly Avalonia.Data.Converters.IValueConverter RankIcon =
        new Avalonia.Data.Converters.FuncValueConverter<int, Avalonia.Media.Imaging.Bitmap?>(t => t > 2 ? Load($"ranks/{t}") : null);

    private static Avalonia.Media.Imaging.Bitmap? Load(string rel)
    {
        if (string.IsNullOrEmpty(rel)) return null;
        if (_iconCache.TryGetValue(rel, out var cached)) return cached;
        Avalonia.Media.Imaging.Bitmap? bmp = null;
        try
        {
            using var s = Avalonia.Platform.AssetLoader.Open(new Uri($"avares://SenseSation/Assets/{rel}.png"));
            bmp = new Avalonia.Media.Imaging.Bitmap(s);
        }
        catch { /* missing */ }
        _iconCache[rel] = bmp;
        return bmp;
    }
}

public partial class TrainerVm : PageVm
{
    public override string Title => "Smart Trainer";
    public override string Glyph => "◎";
    [ObservableProperty] private string _headline = "";
    public ObservableCollection<BenchRow> Bars { get; } = [];
    public ObservableCollection<Weakness> Weaknesses { get; } = [];
    public ObservableCollection<Drill> Plan { get; } = [];

    public TrainerVm() => Data.Changed += () => Dispatcher.UIThread.Post(Sync);
    public override Task OnShownAsync() { Sync(); return Task.CompletedTask; }

    private void Sync()
    {
        var r = Data.Report;
        Headline = r?.Headline ?? "";
        var weak = r?.Weaknesses.ToDictionary(w => w.Benchmark.Key, w => w.Severity) ?? new();
        Bars.Clear();
        foreach (var b in RadiantBenchmarks.All)
        {
            double v = Data.Metrics.ValueFor(b.Key);
            double max = Math.Max(Math.Max(b.Radiant, b.Good), v) * 1.1; if (max <= 0) max = 1;
            string color = weak.TryGetValue(b.Key, out var sev)
                ? sev switch { Severity.Major => "#FF5C7A", Severity.Notable => "#FFCB6B", _ => "#5AA0FF" }
                : "#3DDC97";
            Bars.Add(new BenchRow
            {
                Label = b.Label,
                Vals = b.Unit == "%" ? $"{v:0.#}% / {b.Good:0.#}%" : $"{v:0.##} / {b.Good:0.##}",
                FillPct = Math.Clamp(v / max * 100, 2, 100),
                Brush = Avalonia.Media.Brush.Parse(color),
            });
        }
        Weaknesses.Clear(); foreach (var w in r?.Weaknesses ?? []) Weaknesses.Add(w);
        Plan.Clear(); foreach (var p in r?.Plan ?? []) Plan.Add(p);
    }
    [RelayCommand] private async Task Refresh() => await Data.LoadAsync();
}

// ---- Rank -------------------------------------------------------------------
public partial class RankVm : PageVm
{
    public override string Title => "Rank Tracker";
    public override string Glyph => "▲";
    [ObservableProperty] private string _rank = "Unranked";
    [ObservableProperty] private string _netRr = "+0 RR";
    public ObservableCollection<RrSnapshot> History { get; } = [];

    public RankVm() => Data.Changed += () => Dispatcher.UIThread.Post(Sync);
    public override Task OnShownAsync() { Sync(); return Task.CompletedTask; }
    private void Sync()
    {
        Rank = Data.Rank?.Name ?? "Unranked";
        int net = Data.RrHistory.Sum(s => s.RrDelta);
        NetRr = $"{(net >= 0 ? "+" : "")}{net} RR";
        History.Clear();
        foreach (var s in Data.RrHistory.Reverse().Take(30)) History.Add(s);
    }
    [RelayCommand] private async Task Refresh() => await Data.LoadAsync();
}

// ---- Settings ---------------------------------------------------------------
public partial class SettingsVm : PageVm
{
    public override string Title => "Settings";
    public override string Glyph => "⚙";
    [ObservableProperty] private string _region = "eu";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _tag = "";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _theme = "Valorant Red";
    [ObservableProperty] private bool _useRankTheme;
    public string[] Regions { get; } = ["eu", "na", "ap", "kr", "latam", "br"];
    public string[] Themes { get; } = ThemeManager.Presets.Select(p => p.Name).ToArray();

    public override Task OnShownAsync()
    {
        Region = Data.Account.Region; Name = Data.Account.Name; Tag = Data.Account.Tag;
        ApiKey = Bootstrap.Settings.HenrikApiKey;
        Theme = Bootstrap.Settings.Theme;
        UseRankTheme = Bootstrap.Settings.UseRankTheme;
        Status = $"Mode: {Data.SourceName}";
        return Task.CompletedTask;
    }

    partial void OnThemeChanged(string value) => ApplyTheme();
    partial void OnUseRankThemeChanged(bool value) => ApplyTheme();

    private void ApplyTheme()
    {
        Bootstrap.Settings.SaveTheme(Theme, UseRankTheme);
        if (UseRankTheme) ThemeManager.ApplyRank((Data.Rank ?? Rank.Unranked).Division);
        else ThemeManager.ApplyPreset(Theme);
    }

    [RelayCommand]
    private async Task SaveKey()
    {
        Data.UpdateHenrikKey(ApiKey.Trim());
        Status = $"Mode: {Data.SourceName}";
        if (Data.IsConfigured) await Data.LoadAsync();
    }

    [RelayCommand]
    private async Task SaveAccount()
    {
        Data.UpdateAccount(new RiotId(Region, Name.Trim(), Tag.Trim().TrimStart('#')));
        await Data.LoadAsync();
    }
}

// ---- Live -------------------------------------------------------------------
public sealed class LivePlayer
{
    public required string Name { get; init; }
    public required string Rank { get; init; }
}

public partial class LiveVm : PageVm
{
    public override string Title => "Live Lobby";
    public override string Glyph => "◉";
    [ObservableProperty] private string _status = "Not scanning";
    [ObservableProperty] private bool _busy;
    public ObservableCollection<LivePlayer> Allies { get; } = [];
    public ObservableCollection<LivePlayer> Enemies { get; } = [];

    [RelayCommand]
    private async Task Scan()
    {
        Busy = true; Status = "Scanning…";
        try
        {
            if (!Bootstrap.Live.IsConnected && !await Bootstrap.Live.ConnectAsync())
            { Status = Bootstrap.Live.LastError ?? "No client."; return; }
            var lobby = await Bootstrap.Live.GetCurrentLobbyAsync();
            Allies.Clear(); Enemies.Clear();
            if (lobby is null) { Status = Bootstrap.Live.LastError ?? "No match found."; return; }
            foreach (var p in lobby.Allies) Allies.Add(new LivePlayer { Name = p.NameOrAgent, Rank = p.Rank.Name });
            foreach (var p in lobby.Enemies) Enemies.Add(new LivePlayer { Name = p.NameOrAgent, Rank = p.Rank.Name });
            Status = $"In a match · {lobby.Map}";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { Busy = false; }
    }
}

// ---- Agents -----------------------------------------------------------------
public partial class AgentCard : ObservableObject
{
    public required string Name { get; init; }
    public required string Role { get; init; }
    public string Stats { get; init; } = "";
    [ObservableProperty] private bool _selected;
}

public partial class AgentsVm : PageVm
{
    public override string Title => "Agent Picker";
    public override string Glyph => "◆";
    [ObservableProperty] private string _status = "Join a match";
    [ObservableProperty] private string? _selectedName;
    public ObservableCollection<AgentCard> Agents { get; } = [];

    private const int PreHoverMs = 3500, SettleMs = 600;
    private DispatcherTimer? _timer;
    private PreGameLobby? _pre;
    private string? _seenMatch;
    private DateTime? _seenAt;
    private bool _acting;

    public override Task OnShownAsync()
    {
        if (Agents.Count == 0) BuildGrid();
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        if (_timer.IsEnabled is false) { _timer.Tick += async (_, _) => await Poll(); _timer.Start(); }
        return Task.CompletedTask;
    }

    private void BuildGrid()
    {
        var matches = Data.Matches;
        foreach (var a in AgentInfo.All.OrderBy(a => a.Name))
        {
            var mine = matches.Where(m => m.Agent.Equals(a.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            string stats = mine.Count == 0 ? "" :
                $"{(int)Math.Round(100.0 * mine.Count(m => m.Result == MatchResult.Win) / mine.Count)}% · {mine.Count} games";
            Agents.Add(new AgentCard { Name = a.Name, Role = a.Role, Stats = stats });
        }
    }

    [RelayCommand]
    private void Pick(AgentCard card)
    {
        SelectedName = card.Name;
        foreach (var a in Agents) a.Selected = a == card;
        _seenMatch = null;
    }

    private async Task Poll()
    {
        try
        {
            if (!Bootstrap.Live.IsConnected) await Bootstrap.Live.ConnectAsync();
            _pre = await Bootstrap.Live.GetPreGameAsync();
            Status = _pre is { IsAgentSelect: true } ? $"Character select · {_pre.Map}" : "Waiting for agent select…";
            await Drive();
        }
        catch { }
    }

    private async Task Drive()
    {
        if (_acting || SelectedName is null || _pre is not { IsAgentSelect: true }) return;
        if (_seenMatch != _pre.MatchId) { _seenMatch = _pre.MatchId; _seenAt = DateTime.UtcNow; return; }
        if (_seenAt is { } t && (DateTime.UtcNow - t).TotalMilliseconds < PreHoverMs) return;

        var self = _pre.Allies.FirstOrDefault(a => a.Puuid == Bootstrap.Live.SelfPuuid);
        if (string.IsNullOrEmpty(self.Puuid) || self.Locked) return;
        string current = self.CharacterId is not null ? CharacterTable.Name(self.CharacterId) : "";

        _acting = true;
        try
        {
            if (!current.Equals(SelectedName, StringComparison.OrdinalIgnoreCase))
                await Bootstrap.Live.SelectAgentAsync(SelectedName);
            else { await Task.Delay(SettleMs); await Bootstrap.Live.LockAgentAsync(SelectedName); }
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { _acting = false; }
    }
}
