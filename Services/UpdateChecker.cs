using System.Net.Http;
using System.Text.Json;

namespace SchiloArticleComposer.Services;

public record UpdateInfo(string Version, string Url);

public enum UpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    NoReleasePublished,
}

public record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Update = null);

public class UpdateChecker
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/philieric/schilo-article-composer/releases/latest";

    // Compare uniquement Major.Minor.Build : les tags GitHub ("v1.2.0") n'ont pas de
    // 4e composant Revision, contrairement a AssemblyVersion (toujours "x.y.z.0").
    // Leve une exception (reseau, JSON invalide...) plutot que de l'avaler : seul
    // l'appelant sait s'il doit ignorer l'echec silencieusement (verification au
    // demarrage) ou l'afficher (verification manuelle).
    public async Task<UpdateCheckResult> CheckForUpdateAsync(Version currentVersion)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SchiloArticleComposer-UpdateCheck");

        using var response = await client.GetAsync(LatestReleaseApiUrl);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Pas encore de release publiee sur GitHub.
            return new UpdateCheckResult(UpdateCheckStatus.NoReleasePublished);
        }
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;

        var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
        var htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(htmlUrl))
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoReleasePublished);
        }

        var versionText = tagName.TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var latestVersion))
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoReleasePublished);
        }

        var normalizedCurrent = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(currentVersion.Build, 0));
        return latestVersion > normalizedCurrent
            ? new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, new UpdateInfo(versionText, htmlUrl))
            : new UpdateCheckResult(UpdateCheckStatus.UpToDate);
    }
}
