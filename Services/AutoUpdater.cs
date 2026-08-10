using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace SchiloArticleComposer.Services;

public class AutoUpdater
{
    public async Task<string> DownloadInstallerAsync(string downloadUrl)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SchiloArticleComposer-AutoUpdate");

        var bytes = await client.GetByteArrayAsync(downloadUrl);
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("Le fichier telecharge est vide.");
        }

        var path = Path.Combine(Path.GetTempPath(), $"SchiloArticleComposer-update-{Guid.NewGuid():N}.msi");
        await File.WriteAllBytesAsync(path, bytes);
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
