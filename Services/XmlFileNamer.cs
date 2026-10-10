using System.Globalization;
using System.Text;

namespace SchiloArticleComposer.Services;

// Construit le nom du fichier XML d'une etude selon la convention d'Eric : code de
// reference, version et titre de l'article, sans accent, espaces remplaces par "_".
// Ex: DAN000_version_academique_introduction_generale_au_livre_de_daniel.xml
public static class XmlFileNamer
{
    public static string Build(string code, string version, string title)
    {
        var parts = new[] { Slugify(code, keepCase: true), Slugify(version), Slugify(title) }
            .Where(p => p.Length > 0);
        var name = string.Join("_", parts);
        return (name.Length > 0 ? name : "sections") + ".xml";
    }

    private static string Slugify(string text, bool keepCase = false)
    {
        // Les ligatures ne se decomposent pas en FormD : on les traite a part.
        var expanded = text.Replace("œ", "oe").Replace("Œ", "OE").Replace("æ", "ae").Replace("Æ", "AE");

        var sb = new StringBuilder();
        foreach (var c in expanded.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(keepCase ? c : char.ToLowerInvariant(c));
            }
            else if (sb.Length > 0 && sb[^1] != '_')
            {
                sb.Append('_');
            }
        }

        return sb.ToString().Trim('_');
    }
}
