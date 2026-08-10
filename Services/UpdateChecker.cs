using System.Net;
using System.Net.Http;

namespace SchiloArticleComposer.Services;

public record UpdateInfo(string Version, string Url, string? MsiDownloadUrl);

public enum UpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    NoReleasePublished,
}

public record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Update = null);

// Verifie la version via raw.githubusercontent.com (CDN, pas via api.github.com) : la
// limite anonyme de l'API (60 requetes/heure par IP) s'est epuisee a plusieurs reprises
// en usage reel ce jour-la, avec un effet pervers observe empiriquement — chaque
// nouvelle requete pendant l'epuisement repousse le "reset" d'une heure de plus, donc
// ca ne se retablit jamais tant que quelqu'un continue a verifier. raw.githubusercontent
// (CDN Fastly) n'a pas cette contrainte pratique. Le lien de telechargement du MSI est
// reconstruit de maniere previsible (releases/download/vX.Y.Z/...), egalement hors API.
public class UpdateChecker
{
    private const string Owner = "philieric";
    private const string Repo = "schilo-article-composer";

    public async Task<UpdateCheckResult> CheckForUpdateAsync(Version currentVersion)
    {
        var latestVersionUrl = $"https://raw.githubusercontent.com/{Owner}/{Repo}/main/latest-version.txt";

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SchiloArticleComposer-UpdateCheck");

        using var response = await client.GetAsync(latestVersionUrl);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Pas encore de latest-version.txt publie (avant la toute premiere release
            // qui l'aura cree).
            return new UpdateCheckResult(UpdateCheckStatus.NoReleasePublished);
        }
        response.EnsureSuccessStatusCode();

        var versionText = (await response.Content.ReadAsStringAsync()).Trim();
        if (!Version.TryParse(versionText, out var latestVersion))
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoReleasePublished);
        }

        var normalizedCurrent = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(currentVersion.Build, 0));
        if (latestVersion <= normalizedCurrent)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
        }

        var tag = $"v{versionText}";
        var releaseUrl = $"https://github.com/{Owner}/{Repo}/releases/tag/{tag}";
        var msiDownloadUrl = $"https://github.com/{Owner}/{Repo}/releases/download/{tag}/SchiloArticleComposer.msi";

        return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, new UpdateInfo(versionText, releaseUrl, msiDownloadUrl));
    }
}
