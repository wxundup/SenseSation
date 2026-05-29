using System.Diagnostics;
using SenseSation.Capture;
using SenseSation.Core.Abstractions;
using SenseSation.Data.HenrikDev;
using SenseSation.Data.Radiant;
using SenseSation.Data.Storage;
using SenseSation.Web.Components;
using SenseSation.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ---- Options bound from configuration / user-secrets --------------------------
var henrik = builder.Configuration.GetSection(HenrikOptions.Section).Get<HenrikOptions>() ?? new();
var storage = builder.Configuration.GetSection(SqliteStoreOptions.Section).Get<SqliteStoreOptions>() ?? new();
var capture = builder.Configuration.GetSection(CaptureOptions.Section).Get<CaptureOptions>() ?? new();
builder.Services.AddSingleton(henrik);
builder.Services.AddSingleton(storage);
builder.Services.AddSingleton(capture);

// ---- Shared local Riot session (lockfile auth) -------------------------------
builder.Services.AddSingleton<RiotSession>();

// ---- Match data source --------------------------------------------------------
// Routed at runtime: HenrikDev when a key is set (any account, no game needed),
// otherwise the local Riot client (no key). Switching is live — see MatchSourceRouter.
builder.Services.AddHttpClient<HenrikDevClient>(c => c.Timeout = TimeSpan.FromSeconds(25));
builder.Services.AddScoped(sp => new RadiantMatchSource(sp.GetRequiredService<RiotSession>()));
builder.Services.AddScoped<IMatchDataSource, MatchSourceRouter>();

// ---- Local persistence + live client + capture -------------------------------
builder.Services.AddSingleton<IMatchStore, SqliteMatchStore>();
builder.Services.AddSingleton<ILiveClient, RadiantConnectClient>();
builder.Services.AddSingleton<ScreenRecorder>();
builder.Services.AddSingleton<EngagementDetector>();
builder.Services.AddSingleton<VodReviewService>();
builder.Services.AddSingleton<RoundReviewService>();

// ---- App services ------------------------------------------------------------
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddScoped<AppData>();

// When launched as a standalone exe (no host-provided URL), bind a predictable
// local port so the desktop experience is consistent.
bool standalone = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
                  && !builder.Environment.IsDevelopment();
if (standalone)
    builder.WebHost.UseUrls("http://localhost:5080");

var app = builder.Build();

// Seed the live HenrikOptions key from persisted settings so the saved key
// (set via the Settings UI) is in effect on startup.
henrik.ApiKey = app.Services.GetRequiredService<SettingsStore>().HenrikApiKey;

// Ensure the SQLite schema exists before serving requests.
await app.Services.GetRequiredService<IMatchStore>().InitializeAsync();

// Standalone: pop the dashboard in the default browser once Kestrel is listening.
if (standalone)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var url = app.Urls.FirstOrDefault() ?? "http://localhost:5080";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* headless / no browser — the URL is printed to the console anyway */ }
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Streams recordings/clips from the capture output directory (range-enabled for seeking).
// Path traversal is blocked by verifying the resolved file stays under the output root.
app.MapGet("/media/{*relativePath}", (string relativePath, CaptureOptions opt) =>
{
    var root = Path.GetFullPath(opt.OutputDirectory);
    var full = Path.GetFullPath(Path.Combine(root, relativePath));
    if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        return Results.NotFound();
    return Results.File(full, "video/mp4", enableRangeProcessing: true);
});

app.Run();
