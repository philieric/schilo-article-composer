using System.IO;
using System.Text.Json;
using SchiloArticleComposer.Models;

namespace SchiloArticleComposer.Services;

// Historique des exports XML, pour retrouver rapidement un fichier exporte ou son
// document Word source. Meme emplacement/pattern que AppSettings/PresetStore
// (%LocalAppData%\Schilo Article Composer\).
public static class ExportHistoryStore
{
    private const int MaxEntries = 50;

    private static readonly string HistoryFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Schilo Article Composer", "export-history.json");

    public static List<ExportHistoryEntry> Load()
    {
        try
        {
            if (File.Exists(HistoryFilePath))
            {
                var json = File.ReadAllText(HistoryFilePath);
                var data = JsonSerializer.Deserialize<List<ExportHistoryEntry>>(json);
                if (data != null)
                {
                    return data;
                }
            }
        }
        catch
        {
            // Fichier corrompu/illisible -> on repart d'un historique vide.
        }
        return new List<ExportHistoryEntry>();
    }

    public static void Add(ExportHistoryEntry entry)
    {
        var history = Load();
        history.Insert(0, entry);
        if (history.Count > MaxEntries)
        {
            history.RemoveRange(MaxEntries, history.Count - MaxEntries);
        }
        Save(history);
    }

    private static void Save(List<ExportHistoryEntry> history)
    {
        try
        {
            var dir = Path.GetDirectoryName(HistoryFilePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(HistoryFilePath, json);
        }
        catch
        {
            // Non bloquant : l'historique reste utilisable pour cette session, juste pas persiste.
        }
    }
}
