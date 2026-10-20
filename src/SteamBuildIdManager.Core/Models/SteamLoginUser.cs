namespace SteamBuildIdManager.Core.Models;

/// <summary>One account remembered by the local Steam client (config/loginusers.vdf or userdata).</summary>
public sealed record SteamLoginUser(
    string SteamId64,
    string AccountName,
    string PersonaName,
    bool MostRecent,
    bool AutoLogin,
    long Timestamp)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(PersonaName)
        ? PersonaName
        : !string.IsNullOrWhiteSpace(AccountName) ? AccountName : SteamId64;

    public string LastLoginText => Timestamp > 0
        ? DateTimeOffset.FromUnixTimeSeconds(Timestamp).LocalDateTime.ToString("yyyy-MM-dd HH:mm")
        : "";

    public string DisplayText
    {
        get
        {
            var identity = string.IsNullOrWhiteSpace(AccountName) || AccountName == DisplayName
                ? DisplayName
                : $"{DisplayName} ({AccountName})";
            var recent = MostRecent ? " · 最近登录" : AutoLogin ? " · 自动登录" : "";
            var lastLogin = string.IsNullOrEmpty(LastLoginText) ? "" : $" · {LastLoginText}";
            return $"{identity} · {SteamId64}{recent}{lastLogin}";
        }
    }

    public override string ToString() => DisplayText;
}