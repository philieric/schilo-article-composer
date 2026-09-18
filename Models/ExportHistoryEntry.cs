namespace SchiloArticleComposer.Models;

public class ExportHistoryEntry
{
    public DateTime Date { get; set; }
    public string SourceDocxPath { get; set; } = string.Empty;
    public string ExportedXmlPath { get; set; } = string.Empty;
    public int SectionCount { get; set; }
}
