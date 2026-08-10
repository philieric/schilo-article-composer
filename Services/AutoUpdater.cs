using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace SchiloArticleComposer.Services;

public class AutoUpdater
{
    public async Task<string> DownloadInstallerAsync(string downloadUrl, IProgress<int>? progress = null)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SchiloArticleComposer-AutoUpdate");

        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var path = Path.Combine(Path.GetTempPath(), $"SchiloArticleComposer-update-{Guid.NewGuid():N}.msi");

        long totalRead = 0;
        await using (var contentStream = await response.Content.ReadAsStreamAsync())
        await using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;
                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    progress?.Report((int)(totalRead * 100 / totalBytes.Value));
                }
            }
        }

        if (totalRead == 0)
        {
            throw new InvalidOperationException("Le fichier telecharge est vide.");
        }

        return path;
    }

    // Installe le MSI puis relance l'application, dans un processus detache : l'exe
    // courant est verrouille tant que ce processus tourne, donc l'installation et le
    // redemarrage doivent se faire APRES que ce processus se soit ferme. Le mecanisme
    // MajorUpgrade du MSI (deja en place) gere le remplacement propre de l'ancienne
    // version, pas besoin de desinstaller explicitement au prealable.
    public void InstallAndRestart(string msiPath)
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Impossible de determiner le chemin de l'executable courant.");
        var logPath = Path.Combine(Path.GetTempPath(), "SchiloArticleComposer-update.log");

        // "timeout 3" laisse le temps a ce processus de se terminer completement et de
        // liberer le verrou sur l'exe avant que msiexec ne tente de le remplacer.
        var script =
            $"timeout /t 3 /nobreak >nul & " +
            $"msiexec /i \"{msiPath}\" /qn /l*v \"{logPath}\" & " +
            $"if exist \"{exePath}\" start \"\" \"{exePath}\"";

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        System.Windows.Application.Current.Shutdown();
    }
}
