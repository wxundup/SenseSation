using System.Text.Json;
using SenseSation.Core.Abstractions;

namespace SenseSation.Web.Services;

/// <summary>
/// Persists app settings (tracked account + HenrikDev API key) to a JSON file in
/// app-data, so they survive restarts. Seeded from configuration on first launch.
/// The key lives here (not just in config) so it can be set from the Settings UI.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SenseSation", "settings.json");

    private RiotId _current;
    private string _henrikKey;

    public SettingsStore(IConfiguration config)
    {
        string configKey = config.GetSection("Henrik")["ApiKey"] ?? "";

        if (TryLoad(out var saved, out var savedKey))
        {
            _current = saved;
            // Fall back to the config key if the saved file predates the key field.
            _henrikKey = string.IsNullOrWhiteSpace(savedKey) ? configKey : savedKey;
        }
        else
        {
            var section = config.GetSection("Account");
            _current = new RiotId(section["Region"] ?? "eu", section["Name"] ?? "", section["Tag"] ?? "");
            _henrikKey = configKey;
        }
    }

    public RiotId Current => _current;
    public string HenrikApiKey => _henrikKey;
    public bool HasHenrikKey => !string.IsNullOrWhiteSpace(_henrikKey);
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_current.Name) && !string.IsNullOrWhiteSpace(_current.Tag);

    public void SaveAccount(RiotId id) { _current = id; Persist(); }
    public void SaveHenrikKey(string key) { _henrikKey = key?.Trim() ?? ""; Persist(); }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(
                new Dto(_current.Region, _current.Name, _current.Tag, _henrikKey)));
        }
        catch { /* best-effort */ }
    }

    private bool TryLoad(out RiotId id, out string key)
    {
        id = default; key = "";
        try
        {
            if (!File.Exists(_path)) return false;
            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(_path));
            if (dto is null) return false;
            id = new RiotId(dto.Region ?? "eu", dto.Name ?? "", dto.Tag ?? "");
            key = dto.HenrikApiKey ?? "";
            return true;
        }
        catch { return false; }
    }

    private sealed record Dto(string? Region, string? Name, string? Tag, string? HenrikApiKey);
}
