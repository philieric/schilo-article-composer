using System.Net.Http;
using System.Text.Json;

namespace SchiloArticleComposer.Services;

public record UpdateInfo(string Version, string Url);

public class UpdateChecker
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/philieric/schilo-article-composer/releases/latest";

    // Compare uniquement Major.Minor.Build : les tags GitHub ("v1.2.0") n'ont pas de
    // 4e composant Revision, contrairement a AssemblyVersion (toujours "x.y.z.0").
    public async Task<UpdateInfo?> CheckForUpdateAsync(Version currentVersion)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SchiloArticleComposer-UpdateCheck");

        using var response = await client.GetAsync(LatestReleaseApiUrl);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;

        var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
        var htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(htmlUrl))
        {
            return null;
        }

        var versionText = tagName.TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var latestVersion))
        {
            return null;
        }

        var normalizedCurrent = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(currentVersion.Build, 0));
        return latestVersion > normalizedCurrent ? new UpdateInfo(versionText, htmlUrl) : null;
    }
}
