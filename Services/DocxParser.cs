using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SchiloArticleComposer.Models;

namespace SchiloArticleComposer.Services;

public class DocxParseResult
{
    public List<DocSection> Sections { get; } = new();

    // Etudes detectees (une par Titre 1 "version"). Vide pour un document sans
    // delimiteur d'etude, ou les Titre 1 sont directement les sections.
    public List<DocArticle> Articles { get; } = new();

    public int ParagraphsBeforeFirstHeading { get; set; }
}

public class DocxParser
{
    // Association styleId -> (nom lisible, niveau de plan 0-based si defini)
    private readonly Dictionary<string, (string Name, int? OutlineLevel, string? BasedOn)> _styles = new();

    public DocxParseResult Parse(string filePath)
    {
        // Lecture en partage (FileShare.ReadWrite) : le fichier reste ouvrable meme si
        // Word l'a deja ouvert par ailleurs (cas frequent : on relit un doc en cours d'edition).
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var memoryStream = new MemoryStream();
        fileStream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        using var doc = WordprocessingDocument.Open(memoryStream, false);
        var mainPart = doc.MainDocumentPart ?? throw new InvalidOperationException("Document Word invalide (pas de contenu principal).");

        LoadStyles(mainPart.StyleDefinitionsPart);

        var body = mainPart.Document?.Body ?? throw new InvalidOperationException("Document Word vide.");
        var paragraphs = body.Elements<Paragraph>().ToList();

        return HasStudyDelimiter(paragraphs) ? ParseStudies(paragraphs) : ParseFlatSections(paragraphs);
    }

    // Un document "a etudes" se reconnait a un Titre 1 suivi directement d'un autre titre
    // (Titre 1 "Version academique" puis titre de l'article), sans texte entre les deux.
    // Les anciens documents (Titre 1 = section, suivie de son contenu) n'ont pas cette
    // forme et gardent le traitement d'origine.
    private bool HasStudyDelimiter(List<Paragraph> paragraphs)
    {
        var previousIsHeading1 = false;
        foreach (var para in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(GetParagraphText(para))) continue;

            if (GetMarkedRole(para) == MarkedRole.Delimiter) return true;

            var level = GetHeadingLevel(para);
            if (previousIsHeading1 && level is 0 or 1) return true;
            previousIsHeading1 = level == 0;
        }
        return false;
    }

    private enum MarkedRole { None, Delimiter, ArticleTitle, SectionTitle, Content }

    private enum ParagraphKind { Delimiter, ArticleTitle, SectionTitle, SubHeading, Text }

    // Role indique par le surlignage Word du paragraphe (convention d'Eric) : rose =
    // delimiteur d'etude, vert = titre de l'article, bleu = titre de section, jaune =
    // contenu. Le surlignage, quand il est present, prime sur le style : un Titre 3
    // surligne en bleu est un titre de section, pas un <h3>. Le gris (ligne du code de
    // reference) et les paragraphes non surlignes sont classes d'apres leur style.
    private static MarkedRole GetMarkedRole(Paragraph para)
    {
        var run = para.Descendants<Run>().FirstOrDefault(r => !string.IsNullOrWhiteSpace(GetRunText(r)));
        var color = run?.RunProperties?.Highlight?.Val?.Value;
        if (color == null) return MarkedRole.None;

        if (color == HighlightColorValues.Magenta) return MarkedRole.Delimiter;
        if (color == HighlightColorValues.Green) return MarkedRole.ArticleTitle;
        if (color == HighlightColorValues.Cyan) return MarkedRole.SectionTitle;
        if (color == HighlightColorValues.Yellow) return MarkedRole.Content;
        return MarkedRole.None;
    }

    private bool IsSectionHeading(Paragraph para)
    {
        var role = GetMarkedRole(para);
        return role == MarkedRole.SectionTitle || (role == MarkedRole.None && GetHeadingLevel(para) == 1);
    }

    // Structure attendue (convention d'Eric, documents DANxxx), d'apres le surlignage
    // s'il y en a un, sinon d'apres le style :
    //   1re ligne               -> code de reference (ex: "DAN000"), pour le nom du fichier
    //   rose   / Titre 1        -> delimiteur d'etude ("Version academique") = un fichier XML
    //   vert   / titre suivant  -> titre de l'article
    //   bleu   / Titre 2        -> titre de section
    //   jaune  / texte          -> contenu de la section
    //   Titre 3 et plus (non surligne en bleu) -> <h3>/<h4> dans le contenu
    private DocxParseResult ParseStudies(List<Paragraph> paragraphs)
    {
        var result = new DocxParseResult();
        var writer = new ContentWriter();
        var bibleReferences = new List<string>();
        string? code = null;
        DocArticle? article = null;
        DocSection? current = null;

        void FlushSection()
        {
            var html = writer.Flush();
            if (current != null && article != null)
            {
                current.ContentHtml = html;
                article.Sections.Add(current);
                result.Sections.Add(current);
            }
            current = null;
        }

        for (var i = 0; i < paragraphs.Count; i++)
        {
            var para = paragraphs[i];
            var text = GetParagraphText(para);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var level = GetHeadingLevel(para);
            var awaitingTitle = article != null && article.Title.Length == 0 && article.Sections.Count == 0 && current == null;

            var kind = GetMarkedRole(para) switch
            {
                MarkedRole.Delimiter => ParagraphKind.Delimiter,
                // Un 2e titre d'article dans la meme etude ne peut etre qu'une section.
                MarkedRole.ArticleTitle => awaitingTitle ? ParagraphKind.ArticleTitle : ParagraphKind.SectionTitle,
                MarkedRole.SectionTitle => ParagraphKind.SectionTitle,
                MarkedRole.Content => level.HasValue ? ParagraphKind.SubHeading : ParagraphKind.Text,
                _ => level switch
                {
                    0 => awaitingTitle ? ParagraphKind.ArticleTitle : ParagraphKind.Delimiter,
                    // Titre de l'article saisi en Titre 2 au lieu de Titre 1 : c'est le cas
                    // quand il suit directement le delimiteur et precede un titre de section.
                    1 => awaitingTitle && NextTextIsSectionHeading(paragraphs, i) ? ParagraphKind.ArticleTitle : ParagraphKind.SectionTitle,
                    null => ParagraphKind.Text,
                    _ => ParagraphKind.SubHeading,
                },
            };

            if (kind == ParagraphKind.Delimiter)
            {
                FlushSection();
                article = new DocArticle { Code = code ?? string.Empty, Version = text.Trim() };
                result.Articles.Add(article);
                continue;
            }

            if (article == null)
            {
                if (code == null)
                {
                    code = ExtractStudyCode(text);
                    continue;
                }

                result.ParagraphsBeforeFirstHeading++;
                var reference = ExtractBibleReference(text);
                if (reference != null) bibleReferences.Add(reference);
                continue;
            }

            if (kind == ParagraphKind.ArticleTitle)
            {
                article.Title = text.Trim();
                continue;
            }

            if (kind == ParagraphKind.SectionTitle)
            {
                FlushSection();
                current = new DocSection { Title = text.Trim(), Article = article };
                continue;
            }

            if (current == null)
            {
                if (article.Title.Length == 0)
                {
                    result.ParagraphsBeforeFirstHeading++;
                    continue;
                }

                // Chapeau place entre le titre de l'article et la 1re section (ex: DAN001,
                // version pedagogique) : conserve dans une section portant le titre de l'article.
                current = new DocSection { Title = article.Title, Article = article };
            }

            if (kind == ParagraphKind.SubHeading)
            {
                var tag = level is null or <= 2 ? "h3" : "h4";
                writer.AppendBlock($"<{tag}>{EscapeHtml(text.Trim())}</{tag}>\n");
                continue;
            }

            AppendBodyParagraph(writer, para, text, boldParagraphIsHeading: false);
        }

        FlushSection();

        if (bibleReferences.Count > 0 && result.Articles.Count > 0)
        {
            var first = result.Articles[0];
            var bibleSection = BuildBibleReferencesSection(bibleReferences);
            bibleSection.Article = first;
            first.Sections.Insert(0, bibleSection);
            result.Sections.Insert(0, bibleSection);
        }

        return result;
    }

    private bool NextTextIsSectionHeading(List<Paragraph> paragraphs, int index)
    {
        for (var i = index + 1; i < paragraphs.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(GetParagraphText(paragraphs[i]))) continue;
            return IsSectionHeading(paragraphs[i]);
        }
        return false;
    }

    // "DAN000 Introduction" -> "DAN000" : le code est le premier mot de la premiere ligne.
    private static string ExtractStudyCode(string text)
        => text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    private DocxParseResult ParseFlatSections(List<Paragraph> paragraphs)
    {
        var result = new DocxParseResult();
        var writer = new ContentWriter();
        DocSection? current = null;
        var bibleReferences = new List<string>();

        void FlushSection()
        {
            var html = writer.Flush();
            if (current != null)
            {
                current.ContentHtml = html;
                result.Sections.Add(current);
            }
        }

        foreach (var para in paragraphs)
        {
            var text = GetParagraphText(para);

            if (GetHeadingLevel(para) == 0)
            {
                FlushSection();
                current = new DocSection { Title = text.Trim() };
                continue;
            }

            if (current == null)
            {
                // Contenu avant le premier titre H2 : hors perimetre (page de garde, image...),
                // sauf les lignes de reference biblique du bloc "Textes bibliques" (ex: "Matthieu
                // 13.1-23 (S21)"), qui alimentent une section dediee "textes-bibliques".
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result.ParagraphsBeforeFirstHeading++;

                    var reference = ExtractBibleReference(text);
                    if (reference != null)
                    {
                        bibleReferences.Add(reference);
                    }
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                continue; // paragraphe vide = simple espacement dans Word
            }

            AppendBodyParagraph(writer, para, text, boldParagraphIsHeading: true);
        }

        FlushSection();

        if (bibleReferences.Count > 0)
        {
            result.Sections.Insert(0, BuildBibleReferencesSection(bibleReferences));
        }

        return result;
    }

    private void AppendBodyParagraph(ContentWriter writer, Paragraph para, string text, bool boldParagraphIsHeading)
    {
        var listInfo = GetListInfo(para);
        if (listInfo != null)
        {
            writer.AppendListItem(listInfo.Value.Ordered, RenderRunsInline(para));
            return;
        }

        if (boldParagraphIsHeading && IsWholeParagraphBold(para))
        {
            writer.AppendBlock($"<h3>{EscapeHtml(text.Trim())}</h3>\n");
        }
        else
        {
            writer.AppendBlock($"<p>{RenderRunsInline(para)}</p>\n");
        }
    }

    private static DocSection BuildBibleReferencesSection(List<string> bibleReferences)
    {
        var refsHtml = new StringBuilder();
        foreach (var reference in bibleReferences)
        {
            var tag = GetBookTag(reference);
            refsHtml.Append('<').Append(tag).Append('>')
                .Append(EscapeHtml(reference))
                .Append("</").Append(tag).Append(">\n");
        }

        return new DocSection
        {
            Title = "Textes bibliques",
            ContentHtml = refsHtml.ToString().TrimEnd(),
            Type = "textes-bibliques",
        };
    }

    // Detecte une ligne "Livre chapitre.versets" isolee (ex: "Matthieu 13.1-23 (S21)",
    // "1 Corinthiens 13.1-3"), telle qu'elle introduit chaque passage dans le bloc "Textes
    // bibliques". Renvoie la reference nettoyee (sans l'annotation de traduction entre
    // parentheses), ou null si le texte ne correspond pas a ce format.
    private static readonly Regex BibleReferencePattern = new(
        @"^(?:\d\s)?\p{L}[\p{L}\s]*\d+[.,:]\d+(?:\s*[-–—]\s*\d+)?(?:\s*\([^)]*\))?$",
        RegexOptions.Compiled);

    private static string? ExtractBibleReference(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || !BibleReferencePattern.IsMatch(trimmed))
        {
            return null;
        }

        return Regex.Replace(trimmed, @"\s*\([^)]*\)\s*$", string.Empty).Trim();
    }

    // Balise XML par livre pour le bloc "Textes bibliques" (convention Schilo) : Matthieu,
    // Marc, Luc et Jean ont leur propre balise, tous les autres livres utilisent <BI>.
    private static string GetBookTag(string reference)
    {
        if (reference.StartsWith("Matthieu ", StringComparison.OrdinalIgnoreCase)) return "MT";
        if (reference.StartsWith("Marc ", StringComparison.OrdinalIgnoreCase)) return "MC";
        if (reference.StartsWith("Luc ", StringComparison.OrdinalIgnoreCase)) return "LU";
        if (reference.StartsWith("Jean ", StringComparison.OrdinalIgnoreCase)) return "JE";
        return "BI";
    }

    private void LoadStyles(StyleDefinitionsPart? part)
    {
        _styles.Clear();
        if (part?.Styles == null) return;

        foreach (var style in part.Styles.Elements<Style>())
        {
            var id = style.StyleId?.Value;
            if (id == null) continue;

            var name = style.StyleName?.Val?.Value ?? id;
            int? outline = style.StyleParagraphProperties?.OutlineLevel?.Val?.Value;
            var basedOn = style.BasedOn?.Val?.Value;

            _styles[id] = (name, outline, basedOn);
        }
    }

    private (string Name, int? OutlineLevel) ResolveStyle(string? styleId, int depth = 0)
    {
        if (styleId == null || depth > 10 || !_styles.TryGetValue(styleId, out var s))
        {
            return (styleId ?? "Normal", null);
        }

        if (s.OutlineLevel.HasValue)
        {
            return (s.Name, s.OutlineLevel);
        }

        if (s.BasedOn != null)
        {
            var parent = ResolveStyle(s.BasedOn, depth + 1);
            return (s.Name, parent.OutlineLevel);
        }

        return (s.Name, null);
    }

    // Niveau de plan 0-based du titre (0 = Titre 1, 1 = Titre 2...), ou null si le
    // paragraphe n'est pas un titre.
    private int? GetHeadingLevel(Paragraph para)
    {
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (styleId == null) return null;

        var (name, outlineLevel) = ResolveStyle(styleId);

        if (outlineLevel.HasValue)
        {
            return outlineLevel.Value < 9 ? outlineLevel.Value : null; // 9 = "corps de texte" dans Word
        }

        // Repli si le style ne porte pas de niveau de plan explicite (styles Word
        // renommes/personnalises comme "Titre1" sans OutlineLevel dans le XML).
        var match = Regex.Match(name.Replace(" ", "").ToLowerInvariant(), "^(?:titre|heading)([1-9])$");
        return match.Success ? match.Groups[1].Value[0] - '1' : null;
    }

    private static string GetParagraphText(Paragraph para)
        => string.Concat(para.Descendants<Text>().Select(t => t.Text));

    private static bool IsWholeParagraphBold(Paragraph para)
    {
        var runs = para.Elements<Run>().Where(r => !string.IsNullOrEmpty(GetRunText(r))).ToList();
        if (runs.Count == 0) return false;
        return runs.All(IsRunBold);
    }

    private static bool IsRunBold(Run run)
    {
        var b = run.RunProperties?.Bold;
        if (b == null) return false;
        return b.Val is null || b.Val.Value; // <w:b/> sans valeur = actif ; <w:b w:val="0"/> = inactif
    }

    private static bool IsRunItalic(Run run)
    {
        var i = run.RunProperties?.Italic;
        if (i == null) return false;
        return i.Val is null || i.Val.Value;
    }

    private static string GetRunText(Run run)
        => string.Concat(run.Descendants<Text>().Select(t => t.Text));

    private static string RenderRunsInline(Paragraph para)
    {
        var sb = new StringBuilder();
        foreach (var run in para.Elements<Run>())
        {
            var text = GetRunText(run);
            var hasBreak = run.Elements<Break>().Any();
            if (string.IsNullOrEmpty(text) && !hasBreak) continue;

            var escaped = EscapeHtml(text);
            if (IsRunBold(run)) escaped = $"<strong>{escaped}</strong>";
            if (IsRunItalic(run)) escaped = $"<em>{escaped}</em>";
            sb.Append(escaped);
            if (hasBreak) sb.Append("<br>");
        }
        return sb.ToString();
    }

    private (bool Ordered, int Level)? GetListInfo(Paragraph para)
    {
        var numPr = para.ParagraphProperties?.NumberingProperties;
        var styleNumPr = numPr;
        if (styleNumPr?.NumberingId?.Val == null) return null;

        var level = styleNumPr.NumberingLevelReference?.Val?.Value ?? 0;
        // Sans acces fiable au format exact (decimal/bullet) sans re-ouvrir numbering.xml
        // pour chaque numId/level, on retient par defaut une liste a puces : c'est le
        // rendu le plus sûr visuellement si le format reel etait numerote.
        return (Ordered: false, Level: level);
    }

    private static string EscapeHtml(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // Accumule le HTML d'une section, en regroupant les elements de liste consecutifs.
    private class ContentWriter
    {
        private readonly StringBuilder _html = new();
        private ListRun? _openList;

        public void AppendBlock(string html)
        {
            CloseList();
            _html.Append(html);
        }

        public void AppendListItem(bool ordered, string itemHtml)
        {
            if (_openList == null || _openList.Ordered != ordered)
            {
                CloseList();
                _openList = new ListRun(ordered);
            }
            _openList.Items.Add(itemHtml);
        }

        public string Flush()
        {
            CloseList();
            var html = _html.ToString().Trim();
            _html.Clear();
            return html;
        }

        private void CloseList()
        {
            if (_openList != null)
            {
                _html.Append(_openList.Render());
                _openList = null;
            }
        }
    }

    private class ListRun
    {
        public bool Ordered { get; }
        public List<string> Items { get; } = new();

        public ListRun(bool ordered) => Ordered = ordered;

        public string Render()
        {
            var tag = Ordered ? "ol" : "ul";
            var sb = new StringBuilder();
            sb.Append('<').Append(tag).Append(">\n");
            foreach (var item in Items)
            {
                sb.Append("<li>").Append(item).Append("</li>\n");
            }
            sb.Append("</").Append(tag).Append(">\n");
            return sb.ToString();
        }
    }
}
