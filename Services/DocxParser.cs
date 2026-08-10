using System.IO;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SchiloArticleComposer.Models;

namespace SchiloArticleComposer.Services;

public class DocxParseResult
{
    public List<DocSection> Sections { get; } = new();
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

        var result = new DocxParseResult();
        DocSection? current = null;
        var contentBuilder = new StringBuilder();
        ListRun? openList = null;

        void FlushList()
        {
            if (openList != null && contentBuilder != null)
            {
                contentBuilder.Append(openList.Render());
                openList = null;
            }
        }

        void FlushSection()
        {
            if (current != null)
            {
                FlushList();
                current.ContentHtml = contentBuilder.ToString().Trim();
                result.Sections.Add(current);
            }
            contentBuilder.Clear();
        }

        foreach (var para in body.Elements<Paragraph>())
        {
            var text = GetParagraphText(para);

            if (IsHeadingLevel1(para))
            {
                FlushSection();
                current = new DocSection { Title = text.Trim() };
                continue;
            }

            if (current == null)
            {
                // Contenu avant le premier titre H2 : hors perimetre (page de garde, image, refs...)
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result.ParagraphsBeforeFirstHeading++;
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                continue; // paragraphe vide = simple espacement dans Word
            }

            var listInfo = GetListInfo(para);
            if (listInfo != null)
            {
                if (openList == null || openList.Ordered != listInfo.Value.Ordered)
                {
                    FlushList();
                    openList = new ListRun(listInfo.Value.Ordered);
                }
                openList.Items.Add(RenderRunsInline(para));
                continue;
            }

            FlushList();

            if (IsWholeParagraphBold(para))
            {
                contentBuilder.Append("<h3>").Append(EscapeHtml(text.Trim())).Append("</h3>\n");
            }
            else
            {
                contentBuilder.Append("<p>").Append(RenderRunsInline(para)).Append("</p>\n");
            }
        }

        FlushSection();
        return result;
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

    private bool IsHeadingLevel1(Paragraph para)
    {
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (styleId == null) return false;

        var (name, outlineLevel) = ResolveStyle(styleId);

        if (outlineLevel.HasValue)
        {
            return outlineLevel.Value == 0;
        }

        // Repli si le style ne porte pas de niveau de plan explicite (styles Word
        // renommes/personnalises comme "Titre1" sans OutlineLevel dans le XML).
        var normalized = name.Replace(" ", "").ToLowerInvariant();
        return normalized is "titre1" or "heading1";
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
