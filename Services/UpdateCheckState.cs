using System.Globalization;
using System.IO;

namespace SchiloArticleComposer.Services;

// Persiste la date de la derniere verification reussie (%LocalAppData%) pour eviter
// de solliciter l'API GitHub (limite anonyme : 60 requetes/heure par IP) a chaque
// lancement de l'application.
public static class UpdateCheckState
{
    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Schilo Article Composer", "update-check.txt");

    public static DateTime? GetLastCheckUtc()
    {
        try
        {
            if (!File.Exists(StateFilePath))
            {
                return null;
            }

            var text = File.ReadAllText(StateFilePath).Trim();
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                ? dt
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static void SetLastCheckNowUtc()
    {
        try
        {
            var dir = Path.GetDirectoryName(StateFilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(StateFilePath, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        }
        catch
        {
            // Non bloquant : si l'ecriture echoue, on se contentera de revrifier plus souvent.
        }
    }
}
