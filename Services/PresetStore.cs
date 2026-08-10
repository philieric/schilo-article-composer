using System.IO;
using System.Text.Json;
using SchiloArticleComposer.Models;

namespace SchiloArticleComposer.Services;

// Port du systeme de "presets" (modeles d'instructions IA) de SchiloIA (Python/Tkinter),
// integre comme deuxieme ecran d'Article Composer. Memes presets par defaut, meme
// format JSON (default + presets), mais stocke sous %LocalAppData%\Schilo Article
// Composer\ plutot qu'a cote de l'exe (coherent avec UpdateCheckState/AppSettings).
public static class PresetStore
{
    private static readonly string PresetsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Schilo Article Composer", "schiloia-presets.json");

    private static readonly Dictionary<string, string> DefaultPresets = new()
    {
        ["Correction + mise en forme (complet)"] =
            "Tu es un correcteur et metteur en forme de textes d'étude biblique en français, destinés à être publiés sur le site schilo.org.\n" +
            "Voici un texte que j'ai rédigé pour une étude biblique. Merci de :\n" +
            "1. Corriger l'orthographe, la grammaire et la ponctuation, sans changer le sens ni mon style personnel.\n" +
            "2. Proposer une mise en forme claire pour une page web (titres, sous-titres, paragraphes courts, points clés en listes si pertinent).\n" +
            "3. Signaler les références bibliques qui te semblent incorrectes ou à vérifier.\n\n" +
            "Voici le texte :\n",
        ["Correction orthographe uniquement"] =
            "Tu es un correcteur en français.\n" +
            "Corrige uniquement l'orthographe, la grammaire, la conjugaison et la ponctuation de ce texte d'étude biblique. " +
            "Ne change ni le sens, ni le style, ni la structure, ni les paragraphes. Renvoie le texte corrigé en entier.\n\n" +
            "Voici le texte :\n",
        ["Mise en forme pour publication web (schilo.org)"] =
            "Tu es un metteur en forme de textes pour une publication sur le site web schilo.org.\n" +
            "Reprends ce texte d'étude biblique sans en changer le contenu ni le sens, et propose uniquement une mise en forme " +
            "adaptée à une page web : titres, sous-titres, paragraphes courts, points clés en listes à puces si pertinent.\n\n" +
            "Voici le texte :\n",
        ["Résumé synthétique"] =
            "Tu es un assistant de synthèse.\n" +
            "Résume ce texte d'étude biblique sous forme d'un plan synthétique (idées principales, points clés, références " +
            "bibliques citées), en une page maximum, sans perdre les idées essentielles.\n\n" +
            "Voici le texte :\n",
    };

    public static PresetData Load()
    {
        try
        {
            if (File.Exists(PresetsFilePath))
            {
                var json = File.ReadAllText(PresetsFilePath);
                var data = JsonSerializer.Deserialize<PresetData>(json);
                if (data != null && data.Presets.Count > 0)
                {
                    return data;
                }
            }
        }
        catch
        {
            // Fichier corrompu/illisible -> repli sur les presets par defaut ci-dessous.
        }

        var defaults = new PresetData
        {
            Default = DefaultPresets.Keys.First(),
            Presets = new Dictionary<string, string>(DefaultPresets),
        };
        Save(defaults);
        return defaults;
    }

    public static void Save(PresetData data)
    {
        try
        {
            var dir = Path.GetDirectoryName(PresetsFilePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PresetsFilePath, json);
        }
        catch
        {
            // Non bloquant : les presets restent utilisables pour cette session, juste pas persistes.
        }
    }
}
