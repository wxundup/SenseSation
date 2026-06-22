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
    [ObservableProperty] private string _iconAsset = ""; // asset path e.g. "agents/Reyna"; empty = use Glyph
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
        Pages.Add(new InsightsVm());
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

    private readonly MatchDetailVm _detail = new();
    private PageVm? _backTo;

    [RelayCommand]
    private async Task ShowMatch(string? matchId)
    {
        if (string.IsNullOrEmpty(matchId)) return;
        _backTo = Current;
        foreach (var p in Pages) p.IsActive = false;
        Current = _detail;
        await _detail.Load(matchId);
    }

    [RelayCommand]
    private void Back() => Navigate(_backTo ?? Pages[0]);

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
    [ObservableProperty] private string _session = "";
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
        Session = BuildSession(d);
        Recent.Clear();
        foreach (var x in d.Matches.Take(6)) Recent.Add(x);
        Weaknesses.Clear();
        foreach (var w in d.Report?.Weaknesses ?? []) Weaknesses.Add(w);
        Plan.Clear();
        foreach (var p in d.Report?.Plan ?? []) Plan.Add(p);
    }

    // "Today: 2W · 1L · +18 RR" — only the games played since local midnight.
    private static string BuildSession(AppData d)
    {
        var today = DateTimeOffset.Now.LocalDateTime.Date;
        var games = d.Matches.Where(x => x.StartedAt.LocalDateTime.Date == today).ToList();
        if (games.Count == 0) return "No games today yet";
        int w = games.Count(x => x.Result == MatchResult.Win);
        int l = games.Count(x => x.Result == MatchResult.Loss);
        int rr = d.RrHistory.Where(s => s.At.LocalDateTime.Date == today).Sum(s => s.RrDelta);
        return $"Today: {w}W · {l}L · {(rr >= 0 ? "+" : "")}{rr} RR";
    }

    [RelayCommand] private async Task Refresh() => await Data.LoadAsync();
}

// ---- Matches ----------------------------------------------------------------
public partial class MatchesVm : PageVm
{
    public override string Title => "Matches";
    public override string Glyph => "⚔";
    public ObservableCollection<MatchSummary> Items { get; } = [];
    [ObservableProperty] private string _filter = "All"; // All / Wins / Losses

    public MatchesVm() => Data.Changed += () => Dispatcher.UIThread.Post(Sync);
    public override Task OnShownAsync() { Sync(); return Task.CompletedTask; }
    private void Sync()
    {
        Items.Clear();
        foreach (var m in Data.Matches.Where(Keep)) Items.Add(m);
    }
    private bool Keep(MatchSummary m) => Filter switch
    {
        "Wins" => m.Result == MatchResult.Win,
        "Losses" => m.Result == MatchResult.Loss,
        _ => true,
    };
    [RelayCommand] private void SetFilter(string f) { Filter = f; Sync(); }
    [RelayCommand] private async Task Refresh() => await Data.LoadAsync();
}

// ---- Insights ---------------------------------------------------------------
/// <summary>One aggregated row (a map or an agent) for the Insights breakdowns.</summary>
public sealed record StatRow(string Name, int Games, double WinPct, double Kd)
{
    private static readonly Avalonia.Media.IBrush Good = Avalonia.Media.Brush.Parse("#1FD18E");
    private static readonly Avalonia.Media.IBrush Bad = Avalonia.Media.Brush.Parse("#FF4655");
    public string GamesLabel => Games == 1 ? "1 game" : $"{Games} games";
    public string WinLabel => $"{WinPct:0}%";
    public string KdLabel => $"{Kd:0.00} K/D";
    public Avalonia.Media.IBrush WinBrush => WinPct >= 50 ? Good : Bad;
}

public partial class InsightsVm : PageVm
{
    public override string Title => "Insights";
    public override string Glyph => "▦";
    public ObservableCollection<StatRow> Maps { get; } = [];
    public ObservableCollection<StatRow> Agents { get; } = [];
    [ObservableProperty] private string _summary = "";

    public InsightsVm() => Data.Changed += () => Dispatcher.UIThread.Post(Sync);
    public override Task OnShownAsync() { Sync(); return Task.CompletedTask; }
    private void Sync()
    {
        Maps.Clear(); Agents.Clear();
        foreach (var r in Group(Data.Matches, m => m.Map)) Maps.Add(r);
        foreach (var r in Group(Data.Matches, m => m.Agent)) Agents.Add(r);
        Summary = Data.Matches.Count == 0
            ? "No matches loaded yet — set your account in Settings."
            : $"Across {Data.Matches.Count} matches — where you win and where you don't.";
    }

    // Group matches by map/agent → games, win%, avg K/D. Most-played first.
    private static IEnumerable<StatRow> Group(IEnumerable<MatchSummary> ms, Func<MatchSummary, string> key) =>
        ms.Where(m => !string.IsNullOrWhiteSpace(key(m)))
          .GroupBy(key)
          .Select(g => new StatRow(g.Key, g.Count(),
              100.0 * g.Count(x => x.Result == MatchResult.Win) / g.Count(),
              Math.Round(g.Average(x => x.You.Kd), 2)))
          .OrderByDescending(r => r.Games).ThenByDescending(r => r.WinPct)
          .ToList();

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

    // Active filter chip: accent when the bound value equals ConverterParameter, else flat.
    public static readonly Avalonia.Data.Converters.IValueConverter EqAccent = new EqAccentConv();
    private sealed class EqAccentConv : Avalonia.Data.Converters.IValueConverter
    {
        public object Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => string.Equals(v as string, p as string, StringComparison.Ordinal) ? Sel : Unsel;
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => Avalonia.Data.BindingOperations.DoNothing;
    }

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

    // Loads any bundled asset by relative path (e.g. "agents/Reyna", "icons/brain").
    public static readonly Avalonia.Data.Converters.IValueConverter AssetBitmap =
        new Avalonia.Data.Converters.FuncValueConverter<string?, Avalonia.Media.Imaging.Bitmap?>(Load);

    public static readonly Avalonia.Data.Converters.IValueConverter BuyBrush =
        new Avalonia.Data.Converters.FuncValueConverter<BuyType, Avalonia.Media.IBrush>(b => b switch
        {
            BuyType.FullBuy => Avalonia.Media.Brush.Parse("#1FD18E"),
            BuyType.ForceBuy => Avalonia.Media.Brush.Parse("#FFCB6B"),
            BuyType.Eco => Avalonia.Media.Brush.Parse("#46586A"),
            _ => Avalonia.Media.Brush.Parse("#28333D"),
        });

    public static readonly Avalonia.Data.Converters.IValueConverter SelfBg =
        new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(
            self => self ? Avalonia.Media.Brush.Parse("#22FF4655") : Avalonia.Media.Brushes.Transparent);

    private static Avalonia.Media.Imaging.Bitmap? Load(string? rel)
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

    public TrainerVm() { IconAsset = "icons/brain"; Data.Changed += () => Dispatcher.UIThread.Post(Sync); }
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
    [ObservableProperty] private string _rr = "—";
    [ObservableProperty] private int _tier;
    [ObservableProperty] private string _netRr = "+0 RR";
    [ObservableProperty] private IReadOnlyList<double> _rrPoints = [];
    public ObservableCollection<RrSnapshot> History { get; } = [];

    public RankVm() => Data.Changed += () => Dispatcher.UIThread.Post(Sync);
    public override Task OnShownAsync() { Sync(); return Task.CompletedTask; }
    private void Sync()
    {
        var rk = Data.Rank ?? SenseSation.Core.Models.Rank.Unranked;
        Rank = rk.Name;
        Tier = rk.Tier;
        Rr = rk.Tier > 0 ? $"{rk.Rr} RR" : "—";
        IconAsset = rk.Tier > 2 ? $"ranks/{rk.Tier}" : "";
        int net = Data.RrHistory.Sum(s => s.RrDelta);
        NetRr = $"{(net >= 0 ? "+" : "")}{net} RR";
        RrPoints = Data.RrHistory.Select(s => (double)s.LadderPoints).ToList();
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

// ---- Match detail -----------------------------------------------------------
public sealed record ScoreRow(string Name, string Agent, string RankName, int K, int D, int A, int Hs, int Adr, bool IsSelf, string Puuid)
{
    public string Kda => $"{K} / {D} / {A}";
}

// ---- Career (shared by Live lobby + Match detail) ---------------------------
/// <summary>Inline career popover state. Loads a player's public career from the local client.</summary>
public partial class CareerVm : ObservableObject
{
    [ObservableProperty][NotifyPropertyChangedFor(nameof(Empty))] private bool _open;
    [ObservableProperty] private string _name = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(Empty))] private bool _loading;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(Empty))] private PlayerCareer? _data;

    /// <summary>Open, finished loading, and nothing came back (e.g. local client not running).</summary>
    public bool Empty => Open && !Loading && Data is null;

    public async Task Show(string? puuid, string name)
    {
        if (string.IsNullOrEmpty(puuid)) return;
        Open = true; Loading = true; Data = null; Name = name;
        try { Data = await Bootstrap.Live.GetCareerAsync(puuid); }
        catch { /* needs the local Riot client; panel just shows no data */ }
        finally { Loading = false; }
    }

    [RelayCommand] private void Close() => Open = false;
}

public partial class MatchDetailVm : PageVm
{
    public override string Title => "Match";
    public override string Glyph => "‹";
    [ObservableProperty] private bool _loading;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _header = "";
    [ObservableProperty] private string _sub = "";
    [ObservableProperty] private string _yourLine = "";
    public ObservableCollection<ScoreRow> Red { get; } = [];
    public ObservableCollection<ScoreRow> Blue { get; } = [];
    public ObservableCollection<RoundEconomy> Economy { get; } = [];
    public CareerVm Career { get; } = new();

    [RelayCommand]
    private async Task ViewPlayer(ScoreRow? r)
    {
        if (r is null) return;
        await Career.Show(r.Puuid, r.Name);
    }

    public async Task Load(string matchId)
    {
        Loading = true; Error = ""; Header = "Loading…"; Sub = "";
        Red.Clear(); Blue.Clear(); Economy.Clear();
        try
        {
            var d = await Bootstrap.Data.GetMatchAsync(matchId);
            if (d is null) { Error = "Could not load this match."; Header = "Match"; return; }
            var s = d.Summary;
            Header = $"{s.Result.ToString().ToUpper()} · {s.Map}";
            Sub = $"{s.Agent} · {s.ScoreLabel}";
            YourLine = $"{s.You.Kills}/{s.You.Deaths}/{s.You.Assists}  ·  {s.You.Kd:0.00} K/D  ·  {s.You.DamagePerRound} ADR  ·  {s.You.HeadshotPct}% HS";
            foreach (var p in d.AllPlayers)
            {
                var l = d.Scoreboard.TryGetValue(p.Puuid, out var sc) ? sc : new Scoreline();
                var row = new ScoreRow(p.NameOrAgent, p.Agent, p.Rank.Name, l.Kills, l.Deaths, l.Assists, l.HeadshotPct, l.DamagePerRound, p.IsSelf, p.Puuid);
                (p.Team == Team.Red ? Red : Blue).Add(row);
            }
            foreach (var e in d.Economy) Economy.Add(e);
        }
        catch (Exception ex) { Error = ex.Message; Header = "Match"; }
        finally { Loading = false; }
    }
}

// ---- Live -------------------------------------------------------------------
public sealed record LivePlayer(string Name, string Rank, string Puuid);

public partial class LiveVm : PageVm
{
    public override string Title => "Live Lobby";
    public override string Glyph => "◉";
    public LiveVm() => IconAsset = "icons/monitor";
    [ObservableProperty] private string _status = "Not scanning";
    [ObservableProperty] private string _server = "";
    [ObservableProperty] private bool _busy;
    public CareerVm Career { get; } = new();
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
            foreach (var p in lobby.Allies) Allies.Add(new LivePlayer(p.NameOrAgent, p.Rank.Name, p.Puuid));
            foreach (var p in lobby.Enemies) Enemies.Add(new LivePlayer(p.NameOrAgent, p.Rank.Name, p.Puuid));
            Server = string.IsNullOrEmpty(lobby.Server) ? "" : $"Server: {lobby.Server}";
            Status = $"In a match · {lobby.Map}";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task ViewCareer(LivePlayer? p)
    {
        if (p is null) return;
        await Career.Show(p.Puuid, p.Name);
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
    public AgentsVm() => IconAsset = "agents/Reyna";
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
