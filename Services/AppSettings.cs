using System.IO;
using System.Text.Json;

namespace SchiloArticleComposer.Services;

public class AppSettingsData
{
    // Format "#RRGGBB". Mauve vif par defaut (demande d'Eric).
    public string HtmlTagColor { get; set; } = "#CA14FC";

    // "Light" | "System" | "Dark". "Dark" (theme sombre personnalise, pas juste le
    // sombre WPF-UI/Mica par defaut) par defaut : demande d'Eric le 2026-09-18, le
    // sombre natif (Mica) rend les limites entre barre de titre/contenu/boutons trop
    // peu contrastees. Voir ThemeManager pour le detail des nuances appliquees.
    public string ThemePreference { get; set; } = "Dark";
}

public static class AppSettings
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Schilo Article Composer", "settings.json");

    public static AppSettingsData Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettingsData();
            }

            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettingsData>(json) ?? new AppSettingsData();
        }
        catch
        {
            return new AppSettingsData();
        }
    }

    public static void Save(AppSettingsData settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Non bloquant : la couleur reste appliquee pour cette session, juste pas persistee.
        }
    }
}
