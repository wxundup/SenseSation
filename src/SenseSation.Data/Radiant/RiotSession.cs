using RadiantConnect;
using RadiantConnect.Authentication;
using SenseSation.Core.Models;

namespace SenseSation.Data.Radiant;

/// <summary>
/// Owns the connection to the LOCAL Riot client (lockfile auth) and the shared
/// <see cref="Initiator"/>. Both the live-lobby reader and the no-key match source
/// use this, so the user authenticates once via their own running client — no
/// external API key required.
/// </summary>
public sealed class RiotSession
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Initiator? Initiator { get; private set; }
    public bool IsConnected => Initiator is not null;
    public string? LastError { get; private set; }
    public string? SelfPuuid => TryGetSelfPuuid();

    public LiveEnvironment DetectEnvironment()
    {
        string lockfile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Riot Games", "Riot Client", "Config", "lockfile");

        return new LiveEnvironment
        {
            ValorantRunning = ProcessRunning("VALORANT-Win64-Shipping"),
            RiotClientRunning = ProcessRunning("RiotClientServices") || ProcessRunning("RiotClientUx"),
            LockfileExists = File.Exists(lockfile),
            LockfilePath = lockfile,
        };
    }

    /// <summary>Connects if not already connected. Thread-safe; fails soft with <see cref="LastError"/>.</summary>
    public async Task<bool> EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (Initiator is not null) return true;

        await _gate.WaitAsync(ct);
        try
        {
            if (Initiator is not null) return true;

            var env = DetectEnvironment();
            if (!env.ValorantRunning && !env.RiotClientRunning)
            {
                LastError = "Valorant / Riot Client is not running on this PC.";
                return false;
            }
            if (!env.LockfileExists)
            {
                LastError = $"Lockfile not found at {env.LockfilePath}. Wait for the Riot Client to finish starting.";
                return false;
            }

            var auth = new Authentication();
            var rso = await auth.AuthenticateWithLockFile();
            if (rso is null)
            {
                LastError = "Lockfile auth returned no session. Try running SenseSation as administrator " +
                            "(the local API often requires the same elevation as the Riot Client).";
                return false;
            }

            var init = new Initiator(rso);
            if (init.Client is null)
            {
                LastError = "Connected to the client but could not read the session.";
                return false;
            }

            Initiator = init;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            Initiator = null;
            string raw = ex.Message + (ex.InnerException is { } inner ? $" → {inner.Message}" : "");
            bool tokenIssue = raw.Contains("LockFile", StringComparison.OrdinalIgnoreCase)
                              || raw.Contains("token", StringComparison.OrdinalIgnoreCase)
                              || raw.Contains("NotFound", StringComparison.OrdinalIgnoreCase);
            LastError = tokenIssue
                ? "Couldn't read your Riot session. Open VALORANT itself (not just the Riot launcher) and wait for the " +
                  "main menu, then try again. Still stuck? Add a HenrikDev API key in Settings — that needs no game."
                : $"{ex.GetType().Name}: {raw}";
            return false;
        }
        finally { _gate.Release(); }
    }

    public void SetError(string? error) => LastError = error;

    /// <summary>Drops the cached session so the next call re-authenticates with a fresh lockfile token.</summary>
    public void Invalidate() => Initiator = null;

    /// <summary>
    /// Runs a RadiantConnect call with the live session, re-authenticating once if the
    /// access token has expired (Riot returns BAD_CLAIMS / "validating/decoding RSO Access Token").
    /// Tokens live ~1h; the lockfile always yields a fresh one while the client is running.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<Initiator, Task<T>> op, CancellationToken ct = default)
    {
        if (!await EnsureConnectedAsync(ct))
            throw new InvalidOperationException(LastError ?? "Riot client not connected.");
        try
        {
            return await op(Initiator!);
        }
        catch (Exception ex) when (IsAuthExpired(ex))
        {
            Invalidate();
            if (!await EnsureConnectedAsync(ct))
                throw new InvalidOperationException(LastError ?? "Riot client reconnect failed.");
            return await op(Initiator!);
        }
    }

    private static bool IsAuthExpired(Exception ex)
    {
        string msg = (ex.Message + " " + (ex.InnerException?.Message ?? "")).ToLowerInvariant();
        return msg.Contains("bad_claims")
            || msg.Contains("rso access token")
            || msg.Contains("validating/decoding")
            || msg.Contains("unauthorized")
            || msg.Contains("\"httpstatus\": 401")
            || msg.Contains("\"httpstatus\": 400")
            // RadiantConnect masks an expired-token failure as retry exhaustion.
            || msg.Contains("failed after")
            || msg.Contains("retries");
    }

    private static bool ProcessRunning(string name)
    {
        try { return System.Diagnostics.Process.GetProcessesByName(name).Length > 0; }
        catch { return false; }
    }

    private string? TryGetSelfPuuid()
    {
        try
        {
            var client = Initiator?.Client;
            return client is null ? null : ReflectionHelpers.GetString(client, "UserId");
        }
        catch { return null; }
    }
}
