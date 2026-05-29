namespace SenseSation.Data.HenrikDev;

/// <summary>Configuration for the HenrikDev VALORANT API client.</summary>
public sealed class HenrikOptions
{
    public const string Section = "Henrik";

    /// <summary>API base URL. Override only for proxies/mirrors.</summary>
    public string BaseUrl { get; set; } = "https://api.henrikdev.xyz";

    /// <summary>
    /// HenrikDev API key. Required for almost every endpoint. Obtain a free key
    /// from the HenrikDev Discord dashboard and store it in user-secrets, an
    /// environment variable, or appsettings (not committed).
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>How many competitive matches to pull per refresh (HenrikDev caps low; we page if needed).</summary>
    public int DefaultMatchCount { get; set; } = 10;
}
