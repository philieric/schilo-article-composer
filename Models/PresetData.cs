namespace SchiloArticleComposer.Models;

public class PresetData
{
    public string Default { get; set; } = string.Empty;
    public Dictionary<string, string> Presets { get; set; } = new();
}
