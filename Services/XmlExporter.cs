using System.Xml;
using SchiloArticleComposer.Models;

namespace SchiloArticleComposer.Services;

public class XmlExporter
{
    public void Export(IEnumerable<DocSection> sections, string filePath)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new System.Text.UTF8Encoding(false),
        };

        using var writer = XmlWriter.Create(filePath, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("schilo_sections");

        foreach (var section in sections.Where(s => s.Include))
        {
            writer.WriteStartElement("section");
            writer.WriteAttributeString("type", section.Type);

            writer.WriteStartElement("title");
            writer.WriteCData(section.Title);
            writer.WriteEndElement();

            writer.WriteStartElement("content");
            writer.WriteCData(section.ContentHtml);
            writer.WriteEndElement();

            writer.WriteEndElement(); // section
        }

        writer.WriteEndElement(); // schilo_sections
        writer.WriteEndDocument();
    }
}
