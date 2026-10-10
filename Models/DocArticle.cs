using SchiloArticleComposer.Services;

namespace SchiloArticleComposer.Models;

// Une etude delimitee dans le document Word par un Titre 1 "version" (ex: "Version
// academique", "Version pedagogique"). Chaque etude est exportee dans son propre
// fichier XML, nomme d'apres le code de reference, la version et le titre de l'article.
public class DocArticle
{
    public string Code { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    public List<DocSection> Sections { get; } = new();

    public string Label => Title.Length > 0 ? $"{Version} — {Title}" : Version;

    public string FileName => XmlFileNamer.Build(Code, Version, Title);
}
