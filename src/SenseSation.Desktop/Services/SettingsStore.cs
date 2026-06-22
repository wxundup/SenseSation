using System;
using System.IO;
using System.Text.Json;
using SenseSation.Core.Abstractions;

namespace SenseSation.Desktop.Services;

/// <summary>Persists tracked account + HenrikDev key to app-data JSON. Seeds key from env on first run.</summary>
public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SenseSation", "settings.json");

    private RiotId _current;
    private string _henrikKey;

    public SettingsStore()
    {
        if (TryLoad(out var dto) && dto is not null)
        {
            _current = new RiotId(dto.Region ?? "eu", dto.Name ?? "", dto.Tag ?? "");
            _henrikKey = dto.HenrikApiKey ?? "";
            Theme = dto.Theme ?? "Valorant Red";
            UseRankTheme = dto.UseRankTheme ?? false;
        }
        else
        {
            _current = new RiotId("eu", "", "");
            _henrikKey = Environment.GetEnvironmentVariable("HENRIK__APIKEY") ?? "";
        }
    }

    public RiotId Current => _current;
    public string HenrikApiKey => _henrikKey;
    public bool HasHenrikKey => !string.IsNullOrWhiteSpace(_henrikKey);
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_current.Name) && !string.IsNullOrWhiteSpace(_current.Tag);

    public string Theme { get; private set; } = "Valorant Red";
    public bool UseRankTheme { get; private set; }

    public void SaveAccount(RiotId id) { _current = id; Persist(); }
    public void SaveHenrikKey(string key) { _henrikKey = key?.Trim() ?? ""; Persist(); }
    public void SaveTheme(string theme, bool useRank) { Theme = theme; UseRankTheme = useRank; Persist(); }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(
                new Dto(_current.Region, _current.Name, _current.Tag, _henrikKey, Theme, UseRankTheme)));
        }
        catch { /* best-effort */ }
    }

    private bool TryLoad(out Dto? dto)
    {
        dto = null;
        try
        {
            if (!File.Exists(_path)) return false;
            dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(_path));
            return dto is not null;
        }
        catch { return false; }
    }

    private sealed record Dto(string? Region, string? Name, string? Tag, string? HenrikApiKey,
        string? Theme = null, bool? UseRankTheme = null);
}
