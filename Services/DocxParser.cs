using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
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

    // Points a signaler a Eric apres lecture (cellules fusionnees, tableau trop grand...).
    public List<string> Warnings { get; } = new();
}

public class DocxParser
{
    // Association styleId -> (nom lisible, niveau de plan 0-based si defini)
    private readonly Dictionary<string, (string Name, int? OutlineLevel, string? BasedOn)> _styles = new();
    private MainDocumentPart? _mainPart; // pour resoudre l'adresse des liens hypertexte

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

        _mainPart = mainPart;
        LoadStyles(mainPart.StyleDefinitionsPart);

        var body = mainPart.Document?.Body ?? throw new InvalidOperationException("Document Word vide.");
        // Paragraphes et tableaux, dans l'ordre du document.
        var blocks = body.ChildElements.Where(e => e is Paragraph or Table).ToList();

        return HasStudyDelimiter(blocks) ? ParseStudies(blocks) : ParseFlatSections(blocks);
    }

    // Un document "a etudes" se reconnait a un Titre 1 suivi directement d'un autre titre
    // (Titre 1 "Version academique" puis titre de l'article), sans texte entre les deux.
    // Les anciens documents (Titre 1 = section, suivie de son contenu) n'ont pas cette
    // forme et gardent le traitement d'origine.
    private bool HasStudyDelimiter(List<OpenXmlElement> blocks)
    {
        var previousIsHeading1 = false;
        foreach (var block in blocks)
        {
            if (block is not Paragraph para)
            {
                previousIsHeading1 = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(GetParagraphText(para))) continue;

            var level = GetHeadingLevel(para);
            if (previousIsHeading1 && level is 0 or 1) return true;
            previousIsHeading1 = level == 0;
        }
        return false;
    }

    // Structure attendue (convention d'Eric, documents DANxxx) :
    //   1re ligne         -> code de reference (ex: "DAN000"), pour le nom du fichier
    //   Titre 1           -> delimiteur d'etude ("Version academique") = un fichier XML
    //   titre suivant     -> titre de l'article
    //   Titre 2           -> titre de section
    //   texte             -> contenu de la section
    //   Titre 3 et plus   -> <h3>/<h4> dans le contenu
    private DocxParseResult ParseStudies(List<OpenXmlElement> blocks)
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

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Table table)
            {
                if (article == null)
                {
                    result.ParagraphsBeforeFirstHeading++;
                    continue;
                }

                if (current == null)
                {
                    if (article.Title.Length == 0)
                    {
                        result.ParagraphsBeforeFirstHeading++;
                        continue;
                    }
                    current = new DocSection { Title = article.Title, Article = article };
                }

                writer.AppendBlock(RenderTable(table, current.Title, result.Warnings));
                continue;
            }

            var para = (Paragraph)blocks[i];
            var text = GetParagraphText(para);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var level = GetHeadingLevel(para);

            if (level == 0)
            {
                if (article != null && article.Title.Length == 0 && article.Sections.Count == 0 && current == null)
                {
                    article.Title = text.Trim();
                }
                else
                {
                    FlushSection();
                    article = new DocArticle { Code = code ?? string.Empty, Version = text.Trim() };
                    result.Articles.Add(article);
                }
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

            if (level == 1)
            {
                // Titre de l'article saisi en Titre 2 au lieu de Titre 1 : c'est le cas
                // quand il suit directement le delimiteur et precede un autre Titre 2.
                if (article.Title.Length == 0 && article.Sections.Count == 0 && current == null
                    && NextTextIsSectionHeading(blocks, i))
                {
                    article.Title = text.Trim();
                    continue;
                }

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

            if (level.HasValue)
            {
                var tag = level.Value == 2 ? "h3" : "h4";
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

    private bool NextTextIsSectionHeading(List<OpenXmlElement> blocks, int index)
    {
        for (var i = index + 1; i < blocks.Count; i++)
        {
            if (blocks[i] is not Paragraph para) return false;
            if (string.IsNullOrWhiteSpace(GetParagraphText(para))) continue;
            return GetHeadingLevel(para) == 1;
        }
        return false;
    }

    // "DAN000 Introduction" -> "DAN000" : le code est le premier mot de la premiere ligne.
    private static string ExtractStudyCode(string text)
        => text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    private DocxParseResult ParseFlatSections(List<OpenXmlElement> blocks)
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

        foreach (var block in blocks)
        {
            if (block is Table table)
            {
                if (current == null)
                {
                    result.ParagraphsBeforeFirstHeading++;
                    continue;
                }

                writer.AppendBlock(RenderTable(table, current.Title, result.Warnings));
                continue;
            }

            var para = (Paragraph)block;
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

    // Demande d'Eric : tout tiret long ou moyen saisi dans Word (souvent pose par la
    // correction automatique) sort en tiret court "-", dans les titres comme dans le contenu.
    private static string NormalizeDashes(string text)
        => text.Replace('‒', '-').Replace('–', '-').Replace('—', '-').Replace('―', '-');

    private static string GetParagraphText(Paragraph para)
        => NormalizeDashes(string.Concat(para.Descendants<Text>().Select(t => t.Text)));

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
        => NormalizeDashes(string.Concat(run.Descendants<Text>().Select(t => t.Text)));

    // Rend le texte d'un paragraphe avec gras/italique/sauts de ligne et liens. Le texte
    // des liens hypertexte (w:hyperlink) etait auparavant perdu : ses runs ne sont pas
    // des enfants directs du paragraphe.
    private string RenderRunsInline(Paragraph para, bool allowBold = true)
    {
        var sb = new StringBuilder();
        foreach (var child in para.ChildElements)
        {
            if (child is Run run)
            {
                sb.Append(RenderRun(run, allowBold));
            }
            else if (child is Hyperlink link)
            {
                var inner = string.Concat(link.Elements<Run>().Select(r => RenderRun(r, allowBold)));
                if (inner.Length == 0) continue;

                var href = GetHyperlinkTarget(link);
                sb.Append(href != null ? $"<a href=\"{EscapeAttribute(href)}\">{inner}</a>" : inner);
            }
        }
        return sb.ToString();
    }

    private static string RenderRun(Run run, bool allowBold)
    {
        var text = GetRunText(run);
        var hasBreak = run.Elements<Break>().Any();
        if (string.IsNullOrEmpty(text) && !hasBreak) return string.Empty;

        var escaped = EscapeHtml(text);
        if (allowBold && IsRunBold(run)) escaped = $"<strong>{escaped}</strong>";
        if (IsRunItalic(run)) escaped = $"<em>{escaped}</em>";
        return hasBreak ? escaped + "<br>" : escaped;
    }

    // Lien externe uniquement : un lien vers un signet du document (w:anchor) n'a pas
    // d'equivalent sur le site, on n'en garde que le texte.
    private string? GetHyperlinkTarget(Hyperlink link)
    {
        var id = link.Id?.Value;
        if (id == null || _mainPart == null) return null;
        return _mainPart.HyperlinkRelationships.FirstOrDefault(r => r.Id == id)?.Uri.ToString();
    }

    // Tableau Word -> <table> selon la regle d'import de Schilo Builder : <caption>
    // facultatif, en-tete dans <thead><tr><th>, lignes dans <tbody><tr><td>, aucun
    // attribut de mise en forme (style, width, class). Les cellules fusionnees sont
    // defusionnees (valeur repetee verticalement, cellules vides horizontalement)
    // pour que les colonnes ne se decalent pas a l'import.
    private string RenderTable(Table table, string sectionTitle, List<string> warnings)
    {
        var rows = table.Elements<TableRow>().ToList();
        var headerCount = rows.TakeWhile(IsRepeatedHeaderRow).Count();
        if (headerCount == 0 && rows.Count > 1 && IsWholeRowBold(rows[0]))
        {
            // Pas de "Repeter comme ligne d'en-tete" dans Word, mais 1re ligne toute en
            // gras : c'est l'en-tete dans tous les tableaux des documents DANxxx.
            headerCount = 1;
        }

        var merged = false;
        var nested = false;
        var verticalValues = new Dictionary<int, string>();
        var lines = new List<(bool Header, List<string> Cells)>();

        for (var r = 0; r < rows.Count; r++)
        {
            var isHeader = r < headerCount;
            var cells = new List<string>();
            foreach (var cell in rows[r].Elements<TableCell>())
            {
                var props = cell.TableCellProperties;
                var span = Math.Max(1, props?.GridSpan?.Val?.Value ?? 1);
                var vMerge = props?.VerticalMerge;
                var column = cells.Count;

                string html;
                if (vMerge != null && vMerge.Val?.Value != MergedCellValues.Restart)
                {
                    html = verticalValues.GetValueOrDefault(column, string.Empty);
                    merged = true;
                }
                else
                {
                    html = RenderCell(cell, isHeader, ref nested);
                    if (vMerge != null)
                    {
                        verticalValues[column] = html;
                        merged = true;
                    }
                    else
                    {
                        verticalValues.Remove(column);
                    }
                }

                cells.Add(html);
                for (var k = 1; k < span; k++)
                {
                    cells.Add(string.Empty);
                    merged = true;
                }
            }

            if (cells.All(c => c.Length == 0)) continue; // ligne vide : l'import la retirerait
            lines.Add((isHeader, cells));
        }

        var bodyRows = lines.Count(l => !l.Header);
        var columns = lines.Count == 0 ? 0 : lines.Max(l => l.Cells.Count);
        if (merged)
            warnings.Add($"« {sectionTitle} » : cellules fusionnees defusionnees automatiquement — verifiez le tableau.");
        if (nested)
            warnings.Add($"« {sectionTitle} » : tableau imbrique dans une cellule, remplace par son texte.");
        if (bodyRows > 300 || columns > 20)
            warnings.Add($"« {sectionTitle} » : tableau de {bodyRows} lignes et {columns} colonnes, refuse a l'import (max 300 lignes, 20 colonnes).");

        var sb = new StringBuilder("<table>\n");
        var caption = table.GetFirstChild<TableProperties>()?.GetFirstChild<TableCaption>()?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(caption))
        {
            sb.Append("<caption>").Append(EscapeHtml(NormalizeDashes(caption.Trim()))).Append("</caption>\n");
        }

        void AppendRows(bool header)
        {
            var group = lines.Where(l => l.Header == header).ToList();
            if (group.Count == 0) return;

            var (section, cellTag) = header ? ("thead", "th") : ("tbody", "td");
            sb.Append('<').Append(section).Append(">\n");
            foreach (var line in group)
            {
                sb.Append("<tr>");
                foreach (var cell in line.Cells)
                {
                    sb.Append('<').Append(cellTag).Append('>').Append(cell).Append("</").Append(cellTag).Append('>');
                }
                sb.Append("</tr>\n");
            }
            sb.Append("</").Append(section).Append(">\n");
        }

        AppendRows(header: true);
        AppendRows(header: false);
        sb.Append("</table>\n");
        return sb.ToString();
    }

    // Contenu d'une cellule : un paragraphe Word par ligne (<br> entre eux). Un tableau
    // imbrique est aplati en texte, l'import ne les accepte pas.
    private string RenderCell(TableCell cell, bool isHeader, ref bool nested)
    {
        var parts = new List<string>();
        foreach (var child in cell.ChildElements)
        {
            if (child is Paragraph para)
            {
                var html = RenderRunsInline(para, allowBold: !isHeader).Trim();
                if (html.Length > 0) parts.Add(html);
            }
            else if (child is Table inner)
            {
                nested = true;
                parts.AddRange(inner.Descendants<Paragraph>()
                    .Select(GetParagraphText)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => EscapeHtml(t.Trim())));
            }
        }
        return string.Join("<br>", parts);
    }

    private static bool IsRepeatedHeaderRow(TableRow row)
    {
        var header = row.TableRowProperties?.GetFirstChild<TableHeader>();
        return header != null && header.Val?.InnerText is not ("off" or "0" or "false");
    }

    private static bool IsWholeRowBold(TableRow row)
    {
        var runs = row.Descendants<Run>().Where(r => !string.IsNullOrWhiteSpace(GetRunText(r))).ToList();
        return runs.Count > 0 && runs.All(IsRunBold);
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

    private static string EscapeAttribute(string text)
        => EscapeHtml(text).Replace("\"", "&quot;");

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
